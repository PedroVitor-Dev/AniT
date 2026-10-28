using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using AniT.App;
using AniT.Infrastructure;

internal static class StartupChecks
{
    public static int Probe(string[] args)
    {
        using var instance = ApplicationInstance.TryAcquire(args[1], () => { }, args[0] == "--probe-restart");
        return args[0] == "--probe-restart" ? instance is not null ? 0 : 2 : instance is null ? 0 : 3;
    }

    public static void Run()
    {
        var folder = Path.Combine(Path.GetTempPath(), "anit-startup-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            using var activated = new ManualResetEventSlim();
            var first = ApplicationInstance.TryAcquire(folder, () => activated.Set()) ?? throw new InvalidOperationException("First instance rejected");
            try
            {
                using var duplicate = Child("--probe-instance", folder);
                Require(duplicate.WaitForExit(6000) && duplicate.ExitCode == 0, "A second process must not acquire the data directory");
                Require(activated.Wait(2000), "Second launch must notify the existing window");
                using var restart = Child("--probe-restart", folder);
                Thread.Sleep(200); Require(!restart.HasExited, "Restart must wait for the old process");
                first.Dispose();
                Require(restart.WaitForExit(6000) && restart.ExitCode == 0, "Restart acquires the lease after the old process releases it");
            }
            finally { first.Dispose(); }
            var store = new FirstRunSetupStore(Path.Combine(folder, "Data")); store.Begin();
            Require(App.Database is null && App.MediaPlayer is null, "No application services initialized before onboarding");
            var clock = Stopwatch.StartNew();
            var window = new OnboardingWindow(store, new ProfileSettings("Meu perfil", "", DateTimeOffset.UtcNow), null) { ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
            bool rendered = false; window.ContentRendered += (_, _) => rendered = true; window.Show();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!rendered && DateTime.UtcNow < deadline) Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Require(rendered, "Onboarding first frame must render");
            Console.WriteLine($"PASS: onboarding first frame in {clock.Elapsed.TotalMilliseconds:0} ms after window construction starts (single-PC diagnostic; excludes process/WPF bootstrap).");
            Require(!File.Exists(Path.Combine(folder, "Data", "anit.db")) && App.Database is null && App.MediaPlayer is null, "Onboarding must render without constructing the database or player");
            window.Close();
            Require(store.NeedsSetup, "Closing before completion keeps setup resumable");
            Console.WriteLine("PASS: cross-process activation, restart handoff, onboarding before database/player and interrupted setup.");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    private static Process Child(string operation, string directory)
    {
        var executable = Environment.ProcessPath!;
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add(operation); start.ArgumentList.Add(directory);
        return Process.Start(start)!;
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

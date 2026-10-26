using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AniT.Presentation.Pilot;

internal static class FluentPilotRunner
{
    public static int Run(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var name in new[] { "Colors", "Typography", "Effects", "Animations", "Buttons", "Cards", "Inputs", "Images" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/AniT;component/Themes/{name}.xaml") });
        var count = app.Resources.MergedDictionaries.Count;
        var legacyStyle = new Button().TryFindResource(typeof(Button));
        try
        {
            if (args.Contains("--check-home-cinematic")) { CinematicHomeChecks.Run(app); return 0; }
            if (args.Contains("--preview-home-light") || args.Contains("--preview-home-populated"))
            {
                AniT.App.AppearanceManager.Initialize(app, AniT.Infrastructure.AniTSystemSettings.Default);
                var preview = HomeVisualChecks.Preview(args.Contains("--preview-home-populated"));
                preview.Loaded += (_,_) => HomeVisualChecks.ApplyPreviewAppearance(preview, AniT.Infrastructure.AniTSystemSettings.Default with { ThemeMode = args.Contains("--preview-home-light") ? AniT.Infrastructure.AppearanceThemeMode.Light : AniT.Infrastructure.AppearanceThemeMode.Dark });
                preview.Title = args.Contains("--preview-home-light") ? "AniT · Home Cinematográfica clara" : "AniT · Home Cinematográfica escura";
                app.Run(preview); return 0;
            }
            if (args.Contains("--check-shell")) { ShellChecks.Run(app); return 0; }
            if (args.Contains("--preview-shell")) { AniT.App.AppearanceManager.Initialize(app, AniT.Infrastructure.AniTSystemSettings.Default); app.Run(ShellChecks.Preview()); return 0; }
            if (args.Contains("--check-design-system")) { DesignSystemChecks.Run(app); return 0; }
            if (args.Contains("--preview-fluent")) { app.Run(new FluentPilotWindow()); return 0; }
            if (args.Contains("--benchmark-fluent")) { Benchmark(); return 0; }
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var checks = 0;
            foreach (var renderer in Enum.GetValues<PilotRenderer>())
            {
                var window = new FluentPilotWindow { ShowActivated = false, ShowInTaskbar = false };
                window.Show(); Pump();
                foreach (var theme in Enum.GetValues<PilotTheme>())
                foreach (var scale in new[] { 80, 100, 125 })
                foreach (var text in new[] { 85, 100, 140 })
                foreach (var radius in new[] { 0, 16, 32 })
                foreach (var reduced in new[] { false, true })
                {
                    window.ApplyOptions(new(theme, scale, text, radius, reduced, renderer));
                    window.UpdateLayout();
                    Require(window.ActualWidth > 0 && window.ActualHeight > 0, "Window layout");
                    var input = (Wpf.Ui.Controls.TextBox)window.FindName("CollectionName");
                    Require(input.ActualHeight >= 42 && input.ActualWidth > 100 && input.Template is not null, "Input template/layout");
                    var panel = (Border)window.FindName("FormPanel");
                    Require(panel.CornerRadius.TopLeft == radius, "Panel radius");
                    var toggle = (Wpf.Ui.Controls.ToggleSwitch)window.FindName("MotionSwitch");
                    if (reduced) Require(ReferenceEquals(toggle.Template, window.FindResource("AniT.StaticToggle")), "Reduced motion template");
                    Require(((HwndSource)PresentationSource.FromVisual(window)).CompositionTarget.RenderMode == (renderer == PilotRenderer.Software ? RenderMode.SoftwareOnly : RenderMode.Default), "Window renderer");
                    checks++;
                }
                foreach (var size in new[] { (580d, 420d), (1280d, 720d), (1920d, 1080d), (3440d, 1440d) })
                foreach (var dpi in new[] { 1d, 1.25, 1.5 })
                {
                    window.Width = Math.Max(window.MinWidth, size.Item1 / dpi);
                    window.Height = Math.Max(window.MinHeight, size.Item2 / dpi);
                    window.ApplyOptions(new(InterfaceScale: 125, TextScale: 140)); window.UpdateLayout();
                    var scroller = (ScrollViewer)window.FindName("PilotScroller");
                    Require(scroller.ViewportWidth > 100 && scroller.ExtentWidth <= scroller.ViewportWidth + 1, "No horizontal overflow");
                }
                window.Draft.Name = "";
                Require(!window.Draft.Validate(), "Empty name rejected"); Pump();
                Require(Validation.GetHasError((DependencyObject)window.FindName("CollectionName")), "WPF validation binding");
                window.Draft.Name = "Minha coleção"; Require(window.Draft.Validate(), "Valid name accepted"); Pump();
                Require(!Validation.GetHasError((DependencyObject)window.FindName("CollectionName")), "Validation clears");
                Require(!((Control)window.FindName("DisabledButton")).IsEnabled, "Disabled action");
                window.Activate(); Pump();
                var opener = (Control)window.FindName("DialogButton"); opener.Focus();
                var task = window.OpenDialogAsync(); Pump();
                Require(window.CurrentDialog is not null, "Dialog opened");
                var primary = (Control)window.CurrentDialog!.Template.FindName("PrimaryButton", window.CurrentDialog);
                Require(primary.IsEnabled && primary.Focusable, "Dialog action accepts keyboard focus");
                Require(!((FrameworkElement)window.FindName("Sidebar")).IsEnabled, "Modal siblings disabled");
                window.CurrentDialog!.Hide();
                var timeout = Stopwatch.StartNew();
                while (!task.IsCompleted && timeout.ElapsedMilliseconds < 5000) Pump();
                Require(task.IsCompletedSuccessfully && window.CurrentDialog is null, $"Dialog completes: status={task.Status}; current={window.CurrentDialog is not null}; error={task.Exception}");
                Require(((FrameworkElement)window.FindName("Sidebar")).IsEnabled, "Modal siblings restored");
                window.Close();
            }
            Require(app.Resources.MergedDictionaries.Count == count, "Application dictionaries unchanged");
            Require(ReferenceEquals(legacyStyle, new Button().TryFindResource(typeof(Button))), "Legacy button style unchanged");
            Require(app.TryFindResource("AniT.Background") is null, "Pilot resources do not leak");
            Console.WriteLine($"PASS: {checks} theme/scale/text/radius/motion/render combinations; 24 logical viewport checks; validation, modal lifecycle and resource isolation.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { app.Shutdown(); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Benchmark()
    {
        foreach (var renderer in Enum.GetValues<PilotRenderer>())
        {
            var start = Stopwatch.StartNew();
            var window = new FluentPilotWindow { ShowActivated = false };
            window.ApplyOptions(new(Renderer: renderer)); window.Show(); Pump();
            var first = start.Elapsed.TotalMilliseconds;
            for (var i = 0; i < 20; i++) { window.Draft.Name = $"Aquecimento {i}"; window.UpdateLayout(); Pump(); }
            var process = Process.GetCurrentProcess(); var cpu = process.TotalProcessorTime;
            var samples = new List<double>();
            for (var i = 0; i < 100; i++)
            {
                var timer = Stopwatch.StartNew(); window.Draft.Name = $"Coleção {i}"; window.UpdateLayout(); Pump(); samples.Add(timer.Elapsed.TotalMilliseconds);
            }
            process.Refresh(); samples.Sort();
            Console.WriteLine($"{renderer}: first-layout-ms={first:F2}; update-median-ms={samples[50]:F2}; update-p95-ms={samples[94]:F2}; cpu-ms={(process.TotalProcessorTime - cpu).TotalMilliseconds:F2}; private-MiB={process.PrivateMemorySize64 / 1048576d:F2}; render-tier={RenderCapability.Tier >> 16}; dpi={VisualTreeHelper.GetDpi(window).DpiScaleX:F2}");
            window.Close(); Pump();
        }
    }
}

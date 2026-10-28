using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AniT.Player;

internal static class TheaterPlayerRuntime
{
    private static readonly SemaphoreSlim gate = new(1, 1);
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".dll", ".ax", ".hlsl", ".manifest", ".svg", ".txt", ".md", ".xml", ".png", ".bmp", ".ico", ".json" };

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle, StringBuilder path, uint length, uint flags);

    // A portable copy keeps view presets, history and global media keys away from the normal player.
    public static async Task<string> PrepareAsync(string sourceExecutable, bool viewer, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var binary = File.OpenRead(sourceExecutable);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(binary, cancellationToken));
            var root = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AniT", "Cache", "MovieTheaterPlayer", hash[..16], viewer ? "Viewer" : "Host"));
            Directory.CreateDirectory(root);
            var sourceRoot = Path.GetDirectoryName(Path.GetFullPath(sourceExecutable))!;
            foreach (var source in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!AllowedExtensions.Contains(Path.GetExtension(source))) continue;
                var destination = Path.GetFullPath(Path.Combine(root, Path.GetRelativePath(sourceRoot, source)));
                if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Caminho de runtime inválido.");
                var original = new FileInfo(source); var cached = new FileInfo(destination);
                if (cached.Exists && cached.Length == original.Length && cached.LastWriteTimeUtc == original.LastWriteTimeUtc) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using (var input = File.OpenRead(source))
                await using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 1024, true))
                    await input.CopyToAsync(output, cancellationToken);
                File.SetLastWriteTimeUtc(destination, original.LastWriteTimeUtc);
            }
            var executable = Path.Combine(root, Path.GetFileName(sourceExecutable));
            await File.WriteAllTextAsync(Path.ChangeExtension(executable, ".ini"), "[Settings]\r\nUseGlobalMedia=0\r\nUseWinLirc=0\r\nEnableWebServer=0\r\nKeepHistory=0\r\nShowOSD=0\r\nLaunchFullScreen=0\r\nUpdaterAutoCheck=0\r\n[Internal Filters\\LAVSplitter]\r\nQueueMaxSize=32\r\nQueueMaxPackets=350\r\nNetworkAnalysisDuration=1000\r\n", Encoding.Unicode, cancellationToken);
            // MSIX file virtualization can redirect LocalAppData while Windows SxS probes the
            // unredirected path. Resolve the actual executable so native codec manifests can load.
            using var opened = File.OpenRead(executable);
            var resolved = new StringBuilder(32768);
            var size = GetFinalPathNameByHandle(opened.SafeFileHandle, resolved, (uint)resolved.Capacity, 0);
            if (size == 0 || size >= resolved.Capacity) throw new IOException("Não foi possível resolver o runtime do player.");
            var physical = resolved.ToString();
            return physical.StartsWith(@"\\?\", StringComparison.Ordinal) ? physical[4..] : physical;
        }
        finally { gate.Release(); }
    }
}

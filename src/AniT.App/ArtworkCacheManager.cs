using AniT.Infrastructure;
using System.IO;
using System.Windows.Media.Imaging;

namespace AniT.App;

internal static class ArtworkCacheManager
{
    private const string CacheMarker = ".anit-image-cache";

    public static void EnsureManagedCache(AniTSystemSettings settings)
    {
        var directory = settings.ImageCacheDirectory;
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, CacheMarker), "AniT managed image cache");
    }

    public static long GetSizeBytes(AniTSystemSettings settings)
    {
        var paths = EnumerateManagedFiles(settings).Distinct(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var path in paths)
        {
            try { total += new FileInfo(path).Length; } catch (IOException) { }
        }
        return total;
    }

    public static Task ClearAsync(AniTSystemSettings settings, bool rebuild, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var locked = (settings.LockedArtwork?.Values ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in EnumerateManagedFiles(settings))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (locked.Contains(path)) continue;
                TryDelete(path);
            }

            if (!rebuild || string.IsNullOrWhiteSpace(settings.ArtworkDirectory) || !Directory.Exists(settings.ArtworkDirectory)) return;
            foreach (var marker in Directory.EnumerateFiles(settings.ArtworkDirectory, "*.unavailable", SearchOption.TopDirectoryOnly)) TryDelete(marker);
            foreach (var shared in Directory.EnumerateFiles(settings.ArtworkDirectory, "banner-*", SearchOption.TopDirectoryOnly))
            {
                if (!locked.Contains(shared)) TryDelete(shared);
            }
        }, cancellationToken);

    public static Task OptimizeAsync(IEnumerable<string?> paths, AniTSystemSettings settings, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            if (settings.ImageMaxDimension <= 0) return;
            var locked = (settings.LockedArtwork?.Values ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path!).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Path.IsPathRooted(path) || locked.Contains(path)) continue;
                OptimizeFile(path, settings.ImageMaxDimension);
            }
            TrimToLimit(settings, locked);
        }, cancellationToken);

    private static IEnumerable<string> EnumerateManagedFiles(AniTSystemSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ImageCacheDirectory)
            && Directory.Exists(settings.ImageCacheDirectory)
            && File.Exists(Path.Combine(settings.ImageCacheDirectory, CacheMarker)))
        {
            foreach (var path in Directory.EnumerateFiles(settings.ImageCacheDirectory, "*", SearchOption.AllDirectories))
            {
                if (!Path.GetFileName(path).Equals(CacheMarker, StringComparison.OrdinalIgnoreCase)) yield return path;
            }
        }

        if (string.IsNullOrWhiteSpace(settings.ArtworkDirectory)) yield break;
        var gallery = Path.Combine(settings.ArtworkDirectory, "Gallery");
        if (!Directory.Exists(gallery)) yield break;
        foreach (var path in Directory.EnumerateFiles(gallery, "*", SearchOption.AllDirectories)) yield return path;
    }

    private static void OptimizeFile(string path, int maxDimension)
    {
        try
        {
            if (!File.Exists(path) || Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase)) return;
            BitmapFrame frame;
            using (var source = File.OpenRead(path))
            {
                var decoder = BitmapDecoder.Create(source, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                frame = decoder.Frames[0];
            }
            var largest = Math.Max(frame.PixelWidth, frame.PixelHeight);
            if (largest <= maxDimension) return;
            var scale = maxDimension / (double)largest;
            var resized = new TransformedBitmap(frame, new System.Windows.Media.ScaleTransform(scale, scale));
            BitmapEncoder encoder = Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)
                ? new PngBitmapEncoder()
                : new JpegBitmapEncoder { QualityLevel = 88 };
            encoder.Frames.Add(BitmapFrame.Create(resized));
            var temporary = path + ".optimized";
            using (var destination = File.Create(temporary)) encoder.Save(destination);
            File.Move(temporary, path, true);
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or System.Runtime.InteropServices.ExternalException)
        {
            System.Diagnostics.Debug.WriteLine($"AniT image optimization skipped for {path}: {exception.Message}");
        }
    }

    private static void TrimToLimit(AniTSystemSettings settings, HashSet<string> locked)
    {
        var maximumBytes = settings.ImageCacheLimitMb * 1024L * 1024L;
        var files = EnumerateManagedFiles(settings)
            .Where(path => !locked.Contains(path))
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists)
            .OrderBy(file => file.LastAccessTimeUtc)
            .ToList();
        var total = files.Sum(file => file.Length) + locked.Where(File.Exists).Sum(path => new FileInfo(path).Length);
        foreach (var file in files)
        {
            if (total <= maximumBytes) break;
            var length = file.Length;
            TryDelete(file.FullName);
            total -= length;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AniT.App;

public sealed class AchievementIconConverter : IValueConverter
{
    private const int ThumbnailWidth = 240;
    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path)) return null;
        if (Cache.TryGetValue(path, out var cached)) return cached;

        try
        {
            var escapedPath = string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
            var resourceUri = new Uri($"pack://application:,,,/{escapedPath}", UriKind.Absolute);
            var resource = Application.GetResourceStream(resourceUri);
            if (resource is null) return null;

            using var stream = resource.Stream;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = ThumbnailWidth;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            Cache[path] = image;
            return image;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

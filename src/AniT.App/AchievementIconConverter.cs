using System.Globalization;
using System.Windows.Data;

namespace AniT.App;

public sealed class AchievementIconConverter : IValueConverter
{
    private const int ThumbnailWidth = 240;

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path)) return null;
        var width = parameter is string text && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : ThumbnailWidth;
        try { return ComfortableImageSource.Load(path, width); }
        catch { return null; }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

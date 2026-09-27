using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AniT.App;

/// <summary>
/// Decodes large artwork close to its on-screen size so WPF does not have to
/// squeeze a multi-megapixel image into a tiny control on every render pass.
/// </summary>
public static class ComfortableImageSource
{
    private static readonly ConcurrentDictionary<(string Path, int Width), ImageSource> Cache = new();
    private static readonly DependencyPropertyDescriptor SourceDescriptor =
        DependencyPropertyDescriptor.FromProperty(Image.SourceProperty, typeof(Image));

    public static readonly DependencyProperty AutoOptimizeProperty = DependencyProperty.RegisterAttached(
        "AutoOptimize",
        typeof(bool),
        typeof(ComfortableImageSource),
        new PropertyMetadata(false, OnAutoOptimizeChanged));

    private static readonly DependencyProperty IsObservingProperty = DependencyProperty.RegisterAttached(
        "IsObserving",
        typeof(bool),
        typeof(ComfortableImageSource),
        new PropertyMetadata(false));

    public static bool GetAutoOptimize(DependencyObject element) => (bool)element.GetValue(AutoOptimizeProperty);
    public static void SetAutoOptimize(DependencyObject element, bool value) => element.SetValue(AutoOptimizeProperty, value);

    public static ImageSource Load(string path, int decodePixelWidth)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var width = Math.Clamp(decodePixelWidth, 32, 2048);
        return Cache.GetOrAdd((path, width), static key => Create(key.Path, key.Width));
    }

    private static ImageSource Create(string path, int decodePixelWidth)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        image.DecodePixelWidth = decodePixelWidth;
        Stream? resourceStream = null;

        try
        {
            if (System.IO.Path.IsPathRooted(path))
            {
                image.UriSource = new Uri(path, UriKind.Absolute);
            }
            else if (Uri.TryCreate(path, UriKind.Absolute, out var absoluteUri))
            {
                image.UriSource = absoluteUri;
            }
            else
            {
                var normalizedPath = path.Replace('\\', '/').TrimStart('/');
                var escapedPath = string.Join('/', normalizedPath.Split('/').Select(Uri.EscapeDataString));
                var resourceUri = new Uri($"pack://application:,,,/{escapedPath}", UriKind.Absolute);
                var resource = Application.GetResourceStream(resourceUri)
                    ?? throw new InvalidOperationException($"Recurso de imagem não encontrado: {path}");
                resourceStream = resource.Stream;
                image.StreamSource = resourceStream;
            }

            image.EndInit();
        }
        finally
        {
            resourceStream?.Dispose();
        }
        image.Freeze();
        return image;
    }

    private static void OnAutoOptimizeChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not Image image) return;

        if ((bool)args.NewValue)
        {
            image.Loaded += Image_Loaded;
            image.Unloaded += Image_Unloaded;
            if (image.IsLoaded) Attach(image);
        }
        else
        {
            image.Loaded -= Image_Loaded;
            image.Unloaded -= Image_Unloaded;
            Detach(image);
        }
    }

    private static void Image_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Image image) return;
        Attach(image);
        QueueOptimization(image);
    }

    private static void Image_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is Image image) Detach(image);
    }

    private static void Attach(Image image)
    {
        if ((bool)image.GetValue(IsObservingProperty)) return;
        SourceDescriptor.AddValueChanged(image, ImageSourceChanged);
        image.SetValue(IsObservingProperty, true);
    }

    private static void Detach(Image image)
    {
        if (!(bool)image.GetValue(IsObservingProperty)) return;
        SourceDescriptor.RemoveValueChanged(image, ImageSourceChanged);
        image.SetValue(IsObservingProperty, false);
    }

    private static void ImageSourceChanged(object? sender, EventArgs e)
    {
        if (sender is Image image && image.IsLoaded) QueueOptimization(image);
    }

    private static void QueueOptimization(Image image) =>
        image.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => OptimizeForDisplay(image));

    private static void OptimizeForDisplay(Image image)
    {
        if (image.Source is not BitmapSource bitmap || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0) return;

        var displayWidth = image.ActualWidth > 0 ? image.ActualWidth : image.Width;
        var displayHeight = image.ActualHeight > 0 ? image.ActualHeight : image.Height;
        if (!double.IsFinite(displayWidth) || !double.IsFinite(displayHeight) || displayWidth <= 0 || displayHeight <= 0) return;

        var dpi = VisualTreeHelper.GetDpi(image);
        var widthRatio = displayWidth * dpi.DpiScaleX * 1.5 / bitmap.PixelWidth;
        var heightRatio = displayHeight * dpi.DpiScaleY * 1.5 / bitmap.PixelHeight;
        var scale = Math.Max(widthRatio, heightRatio);

        // Large banners/projectors keep their source quality; tiny cards get a
        // stable intermediate bitmap instead of being resampled every frame.
        if (scale >= 0.82 || Math.Max(displayWidth, displayHeight) > 520) return;
        scale = Math.Clamp(scale, 0.04, 0.82);

        var optimized = new TransformedBitmap(bitmap, new ScaleTransform(scale, scale));
        optimized.Freeze();
        image.SetCurrentValue(Image.SourceProperty, optimized);
    }
}

[MarkupExtensionReturnType(typeof(ImageSource))]
public sealed class ComfortableImageExtension : MarkupExtension
{
    public string Path { get; set; } = string.Empty;
    public int DecodePixelWidth { get; set; } = 256;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        ComfortableImageSource.Load(Path, DecodePixelWidth);
}

public sealed class ComfortableImageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ImageSource source) return source;
        if (value is not string path || string.IsNullOrWhiteSpace(path)) return DependencyProperty.UnsetValue;

        var width = parameter switch
        {
            int number => number,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 256
        };

        try { return ComfortableImageSource.Load(path, width); }
        catch { return DependencyProperty.UnsetValue; }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

using AniT.Infrastructure;
using Microsoft.Win32;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace AniT.App;

internal static class AppearanceManager
{
    private sealed class OriginalText(double fontSize) { public double FontSize { get; } = fontSize; }
    private sealed class OriginalBorder(CornerRadius radius) { public CornerRadius Radius { get; } = radius; }
    private sealed class ForegroundState
    {
        public Dictionary<DependencyProperty, Brush?> Originals { get; } = [];
        public HashSet<DependencyProperty> Adjusted { get; } = [];
    }
    private static readonly ConditionalWeakTable<TextBlock, OriginalText> TextSizes = new();
    private static readonly ConditionalWeakTable<Border, OriginalBorder> BorderRadii = new();
    private static readonly ConditionalWeakTable<DependencyObject, ForegroundState> Foregrounds = new();
    private static readonly ConditionalWeakTable<Window, object> RegisteredWindows = new();
    private static AniTSystemSettings current = AniTSystemSettings.Default;
    private static bool initialized;

    public static void Initialize(Application application)
    {
        if (!initialized)
        {
            initialized = true;
            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler((sender, eventArgs) =>
                {
                    if (sender is not Window window) return;
                    if (!RegisteredWindows.TryGetValue(window, out var registration))
                    {
                        RegisteredWindows.Add(window, new object());
                        window.Activated += (_, _) => ApplyToWindow(window);
                        window.ContentRendered += (_, _) => ApplyToWindow(window);
                    }
                    ApplyToWindow(window);
                }));
            EventManager.RegisterClassHandler(
                typeof(TextBlock),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler((sender, eventArgs) =>
                {
                    if (sender is TextBlock text) ApplyTextAppearance(text, current);
                }));
            EventManager.RegisterClassHandler(
                typeof(Image),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler((sender, eventArgs) =>
                {
                    if (sender is Image image) ApplyImageAppearance(image, current);
                }));
            application.Activated += (_, _) =>
            {
                foreach (Window window in application.Windows) ApplyToWindow(window);
            };
        }
        Apply(AniTSystemSettingsStore.Load());
    }

    public static void Apply(AniTSystemSettings settings)
    {
        current = settings;
        ApplyResources(Application.Current.Resources, settings);
        foreach (Window window in Application.Current.Windows) ApplyToWindow(window);
    }

    public static void ApplyToWindow(Window window)
    {
        ApplyResources(window.Resources, current);
        var light = IsLight(current.ThemeMode);
        window.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#D8E8F5" : "#020B1D"));
        if (window.Content is FrameworkElement root)
        {
            root.LayoutTransform = new ScaleTransform(current.InterfaceScalePercent / 100d, current.InterfaceScalePercent / 100d);
            if (root is Grid grid)
            {
                grid.Background = new RadialGradientBrush(
                    (Color)ColorConverter.ConvertFromString(light ? "#F6FBFF" : "#0A356D"),
                    (Color)ColorConverter.ConvertFromString(light ? "#C9DCEB" : "#020A1A"));
            }
            ApplyElementAppearance(root, current);
        }
    }

    private static void ApplyElementAppearance(DependencyObject element, AniTSystemSettings settings)
    {
        if (element is TextBlock text)
        {
            ApplyTextAppearance(text, settings);
        }

        if (element is Image image) ApplyImageAppearance(image, settings);

        if (element is Border border && border.CornerRadius != default)
        {
            var original = BorderRadii.GetValue(border, item => new OriginalBorder(item.CornerRadius));
            var ratio = settings.CornerRadius / 16d;
            border.CornerRadius = new CornerRadius(
                original.Radius.TopLeft * ratio,
                original.Radius.TopRight * ratio,
                original.Radius.BottomRight * ratio,
                original.Radius.BottomLeft * ratio);
            if (border.Clip is RectangleGeometry clip)
                clip.RadiusX = clip.RadiusY = Math.Max(0, settings.CornerRadius);
        }

        var children = VisualTreeHelper.GetChildrenCount(element);
        for (var index = 0; index < children; index++) ApplyElementAppearance(VisualTreeHelper.GetChild(element, index), settings);
    }

    private static void ApplyImageAppearance(Image image, AniTSystemSettings settings)
    {
        var source = image.Source?.ToString();
        if (string.IsNullOrWhiteSpace(source) || source.Contains("/Assets/", StringComparison.OrdinalIgnoreCase) || source.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) return;
        image.Stretch = settings.ArtworkStretchMode == ArtworkStretchMode.Uniform ? Stretch.Uniform : Stretch.UniformToFill;
        image.RenderTransform = Transform.Identity;
        image.RenderTransformOrigin = new Point(0.5, 0.5);
        if (settings.ArtworkStretchMode == ArtworkStretchMode.Manual)
        {
            image.RenderTransformOrigin = new Point(settings.ArtworkFocusXPercent / 100d, settings.ArtworkFocusYPercent / 100d);
            image.RenderTransform = new ScaleTransform(1.08, 1.08);
        }
        RenderOptions.SetBitmapScalingMode(image, settings.ImageMaxDimension switch
        {
            <= 1280 => BitmapScalingMode.LowQuality,
            <= 1920 => BitmapScalingMode.Linear,
            _ => BitmapScalingMode.HighQuality
        });
    }

    private static void ApplyTextAppearance(TextBlock text, AniTSystemSettings settings)
    {
        var original = TextSizes.GetValue(text, item => new OriginalText(item.FontSize));
        text.FontSize = original.FontSize * settings.TextScalePercent / 100d;
        if (Equals(text.Tag, "ThemeManaged")) return;
        ApplyReadableForeground(text, TextBlock.ForegroundProperty);
    }

    private static void ApplyReadableForeground(DependencyObject element, DependencyProperty property)
    {
        var state = Foregrounds.GetOrCreateValue(element);
        if (!state.Originals.TryGetValue(property, out var original))
        {
            original = CloneBrush(element.GetValue(property) as Brush);
            state.Originals[property] = original;
        }

        if (state.Adjusted.Remove(property) && original is not null)
            element.SetCurrentValue(property, CloneBrush(original));

        var source = element.GetValue(property) as Brush;
        if (!TryGetColor(source, out var foreground) || !TryFindBackgroundColor(element, out var background)) return;
        if (ContrastRatio(foreground, background) >= 4.5) return;

        var replacement = RelativeLuminance(background) < 0.43
            ? Color.FromRgb(247, 250, 255)
            : RelativeLuminance(foreground) > 0.72
                ? Color.FromRgb(19, 42, 66)
                : Color.FromRgb(54, 82, 107);
        element.SetCurrentValue(property, new SolidColorBrush(replacement));
        state.Adjusted.Add(property);
    }

    private static Brush? CloneBrush(Brush? brush) => brush?.CloneCurrentValue();

    private static bool TryFindBackgroundColor(DependencyObject element, out Color color)
    {
        for (DependencyObject? currentElement = element; currentElement is not null; currentElement = GetParent(currentElement))
        {
            var brush = currentElement switch
            {
                Border border => border.Background,
                Panel panel => panel.Background,
                Window window => window.Background,
                Control control => control.Background,
                _ => null
            };
            if (TryGetColor(brush, out color) && color.A >= 96) return true;
        }
        color = Colors.Transparent;
        return false;
    }

    private static DependencyObject? GetParent(DependencyObject element)
    {
        if (element is FrameworkElement frameworkElement && frameworkElement.Parent is not null)
            return frameworkElement.Parent;
        if (element is FrameworkContentElement contentElement && contentElement.Parent is not null)
            return contentElement.Parent;
        return element is Visual || element is System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(element)
            : null;
    }

    private static bool TryGetColor(Brush? brush, out Color color)
    {
        switch (brush)
        {
            case SolidColorBrush solid when solid.Color.A > 0 && solid.Opacity > 0:
                color = solid.Color;
                return true;
            case GradientBrush gradient when gradient.GradientStops.Count > 0:
                var first = gradient.GradientStops.OrderBy(stop => stop.Offset).First().Color;
                var last = gradient.GradientStops.OrderBy(stop => stop.Offset).Last().Color;
                color = Color.FromArgb(
                    (byte)((first.A + last.A) / 2),
                    (byte)((first.R + last.R) / 2),
                    (byte)((first.G + last.G) / 2),
                    (byte)((first.B + last.B) / 2));
                return color.A > 0 && gradient.Opacity > 0;
            default:
                color = Colors.Transparent;
                return false;
        }
    }

    private static double ContrastRatio(Color first, Color second)
    {
        var light = Math.Max(RelativeLuminance(first), RelativeLuminance(second));
        var dark = Math.Min(RelativeLuminance(first), RelativeLuminance(second));
        return (light + 0.05) / (dark + 0.05);
    }

    private static double RelativeLuminance(Color color)
    {
        static double Channel(byte value)
        {
            var component = value / 255d;
            return component <= 0.04045 ? component / 12.92 : Math.Pow((component + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    private static void ApplyResources(ResourceDictionary resources, AniTSystemSettings settings)
    {
        foreach (var merged in resources.MergedDictionaries) ApplyResources(merged, settings);
        var light = IsLight(settings.ThemeMode);
        SetBrush(resources, "HomePageBrush", light ? "#EAF3F9" : "#020B1D");
        SetBrush(resources, "HomeSidebarBrush", light ? "#F7FBFE" : "#E6051835");
        SetBrush(resources, "HomePanelBrush", light ? "#F8FBFE" : "#E9081D3C");
        SetBrush(resources, "HomePanelRaisedBrush", light ? "#EDF4F9" : "#F20B2348");
        SetBrush(resources, "HomeBorderBrush", light ? "#94B5CC" : "#245B91");
        SetBrush(resources, "HomeBorderStrongBrush", settings.AccentColor);
        SetBrush(resources, "HomeTextBrush", light ? "#132A42" : "#F7FAFF");
        SetBrush(resources, "HomeMutedBrush", light ? "#4F6D85" : "#91AED2");
        SetBrush(resources, "HomeAccentSoftBrush", light ? "#D8EDF7" : "#153E6B");
        SetBrush(resources, "HomeHoverBrush", light ? "#DCEAF4" : "#153E6D");
        SetBrush(resources, "HomeHoverTextBrush", light ? "#132A42" : "#FFFFFF");
        SetBrush(resources, "HomeAccentBrush", settings.AccentColor);
        SetBrush(resources, "AniT.Brush.Background", light ? "#EAF3F9" : "#020B1D");
        SetBrush(resources, "AniT.Brush.Surface", light ? "#F8FBFE" : "#071B38");
        SetBrush(resources, "AniT.Brush.SurfaceAlt", light ? "#EDF4F9" : "#0B2348");
        SetBrush(resources, "AniT.Brush.SurfaceHover", light ? "#DCEAF4" : "#153E6B");
        SetBrush(resources, "AniT.Brush.SurfaceSelected", light ? "#CAE7F5" : "#1E5C9F");
        SetBrush(resources, "AniT.Brush.TextPrimary", light ? "#132A42" : "#F7FAFF");
        SetBrush(resources, "AniT.Brush.TextSecondary", light ? "#2E4A63" : "#C8DDF3");
        SetBrush(resources, "AniT.Brush.TextMuted", light ? "#55728A" : "#91AED2");
        SetBrush(resources, "AniT.Brush.Border", light ? "#94B5CC" : "#245B91");
        SetBrush(resources, "AniT.Brush.AccentCyan", settings.AccentColor);
        SetBrush(resources, "AniT.Brush.AccentBlue", settings.AccentColor);
        SetBrush(resources, "AniT.Brush.BorderStrong", settings.AccentColor);
        SetBrush(resources, "BrandBrush", settings.AccentColor);

        var densityWidth = settings.CardSize switch
        {
            AppearanceCardSize.Compact => 156d,
            AppearanceCardSize.Large => 220d,
            _ => 176d
        };
        resources["AniT.Appearance.CardWidth"] = densityWidth;
        resources["AniT.Appearance.CompactCardWidth"] = settings.CardSize switch
        {
            AppearanceCardSize.Compact => 136d,
            AppearanceCardSize.Large => 180d,
            _ => 154d
        };
        resources["AniT.Appearance.EpisodeCardWidth"] = settings.CardSize switch
        {
            AppearanceCardSize.Compact => 252d,
            AppearanceCardSize.Large => 326d,
            _ => 286d
        };
        resources["AniT.Appearance.CornerRadius"] = new CornerRadius(settings.CornerRadius);
        resources["AniT.Appearance.CardsPerRow"] = settings.CardsPerRow;

        var motion = settings.ReduceMotion ? 0.01 : Math.Max(0.05, settings.AnimationIntensity / 100d);
        resources["AniT.Motion.Fast"] = new Duration(TimeSpan.FromSeconds(0.14 * motion));
        resources["AniT.Motion.Normal"] = new Duration(TimeSpan.FromSeconds(0.20 * motion));
        resources["AniT.Motion.Slow"] = new Duration(TimeSpan.FromSeconds(0.28 * motion));
        if (resources.Contains("AniT.Effect.AccentGlow") && resources["AniT.Effect.AccentGlow"] is DropShadowEffect glow)
        {
            var editable = glow.IsFrozen ? glow.Clone() : glow;
            editable.Color = (Color)ColorConverter.ConvertFromString(settings.AccentColor);
            editable.Opacity = settings.GlowIntensity / 100d * 0.62;
            editable.BlurRadius = 6 + settings.GlowIntensity * 0.22;
            resources["AniT.Effect.AccentGlow"] = editable;
        }
    }

    private static void SetBrush(ResourceDictionary resources, string key, string colorValue)
    {
        if (!resources.Contains(key)) return;
        var color = (Color)ColorConverter.ConvertFromString(colorValue);
        if (resources[key] is SolidColorBrush brush && !brush.IsFrozen) brush.Color = color;
        else resources[key] = new SolidColorBrush(color);
    }

    private static bool IsLight(AppearanceThemeMode mode)
    {
        if (mode == AppearanceThemeMode.Light) return true;
        if (mode == AppearanceThemeMode.Dark) return false;
        try
        {
            return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0) is int value && value != 0;
        }
        catch { return false; }
    }
}

[MarkupExtensionReturnType(typeof(ImageSource))]
public sealed class PageMascotExtension : MarkupExtension
{
    public string Page { get; set; } = string.Empty;
    public string Fallback { get; set; } = "Assets/ghost_menu.png";

    public override object? ProvideValue(IServiceProvider serviceProvider)
    {
        var settings = AniTSystemSettingsStore.Load();
        var selected = settings.PageMascots is not null && settings.PageMascots.TryGetValue(Page, out var value)
            ? value
            : "default";
        var path = selected.ToLowerInvariant() switch
        {
            "normal" => "Assets/normal-sf.png",
            "happy" => "Assets/happy-sf.png",
            "happy2" => "Assets/happy2-sf.png",
            "angry" => "Assets/angry-sf.png",
            "sad" => "Assets/sad-sf.png",
            "boring" => "Assets/boring-sf.png",
            "hidden" => null,
            _ => Fallback
        };
        return path is null ? null : new BitmapImage(new Uri($"pack://application:,,,/{path}", UriKind.Absolute));
    }
}

using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using AniT.Infrastructure;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace AniT.App;

public partial class OnboardingWindow
{
    private AniTSystemSettings? appliedAppearance;
    private bool appliedLight;

    // Fluent resources belong to this window. Never change application-wide WPF UI themes.
    internal void ApplyFluentAppearance(AniTSystemSettings settings)
    {
        var light = settings.ThemeMode == AppearanceThemeMode.Light;
        if (settings.ThemeMode == AppearanceThemeMode.Automatic)
        {
            try { light = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0) is int value && value != 0; }
            catch { light = false; }
        }
        if (appliedAppearance == settings && appliedLight == light) return;
        appliedAppearance = settings;
        appliedLight = light;
        Resources.MergedDictionaries.OfType<ThemesDictionary>().First().Theme = light ? ApplicationTheme.Light : ApplicationTheme.Dark;
        string[] roles = { "Background", "Surface", "SurfaceAlt", "SurfaceSelected", "TextPrimary", "TextSecondary", "TextMuted", "Border", "Danger", "AccentCyan" };
        string[] colors = light
            ? new[] { "#F5F7FA", "#FFFFFF", "#EEF2F7", "#E4EDF9", "#182333", "#4E6075", "#536477", "#748398", "#AC2034", "#165FA8" }
            : new[] { "#10151D", "#19212D", "#141B25", "#253D5A", "#EDF2F8", "#BAC8DB", "#A7B5C7", "#62738B", "#FF9AAA", "#86BEFF" };
        for (var i = 0; i < roles.Length; i++) Resources["AniT.Brush." + roles[i]] = Brush(colors[i]);
        Resources["Onboarding.Sidebar"] = Resources["AniT.Brush.SurfaceAlt"];
        foreach (var size in new[] { 10, 11, 12, 13, 14, 15, 16, 17, 22, 24, 32 })
            Resources["Onboarding.Font" + size] = size * settings.TextScalePercent / 100d;
        Resources["ControlContentThemeFontSize"] = Resources["Onboarding.Font14"];
        Resources["Onboarding.CornerRadius"] = new CornerRadius(Math.Clamp(settings.CornerRadius, 0, 32));
        Resources["ControlCornerRadius"] = new CornerRadius(Math.Clamp(settings.CornerRadius / 2d, 0, 16));
        Color accent;
        try { accent = (Color)ColorConverter.ConvertFromString(settings.AccentColor); }
        catch (FormatException) { accent = Color.FromRgb(47, 167, 255); }
        accent.A = 255;
        var onAccent = Contrast(accent, Colors.Black) >= Contrast(accent, Colors.White) ? Colors.Black : Colors.White;
        var accentBrush = new SolidColorBrush(accent);
        Resources["AniT.Brush.AccentBlue"] = accentBrush;
        foreach (var key in new[] { "AccentButtonBackground", "AccentButtonBackgroundPointerOver", "AccentButtonBackgroundPressed" }) Resources[key] = accentBrush;
        foreach (var key in new[] { "AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed" }) Resources[key] = new SolidColorBrush(onAccent);
        Background = (System.Windows.Media.Brush)Resources["AniT.Brush.Background"];
        if (Content is FrameworkElement root) root.LayoutTransform = new ScaleTransform(settings.InterfaceScalePercent / 100d, settings.InterfaceScalePercent / 100d);
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    private static double Contrast(Color a, Color b)
    {
        static double Linear(byte value) { var v = value / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
        static double Luminance(Color c) => .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
        var x = Luminance(a); var y = Luminance(b);
        return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
}

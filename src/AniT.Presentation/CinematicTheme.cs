using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace AniT.Presentation;

/// <summary>One instance per window. Never installs global WPF UI resources.</summary>
public sealed class CinematicTheme
{
    private readonly Window window;
    private readonly ThemesDictionary theme = new();
    private readonly ResourceDictionary values = new();
    private bool applied;
    private bool lastDark;
    private object? lastRoot;
    public AppearanceOptions Options { get; private set; } = new();
    public CinematicTheme(Window window)
    {
        this.window = window;
        AppearanceScope.SetIsManaged(window, true);
        window.Resources.MergedDictionaries.Add(theme);
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/AniT.Presentation;component/Themes/Components.xaml", UriKind.Relative) });
        window.Resources.MergedDictionaries.Add(values);
        Apply(new());
    }

    public void Apply(AppearanceOptions options)
    {
        var normalized = options with { InterfaceScale = Math.Clamp(options.InterfaceScale, 80, 150), TextScale = Math.Clamp(options.TextScale, 85, 150), Radius = Math.Clamp(options.Radius, 0, 32) };
        var dark = options.Theme == VisualTheme.Dark || options.Theme == VisualTheme.System && ApplicationThemeManager.GetSystemTheme().ToString() is "Dark" or "CapturedMotion" or "Glow";
        if (applied && Options == normalized && lastDark == dark && ReferenceEquals(lastRoot, window.Content)) return;
        Options = normalized;
        lastDark = dark; lastRoot = window.Content; applied = true;
        theme.Theme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        string[] roles = ["Background", "Sidebar", "Surface", "Raised", "Text", "Muted", "Border", "Error", "Success", "Warning"];
        string[] colors = dark
            ? ["#0C1017", "#10151E", "#171E29", "#1E2735", "#EDF2F8", "#A7B5C7", "#62738B", "#FF9AAA", "#83DDB4", "#F0CB83"]
            : ["#F5F7FA", "#FFFFFF", "#FFFFFF", "#EEF2F7", "#172334", "#536477", "#748398", "#A92336", "#126342", "#755004"];
        for (var i = 0; i < roles.Length; i++) Set(roles[i], Parse(colors[i]));
        Color accent;
        try { accent = Parse(options.Accent) with { A = 255 }; }
        catch (FormatException) { accent = Parse("#77B5FF"); }
        Set("Accent", accent);
        var onAccent = Contrast(accent, Colors.Black) >= Contrast(accent, Colors.White) ? Colors.Black : Colors.White;
        Set("OnAccent", onAccent);
        Set("AccentHover", Blend(accent, onAccent == Colors.Black ? Colors.White : Colors.Black, .08));
        Set("AccentPressed", Blend(accent, onAccent == Colors.Black ? Colors.White : Colors.Black, .16));
        foreach (var key in new[] { "AccentButtonBackground", "AccentControlElevationBorderBrush" }) values[key] = values["AniT.Accent"];
        values["AccentButtonBackgroundPointerOver"] = values["AniT.AccentHover"];
        values["AccentButtonBackgroundPressed"] = values["AniT.AccentPressed"];
        foreach (var key in new[] { "AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed" }) values[key] = values["AniT.OnAccent"];
        foreach (var key in new[] { "ButtonForeground", "ButtonForegroundPointerOver", "ButtonForegroundPressed", "TextControlForeground", "ComboBoxForeground", "ComboBoxDropDownGlyphForeground" }) values[key] = values["AniT.Text"];
        foreach (var key in new[] { "ButtonBackground", "TextControlBackground", "ComboBoxBackground", "ComboBoxDropDownBackground" }) values[key] = values["AniT.Surface"];
        foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "ComboBoxBackgroundPointerOver", "ComboBoxBackgroundFocused" }) values[key] = values["AniT.Raised"];
        foreach (var key in new[] { "ControlElevationBorderBrush", "TextControlElevationBorderBrush", "ControlStrokeColorDefaultBrush", "ComboBoxDropDownBorderBrush" }) values[key] = values["AniT.Border"];
        foreach (var key in new[] { "TextControlFocusedBorderBrush", "ComboBoxBorderBrushFocused" }) values[key] = values["AniT.Text"];
        values["TextControlPlaceholderForeground"] = values["AniT.Muted"];
        values["AniT.Radius"] = new CornerRadius(Options.Radius);
        values["AniT.ControlRadius"] = new CornerRadius(Options.Radius / 2d);
        values["ControlCornerRadius"] = values["AniT.ControlRadius"];
        values["OverlayCornerRadius"] = values["AniT.Radius"];
        values["AniT.ReduceMotion"] = Options.ReduceMotion;
        AppearanceScope.SetReduceMotion(window, Options.ReduceMotion);
        values["AniT.MotionDuration"] = new Duration(TimeSpan.FromMilliseconds(Options.ReduceMotion ? 0 : 160));
        foreach (var (role, size) in new[] { ("Title", 28d), ("Section", 19d), ("Body", 14d), ("Caption", 12d) })
            values[$"AniT.{role}Size"] = size * Options.TextScale / 100;
        values["ControlContentThemeFontSize"] = 14d * Options.TextScale / 100;
        values["ContentDialogTitleFontSize"] = 19d * Options.TextScale / 100;
        if (window.Content is FrameworkElement root)
            root.LayoutTransform = new ScaleTransform(Options.InterfaceScale / 100d, Options.InterfaceScale / 100d);
    }

    private void Set(string role, Color color) => values[$"AniT.{role}"] = new SolidColorBrush(color);
    private static Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value);
    private static Color Blend(Color a, Color b, double ratio) => Color.FromRgb((byte)(a.R + (b.R - a.R) * ratio), (byte)(a.G + (b.G - a.G) * ratio), (byte)(a.B + (b.B - a.B) * ratio));
    public static double Contrast(Color a, Color b)
    {
        static double L(Color c)
        {
            static double Linear(byte channel) { var v = channel / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
            return .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
        }
        var x = L(a); var y = L(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
}

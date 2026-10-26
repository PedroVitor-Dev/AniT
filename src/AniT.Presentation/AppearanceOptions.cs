namespace AniT.Presentation;

public enum VisualTheme { Dark, Light, System }

/// <summary>UI-only contract; no persistence or application services.</summary>
public sealed record AppearanceOptions(
    VisualTheme Theme = VisualTheme.Dark,
    string Accent = "#77B5FF",
    int InterfaceScale = 100,
    int TextScale = 100,
    int Radius = 16,
    bool ReduceMotion = false);

public interface IAppearanceHost
{
    void ApplyAppearance(AppearanceOptions options);
}

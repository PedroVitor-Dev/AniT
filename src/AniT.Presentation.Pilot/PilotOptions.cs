namespace AniT.Presentation.Pilot;

public enum PilotTheme { Dark, Light, System }
public enum PilotRenderer { Default, Software }

public sealed record PilotOptions(
    PilotTheme Theme = PilotTheme.Dark,
    int InterfaceScale = 100,
    int TextScale = 100,
    int Radius = 16,
    bool ReduceMotion = false,
    PilotRenderer Renderer = PilotRenderer.Default);

using System.Text.Json;
using System.IO;

namespace AniT.App;

public enum AchievementToastPosition
{
    BottomCenter,
    BottomRight,
    TopRight
}

public sealed record AchievementSettings(
    bool ShowNotifications = true,
    bool PlaySound = true,
    double Volume = 0.72,
    AchievementToastPosition Position = AchievementToastPosition.BottomCenter,
    bool ReduceAnimations = false);

internal static class AchievementSettingsStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AniT", "Data", "achievement-settings.json");

    public static AchievementSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AchievementSettings>(File.ReadAllText(FilePath)) ?? new AchievementSettings();
        }
        catch { }
        return new AchievementSettings();
    }

    public static void Save(AchievementSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}

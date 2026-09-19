using System.Text.Json;

namespace AniT.Infrastructure;

public sealed record CustomImageSourceSettings(
    Guid Id,
    string Name,
    string SearchUrlTemplate,
    bool UseForProfileBanners,
    bool UseForAnimeCovers,
    bool IsEnabled = true);

public sealed record AniTSystemSettings(
    int HomeBannerIntervalSeconds,
    IReadOnlyList<CustomImageSourceSettings> ImageSources)
{
    public static AniTSystemSettings Default { get; } = new(8, []);
}

public static class AniTSystemSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly object Sync = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AniT",
        "Data",
        "system-settings.json");

    public static AniTSystemSettings Load()
    {
        lock (Sync)
        {
            try
            {
                if (!File.Exists(FilePath)) return AniTSystemSettings.Default;
                var settings = JsonSerializer.Deserialize<AniTSystemSettings>(File.ReadAllText(FilePath), JsonOptions);
                return Normalize(settings);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                return AniTSystemSettings.Default;
            }
        }
    }

    public static void Save(AniTSystemSettings settings)
    {
        var normalized = Normalize(settings);
        lock (Sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temporaryPath = FilePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(normalized, JsonOptions));
            File.Move(temporaryPath, FilePath, true);
        }
    }

    public static bool TryValidateSource(CustomImageSourceSettings source, out string? error)
    {
        if (string.IsNullOrWhiteSpace(source.Name))
        {
            error = "Informe um nome para a fonte.";
            return false;
        }

        var exampleUrl = source.SearchUrlTemplate.Replace("{query}", "anime", StringComparison.OrdinalIgnoreCase);
        if (!Uri.TryCreate(exampleUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            error = "Use uma URL HTTPS. Você pode inserir {query} no local do termo de busca.";
            return false;
        }

        if (!source.UseForProfileBanners && !source.UseForAnimeCovers)
        {
            error = "Escolha pelo menos um uso: planos de fundo ou capas de anime.";
            return false;
        }

        error = null;
        return true;
    }

    private static AniTSystemSettings Normalize(AniTSystemSettings? settings)
    {
        if (settings is null) return AniTSystemSettings.Default;
        var interval = Math.Clamp(settings.HomeBannerIntervalSeconds, 3, 30);
        var sources = (settings.ImageSources ?? [])
            .Where(source => TryValidateSource(source, out _))
            .GroupBy(source => source.Id)
            .Select(group => group.First() with
            {
                Name = group.First().Name.Trim(),
                SearchUrlTemplate = group.First().SearchUrlTemplate.Trim()
            })
            .Take(20)
            .ToArray();
        return new AniTSystemSettings(interval, sources);
    }
}

using System.Text.Json;

namespace AniT.Infrastructure;

public sealed record CustomImageSourceSettings(
    Guid Id,
    string Name,
    string SearchUrlTemplate,
    bool UseForProfileBanners,
    bool UseForAnimeCovers,
    bool IsEnabled = true,
    bool IsSfw = true);

public enum LibraryDuplicateHandling
{
    KeepAndMark,
    IgnoreIdentical,
    SendToReview
}

public enum AppearanceThemeMode { Dark, Light, Automatic }
public enum AppearanceCardSize { Compact, Comfortable, Large }
public enum HomeContinueBehavior { ResumePlayback, OpenAnimeDetails }
public enum HomeContentPriority { RecentFirst, FavoritesFirst }
public enum ArtworkSourcePriority { AniListFirst, OfficialFirst, CustomFirst }
public enum ArtworkStretchMode { Uniform, UniformToFill, Manual }
public enum PlaybackPlayerMode { IntegratedMpcHc, SystemDefault }
public enum EpisodeNumberDisplayFormat { NumberOnly, EpisodePrefix, SeasonEpisode }
public enum AnimeTitlePreference { Portuguese, English, Romaji, Japanese }
public enum LibraryDefaultSort { RecentlyAdded, Title, ReleaseYearNewest, ReleaseYearOldest, Score, Progress, Favorites }
public enum NotificationDeliveryMode { InAppOnly, WindowsAndInApp }
public enum AniTNotificationKind { NewEpisodes, MetadataUpdated, AchievementUnlocked, WeeklySummary }
public enum AutomaticBackupFrequency { EveryStartup, Daily, Weekly, Monthly }
public enum PersonalDataExportFormat { Json, Csv }

public sealed record AniTSystemSettings(
    int HomeBannerIntervalSeconds,
    IReadOnlyList<CustomImageSourceSettings> ImageSources,
    bool ScanLibraryOnStartup = true,
    bool RefreshMetadataOnStartup = true,
    bool EnableNavigationLoading = true,
    int NavigationLoadingMilliseconds = 620,
    int LibraryBackgroundScanMinutes = 15,
    IReadOnlyList<string>? VideoExtensions = null,
    bool RecognizeSeasonEpisodeCodes = true,
    bool RecognizeEpisodePrefixes = true,
    bool RecognizeDashNumbers = true,
    bool RecognizeBracketNumbers = true,
    bool RecognizeTrailingNumbers = true,
    bool UseFolderSeason = true,
    bool RecognizeSpecials = true,
    string? ArtworkDirectory = null,
    string? ImageCacheDirectory = null,
    LibraryDuplicateHandling DuplicateHandling = LibraryDuplicateHandling.KeepAndMark,
    IReadOnlyList<string>? IgnoredFolders = null,
    IReadOnlyList<string>? IgnoredFiles = null,
    IReadOnlyList<string>? IgnoredWords = null,
    AppearanceThemeMode ThemeMode = AppearanceThemeMode.Dark,
    string AccentColor = "#42C8FF",
    int GlowIntensity = 70,
    AppearanceCardSize CardSize = AppearanceCardSize.Comfortable,
    int CardsPerRow = 0,
    int CornerRadius = 16,
    int AnimationIntensity = 70,
    bool ReduceMotion = false,
    int InterfaceScalePercent = 100,
    int TextScalePercent = 100,
    IReadOnlyDictionary<string, string>? PageMascots = null,
    bool HomeBannerAutoRotate = true,
    IReadOnlyList<string>? HomeSectionOrder = null,
    IReadOnlyList<string>? HiddenHomeSections = null,
    int HomeContinueItems = 4,
    int HomeRecentItems = 5,
    int HomeFavoriteItems = 4,
    int HomeTopRatedItems = 4,
    HomeContinueBehavior HomeContinueBehavior = HomeContinueBehavior.ResumePlayback,
    bool HideCompletedFromContinue = true,
    HomeContentPriority HomeContentPriority = HomeContentPriority.RecentFirst,
    ArtworkSourcePriority ArtworkSourcePriority = ArtworkSourcePriority.AniListFirst,
    int ImageMaxDimension = 2560,
    int ImageCacheLimitMb = 2048,
    bool OnlySfwArtwork = true,
    bool PreferLocalArtwork = true,
    int ArtworkRefreshDays = 14,
    ArtworkStretchMode ArtworkStretchMode = ArtworkStretchMode.UniformToFill,
    int ArtworkFocusXPercent = 50,
    int ArtworkFocusYPercent = 50,
    IReadOnlyDictionary<string, string>? LockedArtwork = null,
    PlaybackPlayerMode PlaybackPlayerMode = PlaybackPlayerMode.IntegratedMpcHc,
    bool ResumePlayback = true,
    int WatchedThresholdPercent = 90,
    bool AutoPlayNextEpisode = false,
    bool SkipOpening = false,
    bool SkipEnding = false,
    string PreferredAudioLanguage = "ja",
    string PreferredSubtitleLanguage = "pt-BR",
    double DefaultPlaybackSpeed = 1.0,
    bool StartPlaybackFullscreen = false,
    int ProgressSaveIntervalSeconds = 5,
    EpisodeNumberDisplayFormat EpisodeNumberDisplayFormat = EpisodeNumberDisplayFormat.NumberOnly,
    AnimeTitlePreference AnimeTitlePreference = AnimeTitlePreference.Romaji,
    LibraryDefaultSort LibraryDefaultSort = LibraryDefaultSort.RecentlyAdded,
    IReadOnlyList<string>? CustomTags = null,
    IReadOnlyList<string>? UserCollections = null,
    bool FavoritesFirstRule = false,
    bool InProgressFirstRule = false,
    bool NotifyNewEpisodes = true,
    bool NotifyMetadataUpdates = true,
    bool NotifyAchievements = true,
    bool WeeklyWatchSummary = true,
    bool QuietHoursEnabled = false,
    int QuietHoursStartHour = 22,
    int QuietHoursEndHour = 8,
    NotificationDeliveryMode NotificationDeliveryMode = NotificationDeliveryMode.InAppOnly,
    bool AutomaticBackupEnabled = false,
    AutomaticBackupFrequency AutomaticBackupFrequency = AutomaticBackupFrequency.Daily,
    int AutomaticBackupRetention = 7,
    string? AutomaticBackupDirectory = null,
    PersonalDataExportFormat PersonalDataExportFormat = PersonalDataExportFormat.Json,
    bool PrepareFutureSync = false,
    int OnlineConnectionLimit = 4,
    bool OfflineMode = false,
    int OnlineSourceTimeoutSeconds = 12,
    bool DiagnosticLoggingEnabled = false,
    IReadOnlyDictionary<string, string>? KeyboardShortcuts = null,
    bool DeveloperMode = false)
{
    public static IReadOnlyList<string> DefaultVideoExtensions { get; } = [".mkv", ".mp4", ".avi", ".mov", ".webm", ".m4v", ".wmv"];
    public static IReadOnlyList<string> DefaultHomeSectionOrder { get; } = ["continue", "recent", "highlights"];
    public static IReadOnlyDictionary<string, string> DefaultKeyboardShortcuts { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Home"] = "Ctrl+H", ["Library"] = "Ctrl+B", ["Search"] = "Ctrl+F",
        ["History"] = "Ctrl+Shift+H", ["Settings"] = "Ctrl+OemComma"
    };
    public static AniTSystemSettings Default { get; } = new(
        8, [], true, true, true, 620, 15, DefaultVideoExtensions,
        ArtworkDirectory: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AniT", "Covers"),
        ImageCacheDirectory: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AniT", "Cache"),
        IgnoredFolders: [], IgnoredFiles: [], IgnoredWords: [], PageMascots: new Dictionary<string, string>(),
        AutomaticBackupDirectory: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AniT Backups"),
        KeyboardShortcuts: DefaultKeyboardShortcuts);
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
    private static string BackupPath => FilePath + ".bak";

    public static AniTSystemSettings Load()
    {
        lock (Sync)
        {
            var primary = TryLoad(FilePath);
            if (primary is not null) return Normalize(primary);
            var backup = TryLoad(BackupPath);
            if (backup is null) return AniTSystemSettings.Default;
            try { WriteAtomically(FilePath, BackupPath, JsonSerializer.Serialize(backup, JsonOptions), preserveExistingBackup: true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AniTDiagnostics.Write("CONFIGURAÇÕES", "As configurações foram lidas do backup, mas o arquivo principal não pôde ser restaurado.", exception);
            }
            return Normalize(backup);
        }
    }

    public static void Save(AniTSystemSettings settings)
    {
        var normalized = Normalize(settings);
        lock (Sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            WriteAtomically(FilePath, BackupPath, JsonSerializer.Serialize(normalized, JsonOptions));
        }
    }

    private static AniTSystemSettings? TryLoad(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<AniTSystemSettings>(File.ReadAllText(path), JsonOptions)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            AniTDiagnostics.Write("CONFIGURAÇÕES", $"Não foi possível ler {Path.GetFileName(path)}.", exception);
            return null;
        }
    }

    private static void WriteAtomically(string path, string backupPath, string json, bool preserveExistingBackup = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, json);
        try
        {
            if (File.Exists(path))
            {
                if (preserveExistingBackup) File.Move(temporaryPath, path, overwrite: true);
                else File.Replace(temporaryPath, path, backupPath, ignoreMetadataErrors: true);
            }
            else File.Move(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
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
        if (!Uri.TryCreate(exampleUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !AniTNetworkSecurity.IsPotentiallyPublicUri(uri))
        {
            error = "Use uma URL HTTPS pública. Endereços locais ou reservados não são permitidos.";
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
        var extensions = NormalizeExtensions(settings.VideoExtensions);
        var dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AniT");
        var artworkDirectory = NormalizeDirectory(settings.ArtworkDirectory, Path.Combine(dataRoot, "Covers"));
        var cacheDirectory = NormalizeDirectory(settings.ImageCacheDirectory, Path.Combine(dataRoot, "Cache"));
        return new AniTSystemSettings(
            interval,
            sources,
            settings.ScanLibraryOnStartup,
            settings.RefreshMetadataOnStartup,
            settings.EnableNavigationLoading,
            Math.Clamp(settings.NavigationLoadingMilliseconds, 250, 1500),
            Math.Clamp(settings.LibraryBackgroundScanMinutes, 0, 360),
            extensions,
            settings.RecognizeSeasonEpisodeCodes,
            settings.RecognizeEpisodePrefixes,
            settings.RecognizeDashNumbers,
            settings.RecognizeBracketNumbers,
            settings.RecognizeTrailingNumbers,
            settings.UseFolderSeason,
            settings.RecognizeSpecials,
            artworkDirectory,
            cacheDirectory,
            Enum.IsDefined(settings.DuplicateHandling) ? settings.DuplicateHandling : LibraryDuplicateHandling.KeepAndMark,
            NormalizeTerms(settings.IgnoredFolders),
            NormalizeTerms(settings.IgnoredFiles),
            NormalizeTerms(settings.IgnoredWords),
            Enum.IsDefined(settings.ThemeMode) ? settings.ThemeMode : AppearanceThemeMode.Dark,
            NormalizeAccent(settings.AccentColor),
            Math.Clamp(settings.GlowIntensity, 0, 100),
            Enum.IsDefined(settings.CardSize) ? settings.CardSize : AppearanceCardSize.Comfortable,
            Math.Clamp(settings.CardsPerRow, 0, 10),
            Math.Clamp(settings.CornerRadius, 0, 32),
            Math.Clamp(settings.AnimationIntensity, 0, 100),
            settings.ReduceMotion,
            Math.Clamp(settings.InterfaceScalePercent, 80, 125),
            Math.Clamp(settings.TextScalePercent, 85, 140),
            NormalizeMascots(settings.PageMascots),
            settings.HomeBannerAutoRotate,
            NormalizeHomeSectionOrder(settings.HomeSectionOrder),
            NormalizeHomeSections(settings.HiddenHomeSections),
            Math.Clamp(settings.HomeContinueItems, 1, 12),
            Math.Clamp(settings.HomeRecentItems, 1, 12),
            Math.Clamp(settings.HomeFavoriteItems, 1, 12),
            Math.Clamp(settings.HomeTopRatedItems, 1, 12),
            Enum.IsDefined(settings.HomeContinueBehavior) ? settings.HomeContinueBehavior : HomeContinueBehavior.ResumePlayback,
            settings.HideCompletedFromContinue,
            Enum.IsDefined(settings.HomeContentPriority) ? settings.HomeContentPriority : HomeContentPriority.RecentFirst,
            Enum.IsDefined(settings.ArtworkSourcePriority) ? settings.ArtworkSourcePriority : ArtworkSourcePriority.AniListFirst,
            NormalizeImageDimension(settings.ImageMaxDimension),
            Math.Clamp(settings.ImageCacheLimitMb, 256, 16384),
            settings.OnlySfwArtwork,
            settings.PreferLocalArtwork,
            Math.Clamp(settings.ArtworkRefreshDays, 0, 365),
            Enum.IsDefined(settings.ArtworkStretchMode) ? settings.ArtworkStretchMode : ArtworkStretchMode.UniformToFill,
            Math.Clamp(settings.ArtworkFocusXPercent, 0, 100),
            Math.Clamp(settings.ArtworkFocusYPercent, 0, 100),
            NormalizeLockedArtwork(settings.LockedArtwork),
            Enum.IsDefined(settings.PlaybackPlayerMode) ? settings.PlaybackPlayerMode : PlaybackPlayerMode.IntegratedMpcHc,
            settings.ResumePlayback,
            Math.Clamp(settings.WatchedThresholdPercent, 50, 100),
            settings.AutoPlayNextEpisode,
            settings.SkipOpening,
            settings.SkipEnding,
            NormalizePlaybackLanguage(settings.PreferredAudioLanguage, "ja", allowOff: false),
            NormalizePlaybackLanguage(settings.PreferredSubtitleLanguage, "pt-BR", allowOff: true),
            NormalizePlaybackSpeed(settings.DefaultPlaybackSpeed),
            settings.StartPlaybackFullscreen,
            Math.Clamp(settings.ProgressSaveIntervalSeconds, 2, 60),
            Enum.IsDefined(settings.EpisodeNumberDisplayFormat) ? settings.EpisodeNumberDisplayFormat : EpisodeNumberDisplayFormat.NumberOnly,
            Enum.IsDefined(settings.AnimeTitlePreference) ? settings.AnimeTitlePreference : AnimeTitlePreference.Romaji,
            Enum.IsDefined(settings.LibraryDefaultSort) ? settings.LibraryDefaultSort : LibraryDefaultSort.RecentlyAdded,
            NormalizeTerms(settings.CustomTags),
            NormalizeTerms(settings.UserCollections),
            settings.FavoritesFirstRule,
            settings.InProgressFirstRule,
            settings.NotifyNewEpisodes,
            settings.NotifyMetadataUpdates,
            settings.NotifyAchievements,
            settings.WeeklyWatchSummary,
            settings.QuietHoursEnabled,
            Math.Clamp(settings.QuietHoursStartHour, 0, 23),
            Math.Clamp(settings.QuietHoursEndHour, 0, 23),
            Enum.IsDefined(settings.NotificationDeliveryMode) ? settings.NotificationDeliveryMode : NotificationDeliveryMode.InAppOnly,
            settings.AutomaticBackupEnabled,
            Enum.IsDefined(settings.AutomaticBackupFrequency) ? settings.AutomaticBackupFrequency : AutomaticBackupFrequency.Daily,
            Math.Clamp(settings.AutomaticBackupRetention, 1, 30),
            NormalizeDirectory(settings.AutomaticBackupDirectory,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AniT Backups")),
            Enum.IsDefined(settings.PersonalDataExportFormat) ? settings.PersonalDataExportFormat : PersonalDataExportFormat.Json,
            settings.PrepareFutureSync,
            Math.Clamp(settings.OnlineConnectionLimit, 1, 16),
            settings.OfflineMode,
            Math.Clamp(settings.OnlineSourceTimeoutSeconds, 3, 120),
            settings.DiagnosticLoggingEnabled,
            NormalizeKeyboardShortcuts(settings.KeyboardShortcuts),
            settings.DeveloperMode);
    }

    private static IReadOnlyList<string> NormalizeExtensions(IReadOnlyList<string>? values)
    {
        var normalized = (values ?? AniTSystemSettings.DefaultVideoExtensions)
            .Select(value => value.Trim().ToLowerInvariant())
            .Where(value => value.Length is > 1 and <= 12 && value.All(character => char.IsLetterOrDigit(character) || character == '.'))
            .Select(value => value.StartsWith('.') ? value : $".{value}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToArray();
        return normalized.Length == 0 ? AniTSystemSettings.DefaultVideoExtensions : normalized;
    }

    private static IReadOnlyList<string> NormalizeTerms(IReadOnlyList<string>? values) => (values ?? [])
        .Select(value => value.Trim())
        .Where(value => value.Length is > 0 and <= 160)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(100)
        .ToArray();

    private static string NormalizeDirectory(string? value, string fallback)
    {
        try { return Path.GetFullPath(string.IsNullOrWhiteSpace(value) ? fallback : value.Trim()); }
        catch { return Path.GetFullPath(fallback); }
    }

    private static string NormalizeAccent(string? value)
    {
        var color = string.IsNullOrWhiteSpace(value) ? "#42C8FF" : value.Trim().ToUpperInvariant();
        return System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9A-F]{6}$") ? color : "#42C8FF";
    }

    private static IReadOnlyDictionary<string, string> NormalizeMascots(IReadOnlyDictionary<string, string>? values)
    {
        var allowedPages = new HashSet<string>(["Inicio", "Explorar", "Biblioteca", "Calendario", "Historico", "Perfil", "Anime", "Configuracoes"], StringComparer.OrdinalIgnoreCase);
        var allowedMascots = new HashSet<string>(["default", "normal", "happy", "happy2", "angry", "sad", "boring", "hidden"], StringComparer.OrdinalIgnoreCase);
        return (values ?? new Dictionary<string, string>())
            .Where(item => allowedPages.Contains(item.Key) && allowedMascots.Contains(item.Value))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> NormalizeHomeSectionOrder(IReadOnlyList<string>? values)
    {
        var allowed = AniTSystemSettings.DefaultHomeSectionOrder.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = (values ?? [])
            .Where(allowed.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => value.ToLowerInvariant())
            .ToList();
        result.AddRange(AniTSystemSettings.DefaultHomeSectionOrder.Where(value => !result.Contains(value, StringComparer.OrdinalIgnoreCase)));
        return result;
    }

    private static IReadOnlyList<string> NormalizeHomeSections(IReadOnlyList<string>? values)
    {
        var allowed = AniTSystemSettings.DefaultHomeSectionOrder.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (values ?? [])
            .Where(allowed.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => value.ToLowerInvariant())
            .ToArray();
    }

    private static int NormalizeImageDimension(int value)
    {
        int[] allowed = [1280, 1920, 2560, 3840, 0];
        return allowed.Contains(value) ? value : 2560;
    }

    private static IReadOnlyDictionary<string, string> NormalizeLockedArtwork(IReadOnlyDictionary<string, string>? values) =>
        (values ?? new Dictionary<string, string>())
        .Where(item => Guid.TryParse(item.Key, out _) && !string.IsNullOrWhiteSpace(item.Value))
        .Take(500)
        .ToDictionary(item => item.Key, item => item.Value.Trim(), StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string> NormalizeKeyboardShortcuts(IReadOnlyDictionary<string, string>? values)
    {
        var allowed = AniTSystemSettings.DefaultKeyboardShortcuts.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, string>(AniTSystemSettings.DefaultKeyboardShortcuts, StringComparer.OrdinalIgnoreCase);
        foreach (var item in values ?? new Dictionary<string, string>())
        {
            var gesture = item.Value?.Trim() ?? string.Empty;
            if (allowed.Contains(item.Key) && gesture.Length is > 0 and <= 40) result[item.Key] = gesture;
        }
        return result;
    }

    private static string NormalizePlaybackLanguage(string? value, string fallback, bool allowOff)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var allowed = allowOff
            ? new[] { "auto", "off", "pt-BR", "ja", "en", "es" }
            : new[] { "auto", "pt-BR", "ja", "en", "es" };
        return allowed.FirstOrDefault(item => item.Equals(normalized, StringComparison.OrdinalIgnoreCase)) ?? fallback;
    }

    private static double NormalizePlaybackSpeed(double value)
    {
        double[] allowed = [0.5, 0.75, 1.0, 1.25, 1.5, 2.0];
        return allowed.MinBy(item => Math.Abs(item - value));
    }
}

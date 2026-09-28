using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniT.App;

public enum AdvancedReprocessTarget { Metadata, Images, Files }

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public const string UpdatingArtworkPath = "Assets/atualizandoimagens.png";
    public static global::AniT.Infrastructure.AniTDbContext Database { get; private set; } = null!;
    public static global::AniT.Player.MpcHcPlayer? MediaPlayer { get; private set; }
    public static global::AniT.Core.Achievements.IAchievementService Achievements { get; private set; } = null!;
    private static global::AniT.Infrastructure.AnimeCoverProvider animeCoverProvider = null!;
    private static Guid? activeEpisodeId;
    private static DateTimeOffset lastProgressWrite;
    private static CancellationTokenSource? playbackMonitorCancellation;
    private static readonly SemaphoreSlim playbackPersistenceLock = new(1, 1);
    private static string databasePath = string.Empty;
    private static string dataRootPath = string.Empty;
    private static readonly global::AniT.Infrastructure.AniTBackupService backupService = new();
    private static readonly global::AniT.Infrastructure.AutomaticBackupService automaticBackupService = new(backupService);
    private static string PendingRelinkMarkerPath => Path.Combine(dataRootPath, "Data", "pending-library-relink");
    private static DateTimeOffset activePlaybackStartedAt;
    private static TimeSpan activePlaybackStartPosition;
    private static PlaybackSegments? activePlaybackSegments;
    private static global::AniT.Infrastructure.AniTSystemSettings activePlaybackSettings = global::AniT.Infrastructure.AniTSystemSettings.Default;
    private static long playbackSession;
    private static readonly object startupLibraryScanSync = new();
    private static Task? startupLibraryScanTask;
    private static readonly SemaphoreSlim libraryScanGate = new(1, 1);
    private static DispatcherTimer? backgroundLibraryScanTimer;
    private static DispatcherTimer? automaticBackupTimer;
    private static readonly object startupMetadataSync = new();
    private static Task? startupMetadataTask;
    public static event EventHandler? LibraryArtworkUpdated;
    public static event EventHandler? FavoritesUpdated;

    /// <summary>
    /// Creates a short-lived database context for values that can be changed while a player is open.
    /// The application-level context is useful for normal screens, but it must not be used to read
    /// playback progress after the player has written it from its background callback.
    /// </summary>
    public static global::AniT.Infrastructure.AniTDbContext OpenFreshDatabase() => global::AniT.Infrastructure.AniTDatabase.Create(databasePath);
    public static string DataRootPath => dataRootPath;
    public static string DatabasePath => databasePath;
    public static string VersionNumber
    {
        get
        {
            var version = Assembly.GetEntryAssembly()?.GetName().Version;
            return version is null
                ? "desconhecida"
                : $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
        }
    }
    public static string DisplayVersion => $"Versão {VersionNumber}";

    public static Task<global::AniT.Infrastructure.AniTBackupManifest> ExportBackupAsync(string destinationPath, CancellationToken cancellationToken = default) =>
        backupService.ExportAsync(dataRootPath, databasePath, destinationPath, CurrentVersion, cancellationToken);

    public static Task<global::AniT.Infrastructure.AniTBackupInspection> InspectBackupAsync(string backupPath, CancellationToken cancellationToken = default) =>
        backupService.InspectAsync(backupPath, cancellationToken);

    public static Task<global::AniT.Infrastructure.AutomaticBackupResult?> RunAutomaticBackupAsync(bool force = false, CancellationToken cancellationToken = default) =>
        automaticBackupService.RunAsync(dataRootPath, databasePath, CurrentVersion,
            global::AniT.Infrastructure.AniTSystemSettingsStore.Load(), force, cancellationToken);

    public static async Task<global::AniT.Infrastructure.AniTBackupRestoreResult> RestoreBackupAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        await playbackPersistenceLock.WaitAsync(cancellationToken);
        try
        {
            var result = await backupService.RestoreAsync(
                backupPath,
                dataRootPath,
                databasePath,
                CurrentVersion,
                () =>
                {
                    playbackMonitorCancellation?.Cancel();
                    activeEpisodeId = null;
                    MediaPlayer?.Dispose();
                    MediaPlayer = null;
                    Database?.Dispose();
                    SqliteConnection.ClearAllPools();
                    return Task.CompletedTask;
                },
                cancellationToken);

            ReopenDataServices();
            Directory.CreateDirectory(Path.GetDirectoryName(PendingRelinkMarkerPath)!);
            File.WriteAllText(PendingRelinkMarkerPath, DateTimeOffset.UtcNow.ToString("O"));
            await Achievements.RecordAsync(new global::AniT.Core.Achievements.AchievementEvent(
                global::AniT.Core.Achievements.AchievementEventType.BackupRestored));
            return result;
        }
        catch
        {
            try { ReopenDataServices(); } catch { }
            throw;
        }
        finally
        {
            playbackPersistenceLock.Release();
        }
    }

    public static void RestartAfterBackupRestore()
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Não foi possível localizar o executável do AniT.");
        var entryAssembly = Assembly.GetEntryAssembly()?.Location;
        var startInfo = Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(entryAssembly)
            ? new ProcessStartInfo(processPath, $"\"{entryAssembly}\"") { UseShellExecute = true }
            : new ProcessStartInfo(processPath) { UseShellExecute = true };
        Process.Start(startInfo);
        Current.Shutdown();
    }

    private static string CurrentVersion => VersionNumber;

    public static bool NeedsLibraryRelinkAfterRestore()
    {
        if (!File.Exists(PendingRelinkMarkerPath)) return false;
        using var context = OpenFreshDatabase();
        var roots = context.LibraryRoots.AsNoTracking().Where(root => root.IsEnabled).Select(root => root.Path).ToArray();
        if (roots.Length > 0 && roots.All(Directory.Exists))
        {
            File.Delete(PendingRelinkMarkerPath);
            return false;
        }
        return true;
    }

    public static bool HasPendingLibraryRelink => File.Exists(PendingRelinkMarkerPath);

    public static async Task SwitchProfileAsync(Guid profileId)
    {
        var currentProfileId = ProfileSettingsStore.ActiveProfileId;
        if (profileId == currentProfileId) return;
        if (!ProfileSettingsStore.GetProfiles().Any(profile => profile.ProfileId == profileId))
            throw new InvalidOperationException("O perfil escolhido não existe mais.");

        await playbackPersistenceLock.WaitAsync();
        var rollbackPath = databasePath + $".profile-switch-{Guid.NewGuid():N}";
        try
        {
            playbackMonitorCancellation?.Cancel();
            backgroundLibraryScanTimer?.Stop();
            MediaPlayer?.Dispose();
            MediaPlayer = null;
            Database?.Dispose();
            SqliteConnection.ClearAllPools();

            CopyDatabase(databasePath, ProfileSettingsStore.GetDatabaseSnapshotPath(currentProfileId));
            CopyDatabase(databasePath, rollbackPath);
            CopyDatabase(ProfileSettingsStore.GetDatabaseSnapshotPath(profileId), databasePath);
            if (!ProfileSettingsStore.SetActive(profileId)) throw new InvalidOperationException("Não foi possível ativar o perfil escolhido.");
            RestartAfterBackupRestore();
        }
        catch
        {
            try
            {
                if (File.Exists(rollbackPath)) CopyDatabase(rollbackPath, databasePath);
                ProfileSettingsStore.SetActive(currentProfileId);
                ReopenDataServices();
            }
            catch { }
            throw;
        }
        finally
        {
            TryDeleteDatabaseFiles(rollbackPath);
            playbackPersistenceLock.Release();
        }
    }

    public static async Task PrepareProfileDatabaseAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        var destinationPath = ProfileSettingsStore.GetDatabaseSnapshotPath(profileId);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        await using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destinationPath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString()))
        {
            await source.OpenAsync(cancellationToken);
            await destination.OpenAsync(cancellationToken);
            source.BackupDatabase(destination);
        }

        await using var profileDatabase = global::AniT.Infrastructure.AniTDatabase.Create(destinationPath);
        await profileDatabase.PlaybackProgresses.ExecuteDeleteAsync(cancellationToken);
        await profileDatabase.Episodes.ExecuteUpdateAsync(update => update
            .SetProperty(episode => episode.Status, global::AniT.Core.WatchStatus.NotStarted)
            .SetProperty(episode => episode.WatchedAt, (DateTimeOffset?)null)
            .SetProperty(episode => episode.Rating, (double?)null)
            .SetProperty(episode => episode.ReviewNotes, (string?)null), cancellationToken);
        await profileDatabase.Anime.ExecuteUpdateAsync(update => update
            .SetProperty(anime => anime.IsFavorite, false)
            .SetProperty(anime => anime.Rating, (double?)null)
            .SetProperty(anime => anime.ReviewNotes, (string?)null), cancellationToken);
        await profileDatabase.AchievementHistory.ExecuteDeleteAsync(cancellationToken);
        await profileDatabase.UserAchievements.ExecuteDeleteAsync(cancellationToken);
        await profileDatabase.AchievementMetrics.ExecuteDeleteAsync(cancellationToken);
    }

    public static async Task ClearWatchHistoryAsync()
    {
        await playbackPersistenceLock.WaitAsync();
        try
        {
            playbackMonitorCancellation?.Cancel();
            activeEpisodeId = null;
            await using var context = OpenFreshDatabase();
            await context.PlaybackProgresses.ExecuteDeleteAsync();
            await context.Episodes.ExecuteUpdateAsync(update => update
                .SetProperty(episode => episode.Status, global::AniT.Core.WatchStatus.NotStarted)
                .SetProperty(episode => episode.WatchedAt, (DateTimeOffset?)null));
            Database.ChangeTracker.Clear();
        }
        finally
        {
            playbackPersistenceLock.Release();
        }
    }

    public static async Task ExportPersonalDataAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        await using var context = OpenFreshDatabase();
        var anime = await context.Anime
            .AsNoTracking()
            .Include(item => item.Seasons)
            .ThenInclude(season => season.Episodes)
            .ThenInclude(episode => episode.PlaybackProgress)
            .OrderBy(item => item.Title)
            .ToListAsync(cancellationToken);
        var achievements = await context.AchievementHistory.AsNoTracking().OrderBy(item => item.UnlockedAt).ToListAsync(cancellationToken);
        var payload = new
        {
            Format = "AniT Personal Data Export",
            Version = 1,
            ExportedAt = DateTimeOffset.UtcNow,
            Profile = ProfileSettingsStore.Load(),
            Favorites = anime.Where(item => item.IsFavorite).Select(item => new { item.Id, item.Title }).ToArray(),
            Ratings = anime.SelectMany(item => item.Seasons.SelectMany(season => season.Episodes
                .Where(episode => episode.Rating is not null || !string.IsNullOrWhiteSpace(episode.ReviewNotes))
                .Select(episode => new { Anime = item.Title, Season = season.Number, Episode = episode.Number, episode.Rating, episode.ReviewNotes }))).ToArray(),
            History = anime.SelectMany(item => item.Seasons.SelectMany(season => season.Episodes
                .Where(episode => episode.Status != global::AniT.Core.WatchStatus.NotStarted || episode.WatchedAt is not null || episode.PlaybackProgress is not null)
                .Select(episode => new
                {
                    Anime = item.Title,
                    Season = season.Number,
                    Episode = episode.Number,
                    Status = episode.Status.ToString(),
                    episode.WatchedAt,
                    PositionSeconds = episode.PlaybackProgress?.PositionSeconds,
                    DurationSeconds = episode.PlaybackProgress?.DurationSeconds,
                    LastPlayedAt = episode.PlaybackProgress?.LastPlayedAt
                }))).ToArray(),
            Achievements = achievements.Select(item => new { item.AchievementId, item.UnlockedAt, item.Points }).ToArray()
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        await File.WriteAllTextAsync(destinationPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    public static async Task ExportPersonalDataCsvAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        await using var context = OpenFreshDatabase();
        var anime = await context.Anime.AsNoTracking()
            .Include(item => item.Seasons)
            .ThenInclude(season => season.Episodes)
            .ThenInclude(episode => episode.PlaybackProgress)
            .OrderBy(item => item.Title)
            .ToListAsync(cancellationToken);
        var csv = new StringBuilder();
        csv.AppendLine("Anime;Temporada;Episódio;Status;Assistido em;Nota;Favorito;Posição (s);Duração (s);Comentário");
        foreach (var item in anime)
        foreach (var season in item.Seasons.OrderBy(value => value.Number))
        foreach (var episode in season.Episodes.OrderBy(value => value.Number))
        {
            csv.Append(Csv(item.Title)).Append(';')
                .Append(season.Number).Append(';')
                .Append(episode.Number).Append(';')
                .Append(Csv(episode.Status.ToString())).Append(';')
                .Append(Csv(episode.WatchedAt?.ToString("O") ?? string.Empty)).Append(';')
                .Append(episode.Rating?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).Append(';')
                .Append(item.IsFavorite ? "Sim" : "Não").Append(';')
                .Append(episode.PlaybackProgress?.PositionSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).Append(';')
                .Append(episode.PlaybackProgress?.DurationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).Append(';')
                .Append(Csv(episode.ReviewNotes ?? string.Empty)).AppendLine();
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        await File.WriteAllTextAsync(destinationPath, csv.ToString(), new UTF8Encoding(true), cancellationToken);
    }

    public static Task<global::AniT.Infrastructure.AniTDatabaseDiagnostic> DiagnoseDatabaseAsync(CancellationToken cancellationToken = default) =>
        global::AniT.Infrastructure.AniTDatabaseMaintenance.DiagnoseAsync(databasePath, cancellationToken);

    public static async Task<(global::AniT.Infrastructure.AniTDatabaseDiagnostic Diagnostic, string RecoveryBackup)> RepairDatabaseAsync(CancellationToken cancellationToken = default)
    {
        await playbackPersistenceLock.WaitAsync(cancellationToken);
        var recoveryDirectory = Path.Combine(dataRootPath, "Recovery");
        Directory.CreateDirectory(recoveryDirectory);
        var recoveryPath = Path.Combine(recoveryDirectory, $"antes-do-reparo-{DateTime.Now:yyyy-MM-dd-HHmmss}.anitbackup");
        try
        {
            await backupService.ExportAsync(dataRootPath, databasePath, recoveryPath, CurrentVersion, cancellationToken);
            playbackMonitorCancellation?.Cancel();
            backgroundLibraryScanTimer?.Stop();
            automaticBackupTimer?.Stop();
            activeEpisodeId = null;
            MediaPlayer?.Dispose();
            MediaPlayer = null;
            Database?.Dispose();
            SqliteConnection.ClearAllPools();
            var result = await global::AniT.Infrastructure.AniTDatabaseMaintenance.RepairAsync(databasePath, cancellationToken);
            ReopenDataServices();
            return (result, recoveryPath);
        }
        catch
        {
            try { ReopenDataServices(); } catch { }
            throw;
        }
        finally
        {
            playbackPersistenceLock.Release();
        }
    }

    private static string Csv(string value)
    {
        // Spreadsheet applications can execute cells beginning with these characters as formulas.
        // Prefix user-controlled values so exported history remains data when opened in Excel.
        var safeValue = value.Length > 0 && value[0] is '=' or '+' or '-' or '@'
            ? "'" + value
            : value;
        return $"\"{safeValue.Replace("\"", "\"\"")}\"";
    }

    public static void CompleteLibraryRelink()
    {
        try { if (File.Exists(PendingRelinkMarkerPath)) File.Delete(PendingRelinkMarkerPath); } catch { }
    }

    private static void ReopenDataServices()
    {
        Database?.Dispose();
        SqliteConnection.ClearAllPools();
        Database = global::AniT.Infrastructure.AniTDatabase.Create(databasePath);
        Achievements = new global::AniT.Infrastructure.Achievements.AchievementService(OpenFreshDatabase);
        AchievementNotificationQueue.Initialize(Achievements);
        var settings = global::AniT.Infrastructure.AniTSystemSettingsStore.Load();
        ArtworkCacheManager.EnsureManagedCache(settings);
        animeCoverProvider = new global::AniT.Infrastructure.AnimeCoverProvider(settings.ArtworkDirectory ?? Path.Combine(dataRootPath, "Covers"));
        ConfigureLibraryBackgroundScan(settings);
        ConfigureAutomaticBackup(settings);
        AppearanceManager.Apply(settings);
        InitializeMediaPlayer();
    }

    private static void CopyDatabase(string source, string destination)
    {
        if (!File.Exists(source)) throw new FileNotFoundException("O banco deste perfil não foi encontrado.", source);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + $".partial-{Guid.NewGuid():N}";
        try
        {
            using (var sourceConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = source, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            using (var destinationConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString()))
            {
                sourceConnection.Open();
                destinationConnection.Open();
                sourceConnection.BackupDatabase(destinationConnection);
            }
            TryDeleteDatabaseFiles(destination + "-wal");
            TryDeleteDatabaseFiles(destination + "-shm");
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void TryDeleteDatabaseFiles(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    public static async Task<bool?> ToggleAnimeFavoriteAsync(Guid animeId)
    {
        await using var context = OpenFreshDatabase();
        var anime = await context.Anime.FirstOrDefaultAsync(item => item.Id == animeId);
        if (anime is null) return null;

        anime.IsFavorite = !anime.IsFavorite;
        var isFavorite = anime.IsFavorite;
        await context.SaveChangesAsync();
        FavoritesUpdated?.Invoke(Current, EventArgs.Empty);

        try
        {
            if (isFavorite)
            {
                await Achievements.RecordAsync(new global::AniT.Core.Achievements.AchievementEvent(
                    global::AniT.Core.Achievements.AchievementEventType.AnimeFavorited,
                    animeId));
            }
            else
            {
                await Achievements.RecalculateAsync();
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"AniT could not update favorite achievements: {exception}");
        }

        return isFavorite;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        dataRootPath = global::System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AniT");
        var dataDirectory = global::System.IO.Path.Combine(dataRootPath, "Data");
        databasePath = global::System.IO.Path.Combine(dataDirectory, "anit.db");
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        AppearanceManager.Initialize(this);

        var profileSelection = new ProfileSelectionWindow();
        MainWindow = profileSelection;
        if (profileSelection.ShowDialog() != true || profileSelection.SelectedProfileId == Guid.Empty)
        {
            Shutdown();
            return;
        }

        try
        {
            ActivateProfileForStartup(profileSelection.SelectedProfileId);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Não foi possível abrir este perfil. Seus dados continuam preservados.\n\n{exception.Message}",
                "Perfil indisponível · AniT",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        Database = global::AniT.Infrastructure.AniTDatabase.Create(databasePath);
        Achievements = new global::AniT.Infrastructure.Achievements.AchievementService(OpenFreshDatabase);
        AchievementNotificationQueue.Initialize(Achievements);
        var settings = global::AniT.Infrastructure.AniTSystemSettingsStore.Load();
        ArtworkCacheManager.EnsureManagedCache(settings);
        var coversDirectory = settings.ArtworkDirectory ?? global::System.IO.Path.Combine(dataRootPath, "Covers");
        animeCoverProvider = new global::AniT.Infrastructure.AnimeCoverProvider(coversDirectory);
        InitializeMediaPlayer();
        ConfigureLibraryBackgroundScan(settings);
        ConfigureAutomaticBackup(settings);
        ShortcutManager.Initialize();
        global::AniT.Infrastructure.AniTDiagnostics.Write("APP", $"AniT {CurrentVersion} iniciado · perfil {ProfileSettingsStore.ActiveProfileId:N}");

        var dashboard = new DashboardWindow();
        MainWindow = dashboard;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        dashboard.ContentRendered += (_, _) =>
        {
            if (!Database.LibraryRoots.Any() || NeedsLibraryRelinkAfterRestore()) AppNavigation.Settings(dashboard, SettingsSection.Shelf);
            else AppNavigation.PrewarmLoadingSurface(dashboard);
        };
        dashboard.Show();
        _ = InitializeAchievementsAsync();
        _ = InitializeWeeklySummaryAsync();
        _ = RunAutomaticBackupSafelyAsync();
    }

    private static void ActivateProfileForStartup(Guid selectedProfileId)
    {
        var activeProfileId = ProfileSettingsStore.ActiveProfileId;
        if (selectedProfileId == activeProfileId) return;
        if (!ProfileSettingsStore.GetProfiles().Any(profile => profile.ProfileId == selectedProfileId))
            throw new InvalidOperationException("O perfil escolhido não existe mais.");

        if (File.Exists(databasePath))
            CopyDatabase(databasePath, ProfileSettingsStore.GetDatabaseSnapshotPath(activeProfileId));

        var selectedDatabase = ProfileSettingsStore.GetDatabaseSnapshotPath(selectedProfileId);
        if (!File.Exists(selectedDatabase))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(selectedDatabase)!);
            using var emptyProfileDatabase = global::AniT.Infrastructure.AniTDatabase.Create(selectedDatabase);
        }
        CopyDatabase(selectedDatabase, databasePath);
        if (!ProfileSettingsStore.SetActive(selectedProfileId))
            throw new InvalidOperationException("Não foi possível ativar o perfil escolhido.");
    }

    private static async Task RunAutomaticBackupSafelyAsync()
    {
        try { await RunAutomaticBackupAsync(); }
        catch (Exception exception) { Debug.WriteLine($"AniT could not create the scheduled backup: {exception}"); }
    }

    private static async Task InitializeAchievementsAsync()
    {
        try
        {
            await Achievements.RecordAsync(new global::AniT.Core.Achievements.AchievementEvent(
                global::AniT.Core.Achievements.AchievementEventType.ApplicationStarted,
                OccurredAt: DateTimeOffset.Now));
            var pending = (await Achievements.GetProgressAsync())
                .Where(item => item.IsUnlocked && !item.PopupShown)
                .Select(item => new global::AniT.Core.Achievements.AchievementUnlock(item.Definition, item.UnlockedAt ?? DateTimeOffset.UtcNow, item.Points))
                .ToArray();
            AchievementNotificationQueue.Enqueue(pending);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"AniT could not initialize achievements: {exception}");
        }
    }

    public static Task RefreshConfiguredLibraryOnStartupAsync()
    {
        lock (startupLibraryScanSync)
        {
            return startupLibraryScanTask ??= Task.Run(() => RefreshConfiguredLibraryCoreAsync());
        }
    }

    public static void ApplyLibrarySettings(global::AniT.Infrastructure.AniTSystemSettings settings)
    {
        ArtworkCacheManager.EnsureManagedCache(settings);
        animeCoverProvider = new global::AniT.Infrastructure.AnimeCoverProvider(
            settings.ArtworkDirectory ?? Path.Combine(dataRootPath, "Covers"));
        ConfigureLibraryBackgroundScan(settings);
        ConfigureAutomaticBackup(settings);
        AppearanceManager.Apply(settings);
    }

    private static void ConfigureAutomaticBackup(global::AniT.Infrastructure.AniTSystemSettings settings)
    {
        automaticBackupTimer?.Stop();
        automaticBackupTimer = null;
        if (!settings.AutomaticBackupEnabled
            || settings.AutomaticBackupFrequency == global::AniT.Infrastructure.AutomaticBackupFrequency.EveryStartup
            || Current?.Dispatcher is null) return;
        automaticBackupTimer = new DispatcherTimer(
            TimeSpan.FromHours(1),
            DispatcherPriority.ApplicationIdle,
            async (_, _) => await RunAutomaticBackupSafelyAsync(),
            Current.Dispatcher);
        automaticBackupTimer.Start();
    }

    private static void ConfigureLibraryBackgroundScan(global::AniT.Infrastructure.AniTSystemSettings settings)
    {
        backgroundLibraryScanTimer?.Stop();
        backgroundLibraryScanTimer = null;
        if (settings.LibraryBackgroundScanMinutes <= 0 || Current?.Dispatcher is null) return;

        backgroundLibraryScanTimer = new DispatcherTimer(
            TimeSpan.FromMinutes(settings.LibraryBackgroundScanMinutes),
            DispatcherPriority.Background,
            async (_, _) => await RefreshConfiguredLibraryCoreAsync(),
            Current.Dispatcher);
        backgroundLibraryScanTimer.Start();
    }

    private static async Task RefreshConfiguredLibraryCoreAsync(bool reportFailures = false, CancellationToken cancellationToken = default)
    {
        if (reportFailures)
            await libraryScanGate.WaitAsync(cancellationToken);
        else if (!await libraryScanGate.WaitAsync(0, cancellationToken))
            return;
        try
        {
            var addedEpisodes = 0;
            var failedRoots = new List<string>();
            Guid[] rootIds;
            await using (var context = OpenFreshDatabase())
            {
                rootIds = await context.LibraryRoots
                    .AsNoTracking()
                    .Where(root => root.IsEnabled)
                    .Select(root => root.Id)
                    .ToArrayAsync(cancellationToken);
            }

            foreach (var rootId in rootIds)
            {
                await using var scanContext = OpenFreshDatabase();
                var root = await scanContext.LibraryRoots.FirstOrDefaultAsync(item => item.Id == rootId, cancellationToken);
                if (root is null) continue;
                var result = await new global::AniT.Infrastructure.LibraryScanner(
                    scanContext,
                    settings: global::AniT.Infrastructure.AniTSystemSettingsStore.Load()).ScanAsync(root, cancellationToken: cancellationToken);
                addedEpisodes += result.EpisodesAdded;
                if (result.Errors > 0 || result.RootUnavailable) failedRoots.Add(root.DisplayName);
            }

            if (reportFailures && failedRoots.Count > 0)
                throw new InvalidOperationException($"Não foi possível atualizar: {string.Join(", ", failedRoots.Distinct(StringComparer.OrdinalIgnoreCase))}.");

            if (addedEpisodes > 0)
                await AniTNotificationService.NotifyAsync(
                    global::AniT.Infrastructure.AniTNotificationKind.NewEpisodes,
                    addedEpisodes == 1 ? "Novo episódio encontrado" : "Novos episódios encontrados",
                    addedEpisodes == 1 ? "O catálogo recebeu 1 novo episódio." : $"O catálogo recebeu {addedEpisodes} novos episódios.");
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"AniT startup library scan failed: {exception}");
            global::AniT.Infrastructure.AniTDiagnostics.Write("BIBLIOTECA", "A atualização da biblioteca falhou.", exception);
            if (reportFailures) throw;
        }
        finally
        {
            libraryScanGate.Release();
        }
    }

    public static Task RefreshMissingAnimeMetadataOnStartupAsync()
    {
        lock (startupMetadataSync)
        {
            return startupMetadataTask ??= Task.Run(RefreshMissingAnimeMetadataCoreAsync);
        }
    }

    private static async Task RefreshMissingAnimeMetadataCoreAsync()
    {
        try
        {
            List<global::AniT.Core.Anime> pending;
            await using (var context = OpenFreshDatabase())
            {
                var anime = await context.Anime.AsNoTracking().OrderBy(item => item.Title).ToListAsync();
                pending = anime
                    .Where(item => string.IsNullOrWhiteSpace(item.CoverPath)
                        || !global::System.IO.File.Exists(item.CoverPath)
                        || item.ReleaseYear is null
                        || string.IsNullOrWhiteSpace(item.Studio))
                    .ToList();
            }

            var artworkUpdated = false;
            var updatedTitles = 0;
            foreach (var item in pending)
            {
                var metadata = await EnsureAnimeMetadataAsync(
                    item.Id,
                    item.Title,
                    item.EnglishTitle,
                    item.CoverPath,
                    savedSynopsis: item.Synopsis,
                    savedCriticScore: item.CriticScore,
                    savedGenres: item.Genres,
                    savedReleaseYear: item.ReleaseYear,
                    savedStudio: item.Studio,
                    refreshCatalogDetails: item.ReleaseYear is null || string.IsNullOrWhiteSpace(item.Studio));
                artworkUpdated |= !string.IsNullOrWhiteSpace(metadata.CoverPath)
                    && global::System.IO.File.Exists(metadata.CoverPath);
                if (!string.Equals(item.CoverPath, metadata.CoverPath, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(item.EnglishTitle, metadata.EnglishTitle, StringComparison.Ordinal)
                    || !string.Equals(item.Synopsis, metadata.Synopsis, StringComparison.Ordinal)
                    || item.CriticScore != metadata.CriticScore
                    || !string.Equals(item.Genres, metadata.Genres, StringComparison.Ordinal)
                    || item.ReleaseYear != metadata.ReleaseYear
                    || !string.Equals(item.Studio, metadata.Studio, StringComparison.Ordinal))
                    updatedTitles++;
            }

            if (artworkUpdated && Current?.Dispatcher is { HasShutdownStarted: false } dispatcher)
            {
                _ = dispatcher.BeginInvoke(() => LibraryArtworkUpdated?.Invoke(null, EventArgs.Empty));
            }
            if (updatedTitles > 0)
                await AniTNotificationService.NotifyAsync(
                    global::AniT.Infrastructure.AniTNotificationKind.MetadataUpdated,
                    updatedTitles == 1 ? "Arte e metadados atualizados" : "Artes e metadados atualizados",
                    updatedTitles == 1 ? "1 título recebeu informações novas." : $"{updatedTitles} títulos receberam informações novas.");
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"AniT startup metadata refresh failed: {exception}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        backgroundLibraryScanTimer?.Stop();
        automaticBackupTimer?.Stop();
        ShortcutManager.Shutdown();
        AniTNotificationService.Shutdown();
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        MediaPlayer?.Dispose();
        Database?.Dispose();
        global::AniT.Infrastructure.AniTDiagnostics.Write("APP", "AniT encerrado normalmente.");
        base.OnExit(e);
    }

    private static async Task InitializeWeeklySummaryAsync()
    {
        try
        {
            var settings = global::AniT.Infrastructure.AniTSystemSettingsStore.Load();
            var now = DateTimeOffset.Now;
            if (!global::AniT.Infrastructure.NotificationPreferences.CanDeliver(
                    settings,
                    global::AniT.Infrastructure.AniTNotificationKind.WeeklySummary,
                    now)) return;
            var state = NotificationRuntimeStateStore.Load();
            if (state.LastWeeklySummaryAt is { } last && now - last < TimeSpan.FromDays(7)) return;

            var since = now.AddDays(-7);
            await using var context = OpenFreshDatabase();
            var episodes = await context.Episodes
                .AsNoTracking()
                .Include(episode => episode.PlaybackProgress)
                .Where(episode => episode.WatchedAt >= since || episode.PlaybackProgress!.LastPlayedAt >= since)
                .ToListAsync();
            var watchedSeconds = episodes.Sum(episode => episode.Status == global::AniT.Core.WatchStatus.Completed
                ? Math.Max(episode.PlaybackProgress?.DurationSeconds ?? 0, episode.PlaybackProgress?.PositionSeconds ?? 0)
                : Math.Max(0, episode.PlaybackProgress?.PositionSeconds ?? 0));
            var duration = TimeSpan.FromSeconds(watchedSeconds);
            var timeLabel = duration.TotalHours >= 1 ? $"{(int)duration.TotalHours}h {duration.Minutes:00}min" : $"{Math.Max(0, duration.Minutes)}min";
            var message = episodes.Count == 0
                ? "Nenhum episódio foi assistido nos últimos sete dias. Uma nova história pode começar hoje."
                : $"Você assistiu {episodes.Count} episódio{(episodes.Count == 1 ? string.Empty : "s")} e acumulou {timeLabel} nos últimos sete dias.";
            if (await AniTNotificationService.NotifyAsync(global::AniT.Infrastructure.AniTNotificationKind.WeeklySummary, "Seu resumo semanal", message))
                NotificationRuntimeStateStore.Save(state with { LastWeeklySummaryAt = now });
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"AniT weekly notification failed: {exception}");
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        global::AniT.Infrastructure.AniTDiagnostics.Write("ERRO", "Exceção não tratada na interface.", e.Exception);
        try
        {
            var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AniT", "Data");
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(
                Path.Combine(logDirectory, "app.log"),
                $"{DateTimeOffset.Now:O}{Environment.NewLine}{e.Exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never replace the original application error.
        }

        MessageBox.Show(
            "Uma tela do AniT encontrou um problema e não pôde ser exibida. O restante do aplicativo continuará aberto.",
            "AniT",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    public static async Task PlayEpisodeAsync(Guid episodeId)
    {
        var playbackSettings = global::AniT.Infrastructure.AniTSystemSettingsStore.Load();
        if (playbackSettings.PlaybackPlayerMode == global::AniT.Infrastructure.PlaybackPlayerMode.IntegratedMpcHc && MediaPlayer is null)
            throw new InvalidOperationException("O MPC-HC integrado não foi encontrado. Reinstale o AniT ou escolha o aplicativo padrão do Windows nas configurações.");
        await using var context = OpenFreshDatabase();
        var episode = await context.Episodes
            .Include(item => item.MediaFiles)
            .ThenInclude(file => file.LibraryRoot)
            .Include(item => item.PlaybackProgress)
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == episodeId)
            ?? throw new InvalidOperationException("O episódio não existe mais na biblioteca.");
        var mediaFile = episode.MediaFiles
            .Where(file => file.Availability == global::AniT.Core.MediaFileAvailability.Available && file.LibraryRoot is not null)
            .OrderByDescending(file => file.IsPreferred)
            .ThenByDescending(file => PreferredLanguageScore(file, playbackSettings.PreferredAudioLanguage))
            .ThenByDescending(file => file.Resolution)
            .FirstOrDefault(file => global::System.IO.File.Exists(global::System.IO.Path.Combine(file.LibraryRoot!.Path, file.RelativePath)));
        if (mediaFile is null) throw new InvalidOperationException("Nenhuma versão disponível deste episódio foi encontrada. Atualize a Biblioteca ou conecte o disco onde o arquivo está salvo.");

        var mediaPath = global::System.IO.Path.Combine(mediaFile.LibraryRoot!.Path, mediaFile.RelativePath);
        activeEpisodeId = episodeId;
        activePlaybackSettings = playbackSettings;
        activePlaybackSegments = LoadPlaybackSegments(mediaPath);
        TimeSpan? resumeAt = playbackSettings.ResumePlayback && episode.PlaybackProgress is { PositionSeconds: > 0 } progress
            ? TimeSpan.FromSeconds(progress.PositionSeconds)
            : null;
        if (playbackSettings.SkipOpening && activePlaybackSegments is { OpeningEndSeconds: > 0 } segments &&
            (resumeAt is null || resumeAt.Value.TotalSeconds < segments.OpeningEndSeconds))
        {
            resumeAt = TimeSpan.FromSeconds(segments.OpeningEndSeconds);
        }
        WritePlaybackLog($"[DB] Episódio {episodeId}: retomando de {resumeAt?.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "0"}s.");
        if (playbackSettings.PlaybackPlayerMode == global::AniT.Infrastructure.PlaybackPlayerMode.SystemDefault)
        {
            Process.Start(new ProcessStartInfo(mediaPath) { UseShellExecute = true });
            await MarkEpisodeAsWatchingAsync(episodeId);
            return;
        }

        await MediaPlayer!.PlayAsync(mediaFile, resumeAt, new global::AniT.Player.MpcHcPlaybackOptions(
            playbackSettings.DefaultPlaybackSpeed,
            playbackSettings.StartPlaybackFullscreen,
            playbackSettings.PreferredAudioLanguage,
            playbackSettings.PreferredSubtitleLanguage));
        activePlaybackStartPosition = resumeAt ?? TimeSpan.Zero;
        activePlaybackStartedAt = DateTimeOffset.UtcNow;
        lastProgressWrite = activePlaybackStartedAt;
        await MarkEpisodeAsWatchingAsync(episodeId);
        await Achievements.RecordAsync(new global::AniT.Core.Achievements.AchievementEvent(
            global::AniT.Core.Achievements.AchievementEventType.EpisodeStarted,
            episodeId));
        playbackMonitorCancellation?.Cancel();
        playbackMonitorCancellation = new CancellationTokenSource();
        var session = Interlocked.Increment(ref playbackSession);
        _ = MonitorPlaybackAsync(session, playbackMonitorCancellation.Token);
    }

    public static async Task<global::AniT.Infrastructure.AnimeMetadataResult> EnsureAnimeMetadataAsync(
        Guid animeId,
        string japaneseTitle,
        string? englishTitle,
        string? savedCoverPath,
        CancellationToken cancellationToken = default,
        string? savedSynopsis = null,
        double? savedCriticScore = null,
        bool forceRefresh = false,
        string? savedGenres = null,
        int? savedReleaseYear = null,
        string? savedStudio = null,
        bool refreshCatalogDetails = false,
        bool refreshArtwork = true)
    {
        var metadata = await animeCoverProvider.EnsureMetadataAsync(
            animeId,
            japaneseTitle,
            englishTitle,
            savedCoverPath,
            cancellationToken,
            savedSynopsis,
            savedCriticScore,
            forceRefresh,
            savedGenres,
            savedReleaseYear,
            savedStudio,
            refreshCatalogDetails,
            refreshArtwork);
        var imageSettings = global::AniT.Infrastructure.AniTSystemSettingsStore.Load();
        await ArtworkCacheManager.OptimizeAsync([metadata.CoverPath, metadata.BannerPath], imageSettings, cancellationToken);
        var englishChanged = !string.IsNullOrWhiteSpace(metadata.EnglishTitle)
            && !string.Equals(metadata.EnglishTitle, englishTitle, StringComparison.Ordinal);
        var coverChanged = metadata.CoverPath is not null
            && !string.Equals(metadata.CoverPath, savedCoverPath, StringComparison.OrdinalIgnoreCase);
        var synopsisChanged = !string.IsNullOrWhiteSpace(metadata.Synopsis)
            && !string.Equals(metadata.Synopsis, savedSynopsis, StringComparison.Ordinal);
        var criticScoreChanged = metadata.CriticScore is not null
            && metadata.CriticScore != savedCriticScore;
        var genresChanged = !string.IsNullOrWhiteSpace(metadata.Genres)
            && !string.Equals(metadata.Genres, savedGenres, StringComparison.Ordinal);
        var releaseYearChanged = metadata.ReleaseYear is not null
            && metadata.ReleaseYear != savedReleaseYear;
        var studioChanged = !string.IsNullOrWhiteSpace(metadata.Studio)
            && !string.Equals(metadata.Studio, savedStudio, StringComparison.Ordinal);
        var originalTitleChanged = !string.IsNullOrWhiteSpace(metadata.OriginalTitle);
        var importedAliases = metadata.Aliases?
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(alias => alias.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (!englishChanged && !coverChanged && !synopsisChanged && !criticScoreChanged && !genresChanged && !releaseYearChanged && !studioChanged && !originalTitleChanged && importedAliases.Length == 0) return metadata;

        await using var context = OpenFreshDatabase();
        var anime = await context.Anime
            .Include(item => item.Aliases)
            .FirstOrDefaultAsync(item => item.Id == animeId, cancellationToken);
        if (anime is null) return metadata;
        if (englishChanged) anime.EnglishTitle = metadata.EnglishTitle;
        if (coverChanged) anime.CoverPath = metadata.CoverPath;
        if (synopsisChanged) anime.Synopsis = metadata.Synopsis;
        if (criticScoreChanged) anime.CriticScore = metadata.CriticScore;
        if (genresChanged) anime.Genres = metadata.Genres;
        if (releaseYearChanged) anime.ReleaseYear = metadata.ReleaseYear;
        if (studioChanged) anime.Studio = metadata.Studio;
        if (originalTitleChanged) anime.OriginalTitle = metadata.OriginalTitle;
        var knownAliases = anime.Aliases
            .Select(alias => alias.NormalizedAlias)
            .Append(global::AniT.Core.AnimeTitleNormalizer.Normalize(anime.Title))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var alias in importedAliases)
        {
            var normalizedAlias = global::AniT.Core.AnimeTitleNormalizer.Normalize(alias);
            if (normalizedAlias.Length == 0 || !knownAliases.Add(normalizedAlias)) continue;
            anime.Aliases.Add(new global::AniT.Core.AnimeAlias
            {
                AnimeId = anime.Id,
                Alias = alias,
                NormalizedAlias = normalizedAlias,
                Source = global::AniT.Core.AnimeAliasSource.Imported
            });
        }
        await context.SaveChangesAsync(cancellationToken);
        return metadata;
    }

    public static async Task<int> ReprocessContentAsync(AdvancedReprocessTarget target, CancellationToken cancellationToken = default)
    {
        global::AniT.Infrastructure.AniTDiagnostics.Write("REPROCESSAR", $"Início: {target}");
        if (target == AdvancedReprocessTarget.Files)
        {
            int roots;
            await using (var context = OpenFreshDatabase())
                roots = await context.LibraryRoots.AsNoTracking().CountAsync(item => item.IsEnabled, cancellationToken);
            await RefreshConfiguredLibraryCoreAsync(reportFailures: true, cancellationToken);
            global::AniT.Infrastructure.AniTDiagnostics.Write("REPROCESSAR", $"Arquivos concluídos em {roots} pasta(s).");
            return roots;
        }

        List<global::AniT.Core.Anime> anime;
        await using (var context = OpenFreshDatabase())
            anime = await context.Anime.AsNoTracking().OrderBy(item => item.Title).ToListAsync(cancellationToken);
        if (target == AdvancedReprocessTarget.Images)
            await ClearArtworkCacheAsync(rebuild: true, cancellationToken);

        var completed = 0;
        foreach (var item in anime)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureAnimeMetadataAsync(
                item.Id, item.Title, item.EnglishTitle, item.CoverPath, cancellationToken,
                item.Synopsis, item.CriticScore, forceRefresh: true, item.Genres,
                item.ReleaseYear, item.Studio, refreshCatalogDetails: target == AdvancedReprocessTarget.Metadata,
                refreshArtwork: target == AdvancedReprocessTarget.Images);
            completed++;
        }
        if (target == AdvancedReprocessTarget.Images && Current?.Dispatcher is { HasShutdownStarted: false } dispatcher)
            _ = dispatcher.BeginInvoke(() => LibraryArtworkUpdated?.Invoke(null, EventArgs.Empty));
        global::AniT.Infrastructure.AniTDiagnostics.Write("REPROCESSAR", $"{target} concluído em {completed} título(s).");
        return completed;
    }

    public static string? GetCachedAnimeBannerPath(Guid animeId) => animeCoverProvider.GetCachedBannerPath(animeId);

    public static async Task<IReadOnlyList<string>> EnsureAnimeArtworkGalleryAsync(
        Guid animeId,
        string title,
        string? coverPath,
        string? bannerPath,
        CancellationToken cancellationToken = default)
    {
        var paths = await animeCoverProvider.EnsureArtworkGalleryAsync(animeId, title, coverPath, bannerPath, cancellationToken);
        var settings = global::AniT.Infrastructure.AniTSystemSettingsStore.Load();
        await ArtworkCacheManager.OptimizeAsync(paths, settings, cancellationToken);
        return paths;
    }

    public static string? GetLockedArtworkPath(Guid animeId)
    {
        var settings = global::AniT.Infrastructure.AniTSystemSettingsStore.Load();
        return settings.LockedArtwork is not null
               && settings.LockedArtwork.TryGetValue(animeId.ToString("D"), out var path)
               && File.Exists(path) ? path : null;
    }

    public static bool SetArtworkLock(Guid animeId, string? path)
    {
        var settings = global::AniT.Infrastructure.AniTSystemSettingsStore.Load();
        var locks = new Dictionary<string, string>(settings.LockedArtwork ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        var key = animeId.ToString("D");
        if (string.IsNullOrWhiteSpace(path)) locks.Remove(key);
        else if (File.Exists(path)) locks[key] = path;
        global::AniT.Infrastructure.AniTSystemSettingsStore.Save(settings with { LockedArtwork = locks });
        return locks.ContainsKey(key);
    }

    public static long GetArtworkCacheSizeBytes() => ArtworkCacheManager.GetSizeBytes(global::AniT.Infrastructure.AniTSystemSettingsStore.Load());

    public static Task ClearArtworkCacheAsync(bool rebuild, CancellationToken cancellationToken = default) =>
        ArtworkCacheManager.ClearAsync(global::AniT.Infrastructure.AniTSystemSettingsStore.Load(), rebuild, cancellationToken);

    public static bool ShouldRefreshAnimeBanner(Guid animeId) => animeCoverProvider.ShouldRefreshBanner(animeId);

    private static string ResolveBundledPlayerPath()
    {
        var deployed = global::System.IO.Path.Combine(AppContext.BaseDirectory, "Player", "MPC-HC", "mpc-hc64.exe");
        if (global::System.IO.File.Exists(deployed)) return deployed;
        return global::System.IO.Path.GetFullPath(global::System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Player", "MPC-HC", "mpc-hc64.exe"));
    }

    private static void InitializeMediaPlayer()
    {
        if (MediaPlayer is not null) return;
        var playerPath = ResolveBundledPlayerPath();
        if (!global::System.IO.File.Exists(playerPath)) return;
        MediaPlayer = new global::AniT.Player.MpcHcPlayer(playerPath);
        MediaPlayer.PositionChanged += async (_, progress) => await PersistProgressAsync(progress);
        MediaPlayer.PlaybackEnded += async (_, _) => await CompleteActiveEpisodeAsync();
        MediaPlayer.PlaybackClosed += async (_, progress) =>
        {
            playbackMonitorCancellation?.Cancel();
            await PersistProgressAsync(progress, force: true);
        };
        MediaPlayer.Diagnostic += (_, message) => WritePlaybackLog($"[MPC] {message}");
    }

    private static async Task PersistProgressAsync(global::AniT.Core.PlaybackPositionChangedEventArgs progress, bool force = false)
    {
        await playbackPersistenceLock.WaitAsync();
        try
        {
            var checkpointAt = DateTimeOffset.Now;
            var previousWrite = lastProgressWrite;
            var saveInterval = Math.Clamp(activePlaybackSettings.ProgressSaveIntervalSeconds, 2, 60);
            if (activeEpisodeId is not { } episodeId || (!force && checkpointAt.ToUniversalTime() - previousWrite < TimeSpan.FromSeconds(saveInterval))) return;
            lastProgressWrite = checkpointAt.ToUniversalTime();
            await using var context = OpenFreshDatabase();
            var episode = await context.Episodes.FirstOrDefaultAsync(item => item.Id == episodeId);
            if (episode is null) return;
            var position = progress.Position;
            if (position == TimeSpan.Zero && activePlaybackStartedAt != default)
            {
                position = activePlaybackStartPosition + (DateTimeOffset.UtcNow - activePlaybackStartedAt);
            }
            else if (progress.Position > TimeSpan.Zero)
            {
                activePlaybackStartPosition = progress.Position;
                activePlaybackStartedAt = DateTimeOffset.UtcNow;
            }
            var savedProgress = await context.PlaybackProgresses.SingleOrDefaultAsync(item => item.EpisodeId == episodeId);
            if (savedProgress is null)
            {
                savedProgress = new global::AniT.Core.PlaybackProgress { EpisodeId = episodeId };
                context.PlaybackProgresses.Add(savedProgress);
            }
            var previousPositionSeconds = Math.Max(0, savedProgress.PositionSeconds);
            savedProgress.PositionSeconds = Math.Max(0, position.TotalSeconds);
            savedProgress.DurationSeconds = progress.Duration?.TotalSeconds ?? savedProgress.DurationSeconds;
            savedProgress.LastPlayedAt = DateTimeOffset.UtcNow;
            var completedNow = false;
            if (savedProgress.DurationSeconds > 0 &&
                savedProgress.PositionSeconds / savedProgress.DurationSeconds * 100d >= activePlaybackSettings.WatchedThresholdPercent &&
                episode.Status != global::AniT.Core.WatchStatus.Completed)
            {
                episode.Status = global::AniT.Core.WatchStatus.Completed;
                episode.WatchedAt = DateTimeOffset.UtcNow;
                completedNow = true;
            }
            else if (episode.Status == global::AniT.Core.WatchStatus.NotStarted) episode.Status = global::AniT.Core.WatchStatus.Watching;
            await context.SaveChangesAsync();
            var positionDelta = Math.Max(0, savedProgress.PositionSeconds - previousPositionSeconds);
            var elapsedCap = previousWrite == default
                ? 15d
                : Math.Max(0, (checkpointAt.ToUniversalTime() - previousWrite).TotalSeconds + 2);
            var watchedSeconds = (long)Math.Floor(Math.Min(positionDelta, elapsedCap));
            if (watchedSeconds > 0)
                await Achievements.RecordWatchCheckpointAsync(watchedSeconds, checkpointAt);
            if (completedNow)
                await Achievements.RecordAsync(new global::AniT.Core.Achievements.AchievementEvent(
                    global::AniT.Core.Achievements.AchievementEventType.EpisodeCompleted,
                    episodeId));
            WritePlaybackLog($"[DB] Episódio {episodeId}: salvo em {savedProgress.PositionSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}s.");
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"AniT could not persist playback progress: {exception}");
            WritePlaybackLog($"[ERRO] Falha ao salvar o progresso: {exception.Message}");
        }
        finally
        {
            playbackPersistenceLock.Release();
        }
    }

    private static async Task MonitorPlaybackAsync(long session, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && session == Volatile.Read(ref playbackSession) && MediaPlayer is not null)
        {
            try
            {
                var pollingSeconds = activePlaybackSettings.SkipEnding ? 2 : Math.Clamp(activePlaybackSettings.ProgressSaveIntervalSeconds, 2, 60);
                await Task.Delay(TimeSpan.FromSeconds(pollingSeconds), cancellationToken);
                TimeSpan position;
                try
                {
                    position = await MediaPlayer.GetPositionAsync(cancellationToken);
                }
                catch
                {
                    position = activePlaybackStartPosition + (DateTimeOffset.UtcNow - activePlaybackStartedAt);
                }
                if (activePlaybackSettings.SkipEnding && activePlaybackSegments is { EndingStartSeconds: > 0, EndingEndSeconds: > 0 } segments &&
                    position.TotalSeconds >= segments.EndingStartSeconds && position.TotalSeconds < segments.EndingEndSeconds)
                {
                    position = TimeSpan.FromSeconds(segments.EndingEndSeconds);
                    await MediaPlayer.SeekAsync(position, cancellationToken);
                    activePlaybackStartPosition = position;
                    activePlaybackStartedAt = DateTimeOffset.UtcNow;
                }
                await PersistProgressAsync(new global::AniT.Core.PlaybackPositionChangedEventArgs(position, null));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // The player can close before its disconnect notification reaches the host window.
                // Persist the elapsed fallback once more instead of silently losing the session.
                await PersistProgressAsync(new global::AniT.Core.PlaybackPositionChangedEventArgs(
                    activePlaybackStartPosition + (DateTimeOffset.UtcNow - activePlaybackStartedAt), null), force: true);
                return;
            }
        }
    }

    private static async Task MarkEpisodeAsWatchingAsync(Guid episodeId)
    {
        await using var context = OpenFreshDatabase();
        var episode = await context.Episodes.FirstOrDefaultAsync(item => item.Id == episodeId);
        if (episode is null || episode.Status == global::AniT.Core.WatchStatus.Completed) return;
        episode.Status = global::AniT.Core.WatchStatus.Watching;
        if (!await context.PlaybackProgresses.AnyAsync(item => item.EpisodeId == episodeId))
        {
            context.PlaybackProgresses.Add(new global::AniT.Core.PlaybackProgress
            {
                EpisodeId = episodeId,
                PositionSeconds = 0,
                DurationSeconds = 0,
                LastPlayedAt = DateTimeOffset.UtcNow
            });
        }
        await context.SaveChangesAsync();
        WritePlaybackLog($"[DB] Episódio {episodeId}: checkpoint criado em 0s.");
    }

    private static void WritePlaybackLog(string message)
    {
        try
        {
            var path = global::System.IO.Path.Combine(global::System.IO.Path.GetDirectoryName(databasePath)!, "player.log");
            global::System.IO.File.AppendAllText(path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never disrupt playback.
        }
    }

    private static async Task CompleteActiveEpisodeAsync()
    {
        Guid? nextEpisodeId = null;
        await playbackPersistenceLock.WaitAsync();
        try
        {
            if (activeEpisodeId is not { } episodeId) return;
            await using var context = OpenFreshDatabase();
            var episode = await context.Episodes.Include(item => item.PlaybackProgress).Include(item => item.Season).FirstOrDefaultAsync(item => item.Id == episodeId);
            if (episode is null) return;
            var completedNow = episode.Status != global::AniT.Core.WatchStatus.Completed;
            episode.Status = global::AniT.Core.WatchStatus.Completed;
            episode.WatchedAt = DateTimeOffset.UtcNow;
            if (episode.PlaybackProgress is { DurationSeconds: > 0 } progress) progress.PositionSeconds = progress.DurationSeconds;
            if (activePlaybackSettings.AutoPlayNextEpisode) nextEpisodeId = await FindNextEpisodeIdAsync(context, episode);
            await context.SaveChangesAsync();
            playbackMonitorCancellation?.Cancel();
            if (completedNow)
                await Achievements.RecordAsync(new global::AniT.Core.Achievements.AchievementEvent(
                    global::AniT.Core.Achievements.AchievementEventType.EpisodeCompleted,
                    episodeId));
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"AniT could not complete playback: {exception}");
        }
        finally
        {
            playbackPersistenceLock.Release();
        }
        if (nextEpisodeId is { } nextId)
        {
            try { await PlayEpisodeAsync(nextId); }
            catch (Exception exception) { WritePlaybackLog($"[ERRO] Não foi possível iniciar o próximo episódio: {exception.Message}"); }
        }
    }

    private static int PreferredLanguageScore(global::AniT.Core.MediaFile file, string preferredLanguage)
    {
        if (preferredLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase)) return 0;
        var source = $"{file.Language} {file.FileName}";
        var aliases = preferredLanguage.ToLowerInvariant() switch
        {
            "pt-br" => new[] { "pt-br", "pt_br", "portuguese", "português", "brazil", "dub" },
            "ja" => new[] { "japanese", "japonês", "jpn", "[ja]", "_ja" },
            "en" => new[] { "english", "inglês", "eng", "[en]", "_en" },
            "es" => new[] { "spanish", "español", "espanhol", "spa", "[es]", "_es" },
            _ => new[] { preferredLanguage }
        };
        return aliases.Any(alias => source.Contains(alias, StringComparison.OrdinalIgnoreCase)) ? 1 : 0;
    }

    private static PlaybackSegments? LoadPlaybackSegments(string mediaPath)
    {
        var candidates = new[] { mediaPath + ".anit-segments.json", global::System.IO.Path.ChangeExtension(mediaPath, ".anit-segments.json") };
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!global::System.IO.File.Exists(candidate)) continue;
            try
            {
                var segments = global::System.Text.Json.JsonSerializer.Deserialize<PlaybackSegments>(
                    global::System.IO.File.ReadAllText(candidate),
                    new global::System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (segments is not null) return segments;
            }
            catch (Exception exception) when (exception is IOException or global::System.Text.Json.JsonException)
            {
                WritePlaybackLog($"[AVISO] Marcações ignoradas em {candidate}: {exception.Message}");
            }
        }
        return null;
    }

    private static async Task<Guid?> FindNextEpisodeIdAsync(global::AniT.Infrastructure.AniTDbContext context, global::AniT.Core.Episode current)
    {
        if (current.Season is null) return null;
        var animeId = current.Season.AnimeId;
        var seasonNumber = current.Season.Number;
        return await context.Episodes
            .Where(candidate => candidate.Season != null && candidate.Season.AnimeId == animeId &&
                (candidate.Season.Number > seasonNumber || candidate.Season.Number == seasonNumber && candidate.Number > current.Number) &&
                candidate.MediaFiles.Any(file => file.Availability == global::AniT.Core.MediaFileAvailability.Available))
            .OrderBy(candidate => candidate.Season!.Number)
            .ThenBy(candidate => candidate.Number)
            .Select(candidate => (Guid?)candidate.Id)
            .FirstOrDefaultAsync();
    }

    private sealed record PlaybackSegments(double OpeningStartSeconds, double OpeningEndSeconds, double EndingStartSeconds, double EndingEndSeconds);
}


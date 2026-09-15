using System.Configuration;
using System.Data;
using System.Windows;
using Microsoft.EntityFrameworkCore;

namespace AniT.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static global::AniT.Infrastructure.AniTDbContext Database { get; private set; } = null!;
    public static global::AniT.Player.MpcHcPlayer? MediaPlayer { get; private set; }
    private static global::AniT.Infrastructure.AnimeCoverProvider animeCoverProvider = null!;
    private static Guid? activeEpisodeId;
    private static DateTimeOffset lastProgressWrite;
    private static CancellationTokenSource? playbackMonitorCancellation;
    private static readonly SemaphoreSlim playbackPersistenceLock = new(1, 1);
    private static string databasePath = string.Empty;
    private static DateTimeOffset activePlaybackStartedAt;
    private static TimeSpan activePlaybackStartPosition;
    private static long playbackSession;

    /// <summary>
    /// Creates a short-lived database context for values that can be changed while a player is open.
    /// The application-level context is useful for normal screens, but it must not be used to read
    /// playback progress after the player has written it from its background callback.
    /// </summary>
    public static global::AniT.Infrastructure.AniTDbContext OpenFreshDatabase() => global::AniT.Infrastructure.AniTDatabase.Create(databasePath);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var dataDirectory = global::System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AniT", "Data");
        databasePath = global::System.IO.Path.Combine(dataDirectory, "anit.db");
        Database = global::AniT.Infrastructure.AniTDatabase.Create(databasePath);
        var coversDirectory = global::System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AniT", "Covers");
        animeCoverProvider = new global::AniT.Infrastructure.AnimeCoverProvider(coversDirectory);
        var playerPath = ResolveBundledPlayerPath();
        if (global::System.IO.File.Exists(playerPath))
        {
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

        var dashboard = new DashboardWindow();
        MainWindow = dashboard;
        dashboard.Show();
        if (!Database.LibraryRoots.Any())
        {
            _ = new SetupShelfWindow { Owner = dashboard }.ShowDialog();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        MediaPlayer?.Dispose();
        Database?.Dispose();
        base.OnExit(e);
    }

    public static async Task PlayEpisodeAsync(Guid episodeId)
    {
        if (MediaPlayer is null) throw new InvalidOperationException("O MPC-HC integrado não foi encontrado. Reinstale o AniT ou escolha outro player nas configurações.");
        await using var context = OpenFreshDatabase();
        var episode = await context.Episodes
            .Include(item => item.MediaFile)
            .ThenInclude(file => file!.LibraryRoot)
            .Include(item => item.PlaybackProgress)
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == episodeId)
            ?? throw new InvalidOperationException("O episódio não existe mais na biblioteca.");
        if (episode.MediaFile is null) throw new InvalidOperationException("Este episódio ainda não possui um arquivo de mídia associado.");

        activeEpisodeId = episodeId;
        TimeSpan? resumeAt = episode.PlaybackProgress is { PositionSeconds: > 0 } progress ? TimeSpan.FromSeconds(progress.PositionSeconds) : null;
        WritePlaybackLog($"[DB] Episódio {episodeId}: retomando de {resumeAt?.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "0"}s.");
        await MediaPlayer.PlayAsync(episode.MediaFile, resumeAt);
        activePlaybackStartPosition = resumeAt ?? TimeSpan.Zero;
        activePlaybackStartedAt = DateTimeOffset.UtcNow;
        await MarkEpisodeAsWatchingAsync(episodeId);
        playbackMonitorCancellation?.Cancel();
        playbackMonitorCancellation = new CancellationTokenSource();
        var session = Interlocked.Increment(ref playbackSession);
        _ = MonitorPlaybackAsync(session, playbackMonitorCancellation.Token);
    }

    public static async Task<string?> EnsureAnimeCoverAsync(
        Guid animeId,
        string title,
        string? savedCoverPath,
        CancellationToken cancellationToken = default)
    {
        var coverPath = await animeCoverProvider.EnsureCoverAsync(animeId, title, savedCoverPath, cancellationToken);
        if (coverPath is null || string.Equals(coverPath, savedCoverPath, StringComparison.OrdinalIgnoreCase)) return coverPath;

        await using var context = OpenFreshDatabase();
        var anime = await context.Anime.FirstOrDefaultAsync(item => item.Id == animeId, cancellationToken);
        if (anime is null) return coverPath;
        anime.CoverPath = coverPath;
        await context.SaveChangesAsync(cancellationToken);
        return coverPath;
    }

    private static string ResolveBundledPlayerPath()
    {
        var deployed = global::System.IO.Path.Combine(AppContext.BaseDirectory, "Player", "MPC-HC", "mpc-hc64.exe");
        if (global::System.IO.File.Exists(deployed)) return deployed;
        return global::System.IO.Path.GetFullPath(global::System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Player", "MPC-HC", "mpc-hc64.exe"));
    }

    private static async Task PersistProgressAsync(global::AniT.Core.PlaybackPositionChangedEventArgs progress, bool force = false)
    {
        await playbackPersistenceLock.WaitAsync();
        try
        {
            if (activeEpisodeId is not { } episodeId || (!force && DateTimeOffset.UtcNow - lastProgressWrite < TimeSpan.FromSeconds(5))) return;
            lastProgressWrite = DateTimeOffset.UtcNow;
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
            savedProgress.PositionSeconds = Math.Max(0, position.TotalSeconds);
            savedProgress.DurationSeconds = progress.Duration?.TotalSeconds ?? savedProgress.DurationSeconds;
            savedProgress.LastPlayedAt = DateTimeOffset.UtcNow;
            if (episode.Status == global::AniT.Core.WatchStatus.NotStarted) episode.Status = global::AniT.Core.WatchStatus.Watching;
            await context.SaveChangesAsync();
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
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                TimeSpan position;
                try
                {
                    position = await MediaPlayer.GetPositionAsync(cancellationToken);
                }
                catch
                {
                    position = activePlaybackStartPosition + (DateTimeOffset.UtcNow - activePlaybackStartedAt);
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
        await playbackPersistenceLock.WaitAsync();
        try
        {
            if (activeEpisodeId is not { } episodeId) return;
            await using var context = OpenFreshDatabase();
            var episode = await context.Episodes.Include(item => item.PlaybackProgress).FirstOrDefaultAsync(item => item.Id == episodeId);
            if (episode is null) return;
            episode.Status = global::AniT.Core.WatchStatus.Completed;
            episode.WatchedAt = DateTimeOffset.UtcNow;
            if (episode.PlaybackProgress is { DurationSeconds: > 0 } progress) progress.PositionSeconds = progress.DurationSeconds;
            await context.SaveChangesAsync();
            playbackMonitorCancellation?.Cancel();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"AniT could not complete playback: {exception}");
        }
        finally
        {
            playbackPersistenceLock.Release();
        }
    }
}


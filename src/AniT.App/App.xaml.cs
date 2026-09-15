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
    private static Guid? activeEpisodeId;
    private static DateTimeOffset lastProgressWrite;
    private static CancellationTokenSource? playbackMonitorCancellation;
    private static readonly SemaphoreSlim playbackPersistenceLock = new(1, 1);
    private static string databasePath = string.Empty;
    private static DateTimeOffset activePlaybackStartedAt;
    private static TimeSpan activePlaybackStartPosition;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var dataDirectory = global::System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AniT", "Data");
        databasePath = global::System.IO.Path.Combine(dataDirectory, "anit.db");
        Database = global::AniT.Infrastructure.AniTDatabase.Create(databasePath);
        var playerPath = ResolveBundledPlayerPath();
        if (global::System.IO.File.Exists(playerPath))
        {
            MediaPlayer = new global::AniT.Player.MpcHcPlayer(playerPath);
            MediaPlayer.PositionChanged += async (_, progress) => await PersistProgressAsync(progress);
            MediaPlayer.PlaybackEnded += async (_, _) => await CompleteActiveEpisodeAsync();
            MediaPlayer.PlaybackClosed += async (_, progress) => await PersistProgressAsync(progress, force: true);
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
        var episode = await Database.Episodes
            .Include(item => item.MediaFile)
            .ThenInclude(file => file!.LibraryRoot)
            .Include(item => item.PlaybackProgress)
            .FirstOrDefaultAsync(item => item.Id == episodeId)
            ?? throw new InvalidOperationException("O episódio não existe mais na biblioteca.");
        if (episode.MediaFile is null) throw new InvalidOperationException("Este episódio ainda não possui um arquivo de mídia associado.");

        activeEpisodeId = episodeId;
        TimeSpan? resumeAt = episode.PlaybackProgress is { PositionSeconds: > 0 } progress ? TimeSpan.FromSeconds(progress.PositionSeconds) : null;
        await MediaPlayer.PlayAsync(episode.MediaFile, resumeAt);
        activePlaybackStartPosition = resumeAt ?? TimeSpan.Zero;
        activePlaybackStartedAt = DateTimeOffset.UtcNow;
        await MarkEpisodeAsWatchingAsync(episodeId);
        playbackMonitorCancellation?.Cancel();
        playbackMonitorCancellation = new CancellationTokenSource();
        _ = MonitorPlaybackAsync(playbackMonitorCancellation.Token);
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
            using var context = global::AniT.Infrastructure.AniTDatabase.Create(databasePath);
            var episode = await context.Episodes.Include(item => item.PlaybackProgress).FirstOrDefaultAsync(item => item.Id == episodeId);
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
            episode.PlaybackProgress ??= new global::AniT.Core.PlaybackProgress { EpisodeId = episodeId };
            episode.PlaybackProgress.PositionSeconds = position.TotalSeconds;
            episode.PlaybackProgress.DurationSeconds = progress.Duration?.TotalSeconds ?? 0;
            episode.PlaybackProgress.LastPlayedAt = DateTimeOffset.UtcNow;
            if (episode.Status == global::AniT.Core.WatchStatus.NotStarted) episode.Status = global::AniT.Core.WatchStatus.Watching;
            await context.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"AniT could not persist playback progress: {exception}");
        }
        finally
        {
            playbackPersistenceLock.Release();
        }
    }

    private static async Task MonitorPlaybackAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && MediaPlayer is not null)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
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
                return;
            }
        }
    }

    private static async Task MarkEpisodeAsWatchingAsync(Guid episodeId)
    {
        using var context = global::AniT.Infrastructure.AniTDatabase.Create(databasePath);
        var episode = await context.Episodes.FirstOrDefaultAsync(item => item.Id == episodeId);
        if (episode is null || episode.Status == global::AniT.Core.WatchStatus.Completed) return;
        episode.Status = global::AniT.Core.WatchStatus.Watching;
        await context.SaveChangesAsync();
    }

    private static async Task CompleteActiveEpisodeAsync()
    {
        await playbackPersistenceLock.WaitAsync();
        try
        {
            if (activeEpisodeId is not { } episodeId) return;
            using var context = global::AniT.Infrastructure.AniTDatabase.Create(databasePath);
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


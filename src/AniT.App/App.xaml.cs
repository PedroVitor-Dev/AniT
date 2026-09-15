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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var dataDirectory = global::System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AniT", "Data");
        Database = global::AniT.Infrastructure.AniTDatabase.Create(global::System.IO.Path.Combine(dataDirectory, "anit.db"));
        var playerPath = ResolveBundledPlayerPath();
        if (global::System.IO.File.Exists(playerPath))
        {
            MediaPlayer = new global::AniT.Player.MpcHcPlayer(playerPath);
            MediaPlayer.PositionChanged += async (_, progress) => await PersistProgressAsync(progress);
            MediaPlayer.PlaybackEnded += async (_, _) => await CompleteActiveEpisodeAsync();
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

    private static async Task PersistProgressAsync(global::AniT.Core.PlaybackPositionChangedEventArgs progress)
    {
        if (activeEpisodeId is not { } episodeId || DateTimeOffset.UtcNow - lastProgressWrite < TimeSpan.FromSeconds(15)) return;
        lastProgressWrite = DateTimeOffset.UtcNow;
        var episode = await Database.Episodes.Include(item => item.PlaybackProgress).FirstOrDefaultAsync(item => item.Id == episodeId);
        if (episode is null) return;
        episode.PlaybackProgress ??= new global::AniT.Core.PlaybackProgress { EpisodeId = episodeId };
        episode.PlaybackProgress.PositionSeconds = progress.Position.TotalSeconds;
        episode.PlaybackProgress.DurationSeconds = progress.Duration?.TotalSeconds ?? 0;
        episode.PlaybackProgress.LastPlayedAt = DateTimeOffset.UtcNow;
        if (episode.Status == global::AniT.Core.WatchStatus.NotStarted) episode.Status = global::AniT.Core.WatchStatus.Watching;
        await Database.SaveChangesAsync();
    }

    private static async Task MonitorPlaybackAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && MediaPlayer is not null)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
                var position = await MediaPlayer.GetPositionAsync(cancellationToken);
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

    private static async Task CompleteActiveEpisodeAsync()
    {
        if (activeEpisodeId is not { } episodeId) return;
        var episode = await Database.Episodes.Include(item => item.PlaybackProgress).FirstOrDefaultAsync(item => item.Id == episodeId);
        if (episode is null) return;
        episode.Status = global::AniT.Core.WatchStatus.Completed;
        episode.WatchedAt = DateTimeOffset.UtcNow;
        if (episode.PlaybackProgress is { DurationSeconds: > 0 } progress) progress.PositionSeconds = progress.DurationSeconds;
        await Database.SaveChangesAsync();
        playbackMonitorCancellation?.Cancel();
    }
}


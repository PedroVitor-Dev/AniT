using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AniT.Core;
using AniT.Infrastructure;

namespace AniT.App;

public partial class App
{
    static string onlinePlayerTitle = "Online Anime Player";
    static bool onlinePlayerFullscreen;
    static Guid? onlinePlayerEpisode;
    static OnlinePlayerWindow? onlinePlayerWindow;
    static OnlinePlayerPreferencesStore OnlinePlayerPreferences()=>new(Path.Combine(dataRootPath,"Online",ProfileSettingsStore.ActiveProfileId.ToString("N"),"player-preferences.json"));
    static void ShowOnlinePlayer(global::AniT.Player.MpvOnlinePlayer player)
    {
        onlinePlayerWindow?.CloseFromPlayer();
        var window = new OnlinePlayerWindow(player, onlinePlayerTitle, OnlinePlayerPreferences(), new PlaybackMomentStore(Path.Combine(dataRootPath,"Online",ProfileSettingsStore.ActiveProfileId.ToString("N"),"moments.json")), onlinePlayerEpisode);
        onlinePlayerWindow = window;
        window.Closed += (_, _) => { if (ReferenceEquals(onlinePlayerWindow, window)) onlinePlayerWindow = null; };
        window.Show();
        player.AttachVideoWindow(window.VideoHandle);
        if (onlinePlayerFullscreen) window.SetFullscreen(true);
    }
    static OnlineAnimeService? online;
    static OnlineTraceJournal? onlineJournal;
    static OnlinePlaybackTrace? activeOnlineTrace;
    static OnlinePlaybackTrace BeginOnlineTrace(long clickedAt)
    {
        lock (onlineServiceSync) {
            activeOnlineTrace?.Finish("replaced");
            onlineJournal ??= new OnlineTraceJournal(Path.Combine(dataRootPath, "Diagnostics", "Online"));
            var journal = onlineJournal;
            activeOnlineTrace = new OnlinePlaybackTrace(journal.Write, startTimestamp: clickedAt);
            activeOnlineTrace.Mark("episode_resolved");
            return activeOnlineTrace;
        }
    }
    static CancellationTokenSource? onlineMonitor;
    static Task? onlineMonitorTask;
    static readonly object onlineServiceSync = new();
    public static OnlineAnimeService Online { get { lock (onlineServiceSync) return online ??= CreateOnlineService(); } }

    static void ResumeOnlineDownloads()
    {
        var saved = Path.Combine(dataRootPath, "Online", ProfileSettingsStore.ActiveProfileId.ToString("N"), "downloads.json");
        try { if (File.Exists(saved)) _ = Online; }
        catch (Exception ex) { global::AniT.Infrastructure.AniTDiagnostics.Write("ONLINE", ex.Message); }
    }

    static OnlineAnimeService CreateOnlineService()
    {
        var service = new OnlineAnimeService(databasePath, Path.Combine(dataRootPath, "Online", ProfileSettingsStore.ActiveProfileId.ToString("N")));
        service.Diagnostic = message => WritePlaybackLog("[ONLINE] " + message);
        onlineMonitor = new CancellationTokenSource();
        var token = onlineMonitor.Token;
        onlineMonitorTask = Task.Run(async () => {
            while (!token.IsCancellationRequested) {
                try { await service.SnapshotAsync(token); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { global::AniT.Infrastructure.AniTDiagnostics.Write("ONLINE", ex.Message); }
                try { await Task.Delay(TimeSpan.FromSeconds(3), token); } catch (OperationCanceledException) { break; }
            }
        });
        return service;
    }

    public static async Task StopOnlineAsync()
    {
        OnlineAnimeService? service; CancellationTokenSource? monitor; Task? monitoring;
        OnlinePlaybackTrace? trace; OnlineTraceJournal? journal;
        lock (onlineServiceSync) {
            service = online; online = null;
            monitor = onlineMonitor; onlineMonitor = null;
            monitoring = onlineMonitorTask; onlineMonitorTask = null;
            trace = activeOnlineTrace; activeOnlineTrace = null;
            journal = onlineJournal; onlineJournal = null;
        }
        monitor?.Cancel();
        if (monitoring is not null) await monitoring.ConfigureAwait(false);
        if (service is not null) await service.DisposeAsync().ConfigureAwait(false);
        trace?.Finish("service_stopped");
        if (journal is not null) await journal.DisposeAsync().ConfigureAwait(false);
        monitor?.Dispose();
    }

    static async Task PauseOnlinePlaybackAsync(Uri? expectedAddress = null)
    {
        if (online is not { } service) return;
        try { await Task.Run(() => service.PausePlaybackAsync(expectedAddress)); }
        catch (Exception ex) { WritePlaybackLog("[ONLINE] Ao pausar episódio: " + ex.Message); }
    }

    static async Task<Uri> PrepareOnlineStreamAsync(MediaFile file, OnlinePlaybackTrace trace)
    {
        // A modeless preparation surface leaves cancellation accessible while peers supply the first pieces.
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var window = new OnlineBufferWindow(timeout);
        window.Show();
        var progress = new Progress<OnlinePreparationProgress>(window.Report);
        trace.Mark("preparation_window_shown");
        try {
            return await Task.Run(async () => {
                timeout.Token.ThrowIfCancellationRequested();
                var service = Online; trace.Mark("online_service_ready");
                timeout.Token.ThrowIfCancellationRequested();
                return await service.OpenStreamAsync(file, timeout.Token, progress, trace);
            });
        }
        catch (OperationCanceledException) { trace.Finish("preparation_cancelled"); throw new InvalidOperationException("Preparação interrompida. Verifique a disponibilidade do torrent em Online Anime Player e tente novamente."); }
        catch { trace.Finish("preparation_failed"); throw; }
        finally { window.Close(); }
    }
}

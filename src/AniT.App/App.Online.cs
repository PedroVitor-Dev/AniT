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
    static OnlineAnimeService? online;
    static OnlineTraceJournal? onlineJournal;
    static OnlinePlaybackTrace? activeOnlineTrace;
    static OnlinePlaybackTrace BeginOnlineTrace(long clickedAt)
    {
        activeOnlineTrace?.Finish("replaced");
        onlineJournal ??= new OnlineTraceJournal(Path.Combine(dataRootPath, "Diagnostics", "Online"));
        var journal = onlineJournal;
        activeOnlineTrace = new OnlinePlaybackTrace(journal.Write, startTimestamp: clickedAt);
        activeOnlineTrace.Mark("episode_resolved");
        return activeOnlineTrace;
    }
    static CancellationTokenSource? onlineMonitor;
    static Task? onlineMonitorTask;
    public static OnlineAnimeService Online => online ??= CreateOnlineService();

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
        onlineMonitor?.Cancel();
        if (onlineMonitorTask is not null) await onlineMonitorTask.ConfigureAwait(false);
        if (online is { } service) { online = null; await service.DisposeAsync().ConfigureAwait(false); }
        activeOnlineTrace?.Finish("service_stopped"); activeOnlineTrace = null;
        if (onlineJournal is { } journal) { onlineJournal = null; await journal.DisposeAsync().ConfigureAwait(false); }
        onlineMonitor?.Dispose(); onlineMonitor = null; onlineMonitorTask = null;
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
        try { var service = Online; trace.Mark("online_service_ready"); return await Task.Run(() => service.OpenStreamAsync(file, timeout.Token, progress, trace)); }
        catch (OperationCanceledException) { trace.Finish("preparation_cancelled"); throw new InvalidOperationException("Preparação interrompida. Verifique a disponibilidade do torrent em Online Anime Player e tente novamente."); }
        catch { trace.Finish("preparation_failed"); throw; }
        finally { window.Close(); }
    }
}

internal sealed class OnlineBufferWindow : Window
{
    readonly System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
    readonly System.Windows.Controls.TextBlock transferStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    readonly System.Windows.Controls.ProgressBar transferBar = new() { IsIndeterminate = true, Height = 5 };

    public void Report(OnlinePreparationProgress progress)
    {
        if (!IsLoaded) return;
        transferStatus.Text = progress.Error is { } error ? "Falha na conexão: " + error
            : $"{progress.Peers} conexões · {progress.BytesPerSecond / 1024d:0} KB/s · {elapsed.Elapsed.Minutes:00}:{elapsed.Elapsed.Seconds:00} de espera\n"
              + (progress.Peers == 0 ? "Procurando fontes para este episódio…" : $"{progress.ReceivedBytes / (1024d * 1024):0.0} MB recebidos · {progress.Percent:0.0}% do episódio verificado");
        if (progress.InitialPieceBytes > 0)
            transferStatus.Text += $"\nBloco inicial: {progress.InitialPieceBytes / (1024d * 1024):0.##} MB · "
                + (progress.InitialPieceReady ? "verificado" : "aguardando uma peça completa para verificar");
        if (progress.Stage is { } stage) transferStatus.Text = stage + "\n" + transferStatus.Text;
        transferBar.IsIndeterminate = true;
    }

    public OnlineBufferWindow(CancellationTokenSource cancellation)
    {
        Title = "Preparando streaming · AniT"; Width = 480; Height = 380; MinWidth = 360; MinHeight = 330;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        StableWindowRendering.SetEnabled(this, true);
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/AniT;component/Styles/TheaterTheme.xaml", UriKind.Relative) });
        SetResourceReference(BackgroundProperty, "AniT.Brush.Background");
        SetResourceReference(ForegroundProperty, "AniT.Brush.TextPrimary");
        var panel = new System.Windows.Controls.Grid { Margin = new Thickness(24) };
        panel.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Preparando seu episódio", FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var body = new System.Windows.Controls.StackPanel();
        body.Children.Add(new System.Windows.Controls.TextBlock { Text = "Cloud · Baixando somente o episódio que você abriu. Priorizando o começo do vídeo; os outros episódios ficam aguardando.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) });
        body.Children.Add(transferBar); body.Children.Add(transferStatus);
        var scroll = new System.Windows.Controls.ScrollViewer { Content = body, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled };
        System.Windows.Controls.Grid.SetRow(scroll, 1); panel.Children.Add(scroll);
        var cancel = new System.Windows.Controls.Button { Content = "Cancelar", Style = (Style)FindResource("CinemaAction"), Margin = new Thickness(0, 18, 0, 0), Padding = new Thickness(12, 7, 12, 7), MinHeight = 40 };
        cancel.Click += (_, _) => cancellation.Cancel();
        System.Windows.Controls.Grid.SetRow(cancel, 2); panel.Children.Add(cancel); Content = panel;
        Closing += (_, _) => cancellation.Cancel();
    }
}

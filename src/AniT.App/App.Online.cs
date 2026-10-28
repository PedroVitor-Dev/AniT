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
        onlineMonitor?.Dispose(); onlineMonitor = null; onlineMonitorTask = null;
    }

    static async Task<Uri> PrepareOnlineStreamAsync(MediaFile file)
    {
        // A modeless preparation surface leaves cancellation accessible while peers supply the first pieces.
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var window = new OnlineBufferWindow(timeout);
        window.Show();
        try { var service = Online; return await Task.Run(() => service.OpenStreamAsync(file, timeout.Token)); }
        catch (OperationCanceledException) { throw new InvalidOperationException("Preparação interrompida. Verifique a disponibilidade do torrent em Online Anime Player e tente novamente."); }
        finally { window.Close(); }
    }
}

internal sealed class OnlineBufferWindow : Window
{
    public OnlineBufferWindow(CancellationTokenSource cancellation)
    {
        Title = "Preparando streaming · AniT"; Width = 440; Height = 240; MinWidth = 340; MinHeight = 220;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "AniT.Brush.Background");
        SetResourceReference(ForegroundProperty, "AniT.Brush.TextPrimary");
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Preparando seu episódio", FontSize = 22, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Cloud · Baixando os trechos iniciais e o índice do vídeo. A disponibilidade depende dos participantes do torrent.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) });
        panel.Children.Add(new System.Windows.Controls.ProgressBar { IsIndeterminate = true, Height = 5 });
        var cancel = new System.Windows.Controls.Button { Content = "Cancelar", Margin = new Thickness(0, 18, 0, 0), Padding = new Thickness(12, 7, 12, 7) };
        cancel.Click += (_, _) => cancellation.Cancel(); panel.Children.Add(cancel); Content = panel;
        Closing += (_, _) => cancellation.Cancel();
    }
}

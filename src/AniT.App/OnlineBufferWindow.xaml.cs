using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AniT.Infrastructure;

namespace AniT.App;

internal partial class OnlineBufferWindow : Window
{
    readonly Stopwatch elapsed = Stopwatch.StartNew();
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly CancellationTokenSource cancellation;
    OnlinePreparationProgress? latest;
    bool closed;
    string mascot = "loading";

    public OnlineBufferWindow(CancellationTokenSource cancellation)
    {
        this.cancellation = cancellation;
        InitializeComponent();
        StableWindowRendering.SetEnabled(this, true);
        clock.Tick += (_, _) => RefreshStatus();
        Loaded += (_, _) => { RefreshStatus(); clock.Start(); };
        Closed += (_, _) => { closed = true; clock.Stop(); };
        Closing += (_, _) => cancellation.Cancel();
    }
    public void Report(OnlinePreparationProgress progress)
    {
        if (closed) return;
        latest = progress;
        if (IsLoaded) RefreshStatus();
    }
    void CancelClick(object sender, RoutedEventArgs args)
    {
        cancellation.Cancel();
        CancelAction.IsEnabled = false;
        CancelAction.Content = "Cancelando…";
    }
    void RefreshStatus()
    {
        var progress = latest ?? new OnlinePreparationProgress(0, 0, 0, 0, null,
            Stage: "Organizando a abertura do seu episódio…", TransferStarted: false);
        var waiting = $"{(int)elapsed.Elapsed.TotalMinutes:00}:{elapsed.Elapsed.Seconds:00} de espera";
        StageText.Text = progress.Error is { } error ? "Falha na conexão: " + error
            : progress.Stage ?? (progress.TransferStarted ? progress.Peers == 0 ? "Procurando fontes para este episódio…" : "Recebendo os primeiros trechos…" : "Preparando seu episódio…");
        TransferStatus.Text = !progress.TransferStarted ? waiting
            : $"{progress.Peers} conexões · {progress.BytesPerSecond / 1024d:0} KB/s · {waiting}\n"
                + (progress.Peers == 0 ? "Procurando fontes para este episódio…" : $"{progress.ReceivedBytes / (1024d * 1024):0.0} MB recebidos · {progress.Percent:0.0}% do episódio verificado");
        if (progress.TransferStarted && progress.InitialPieceBytes > 0)
            TransferStatus.Text += $"\nBloco inicial: {progress.InitialPieceBytes / (1024d * 1024):0.##} MB · "
                + (progress.InitialPieceReady ? "verificado" : "aguardando uma peça completa para verificar");
        // A whole-file percentage is not a measure of preparation. Never fabricate a countdown.
        TransferBar.IsIndeterminate = progress.Error is null && !AppearanceManager.MotionReduced;
        TransferBar.Visibility = AppearanceManager.MotionReduced ? Visibility.Collapsed : Visibility.Visible;
        TransferBar.Value = 0;
        HintText.Text = progress.Error is not null ? "Cancele e tente novamente para procurar novas fontes."
            : progress.TransferStarted && progress.Peers == 0 ? "Ainda sem fontes conectadas. A disponibilidade depende de quem está compartilhando."
            : "O vídeo abre assim que os trechos necessários estiverem disponíveis.";
        var next = progress.Error is not null ? "error"
            : progress.TransferStarted && progress.Peers == 0 ? "offline" : "loading";
        if (next != mascot) {
            mascot = next;
            Mascot.Source = new BitmapImage(new Uri($"pack://application:,,,/AniT;component/Assets/Player/{next}.png"));
        }
    }
}

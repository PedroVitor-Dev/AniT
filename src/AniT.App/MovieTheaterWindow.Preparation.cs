using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using AniT.Theater;

namespace AniT.App;

public partial class MovieTheaterWindow
{
    private string? pendingDownloadPath;
    private bool advancingPreparation, automaticOpenAttempted;
    private bool? lastInviteConnection;

    internal async Task ScheduleP2pDownloadAsync(string destination)
    {
        if (room is not { Mode: TheaterMode.PeerSync, IsHost: false } || busy) return;
        pendingDownloadPath = destination;
        TheaterStatus.Text = room.PreparationStarted ? "Destino escolhido. O AniT vai baixar e verificar o episódio." : "Destino escolhido. Aguarde o host iniciar a preparação para o download começar.";
        UpdateControls(); await AdvanceP2pPreparationAsync();
    }

    private async Task AdvanceP2pPreparationAsync()
    {
        if (closed || busy || advancingPreparation || room is not { Mode: TheaterMode.PeerSync, IsHost: false, IsConnected: true, PreparationStarted: true } active || !active.Clock.IsReady) return;
        if (active.HasMedia ? prepared || automaticOpenAttempted || selectedPath is null : pendingDownloadPath is null) return;
        advancingPreparation = true;
        try
        {
            if (!active.HasMedia && pendingDownloadPath is { } destination)
            {
                pendingDownloadPath = null;
                await DownloadMediaAsync(destination);
            }
            if (!closed && ReferenceEquals(active, room) && active.IsConnected && active.HasMedia && selectedPath is not null && !prepared && !automaticOpenAttempted)
            {
                automaticOpenAttempted = true;
                await RunAsync(OpenSessionVideoAsync);
            }
        }
        finally { advancingPreparation = false; if (!closed) UpdateControls(); }
    }

    private async void ConfirmPresence_Click(object sender, RoutedEventArgs e) => await ConfirmPresenceAsync();

    private Task ConfirmPresenceAsync() => RunAsync(async token =>
    {
        if (room is null) return;
        await room.ConfirmParticipationAsync(token);
        TheaterStatus.Text = "Presença confirmada. O vídeo continua pausado até o host autorizar o início.";
    });

    private void UpdateP2pPreparationView()
    {
        if (closed || room is not { Mode: TheaterMode.PeerSync } active) return;
        if (active.IsHost && lastInviteConnection != active.IsConnected) { HostInvitePanel.IsExpanded = !active.IsConnected; lastInviteConnection = active.IsConnected; }
        var guestHasFile = active.IsHost ? active.RemoteHasMedia : active.HasMedia;
        var progress = active.DownloadProgress;
        var bytes = guestHasFile ? active.Episode.Length : progress.Bytes;
        var percent = Math.Clamp(bytes * 100d / active.Episode.Length, 0, 100);
        var hostNow = ClockEstimator.NowMs + (active.IsHost ? 0 : active.Clock.OffsetMs);
        var deadline = active.IsHost ? scheduledStart : active.LatestState is { Playing: true } state ? state.HostTimeMs : 0;
        var countdown = active.PlaybackAuthorized && deadline > hostNow;
        var seconds = Math.Max(0, (int)Math.Ceiling((deadline - hostNow) / 1000d));
        var unlocked = active.PlaybackAuthorized && active.BothReady && !countdown;

        P2pLockLabel.Text = !active.IsConnected ? "Bloqueado · aguardando conexão" : countdown ? $"Começa em {seconds}…" : unlocked ? "Sessão liberada pelo host" : "Início bloqueado";
        P2pLockIcon.Stroke = (System.Windows.Media.Brush)FindResource(unlocked ? "AniT.Brush.Success" : "AniT.Brush.Warning");
        P2pLockIcon.Data = System.Windows.Media.Geometry.Parse(unlocked ? "M17,10 V6 C17,0 7,0 7,6 M5,10 H19 V22 H5 Z" : "M7,10 V7 C7,0 17,0 17,7 V10 M5,10 H19 V22 H5 Z");
        P2pSessionProgress.Value = unlocked ? 100 : countdown ? 95 : active.GuestConfirmed ? 90 : active.BothReady ? 80 : active.PreparationStarted ? 20 + percent * .5 : 0;
        P2pElapsedLabel.Text = active.PreparationStarted ? $"Tempo de preparação: {Format(TimeSpan.FromMilliseconds(Math.Max(0, (active.ApprovedStartAtMs > 0 ? Math.Min(hostNow, active.ApprovedStartAtMs) : hostNow) - active.PreparationStartedAtMs)))}" : "A preparação começa quando o host liberar.";
        P2pStepRelease.Text = active.PreparationStarted ? "✓ 1. Host liberou a preparação" : "1. Aguardando o host liberar a preparação";
        P2pStepFile.Text = guestHasFile ? "✓ 2. Arquivo do convidado verificado" : progress.Phase == EpisodeTransferPhase.Verifying ? "2. Download completo · conferindo SHA-256…" : bytes > 0 ? $"2. Baixando e verificando · {percent:0}%" : "2. Aguardando download ou cópia local";
        P2pStepConfirmation.Text = active.GuestConfirmed ? "✓ 3. Convidado confirmou presença" : active.BothReady ? "3. Aguardando confirmação do convidado" : "3. Confirmação bloqueada até abrir o vídeo";
        P2pStepApproval.Text = active.PlaybackAuthorized ? "✓ 4. Host autorizou a reprodução" : "4. Somente o host pode autorizar o início";
        P2pStepCountdown.Text = countdown ? $"5. Contagem sincronizada · {seconds} segundos" : unlocked ? "✓ 5. Reprodução sob controle do host" : "5. Contagem regressiva aguardando autorização";
        P2pNextAction.Text = !active.IsConnected ? active.IsHost ? "Aguarde o convidado entrar. Você pode liberar a preparação enquanto aguarda." : "A conexão foi interrompida. Saia e entre novamente; o início permanece bloqueado." : !active.PreparationStarted
            ? active.IsHost ? "Clique em Iniciar preparação para abrir seu vídeo pausado e liberar os downloads." : "O host ainda não liberou. Você pode escolher onde salvar enquanto aguarda."
            : !guestHasFile ? active.IsHost ? "Aguarde o convidado baixar e verificar o episódio. O início continua bloqueado." : pendingDownloadPath is not null ? $"Destino escolhido: {Path.GetFileName(pendingDownloadPath)}. O download começará automaticamente." : active.IsDownloading ? "O AniT está recebendo o episódio. Depois da verificação, o vídeo abrirá pausado." : "Escolha onde salvar para o AniT baixar, verificar e abrir o episódio."
            : !active.BothReady ? !active.LocalReady && !busy && (automaticOpenAttempted || active.IsHost && active.PreparationStarted) ? "O vídeo não está aberto. Use Reabrir episódio abaixo para tentar novamente; o início continua bloqueado." : "O AniT está abrindo os vídeos pausados. Aguarde todos os computadores ficarem prontos."
            : !active.GuestConfirmed ? active.IsHost ? "Os vídeos estão abertos. Aguarde a confirmação de presença do convidado." : "Seu vídeo está aberto e pausado. Clique em Confirmar presença para avisar o host."
            : !active.PlaybackAuthorized ? active.IsHost ? "Tudo pronto. Clique em Autorizar e iniciar para dar o aval e iniciar a contagem de 5 segundos." : "Você confirmou presença. Aguarde o aval do host; a sessão não começa sozinha."
            : countdown ? "O host autorizou. Todos os computadores seguem a mesma contagem regressiva." : "Somente o host controla play, pausa, posição, áudio e legendas.";

        TransferProgress.Value = percent;
        TransferLabel.Text = guestHasFile ? "Arquivo OK · cópia completa verificada" : progress.Phase == EpisodeTransferPhase.Verifying ? "100% recebido · verificando o conteúdo completo…" : $"{percent:0}% · {bytes / 1048576d:0.0} / {active.Episode.Length / 1048576d:0.0} MB recebidos";
        var elapsed = active.TransferStartedAtMs > 0 ? Math.Max(0, (active.TransferFinishedAtMs > 0 ? active.TransferFinishedAtMs : ClockEstimator.NowMs) - active.TransferStartedAtMs) / 1000d : 0;
        var rate = elapsed > 0 ? bytes / elapsed : 0;
        TransferTimingLabel.Text = elapsed <= 0 ? guestHasFile ? "Cópia local verificada · download não necessário." : !active.PreparationStarted ? "Download bloqueado até a liberação do host." : "Download aguardando a escolha do destino no convidado." : guestHasFile ? $"Download verificado · tempo decorrido {Format(TimeSpan.FromSeconds(elapsed))}" : $"Tempo decorrido {Format(TimeSpan.FromSeconds(elapsed))} · média {rate / 1048576d:0.0} MB/s";
        DownloadButton.Content = pendingDownloadPath is null ? "_Escolher onde salvar" : "_Alterar destino";
        DownloadButton.IsEnabled = !busy && active.IsConnected && active.SharingAvailable && !active.HasMedia;
        PreparePlayerButton.Visibility = active.IsHost && !active.LocalReady || !active.IsHost && active.IsConnected && active.PreparationStarted && active.HasMedia && automaticOpenAttempted && !prepared ? Visibility.Visible : Visibility.Collapsed;
        PreparePlayerButton.Content = active.IsHost && !active.PreparationStarted ? "_Iniciar preparação" : "_Reabrir episódio";
        ConfirmPresenceButton.Visibility = !active.IsHost && !active.GuestConfirmed && active.IsConnected && active.PreparationStarted && active.LocalReady ? Visibility.Visible : Visibility.Collapsed;
        ConfirmPresenceButton.IsEnabled = !busy && active.IsConnected && active.PreparationStarted && active.LocalReady;
        PlayButton.Visibility = active.IsHost && active.CanApprovePlayback && scheduledStart == 0 ? Visibility.Visible : Visibility.Collapsed;
        PlayButton.Content = active.PlaybackAuthorized ? "_Retomar juntos" : "_Autorizar e iniciar";
        PlayButton.IsEnabled = !busy && active.IsHost && active.CanApprovePlayback && prepared;
        PauseButton.IsEnabled = SeekButton.IsEnabled = !busy && active.IsHost && active.BothReady && active.PlaybackAuthorized && prepared;
        ReadyActionHint.Text = active.IsHost ? "Você libera a preparação e dá o aval final. O AniT mantém o vídeo pausado durante a espera." : "O AniT abre o vídeo após o download e a verificação. Você só confirma presença; o host autoriza o início.";
    }
}

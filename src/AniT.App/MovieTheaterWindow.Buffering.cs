using System;
using System.Threading.Tasks;
using System.Windows;
using AniT.Theater;

namespace AniT.App;

public partial class MovieTheaterWindow
{
    private double lastGuestPosition;
    private bool bufferAnchorApplied;
    private long bufferWaitStartedAt;

    private void LockPlayerForBuffer()
    {
        try { player?.SetPlaybackPermission(false); }
        catch (Exception) { /* The transport monitor remains active even while MPC IPC is waiting on I/O. */ }
    }

    private async Task SampleGuestBufferAsync()
    {
        if (closed || !prepared || room is not { IsHost: false, Mode: TheaterMode.DirectStream, BothReady: true } active) return;
        var expected = !ended && active.LatestState is { Playing: true } state && ClockEstimator.NowMs + active.Clock.OffsetMs >= state.HostTimeMs;
        try { await active.ObserveStreamingPlaybackAsync(lastGuestPosition, expected, lifetime.Token); }
        catch (OperationCanceledException) when (closed) { }
        catch (Exception) { LockPlayerForBuffer(); if (!closed) SyncLabel.Text = "Conexão interrompida · reprodução pausada."; }
    }

    private async Task HoldStreamForGuestAsync(TheaterRoom active)
    {
        scheduledStart = 0; LockPlayerForBuffer();
        var position = await player!.GetPositionAsync(lifetime.Token);
        if (!bufferAnchorApplied)
        {
            // Host chooses the common point; a guest report cannot rewind arbitrary portions of the episode.
            var guestPosition = active.BufferStatus?.PositionSeconds ?? position.TotalSeconds;
            var anchor = Math.Clamp(guestPosition, Math.Max(0, position.TotalSeconds - 3), position.TotalSeconds);
            if (position.TotalSeconds - anchor > .08) { position = TimeSpan.FromSeconds(anchor); await player.SeekAsync(position, lifetime.Token); }
            bufferAnchorApplied = true;
        }
        player.SetSpeed(1);
        await active.PublishAsync(position.TotalSeconds, false, lifetime.Token, audioTrack: player.AudioTrack, subtitleTrack: player.SubtitleTrack);
        PositionLabel.Text = $"{Format(position)} / {Format(player.Duration ?? TimeSpan.Zero)}";
        SyncLabel.Text = active.GuestBuffering ? "Sessão pausada · aguardando o buffer do convidado." : "Buffer pronto · o host pode retomar todos os vídeos.";
    }

    private void UpdateStreamBufferView()
    {
        if (closed) return;
        var active = room;
        UpdateStreamConnectionView(active);
        if (active is not { Mode: TheaterMode.DirectStream, StreamHold: true })
        {
            StreamBufferPanel.Visibility = Visibility.Collapsed; bufferAnchorApplied = false; bufferWaitStartedAt = 0;
            return;
        }
        StreamBufferPanel.Visibility = Visibility.Visible;
        if (bufferWaitStartedAt == 0) bufferWaitStartedAt = ClockEstimator.NowMs;
        var status = active.BufferStatus;
        var waiting = active.GuestBuffering;
        StreamBufferTitle.Foreground = (System.Windows.Media.Brush)FindResource(waiting ? "AniT.Brush.Warning" : "AniT.Brush.Success");
        StreamBufferTitle.Text = waiting ? active.IsHost ? "Aguardando o buffer do convidado" : "Recebendo os próximos trechos" : "Buffer pronto";
        StreamBufferMessage.Text = waiting
            ? active.IsHost ? "Todos os vídeos estão pausados para ninguém ficar para trás. Aguarde os dados chegarem ao convidado." : "O host foi avisado. A sessão está pausada enquanto o AniT recebe os dados do vídeo."
            : active.IsHost ? "A conexão voltou a fornecer os dados necessários. Clique em Retomar juntos para continuar a sessão." : "Os dados chegaram. Aguarde o host retomar a sessão; seu vídeo continua pausado.";
        if (active.StreamConnection is { Limited: true }) StreamBufferMessage.Text += " O recebimento medido está abaixo da taxa média estimada do vídeo.";
        var percent = status is { RequiredBytes: > 0 } ? Math.Clamp(100d * status.ReceivedBytes / status.RequiredBytes, 0, 100) : 0;
        StreamBufferProgress.Value = waiting ? percent : 100;
        StreamBufferDetails.Text = $"{(waiting && percent >= 100 ? "Conferindo estabilidade" : waiting ? $"Reserva para retomar: {percent:0}%" : "Pronto para retomar")} · espera {Format(TimeSpan.FromMilliseconds(Math.Max(0, ClockEstimator.NowMs - bufferWaitStartedAt)))}";
        if (active.IsHost)
        {
            PlayButton.Content = "_Retomar juntos";
            PlayButton.Visibility = !waiting && prepared ? Visibility.Visible : Visibility.Collapsed;
            PlayButton.IsEnabled = !busy && !waiting && prepared && active.BothReady;
        }
        SeekPanel.Visibility = PlaybackButtonsPanel.Visibility = Visibility.Collapsed;
    }
    private void UpdateStreamConnectionView(TheaterRoom? active)
    {
        if (active is not { Mode: TheaterMode.DirectStream, IsConnected: true }) return;
        var info = active.StreamConnection;
        ConnectionLabel.Text = info is not { Measured: true }
            ? "Streaming direto · medindo a transferência durante a leitura do vídeo…"
            : $"Streaming direto · {info.BytesPerSecond * 8 / 1000000:0.0} Mb/s recebidos · ida e volta {info.RoundTripMs:0} ms · reserva alvo {info.TargetBufferBytes / 1048576d:0} MB";
    }

}

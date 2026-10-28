namespace AniT.Theater;

public enum EpisodeTransferPhase { Waiting, Receiving, Verifying, Verified }
public sealed record EpisodeTransferProgress(long Bytes, EpisodeTransferPhase Phase);

public sealed partial class TheaterRoom
{
    private long preparationAt, transferAt, transferFinishedAt, approvedStartAt, lastProgressSent;
    private volatile bool guestConfirmed, playbackAuthorized;
    private EpisodeTransferProgress transferProgress = new(0, EpisodeTransferPhase.Waiting);
    public bool PreparationStarted => Interlocked.Read(ref preparationAt) > 0;
    public long PreparationStartedAtMs => Interlocked.Read(ref preparationAt);
    public long TransferStartedAtMs => Interlocked.Read(ref transferAt);
    public long TransferFinishedAtMs => Interlocked.Read(ref transferFinishedAt);
    public long ApprovedStartAtMs => Interlocked.Read(ref approvedStartAt);
    public bool GuestConfirmed => IsHost ? EveryGuest(p => p.Confirmed) : guestConfirmed;
    public bool PlaybackAuthorized => Mode == TheaterMode.DirectStream || playbackAuthorized;
    public bool CanApprovePlayback => Mode == TheaterMode.PeerSync && PreparationStarted && BothReady && GuestConfirmed;
    public EpisodeTransferProgress DownloadProgress => IsHost ? ActiveGuests.Select(p => p.Progress).OrderBy(p => p.Bytes).FirstOrDefault() ?? new(0, EpisodeTransferPhase.Waiting) : Volatile.Read(ref transferProgress);

    public async Task BeginPreparationAsync(CancellationToken cancellationToken = default)
    {
        if (!IsHost || Mode != TheaterMode.PeerSync) throw new InvalidOperationException("Somente o host pode liberar a preparação P2P.");
        if (!PreparationStarted) Interlocked.Exchange(ref preparationAt, ClockEstimator.NowMs);
        await BroadcastAsync(new("prepare", Clock: PreparationStartedAtMs), cancellationToken).ConfigureAwait(false);
        Changed?.Invoke();
    }

    public async Task ConfirmParticipationAsync(CancellationToken cancellationToken = default)
    {
        if (IsHost || Mode != TheaterMode.PeerSync || !PreparationStarted || !IsConnected || !LocalReady || !HasMedia)
            throw new InvalidOperationException("Aguarde a verificação e a abertura do vídeo antes de confirmar presença.");
        guestConfirmed = true;
        try { await peer!.Channel.SendAsync(new("confirmed"), cancellationToken).ConfigureAwait(false); }
        catch { guestConfirmed = false; throw; }
        Changed?.Invoke();
    }

    public async Task<long> ApprovePlaybackAsync(double positionSeconds, CancellationToken cancellationToken = default, int? audioTrack = null, int? subtitleTrack = null)
    {
        if (!IsHost || !CanApprovePlayback) throw new InvalidOperationException("Aguarde o arquivo verificado, os dois vídeos abertos e a confirmação do convidado.");
        var start = ClockEstimator.NowMs + 5000;
        playbackAuthorized = true; if (ApprovedStartAtMs == 0) Interlocked.Exchange(ref approvedStartAt, start);
        try { await PublishAsync(positionSeconds, true, cancellationToken, start, audioTrack, subtitleTrack).ConfigureAwait(false); }
        catch { playbackAuthorized = false; Interlocked.Exchange(ref approvedStartAt, 0); throw; }
        Changed?.Invoke(); return start;
    }

    private void InvalidatePreparationApproval()
    {
        guestConfirmed = false; playbackAuthorized = false; Interlocked.Exchange(ref approvedStartAt, 0);
    }

    private async Task ReportDownloadAsync(EpisodeTransferPhase phase, CancellationToken token, bool force = false)
    {
        var bytes = TransferredBytes;
        Volatile.Write(ref transferProgress, new(bytes, phase));
        if (phase == EpisodeTransferPhase.Receiving && Interlocked.Read(ref transferAt) == 0) Interlocked.Exchange(ref transferAt, ClockEstimator.NowMs);
        var now = ClockEstimator.NowMs;
        if (!force && now - lastProgressSent < 500 && bytes != media.Length) return;
        lastProgressSent = now;
        if (peer is { } link) await link.Channel.SendAsync(new("download-progress", Progress: DownloadProgress), token).ConfigureAwait(false);
        Changed?.Invoke();
    }

    private void ReceiveDownloadProgress(Peer link, EpisodeTransferProgress? progress)
    {
        if (progress is null || !Enum.IsDefined(progress.Phase) || progress.Bytes < link.Progress.Bytes || progress.Bytes > media.Length || progress.Phase == EpisodeTransferPhase.Verified || progress.Phase == EpisodeTransferPhase.Verifying && progress.Bytes != media.Length)
            throw new InvalidDataException("Progresso de download inválido.");
        if (progress.Phase == EpisodeTransferPhase.Receiving && TransferStartedAtMs == 0) Interlocked.Exchange(ref transferAt, ClockEstimator.NowMs);
        link.Progress = progress; Changed?.Invoke();
    }
}

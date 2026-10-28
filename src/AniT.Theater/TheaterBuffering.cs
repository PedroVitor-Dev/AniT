namespace AniT.Theater;

public sealed partial class TheaterRoom
{
    private readonly StreamBufferMonitor bufferMonitor = new();
    private readonly SemaphoreSlim bufferReportGate = new(1, 1);
    private StreamBufferStatus? bufferStatus;
    private long bufferRevision, receivedBufferRevision, lastBufferReportAt, bufferResumeRevision;
    private volatile bool streamHold;
    public bool StreamHold => Mode == TheaterMode.DirectStream && BothReady && (streamHold || GuestBuffering);
    public StreamBufferStatus? BufferStatus => IsHost ? ActiveGuests.Select(p => p.Buffer).Where(b => b is not null).OrderByDescending(b => b!.Waiting).ThenBy(b => b!.PositionSeconds).FirstOrDefault() : Volatile.Read(ref bufferStatus);
    public bool GuestBuffering => Mode == TheaterMode.DirectStream && BufferStatus?.Waiting == true;

    /// <summary>Samples cached player position and real HTTP demand, independently of potentially blocked MPC IPC.</summary>
    public async Task ObserveStreamingPlaybackAsync(double positionSeconds, bool expectedPlaying, CancellationToken cancellationToken = default)
    {
        if (IsHost || Mode != TheaterMode.DirectStream || !BothReady || streamProxy is not { } proxy) return;
        if (!double.IsFinite(positionSeconds) || positionSeconds is < 0 or > 172800) throw new ArgumentOutOfRangeException(nameof(positionSeconds));
        if (!await bufferReportGate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return;
        try
        {
            var now = ClockEstimator.NowMs;
            var sample = bufferMonitor.Observe(now, positionSeconds, expectedPlaying, proxy.PendingDemand, proxy.IsBlockPending,
                AdaptiveSnapshot.Policy, offset => recoveryTargetBytes > 0 && proxy.GetReserve(offset, recoveryTargetBytes).Complete);
            var previous = BufferStatus;
            if (!sample.Waiting && previous?.Waiting != true) { await ReportConnectionAsync(cancellationToken).ConfigureAwait(false); return; }
            var newWait = sample.Waiting && previous?.Waiting != true;
            if (newWait)
            {
                adaptiveBuffer.NotifyWait(now);
                // Freeze this hold's target; changing measurements must not keep moving the recovery goal.
                recoveryTargetBytes = AdaptiveSnapshot.Policy.ReadAheadBytes;
            }
            if (sample.Waiting) proxy.EnsureReserve(sample.Offset, recoveryTargetBytes);
            else proxy.ReleaseReserve();
            var reserve = proxy.GetReserve(sample.Offset, Math.Max(1, recoveryTargetBytes), offset =>
            {
                var block = streamBlocks.Values.FirstOrDefault(b => b.Offset == offset);
                return block is null ? 0 : block.Received;
            });
            if (previous?.Waiting == sample.Waiting && now - lastBufferReportAt < 250) return;
            var report = new StreamBufferStatus(Interlocked.Increment(ref bufferRevision), sample.Waiting, sample.PositionSeconds, reserve.ReceivedBytes, reserve.RequiredBytes);
            Volatile.Write(ref bufferStatus, report);
            // Publish the waiting status first so an older resume cannot clear the new hold concurrently.
            if (sample.Waiting) streamHold = true;
            lastBufferReportAt = now;
            Changed?.Invoke();
            var link = peer ?? throw new IOException("Host desconectado.");
            await link.Channel.SendAsync(new("buffer-status", Buffer: report), cancellationToken).ConfigureAwait(false);
            await ReportConnectionAsync(cancellationToken, force: newWait).ConfigureAwait(false);
        }
        finally { bufferReportGate.Release(); }
    }

    private void ReceiveBufferStatus(Peer link, StreamBufferStatus? report)
    {
        if (report is not { IsValid: true }) throw new InvalidDataException("Estado de buffer inválido.");
        if (report.Revision <= Interlocked.Read(ref link.BufferRevision)) return;
        Interlocked.Exchange(ref link.BufferRevision, report.Revision);
        if (!BothReady) return;
        link.Buffer = report;
        if (report.Waiting) streamHold = true;
        Changed?.Invoke();
    }

    private void ObserveHostBufferHold(TimelineState state)
    {
        if (Mode != TheaterMode.DirectStream) return;
        if (state.BufferHold) streamHold = true;
        if (!streamHold) return;
        if (state.Playing && !GuestBuffering && (BufferStatus is not { } status || state.BufferResumeRevision >= status.Revision))
        {
            streamHold = false; Changed?.Invoke();
        }
    }

    public async Task ResumeStreamingAsync(double positionSeconds, long startAtMs, CancellationToken cancellationToken = default, int? audioTrack = null, int? subtitleTrack = null)
    {
        if (!IsHost || Mode != TheaterMode.DirectStream || !BothReady || GuestBuffering) throw new InvalidOperationException("Aguarde o buffer do convidado ficar pronto para retomar juntos.");
        var held = streamHold; streamHold = false;
        var previousResume = Interlocked.Exchange(ref bufferResumeRevision, BufferStatus?.Revision ?? 0);
        try { await PublishAsync(positionSeconds, true, cancellationToken, startAtMs, audioTrack, subtitleTrack).ConfigureAwait(false); }
        catch { if (held) streamHold = true; Interlocked.Exchange(ref bufferResumeRevision, previousResume); throw; }
        Changed?.Invoke();
    }

    private void ClearStreamBuffering(bool newSession = false)
    {
        streamHold = false;
        foreach (var guest in ActiveGuests) guest.Buffer = null;
        if (newSession)
        {
            Interlocked.Exchange(ref bufferRevision, 0); Interlocked.Exchange(ref receivedBufferRevision, 0);
            Interlocked.Exchange(ref bufferResumeRevision, 0); lastBufferReportAt = 0;
            Interlocked.Exchange(ref connectionRevision, 0); Interlocked.Exchange(ref receivedConnectionRevision, 0); lastConnectionReportAt = 0;
            Volatile.Write(ref remoteConnection, null);
        }
        recoveryTargetBytes = 0; streamProxy?.ReleaseReserve();
        Volatile.Write(ref bufferStatus, null); bufferMonitor.Reset();
    }
}

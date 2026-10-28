namespace AniT.Theater;

public sealed partial class TheaterRoom
{
    private StreamConnectionInfo? remoteConnection;
    private long connectionRevision, receivedConnectionRevision, lastConnectionReportAt;
    private int recoveryTargetBytes;
    private StreamAdaptiveSnapshot AdaptiveSnapshot => adaptiveBuffer.Snapshot(ClockEstimator.NowMs, Clock.RoundTripMs, Clock.JitterMs);
    public StreamConnectionInfo? StreamConnection
    {
        get
        {
            if (Mode != TheaterMode.DirectStream) return null;
            if (IsHost) return ActiveGuests.Select(p => p.Connection).FirstOrDefault(c => c is not null);
            var info = AdaptiveSnapshot.Connection;
            return StreamHold && recoveryTargetBytes > 0 ? info with { TargetBufferBytes = recoveryTargetBytes } : info;
        }
    }

    public void ConfigureStreamingMedia(TimeSpan duration)
    {
        if (!IsHost && Mode == TheaterMode.DirectStream) adaptiveBuffer.ConfigureMedia(media.Length, duration);
    }

    private async Task ReportConnectionAsync(CancellationToken cancellationToken, bool force = false)
    {
        var now = ClockEstimator.NowMs;
        if (!force && now - lastConnectionReportAt < 1000) return;
        var link = peer ?? throw new IOException("Host desconectado.");
        lastConnectionReportAt = now;
        await link.Channel.SendAsync(new("stream-connection", Request: Interlocked.Increment(ref connectionRevision), Connection: StreamConnection), cancellationToken).ConfigureAwait(false);
    }

    private void ReceiveConnectionInfo(Peer link, RoomPacket packet)
    {
        if (packet.Request <= 0 || packet.Connection is not { IsValid: true } info) throw new InvalidDataException("Medição de conexão inválida.");
        if (packet.Request <= Interlocked.Read(ref link.ConnectionRevision)) return;
        Interlocked.Exchange(ref link.ConnectionRevision, packet.Request);
        link.Connection = info; Changed?.Invoke();
    }
}

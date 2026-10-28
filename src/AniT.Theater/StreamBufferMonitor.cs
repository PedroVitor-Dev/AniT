namespace AniT.Theater;

public sealed record StreamBufferStatus(long Revision, bool Waiting, double PositionSeconds, int ReceivedBytes, int RequiredBytes)
{
    public bool IsValid => Revision > 0 && double.IsFinite(PositionSeconds) && PositionSeconds is >= 0 and <= 172800
        && RequiredBytes is >= 1 and <= StreamAdaptiveBuffer.MaxReserveBytes && ReceivedBytes >= 0 && ReceivedBytes <= RequiredBytes;
}

internal sealed record StreamReadDemand(long Offset, int Count, long SinceMs);
internal sealed record StreamBufferSample(bool Waiting, double PositionSeconds, long Offset, int RequiredBytes);

/// <summary>Holds stalled playback, or proactively holds prolonged demand stalls before decoder buffers drain. Prefetch never does.</summary>
internal sealed class StreamBufferMonitor
{
    private readonly object gate = new();
    private double position, heldPosition;
    private long advancedAt, recoveryAt, heldOffset;
    private long? demandStartedAt;
    private long demandSeenAt;
    private int heldCount, stableRecoveryMs = 750;
    private bool initialized, expectingPlayback, waiting;

    public StreamBufferSample Observe(long now, double currentPosition, bool expectedPlaying, StreamReadDemand? demand, Func<long, bool> pending, StreamBufferPolicy? policy = null, Func<long, bool>? reserveReady = null)
    {
        policy ??= new(2 * TheaterRoom.StreamBlockSize);
        lock (gate)
        {
            if (!initialized || !expectedPlaying || !expectingPlayback || Math.Abs(currentPosition - position) >= .03)
            {
                position = currentPosition; advancedAt = now; initialized = true;
            }
            expectingPlayback = expectedPlaying;
            // Progressive delivery can satisfy many short reads without ever completing a video frame.
            // Sustained lack of playback progress must still hold the group, even when each read is brief.
            var demandAge = demand is null ? 0 : Math.Max(0, now - demand.SinceMs);
            if (!expectedPlaying || demand is null && now - demandSeenAt > 500) demandStartedAt = null;
            if (expectedPlaying && demand is not null)
            {
                if (demandStartedAt is null || now - demandSeenAt > 500) demandStartedAt = Math.Min(now, demand.SinceMs);
                demandSeenAt = now;
            }
            var progressiveStall = demandStartedAt is { } began && now - Math.Max(advancedAt, began) >= policy.PositionStallMs + policy.DemandGraceMs && demandAge >= Math.Min(100, policy.DemandGraceMs);
            if (!waiting && expectedPlaying && demand is not null && (progressiveStall || demandAge >= policy.DemandGraceMs && (now - advancedAt >= policy.PositionStallMs || demandAge >= policy.ProactiveWaitMs)))
            {
                waiting = true; stableRecoveryMs = policy.RecoveryStableMs; heldPosition = currentPosition; heldOffset = demand.Offset; heldCount = demand.Count; recoveryAt = 0;
            }
            if (waiting)
            {
                var transientMs = Math.Clamp(policy.DemandGraceMs / 3, 200, 500);
                if (!pending(heldOffset) && demand is not null && now - demand.SinceMs >= transientMs)
                {
                    heldOffset = demand.Offset; heldCount = demand.Count;
                }
                var recovered = !pending(heldOffset) && (demand is null || now - demand.SinceMs < transientMs) && (reserveReady?.Invoke(heldOffset) ?? true);
                if (!recovered) recoveryAt = 0;
                else if (recoveryAt == 0) recoveryAt = now;
                else if (now - recoveryAt >= stableRecoveryMs) { waiting = false; advancedAt = now; }
            }
            return new(waiting, heldPosition, heldOffset, Math.Max(1, heldCount));
        }
    }

    public void Reset()
    {
        lock (gate) { initialized = expectingPlayback = waiting = false; advancedAt = recoveryAt = 0; demandStartedAt = null; demandSeenAt = 0; }
    }
}

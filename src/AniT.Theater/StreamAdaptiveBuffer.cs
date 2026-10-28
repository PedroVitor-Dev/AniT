namespace AniT.Theater;

public sealed record StreamConnectionInfo(double BytesPerSecond, double RoundTripMs, double JitterMs, int TargetBufferBytes, bool Measured, bool Limited)
{
    public bool IsValid => double.IsFinite(BytesPerSecond) && BytesPerSecond is >= 0 and <= 536870912
        && double.IsFinite(RoundTripMs) && RoundTripMs is >= 0 and <= 10000
        && double.IsFinite(JitterMs) && JitterMs is >= 0 and <= 10000
        && TargetBufferBytes is >= StreamAdaptiveBuffer.MinReserveBytes and <= StreamAdaptiveBuffer.MaxReserveBytes;
}

internal sealed record StreamBufferPolicy(int ReadAheadBytes, int DemandGraceMs = 600, int PositionStallMs = 800, int ProactiveWaitMs = 2000, int RecoveryStableMs = 750);
internal sealed record StreamAdaptiveSnapshot(StreamConnectionInfo Connection, StreamBufferPolicy Policy);

/// <summary>Estimates application-level throughput only while media requests are active, never from cache hits or idle pauses.</summary>
internal sealed class StreamAdaptiveBuffer
{
    internal const int MinReserveBytes = 1024 * 1024, MaxReserveBytes = 8 * 1024 * 1024;
    private readonly object gate = new();
    private long windowAt, windowBytes, totalBytes, lastByteAt, lastWaitAt;
    private int requests, samples, pressure;
    private double rate, mediaRate;

    public void ConfigureMedia(long length, TimeSpan duration)
    {
        if (length <= 0 || duration <= TimeSpan.Zero || duration.TotalSeconds > 172800) return;
        lock (gate) mediaRate = Math.Clamp(length / duration.TotalSeconds, 1024d, 128 * 1024 * 1024d);
    }

    public void RequestStarted(long now)
    {
        lock (gate) { if (requests++ == 0) { windowAt = lastByteAt = now; windowBytes = 0; } }
    }

    public void Received(int bytes, long now)
    {
        if (bytes <= 0) return;
        lock (gate)
        {
            if (requests == 0) return;
            windowBytes += bytes; totalBytes += bytes; lastByteAt = now;
            if (now - windowAt >= 250) Flush(now);
        }
    }

    public void RequestFinished(long now)
    {
        lock (gate) { if (requests > 0 && --requests == 0) Flush(now); }
    }

    private void Flush(long now)
    {
        var elapsed = Math.Max(1, now - windowAt);
        if (windowBytes > 0 && (rate == 0 || elapsed >= 20 || windowBytes >= 512 * 1024))
        {
            var observed = Math.Min(536870912d, windowBytes * 1000d / elapsed);
            rate = rate == 0 ? observed : rate + (observed < rate ? .5 : .2) * (observed - rate);
            samples++;
        }
        windowBytes = 0; windowAt = now;
    }

    public void NotifyWait(long now)
    {
        lock (gate) { pressure = Math.Min(4, PressureAt(now) + 1); lastWaitAt = now; }
    }

    private int PressureAt(long now) => Math.Max(0, pressure - (int)Math.Min(4, Math.Max(0, now - lastWaitAt) / 30000));

    public StreamAdaptiveSnapshot Snapshot(long now, double roundTripMs, double jitterMs)
    {
        lock (gate)
        {
            var rtt = double.IsFinite(roundTripMs) ? Math.Clamp(roundTripMs, 0, 10000) : 0;
            var jitter = double.IsFinite(jitterMs) ? Math.Clamp(jitterMs, 0, 10000) : 0;
            var effectiveRate = rate;
            // A stalled active request lowers the estimate; an idle/paused connection does not.
            if (requests > 0 && now - lastByteAt > 1500)
                effectiveRate = Math.Min(rate, windowBytes * 1000d / Math.Max(1, now - windowAt));
            var measured = samples >= 2 && totalBytes >= MinReserveBytes;
            var low = measured && mediaRate > 0 && effectiveRate < mediaRate * 1.25;
            var risk = PressureAt(now);
            var seconds = Math.Clamp(1.2 + rtt / 250d + jitter / 150d + risk * 1.5 + (low ? 2 : 0), 1, 8);
            var target = !measured ? (2 + risk) * MinReserveBytes : (int)Math.Min(MaxReserveBytes, Math.Ceiling((mediaRate > 0 ? mediaRate : MinReserveBytes) * seconds / MinReserveBytes) * MinReserveBytes);
            target = Math.Clamp(target, MinReserveBytes, MaxReserveBytes);
            var policy = !measured ? new StreamBufferPolicy(target)
                : new StreamBufferPolicy(target,
                    (int)Math.Clamp(400 + rtt * 2 + jitter * 4, 600, 1500),
                    (int)Math.Clamp(600 + jitter * 2, 800, 1600),
                    (int)Math.Clamp(750 + rtt * 2 + jitter * 4 + TheaterRoom.StreamBlockSize * 1000d / Math.Max(65536, effectiveRate), 1500, 6000),
                    (int)Math.Clamp(400 + rtt * 2 + jitter * 4 + risk * 250, 500, 2500));
            return new(new(effectiveRate, rtt, jitter, target, measured, low), policy);
        }
    }
}

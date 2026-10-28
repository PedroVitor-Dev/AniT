using System.Diagnostics;

namespace AniT.Theater;

public sealed record TimelineState(long Revision, double PositionSeconds, bool Playing, long HostTimeMs, int? AudioTrack = null, int? SubtitleTrack = null, bool BufferHold = false, long BufferResumeRevision = 0)
{
    public bool IsValid => BufferResumeRevision >= 0 && (!BufferHold || !Playing) && Revision > 0 && double.IsFinite(PositionSeconds) && PositionSeconds is >= 0 and <= 172800 && HostTimeMs >= 0 && (AudioTrack is null or >= 0 and <= 1024) && (SubtitleTrack is null or >= -1 and <= 1024);
    public double PositionAt(double hostTimeMs) => Math.Clamp(PositionSeconds + (Playing ? Math.Max(0, hostTimeMs - HostTimeMs) / 1000 : 0), 0, 172800);
}

public sealed class ClockEstimator
{
    private readonly Queue<(double Rtt, double Offset)> samples = new();
    public bool IsReady => samples.Count > 0;
    public double OffsetMs { get; private set; }
    public double RoundTripMs { get; private set; }
    public double JitterMs { get; private set; }
    public static long NowMs => (long)(Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency);

    public void Observe(long sent, long received, long remote)
    {
        if (sent < 0 || remote < 0 || received < sent || received - sent > 5000) return;
        samples.Enqueue((received - sent, remote - (sent / 2d + received / 2d)));
        while (samples.Count > 8) samples.Dequeue();
        var best = samples.MinBy(x => x.Rtt);
        OffsetMs = best.Offset;
        RoundTripMs = best.Rtt;
        var average = samples.Average(x => x.Rtt);
        JitterMs = samples.Average(x => Math.Abs(x.Rtt - average));
    }
}

public readonly record struct SyncCorrection(double TargetSeconds, bool Seek, double Speed);

public static class TimelineSync
{
    // Engineering thresholds, not a claim of measured accuracy on real PCs.
    public static SyncCorrection Correct(TimelineState state, double guestSeconds, double hostNowMs, bool streaming = false)
    {
        if (!state.IsValid || !double.IsFinite(guestSeconds) || !double.IsFinite(hostNowMs)) throw new ArgumentException("Estado de reprodução inválido.");
        var target = state.PositionAt(hostNowMs);
        var drift = target - guestSeconds;
        // Network buffering needs a wider deadband: repeated HTTP seeks discard useful buffered video.
        var seek = Math.Abs(drift) > (state.Playing ? streaming ? .75 : .35 : .10);
        var rate = streaming ? .04 : .02;
        var speed = !state.Playing || seek || Math.Abs(drift) < (streaming ? .12 : .08) ? 1 : drift > 0 ? 1 + rate : 1 - rate;
        return new(target, seek, speed);
    }
}

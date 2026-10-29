namespace AniT.Core;

public sealed record OnlineBufferSample(double? Seconds, double? ForwardBytes, double? InputBytesPerSecond,
    bool Paused = false, bool Buffering = false, bool Seeking = false, bool Idle = false,
    bool EndOfStream = false, double Speed = 1, bool HasPlayed = true, long Underruns = 0);

public sealed record OnlineBufferPlan(double ResumeSeconds, double AheadSeconds, long MaxBytes,
    bool CanPrepareIndex = false, double? SupplyRatio = null, double? MediaBytesPerSecond = null)
{
    public static OnlineBufferPlan Initial { get; } = new(1, 10, 32L * 1024 * 1024);
}

/// <summary>Bounded estimates from the player's actual forward packet cache, not torrent-wide progress.</summary>
public sealed class OnlineBufferPolicy(TimeProvider? timeProvider = null)
{
    readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    long? previous;
    long observedUnderruns;
    double? lastCache, supply, mediaRate;
    double jitter, debt, stable;
    bool wasBuffering, wasSeeking;
    double resume = 1, ahead = 10;
    long budget = 32L * 1024 * 1024;
    public void ResetAfterSeek()
    {
        lastCache = supply = null; jitter = debt = stable = 0; wasBuffering = false;
        resume = 1; ahead = 10; previous = null; observedUnderruns = 0;
    }
    static double? Nonnegative(double? value) => value is { } number && double.IsFinite(number) && number >= 0 ? number : null;
    public OnlineBufferPlan Update(OnlineBufferSample sample)
    {
        var stamp = clock.GetTimestamp();
        var dt = previous is { } old ? Math.Clamp(clock.GetElapsedTime(old, stamp).TotalSeconds, 0, 5) : 0;
        previous = stamp;
        var seconds = Nonnegative(sample.Seconds);
        var bytes = Nonnegative(sample.ForwardBytes);
        var input = Nonnegative(sample.InputBytesPerSecond);
        var speed = double.IsFinite(sample.Speed) && sample.Speed > 0 ? Math.Clamp(sample.Speed, .25, 4) : 1;
        if (sample.Seeking) {
            if (!wasSeeking) ResetAfterSeek();
            wasSeeking = true; lastCache = null;
            return new(Math.Clamp(speed, .25, 16), Math.Clamp(10 * speed, 8, 60), budget, false, null, mediaRate);
        }
        wasSeeking = false;
        if (!sample.HasPlayed) {
            lastCache = null;
            return new(Math.Clamp(speed, .25, 16), Math.Clamp(10 * speed, 8, 60), budget, false, null, mediaRate);
        }
        var alpha = dt > 0 ? 1 - Math.Exp(-dt / 4) : 1;
        if (!sample.Paused && !sample.EndOfStream && seconds >= .5 && bytes > 0) {
            var estimate = Math.Clamp(bytes.Value / seconds.Value, 1024, 64 * 1024 * 1024);
            mediaRate = mediaRate is { } prior ? prior + alpha * (estimate - prior) : estimate;
        }
        double? measured = null;
        if (!sample.Paused && !sample.EndOfStream && (!sample.Idle || sample.Buffering)) {
            if (input is { } rate && mediaRate is > 0) measured = rate / (mediaRate.Value * speed);
            else if (seconds is { } current && lastCache is { } before && dt >= .25 && dt <= 3)
                measured = (current - before) / dt / speed + (sample.Buffering || !sample.HasPlayed ? 0 : 1);
        }
        if (measured is { } ratio) {
            ratio = Math.Clamp(ratio, 0, 8);
            if (supply is { } oldRatio) { jitter += alpha * (Math.Abs(ratio - oldRatio) - jitter); supply += alpha * (ratio - oldRatio); }
            else supply = ratio;
        }
        var underrun = sample.Buffering && !sample.Paused && sample.HasPlayed && !sample.EndOfStream;
        var counter = Math.Max(0, sample.Underruns);
        var missed = counter > observedUnderruns ? counter - observedUnderruns : 0;
        observedUnderruns = counter;
        if (!sample.Paused && !sample.EndOfStream && (missed > 0 || underrun && !wasBuffering)) {
            debt = Math.Min(4, debt + 2 * Math.Max(1, Math.Min(2, missed))); stable = 0;
        }
        wasBuffering = underrun;
        if (!sample.Paused && !sample.Buffering && sample.HasPlayed && seconds >= resume * speed + 2 * speed && (sample.Idle || supply >= 1.4)) {
            stable += dt;
            if (stable >= 15) debt = Math.Max(0, debt - dt / 20);
        } else if (!sample.Paused) stable = 0;
        var baseline = supply switch { >= 2 => 1, >= 1.25 => 2, >= 1 => 3, >= .75 => 5, < .75 => 6, _ => 1 };
        var wanted = Math.Clamp(baseline + debt + Math.Min(2, jitter), 1, 8);
        // Increase protection promptly; reduce only after sustained recovery.
        if (wanted >= resume) resume = wanted;
        else if (stable >= 15 || sample.EndOfStream) resume = Math.Max(wanted, resume - dt / 10);
        var targetAhead = Math.Clamp(resume * 3 + 5, 10, 30);
        ahead = targetAhead >= ahead ? targetAhead : Math.Max(targetAhead, ahead - dt);
        var resumeMedia = Math.Clamp(resume * speed, .25, 16);
        var aheadMedia = Math.Clamp(ahead * speed, 8, 60);
        if (sample.EndOfStream) resumeMedia = Math.Min(resumeMedia, Math.Max(.25, seconds ?? 1));
        if (mediaRate is { } bitrate) {
            var desired = (long)Math.Clamp(bitrate * aheadMedia * 1.25, 32 * 1024 * 1024, 96 * 1024 * 1024);
            // Quantize to 8 MiB; no per-packet option churn or unbounded allocation.
            budget = Math.Clamp(((desired + 8 * 1024 * 1024 - 1) / (8 * 1024 * 1024)) * (8 * 1024 * 1024), 32L * 1024 * 1024, 96L * 1024 * 1024);
        }
        var forIndex = sample.HasPlayed && !sample.Seeking && !sample.Buffering && seconds is { } reserve
            && reserve >= Math.Max(5 * speed, resumeMedia + 2 * speed)
            && (sample.EndOfStream || sample.Idle || stable >= 3 && supply >= 1.25);
        lastCache = sample.Paused || sample.Idle || sample.EndOfStream ? null : seconds;
        return new(resumeMedia, aheadMedia, budget, forIndex, supply, mediaRate);
    }
}

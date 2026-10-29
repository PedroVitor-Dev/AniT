using AniT.Core;

namespace AniT.Tests;

public sealed class OnlineBufferPolicyTests
{
    sealed class Clock : TimeProvider
    {
        long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        internal void Step(int seconds = 1) => ticks += seconds * TimeSpan.TicksPerSecond;
    }
    static OnlineBufferSample Flow(double rate = 500000, double seconds = 12)
        => new(seconds, seconds * 100000, rate);

    [Fact]
    public void HealthyFlowKeepsStartupSmallAndAllowsIndexOnlyAfterStableReserve()
    {
        var clock = new Clock(); var policy = new OnlineBufferPolicy(clock);
        var first = policy.Update(Flow());
        Assert.Equal(1, first.ResumeSeconds); Assert.False(first.CanPrepareIndex);
        clock.Step(4); var stable = policy.Update(Flow());
        Assert.True(stable.CanPrepareIndex); Assert.Equal(10, stable.AheadSeconds);
        Assert.Equal(32 * 1024 * 1024, stable.MaxBytes);
    }

    [Fact]
    public void SlowFlowRaisesReserveAndRecoveryLowersItGradually()
    {
        var clock = new Clock(); var policy = new OnlineBufferPolicy(clock);
        var slow = policy.Update(Flow(30000));
        Assert.InRange(slow.ResumeSeconds, 5, 8);
        Assert.InRange(slow.AheadSeconds, 20, 30);
        clock.Step(); var immediate = policy.Update(Flow(600000, 40));
        Assert.True(immediate.ResumeSeconds >= slow.ResumeSeconds, "A single burst must not erase protection");
        var recovered = immediate;
        for (var second = 0; second < 130; second++) { clock.Step(); recovered = policy.Update(Flow(600000, 40)); }
        Assert.True(recovered.ResumeSeconds < slow.ResumeSeconds);
        Assert.True(recovered.ResumeSeconds >= 1);
    }

    [Fact]
    public void OneLongUnderrunIsNotCountedAsManyAndManualPauseIsNeverAnUnderrun()
    {
        var clock = new Clock(); var policy = new OnlineBufferPolicy(clock);
        policy.Update(Flow(150000)); clock.Step();
        var once = policy.Update(Flow(150000) with { Buffering = true });
        for (var second = 0; second < 20; second++) { clock.Step(); policy.Update(Flow(150000) with { Buffering = true }); }
        var still = policy.Update(Flow(150000) with { Buffering = true });
        Assert.Equal(once.ResumeSeconds, still.ResumeSeconds, 5);
        clock.Step(); policy.Update(Flow(150000)); clock.Step();
        var repeated = policy.Update(Flow(150000) with { Buffering = true });
        Assert.True(repeated.ResumeSeconds > once.ResumeSeconds);
        var paused = new OnlineBufferPolicy(clock);
        for (var second = 0; second < 20; second++) { clock.Step(); paused.Update(Flow(0) with { Paused = true, Buffering = true }); }
        Assert.Equal(1, paused.Update(Flow(0) with { Paused = true, Buffering = true }).ResumeSeconds);
    }

    [Fact]
    public void IdleZeroThroughputIsNotMistakenForNetworkFailure()
    {
        var clock = new Clock(); var policy = new OnlineBufferPolicy(clock);
        policy.Update(Flow());
        for (var second = 0; second < 20; second++) {
            clock.Step(); var plan = policy.Update(Flow(0) with { Idle = true });
            Assert.Equal(1, plan.ResumeSeconds); Assert.True(plan.SupplyRatio >= 2);
        }
    }

    [Fact]
    public void SeekingStartupAndEndOfFileCannotCreateAnImpossibleWait()
    {
        var clock = new Clock(); var policy = new OnlineBufferPolicy(clock);
        policy.Update(Flow(0)); clock.Step();
        var seeking = policy.Update(Flow(0) with { Seeking = true });
        Assert.Equal(1, seeking.ResumeSeconds); Assert.False(seeking.CanPrepareIndex); Assert.Null(seeking.SupplyRatio);
        var starting = policy.Update(new(0, null, 0, Buffering: true, HasPlayed: false));
        Assert.Equal(1, starting.ResumeSeconds);
        var ending = policy.Update(new(.3, 10000, 0, EndOfStream: true));
        Assert.InRange(ending.ResumeSeconds, .25, .3);
    }

    [Fact]
    public void MissingInvalidAndExtremeMeasurementsRemainBoundedAndSpeedUsesMediaSeconds()
    {
        var clock = new Clock(); var policy = new OnlineBufferPolicy(clock);
        foreach (var sample in new[] { new OnlineBufferSample(null, null, null), new(double.NaN, double.PositiveInfinity, -1),
                     new OnlineBufferSample(1, double.MaxValue, double.MaxValue, Speed: 4), new(0, 0, 0, Buffering: true) }) {
            clock.Step(); var plan = policy.Update(sample);
            Assert.InRange(plan.ResumeSeconds, .25, 16); Assert.InRange(plan.AheadSeconds, 8, 60);
            Assert.InRange(plan.MaxBytes, 32L * 1024 * 1024, 96L * 1024 * 1024);
            Assert.True(double.IsFinite(plan.ResumeSeconds));
        }
        var fast = new OnlineBufferPolicy(clock).Update(Flow(1000000) with { Speed = 2 });
        Assert.Equal(2, fast.ResumeSeconds); Assert.Equal(20, fast.AheadSeconds);
    }
    [Fact]
    public void HighBitrateCanIncreaseCapacityButNeverExceedTheForwardMemoryBudget()
    {
        var policy = new OnlineBufferPolicy();
        var high = policy.Update(new(8, 64L * 1024 * 1024, 32L * 1024 * 1024));
        Assert.Equal(96L * 1024 * 1024, high.MaxBytes);
        Assert.True(high.AheadSeconds >= high.ResumeSeconds);
    }

    [Fact]
    public void BriefUnderrunsBetweenSamplesAreCountedOnceAndSeekingClearsTheirHistory()
    {
        var clock = new Clock(); var policy = new OnlineBufferPolicy(clock);
        policy.Update(Flow()); clock.Step();
        var afterShortStall = policy.Update(Flow() with { Underruns = 1 });
        Assert.Equal(3, afterShortStall.ResumeSeconds);
        clock.Step(); Assert.Equal(3, policy.Update(Flow() with { Underruns = 1 }).ResumeSeconds);
        policy.ResetAfterSeek();
        Assert.Equal(1, policy.Update(Flow()).ResumeSeconds);
    }

}

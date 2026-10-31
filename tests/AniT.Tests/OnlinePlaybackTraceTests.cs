using AniT.Core;
using AniT.Infrastructure;
using System.Text.Json;

namespace AniT.Tests;

public sealed class OnlinePlaybackTraceTests
{
    sealed class Clock : TimeProvider
    {
        public long Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + TimeSpan.FromTicks(Ticks);
        public void Advance(int ms) => Ticks += TimeSpan.FromMilliseconds(ms).Ticks;
    }
    [Fact]
    public void IncludesLookupTimeButClickStartsAtZeroAndMilestonesAreUnique()
    {
        var clock = new Clock(); clock.Advance(300);
        var events = new List<OnlinePlaybackMetric>(); var trace = new OnlinePlaybackTrace(events.Add, clock, 0);
        trace.Mark("episode_resolved"); trace.Mark("episode_resolved"); clock.Advance(500); trace.Mark("header_verified"); trace.Finish("closed");
        Assert.Equal(0, events[0].ElapsedMs);
        Assert.Equal(300, events.Single(item => item.Name == "episode_resolved").ElapsedMs);
        Assert.Equal(800, events.Last().Values["header_verified_ms"]);
        Assert.Equal(Enumerable.Range(1, events.Count).Select(i => (long)i), events.Select(item => item.Sequence));
    }
    [Fact]
    public void SeparatesStartupUserPauseSeekAndRebufferAndIncludesAnUnfinishedStall()
    {
        var clock = new Clock(); var events = new List<OnlinePlaybackMetric>(); var trace = new OnlinePlaybackTrace(events.Add, clock);
        trace.PlaybackState(true, false, false); clock.Advance(500); trace.PlaybackState(false, false, false);
        trace.Mark("first_video_frame_signal");
        trace.PlaybackState(true, true, false); clock.Advance(1000); trace.PlaybackState(false, true, false);
        trace.PlaybackState(true, false, true); clock.Advance(700); trace.PlaybackState(false, false, false);
        trace.PlaybackState(true, false, false); clock.Advance(1200); trace.Finish("closed");
        var summary = events.Last();
        Assert.Equal(1, summary.Values["rebuffer_count"]); Assert.Equal(1200, summary.Values["rebuffer_ms"]);
        Assert.Equal(500, events.Single(item => item.Name == "startup_buffer_end").Values["duration_ms"]);
        Assert.Equal(700, events.Single(item => item.Name == "seek_buffer_end").Values["duration_ms"]);
    }
    [Fact]
    public void SamplingIsBoundedAndLateEventsDoNotContaminateFinishedSessions()
    {
        var clock = new Clock(); var events = new List<OnlinePlaybackMetric>(); var trace = new OnlinePlaybackTrace(events.Add, clock);
        for (var i = 0; i < 100; i++) trace.Sample("player", ("cache_seconds", 2));
        clock.Advance(1000); trace.Sample("player", ("cache_seconds", double.NaN), ("drops", 0));
        trace.Finish("cancelled"); var count = events.Count;
        trace.Mark("first_video_frame_signal"); trace.Sample("network"); trace.Finish("closed");
        Assert.Equal(count, events.Count); Assert.Equal(2, events.Count(item => item.Kind == "sample"));
        Assert.DoesNotContain("first_video_frame_signal_ms", events.Last().Values.Keys);
        Assert.DoesNotContain("cache_seconds", events.Where(item => item.Kind == "sample").Last().Values.Keys);
    }
    [Fact]
    public void NetworkCountersHandleTorrentRestartsWithoutNegativeOrLostTransfer()
    {
        var clock = new Clock(); var events = new List<OnlinePlaybackMetric>(); var trace = new OnlinePlaybackTrace(events.Add, clock);
        trace.BeginNetwork(1000); trace.ObserveNetwork(1200, 1, 200);
        clock.Advance(1000); trace.ObserveNetwork(50, 0, 50);
        clock.Advance(1000); trace.ObserveNetwork(100, 1, 50); trace.Finish("closed");
        Assert.Equal(new double[] { 200, 250, 300 }, events.Where(item => item.Name == "network").Select(item => item.Values["bytes_received"]));
        Assert.Single(events, item => item.Name == "first_data_observed");
    }
    [Fact]
    public void BrokenDiagnosticSinkCannotBreakPlayback()
    {
        var trace = new OnlinePlaybackTrace(_ => throw new IOException("Disk unavailable"));
        trace.Mark("header_verified"); trace.PlaybackState(true, false, false); trace.Finish("closed");
    }
    [Fact]
    public void ConcurrentMilestonesAndFinishHaveOneTerminalRecord()
    {
        var events = new List<OnlinePlaybackMetric>(); var trace = new OnlinePlaybackTrace(events.Add);
        Parallel.For(0, 50, _ => trace.Mark("first_peer_observed"));
        Parallel.For(0, 50, _ => trace.Finish("closed"));
        Assert.Single(events, item => item.Name == "first_peer_observed"); Assert.Single(events, item => item.Kind == "summary");
    }
    [Fact]
    public async Task JournalFlushesParseableLocalEventsAndFailureIsNonFatal()
    {
        var root = Path.Combine(Path.GetTempPath(), "AniT.Trace.Tests", Guid.NewGuid().ToString("N"));
        await using (var journal = new OnlineTraceJournal(root)) {
            var trace = new OnlinePlaybackTrace(journal.Write); trace.Mark("http_stream_ready"); trace.Finish("closed");
        }
        var lines = await File.ReadAllLinesAsync(Directory.GetFiles(root, "*.jsonl").Single());
        Assert.Equal(3, lines.Length);
        Assert.Equal("summary", JsonDocument.Parse(lines.Last()).RootElement.GetProperty("Kind").GetString());
        var invalid = Path.Combine(root, "file"); await File.WriteAllTextAsync(invalid, "not a folder");
        await using var failed = new OnlineTraceJournal(invalid);
        new OnlinePlaybackTrace(failed.Write).Finish("closed"); await failed.DisposeAsync(); Assert.True(failed.WriteFailed);
    }
}

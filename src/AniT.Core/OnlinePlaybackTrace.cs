using System.Diagnostics;

namespace AniT.Core;

public sealed record OnlinePlaybackMetric(Guid SessionId, long Sequence, DateTimeOffset Utc, double ElapsedMs,
    string Kind, string Name, IReadOnlyDictionary<string, double> Values);

/// <summary>Monotonic, local-only diagnostics. Sink failures never affect playback.</summary>
public sealed class OnlinePlaybackTrace
{
    readonly object sync = new();
    readonly TimeProvider clock;
    readonly long started;
    readonly DateTimeOffset utcStart;
    readonly Action<OnlinePlaybackMetric> sink;
    readonly Dictionary<string, double> milestones = new();
    readonly Dictionary<string, double> samples = new();
    readonly Guid id = Guid.NewGuid();
    long sequence;
    bool finished;
    string? bufferKind;
    double bufferStart, bufferTotal;
    int rebufferCount;
    bool networkStarted;
    long lastReceived, receivedInSession;
    public Guid SessionId => id;

    public OnlinePlaybackTrace(Action<OnlinePlaybackMetric> sink, TimeProvider? clock = null, long? startTimestamp = null)
    {
        this.clock = clock ?? TimeProvider.System; this.sink = sink;
        started = startTimestamp ?? this.clock.GetTimestamp();
        utcStart = this.clock.GetUtcNow() - this.clock.GetElapsedTime(started);
        milestones["click"] = 0; Emit("milestone", "click", [], 0);
    }
    double Now => Math.Max(0, clock.GetElapsedTime(started).TotalMilliseconds);
    public void Mark(string name, params (string Key, double Value)[] values)
    {
        lock (sync) {
            if (finished || milestones.ContainsKey(name)) return;
            milestones[name] = Now; Emit("milestone", name, values);
        }
    }
    public void Sample(string source, params (string Key, double Value)[] values)
    {
        lock (sync) {
            if (finished || samples.TryGetValue(source, out var previous) && Now - previous < 1000) return;
            samples[source] = Now; Emit("sample", source, values);
        }
    }
    public void BeginNetwork(long received)
    {
        lock (sync) { if (!networkStarted) { networkStarted = true; lastReceived = received; } }
    }
    public void ObserveNetwork(long received, int peers, double bytesPerSecond)
    {
        lock (sync) {
            if (finished) return;
            if (!networkStarted) BeginNetwork(received);
            receivedInSession += Math.Max(0, received >= lastReceived ? received - lastReceived : received);
            lastReceived = received;
            if (peers > 0) Mark("first_peer_observed");
            if (receivedInSession > 0) Mark("first_data_observed");
            Sample("network", ("peers", peers), ("bytes_received", receivedInSession), ("bytes_per_second", bytesPerSecond));
        }
    }
    public void Event(string name, params (string Key, double Value)[] values)
    {
        lock (sync) { if (!finished) Emit("event", name, values); }
    }
    public void PlaybackState(bool buffering, bool paused, bool seeking)
    {
        lock (sync) {
            if (finished) return;
            var next = buffering && !paused ? !milestones.ContainsKey("first_video_frame_signal") ? "startup_buffer"
                : seeking ? "seek_buffer" : "rebuffer" : null;
            if (next == bufferKind) return;
            CloseBuffer(); bufferKind = next;
            if (next is not null) { bufferStart = Now; if (next == "rebuffer") rebufferCount++; Emit("event", next + "_start", []); }
        }
    }
    void CloseBuffer()
    {
        if (bufferKind is null) return;
        var duration = Now - bufferStart;
        if (bufferKind == "rebuffer") bufferTotal += duration;
        Emit("event", bufferKind + "_end", [("duration_ms", duration)]); bufferKind = null;
    }
    public void Finish(string outcome)
    {
        lock (sync) {
            if (finished) return;
            CloseBuffer();
            var values = milestones.Select(item => (item.Key + "_ms", item.Value)).ToList();
            values.Add(("rebuffer_count", rebufferCount)); values.Add(("rebuffer_ms", bufferTotal));
            Emit("summary", outcome, values.ToArray()); finished = true;
        }
    }
    void Emit(string kind, string name, (string Key, double Value)[] values, double? elapsedOverride = null)
    {
        var data = values.Where(item => double.IsFinite(item.Value)).ToDictionary(item => item.Key, item => item.Value);
        var elapsed = elapsedOverride ?? Now;
        try { sink(new(id, ++sequence, utcStart + TimeSpan.FromMilliseconds(elapsed), elapsed, kind, name, data)); } catch { /* Diagnostics are best effort. */ }
    }
}

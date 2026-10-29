using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AniT.Core;

namespace AniT.Player;

public sealed partial class MpvOnlinePlayer
{
    volatile OnlineBufferPlan bufferPlan = OnlineBufferPlan.Initial;
    volatile MpvCacheState cacheState = MpvCacheState.Empty;
    int bufferEpoch;
    long cacheUnderruns;
    double playbackSpeed = 1;
    long cacheSecondsTimestamp = Stopwatch.GetTimestamp();
    void ResetBufferObservation()
    {
        cacheState = MpvCacheState.Empty; cacheSeconds = null; cacheSecondsTimestamp = Stopwatch.GetTimestamp();
        bufferPlan = OnlineBufferPlan.Initial;
        Interlocked.Exchange(ref cacheUnderruns, 0);
        Interlocked.Increment(ref bufferEpoch);
    }
    static double? BufferNumber(JsonElement value) => value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var number) && double.IsFinite(number) && number >= 0 ? number : null;
    OnlineBufferSample BufferSample()
    {
        var state = cacheState;
        var stale = Stopwatch.GetElapsedTime(state.Timestamp).TotalSeconds > 5 && !state.Idle && !state.Eof && !paused;
        var fallback = Stopwatch.GetElapsedTime(cacheSecondsTimestamp).TotalSeconds <= 5 || paused ? cacheSeconds : null;
        var seconds = stale ? fallback : state.ContiguousSeconds(position?.TotalSeconds, fallback);
        return new(seconds, stale ? null : state.Bytes, stale ? null : state.InputRate,
            paused, buffering, seeking, state.Idle, state.Eof && seconds > 0, playbackSpeed, videoReady && playbackRestarted, Interlocked.Read(ref cacheUnderruns));
    }
    void ManageIndexWarmup(OnlineBufferPlan plan, OnlineBufferSample sample, CancellationToken token)
    {
        bool restart;
        lock (warmingSync) {
            if (indexFetching && (sample.Buffering || sample.Seeking || sample.Seconds < Math.Max(2, plan.ResumeSeconds / 2)))
                warmingIndex?.Cancel();
            restart = plan.CanPrepareIndex && warmingIndex is null && !usingIndex && prepareSeek is not null;
        }
        if (restart && CurrentFilePath is { } path && Uri.TryCreate(path, UriKind.Absolute, out var address))
            _ = WarmIndexAsync(address, token);
    }
    async Task ResetBufferOptionsAsync(CancellationToken token)
    {
        foreach (var (option, value) in BufferOptions(OnlineBufferPlan.Initial))
            await CommandAsync(["set_property", option, value], token);
    }
    static (string Option, object Value)[] BufferOptions(OnlineBufferPlan plan) => [
        ("cache-pause-wait", plan.ResumeSeconds), ("cache-secs", plan.AheadSeconds),
        ("demuxer-readahead-secs", plan.AheadSeconds), ("demuxer-max-bytes", plan.MaxBytes)
    ];
    internal async Task ControlBufferAsync(int expectedGeneration, CancellationToken token)
    {
        var controlTrace = telemetry;
        var policy = new OnlineBufferPolicy(); var epoch = Volatile.Read(ref bufferEpoch);
        var applied = BufferOptions(OnlineBufferPlan.Initial).ToDictionary(item => item.Option, item => Convert.ToDouble(item.Value));
        var unavailable = new HashSet<string>();
        try {
            while (!token.IsCancellationRequested && !closing && generation == expectedGeneration) {
                if (epoch != Volatile.Read(ref bufferEpoch)) { epoch = Volatile.Read(ref bufferEpoch); policy.ResetAfterSeek(); }
                if (!awaitingFileStart) {
                    var sample = BufferSample(); var plan = policy.Update(sample); bufferPlan = plan;
                    ManageIndexWarmup(plan, sample, token);
                    controlTrace?.Sample("buffer_policy", ("reserve_seconds", sample.Seconds ?? double.NaN),
                        ("resume_seconds", plan.ResumeSeconds), ("ahead_seconds", plan.AheadSeconds),
                        ("max_bytes", plan.MaxBytes), ("supply_ratio", plan.SupplyRatio ?? double.NaN),
                        ("index_allowed", plan.CanPrepareIndex ? 1 : 0), ("underruns_observed", sample.Underruns));
                    foreach (var (option, value) in BufferOptions(plan)) {
                        token.ThrowIfCancellationRequested();
                        if (generation != expectedGeneration || closing) return;
                        if (epoch != Volatile.Read(ref bufferEpoch)) break;
                        var number = Convert.ToDouble(value);
                        var threshold = option == "cache-pause-wait" ? .5 : option == "demuxer-max-bytes" ? 8 * 1024 * 1024 : 2;
                        if (unavailable.Contains(option) || Math.Abs(number - applied[option]) < threshold) continue;
                        try {
                            await CommandAsync(["set_property", option, value], token); applied[option] = number;
                            controlTrace?.Event("buffer_option_applied", ("option_index", Array.FindIndex(BufferOptions(plan), item => item.Option == option)), ("value", number));
                        } catch (IOException) when (!token.IsCancellationRequested) {
                            unavailable.Add(option); controlTrace?.Event("buffer_option_unavailable", ("option_index", Array.FindIndex(BufferOptions(plan), item => item.Option == option)));
                        }
                    }
                }
                await Task.Delay(1000, token);
            }
        } catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or ObjectDisposedException or TimeoutException) {
            controlTrace?.Event("buffer_control_unavailable"); Trace("Buffer adaptativo: " + error.GetType().Name);
        }
    }
}

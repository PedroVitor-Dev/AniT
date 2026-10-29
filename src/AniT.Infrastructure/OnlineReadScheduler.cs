using System.Diagnostics;

namespace AniT.Infrastructure;

/// <summary>One stream cursor; the newest live demand owns missing data. Cached older reads may proceed.</summary>
internal sealed class OnlineReadScheduler(Stream source, Func<long, int, bool>? available, CancellationToken lifetime)
{
    internal sealed class Demand(long id, long offset)
    {
        internal readonly long Id = id;
        internal long Offset = offset;
        internal bool WaitReported;
    }
    readonly object sync = new();
    readonly SemaphoreSlim reading = new(1, 1);
    readonly SortedDictionary<long, Demand> demands = [];
    TaskCompletionSource changed = NewSignal();
    CancellationTokenSource? turn;
    long next;
    internal Action<string, long, double>? Observe { get; set; }
    void Report(string name, long id, double ms) { try { Observe?.Invoke(name, id, ms); } catch { } }
    static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    void Pulse() { var previous = changed; changed = NewSignal(); previous.TrySetResult(); }
    internal Demand Add(long offset)
    {
        lock (sync) {
            var demand = new Demand(++next, offset); demands.Add(demand.Id, demand);
            // Cancellation is only for an actual newer request, never a periodic read deadline.
            turn?.Cancel(); if (turn is null) source.Position = offset; Pulse(); return demand;
        }
    }
    internal void Remove(Demand demand)
    {
        lock (sync) { if (demands.ContainsKey(demand.Id)) { var latest = demands.Keys.Last() == demand.Id; demands.Remove(demand.Id); if (latest) { turn?.Cancel(); if (turn is null && demands.Count > 0) source.Position = demands.Values.Last().Offset; } Pulse(); } }
    }
    internal async ValueTask<int> ReadAsync(Demand demand, long offset, Memory<byte> buffer, CancellationToken token, bool indexBytes = false)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime);
        token = operation.Token;
        var started = Stopwatch.GetTimestamp(); var preempted = false;
        while (true) {
            token.ThrowIfCancellationRequested();
            Task signal;
            lock (sync) { demand.Offset = offset; signal = changed.Task; }
            await reading.WaitAsync(token);
            var waiting = false;
            try {
                CancellationTokenSource? attempt = null;
                lock (sync) {
                    var current = demands.Values.LastOrDefault();
                    if (current is null || !demands.ContainsKey(demand.Id)) throw new OperationCanceledException(token);
                    if (current.Id != demand.Id && (available is null || !available(offset, buffer.Length))) waiting = true;
                    else { turn = attempt = CancellationTokenSource.CreateLinkedTokenSource(token); }
                }
                if (!waiting) {
                    try {
                        source.Position = offset;
                        if (!demand.WaitReported && available is not null && !available(offset, buffer.Length)) {
                            demand.WaitReported = true; Report("read_wait_started", demand.Id, offset);
                        }
                        var count = indexBytes && source is MatroskaStreamingView view
                            ? await view.ReadIndexBytesAsync(buffer, attempt!.Token) : await source.ReadAsync(buffer, attempt!.Token);
                        lock (sync) demand.Offset = offset + count;
                        if (preempted) Report("read_resumed", demand.Id, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                        return count;
                    } catch (OperationCanceledException) when (!token.IsCancellationRequested) {
                        preempted = true;
                        Report("read_preempted", demand.Id, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    } finally {
                        lock (sync) {
                            if (ReferenceEquals(turn, attempt)) turn = null;
                            attempt?.Dispose();
                            // Reading already cached bytes for an old HTTP response must not
                            // leave the torrent's streaming picker pointing away from current demand.
                            var current = demands.Values.LastOrDefault();
                            if (current is not null && current.Id != demand.Id) source.Position = current.Offset;
                        }
                    }
                }
            } finally { reading.Release(); }
            // The signal wakes immediately when ownership changes. Poll only the bitfield;
            // never seek/restart an older missing read while a newer request is active.
            await Task.WhenAny(signal, Task.Delay(75, token));
        }
    }
}

using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AniT.Theater;

/// <summary>Loopback-only HTTP byte ranges for MPC-HC; remote data travels exclusively inside the room's pinned TLS channel.</summary>
internal sealed class TheaterStreamProxy : IAsyncDisposable
{
    internal const int StartupBlockSize = 256 * 1024;
    private readonly Func<StreamBufferPolicy> policy;
    private long aheadStart, aheadEnd;
    private int aheadOwner;
    private bool reserving, warming;
    private Task readAhead = Task.CompletedTask;
    private const int CacheBlocks = 32; // 32 MiB, independent of episode size.
    private readonly long length;
    private readonly Func<long, int, CancellationToken, Task<StreamReadBlock>> fetch;
    private readonly ConcurrentDictionary<int, Task> loaders = new();
    private int loaderSequence, deliveredOwner;
    private long deliveredEnd = -1;
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim clients = new(8, 8);
    private readonly object cacheLock = new();
    private readonly Dictionary<long, (TaskCompletionSource<StreamReadBlock> Ready, TaskCompletionSource Cached)> pending = new();
    private readonly Dictionary<long, (int Readers, StreamReadDemand Demand)> demands = new();

    internal StreamReadDemand? PendingDemand
    {
        get { lock (cacheLock) return demands.Values.Select(d => d.Demand).MinBy(d => d.SinceMs); }
    }

    internal bool IsBlockPending(long offset) { lock (cacheLock) return pending.ContainsKey(offset); }

    private async Task<(StreamReadBlock Block, int Available)> ReadDemandAsync(long offset, int within)
    {
        var task = GetBlockAsync(offset);
        if (task.IsCompletedSuccessfully && task.Result.Received > within) return (task.Result, task.Result.Received);
        lock (cacheLock)
        {
            if (demands.TryGetValue(offset, out var existing)) demands[offset] = (existing.Readers + 1, existing.Demand);
            else demands[offset] = (1, new(offset, (int)Math.Min(offset == 0 ? StartupBlockSize : TheaterRoom.StreamBlockSize, length - offset), ClockEstimator.NowMs));
        }
        try
        {
            var block = await task.ConfigureAwait(false);
            return (block, await block.AvailableAsync(within, lifetime.Token).ConfigureAwait(false));
        }
        finally
        {
            lock (cacheLock)
            {
                if (demands.TryGetValue(offset, out var current))
                {
                    if (current.Readers == 1) demands.Remove(offset);
                    else demands[offset] = (current.Readers - 1, current.Demand);
                }
            }
        }
    }
    private readonly Dictionary<long, LinkedListNode<(long Offset, byte[] Bytes)>> cache = new();
    private readonly LinkedList<(long Offset, byte[] Bytes)> recent = new();
    private readonly ConcurrentDictionary<int, Task> handlers = new();
    private readonly string resource, contentType;
    private readonly Task accept;
    private int sequence, disposed;
    public Uri Address { get; }

    public TheaterStreamProxy(long length, string filename, Func<long, int, CancellationToken, Task<byte[]>> fetch, Func<StreamBufferPolicy>? policy = null)
        : this(length, filename, async (offset, count, token) => StreamReadBlock.Completed(await fetch(offset, count, token).ConfigureAwait(false)), policy) { }

    public TheaterStreamProxy(long length, string filename, Func<long, int, CancellationToken, Task<StreamReadBlock>> fetch, Func<StreamBufferPolicy>? policy = null)
    {
        this.length = length; this.fetch = fetch; this.policy = policy ?? (() => new(2 * TheaterRoom.StreamBlockSize));
        contentType = Path.GetExtension(filename).ToLowerInvariant() switch { ".avi" => "video/x-msvideo", ".mkv" => "video/x-matroska", ".mp4" or ".m4v" => "video/mp4", ".webm" => "video/webm", ".ts" or ".m2ts" => "video/mp2t", _ => "application/octet-stream" };
        resource = "/" + TheaterInvite.NewToken() + "/episode" + Path.GetExtension(filename);
        listener.Start(8);
        Address = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}{resource}");
        accept = AcceptAsync();
    }

    public async Task PrimeAsync(CancellationToken cancellationToken)
    {
        var block = await GetBlockAsync(0).WaitAsync(cancellationToken).ConfigureAwait(false);
        await block.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task<StreamReadBlock> GetBlockAsync(long start)
    {
        lock (cacheLock)
        {
            lifetime.Token.ThrowIfCancellationRequested();
            if (cache.TryGetValue(start, out var item))
            {
                recent.Remove(item); recent.AddFirst(item); return Task.FromResult(StreamReadBlock.Completed(item.Value.Bytes));
            }
            if (pending.TryGetValue(start, out var existing)) return existing.Ready.Task;
            var completion = new TaskCompletionSource<StreamReadBlock>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pending.Add(start, (completion, cached));
            var id = Interlocked.Increment(ref loaderSequence);
            var loading = LoadBlockAsync(start, completion, cached); loaders[id] = loading;
            _ = RemoveLoaderAsync(id, loading);
            return completion.Task;
        }
    }

    private async Task RemoveLoaderAsync(int id, Task loading)
    {
        try { await loading.ConfigureAwait(false); } finally { loaders.TryRemove(id, out _); }
    }

    private async Task LoadBlockAsync(long start, TaskCompletionSource<StreamReadBlock> completion, TaskCompletionSource cached)
    {
        StreamReadBlock? block = null;
        try
        {
            block = await fetch(start, (int)Math.Min(start == 0 ? StartupBlockSize : TheaterRoom.StreamBlockSize, length - start), lifetime.Token).ConfigureAwait(false);
            completion.TrySetResult(block);
            var bytes = await block.Completion.Task.WaitAsync(lifetime.Token).ConfigureAwait(false);
            lock (cacheLock)
            {
                var added = recent.AddFirst((start, bytes)); cache.Add(start, added);
                if (cache.Count > CacheBlocks) { var last = recent.Last!; cache.Remove(last.Value.Offset); recent.RemoveLast(); }
                pending.Remove(start);
            }
        }
        catch (Exception error)
        {
            block?.Completion.TrySetException(error);
            lock (cacheLock) pending.Remove(start);
            completion.TrySetException(error);
        }
        finally { cached.TrySetResult(); }
    }

    private long NextBlock(long offset) => offset == 0 ? StartupBlockSize : offset + TheaterRoom.StreamBlockSize;

    internal (int ReceivedBytes, int RequiredBytes, bool Complete) GetReserve(long start, int targetBytes, Func<long, int>? partial = null)
    {
        lock (cacheLock)
        {
            var required = (int)Math.Min(Math.Clamp(targetBytes, 1, StreamAdaptiveBuffer.MaxReserveBytes), length - start);
            var complete = 0; var received = 0; var cursor = start;
            while (cursor < length && complete < required)
            {
                if (!cache.TryGetValue(cursor, out var block))
                {
                    received = complete + Math.Max(0, partial?.Invoke(cursor) ?? 0); break;
                }
                complete += block.Value.Bytes.Length; received = complete; cursor = NextBlock(cursor);
            }
            return (Math.Min(received, required), required, complete >= required);
        }
    }

    internal void EnsureReserve(long start, int targetBytes)
    {
        lock (cacheLock)
        {
            if (disposed != 0) return;
            reserving = true; aheadStart = start; aheadEnd = Math.Min(length, start + Math.Clamp(targetBytes, 1, StreamAdaptiveBuffer.MaxReserveBytes));
            StartWarming();
        }
    }

    internal void ReleaseReserve() { lock (cacheLock) { reserving = false; aheadOwner = 0; } }

    private void ScheduleAhead(long start, long last, int owner)
    {
        var size = policy().ReadAheadBytes;
        lock (cacheLock)
        {
            if (disposed != 0 || reserving || owner < aheadOwner) return;
            aheadOwner = owner; aheadStart = start; aheadEnd = Math.Min(last + 1, start + Math.Clamp(size, 1, StreamAdaptiveBuffer.MaxReserveBytes));
            StartWarming();
        }
    }

    // Two pipelined speculative requests leave one slot for demand; refill as soon as either finishes.
    private void StartWarming()
    {
        if (warming) return;
        warming = true; readAhead = WarmAsync();
    }

    private async Task WarmAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                Task<StreamReadBlock>[] work;
                Task[] cached;
                lock (cacheLock)
                {
                    var offsets = new List<long>(TheaterRoom.StreamRequestWindow - 1);
                    for (var cursor = aheadStart; cursor < aheadEnd && offsets.Count < TheaterRoom.StreamRequestWindow - 1; cursor = NextBlock(cursor))
                        if (!cache.ContainsKey(cursor)) offsets.Add(cursor);
                    if (offsets.Count == 0) { warming = false; return; }
                    work = offsets.Select(GetBlockAsync).ToArray();
                    cached = offsets.Select(offset => pending.TryGetValue(offset, out var p) ? p.Cached.Task : Task.CompletedTask).ToArray();
                }
                var blocks = await Task.WhenAll(work).ConfigureAwait(false);
                await Task.WhenAny(cached).ConfigureAwait(false);
                foreach (var block in blocks)
                    if (block.Completion.Task.IsFaulted || block.Completion.Task.IsCanceled) await block.Completion.Task.ConfigureAwait(false);
            }
        }
        catch (Exception) { /* Demand reads report failures; room cancellation also stops speculative I/O. */ }
        lock (cacheLock) warming = false;
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(lifetime.Token).ConfigureAwait(false);
                if (!clients.Wait(0)) { client.Dispose(); continue; }
                var id = Interlocked.Increment(ref sequence);
                var task = HandleAsync(client, id); handlers[id] = task;
                _ = RemoveHandlerAsync(id, task);
            }
        }
        catch (Exception) when (lifetime.IsCancellationRequested) { }
    }
    private async Task RemoveHandlerAsync(int id, Task task) { try { await task.ConfigureAwait(false); } finally { handlers.TryRemove(id, out _); } }

    private async Task HandleAsync(TcpClient client, int id)
    {
        using (client)
            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var header = new byte[8192]; int used = 0, end;
                do
                {
                    if (used == header.Length) { await ReplyAsync(stream, "431 Request Header Fields Too Large").ConfigureAwait(false); return; }
                    var read = await stream.ReadAsync(header.AsMemory(used), timeout.Token).ConfigureAwait(false); if (read == 0) return; used += read;
                    end = Encoding.ASCII.GetString(header, 0, used).IndexOf("\r\n\r\n", StringComparison.Ordinal);
                } while (end < 0);
                var lines = Encoding.ASCII.GetString(header, 0, end).Split("\r\n"); var request = lines[0].Split(' ');
                if (request.Length != 3 || request[1] != resource) { await ReplyAsync(stream, "404 Not Found").ConfigureAwait(false); return; }
                if (request[0] is not ("GET" or "HEAD")) { await ReplyAsync(stream, "405 Method Not Allowed").ConfigureAwait(false); return; }
                var ranges = lines.Skip(1).Where(l => l.StartsWith("Range:", StringComparison.OrdinalIgnoreCase)).ToArray();
                long start = 0, last = length - 1;
                if (ranges.Length > 1 || ranges.Length == 1 && !TryRange(ranges[0][6..].Trim(), length, out start, out last))
                { await ReplyAsync(stream, "416 Range Not Satisfiable", $"Content-Range: bytes */{length}\r\n").ConfigureAwait(false); return; }
                var partial = ranges.Length == 1;
                var response = $"HTTP/1.1 {(partial ? "206 Partial Content" : "200 OK")}\r\nAccept-Ranges: bytes\r\nContent-Type: {contentType}\r\nContent-Length: {last - start + 1}\r\nConnection: close\r\nCache-Control: no-store\r\n" + (partial ? $"Content-Range: bytes {start}-{last}/{length}\r\n" : "") + "\r\n";
                await WriteAsync(stream, Encoding.ASCII.GetBytes(response)).ConfigureAwait(false);
                if (request[0] == "HEAD") return;
                bool sequential;
                lock (cacheLock) sequential = start > 0 && start == deliveredEnd + 1;
                var prefetch = sequential || last - start + 1 >= TheaterRoom.StreamBlockSize;
                while (start <= last)
                {
                    var aligned = start < StartupBlockSize ? 0 : StartupBlockSize + (start - StartupBlockSize) / TheaterRoom.StreamBlockSize * TheaterRoom.StreamBlockSize;
                    var offset = (int)(start - aligned);
                    var data = await ReadDemandAsync(aligned, offset).ConfigureAwait(false);
                    // Follow sequential short ranges beyond the HTTP boundary; isolated probes stay cheap.
                    var next = NextBlock(aligned);
                    if (prefetch && next < length) ScheduleAhead(next, length - 1, id);
                    var count = (int)Math.Min(data.Available - offset, last - start + 1);
                    await WriteAsync(stream, data.Block.Bytes.AsMemory(offset, count)).ConfigureAwait(false); start += count;
                    lock (cacheLock) if (id >= deliveredOwner) { deliveredOwner = id; deliveredEnd = start - 1; }
                }
            }
            catch (Exception) { /* Closing a player/seek cancels its HTTP socket; the room remains available. */ }
            finally { clients.Release(); }
    }

    private async Task WriteAsync(NetworkStream stream, ReadOnlyMemory<byte> bytes)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await stream.WriteAsync(bytes, timeout.Token).ConfigureAwait(false);
    }
    private Task ReplyAsync(NetworkStream stream, string status, string extra = "")
        => WriteAsync(stream, Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\n{extra}Content-Length: 0\r\nConnection: close\r\n\r\n"));

    internal static bool TryRange(string value, long length, out long first, out long last)
    {
        first = 0; last = length - 1;
        if (!value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) || value.Contains(',')) return false;
        var parts = value[6..].Split('-'); if (parts.Length != 2) return false;
        static bool Number(string text, out long number) => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number);
        if (parts[0].Length == 0)
        {
            if (!Number(parts[1], out var suffix) || suffix <= 0) return false;
            first = Math.Max(0, length - suffix); return true;
        }
        if (!Number(parts[0], out first) || first >= length || first < 0) return false;
        if (parts[1].Length == 0) return true;
        if (!Number(parts[1], out var requested) || requested < first) return false;
        last = Math.Min(last, requested); return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel(); listener.Stop();
        try { await accept.ConfigureAwait(false); } catch (Exception) { }
        await Task.WhenAll(handlers.Values).ConfigureAwait(false);
        try { await readAhead.ConfigureAwait(false); } catch (Exception) { }
        try { await Task.WhenAll(loaders.Values).ConfigureAwait(false); } catch (Exception) { }
        lock (cacheLock) { cache.Clear(); recent.Clear(); }
        lifetime.Dispose();
    }
}

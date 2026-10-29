using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AniT.Infrastructure;

/// <summary>Loopback-only, token-scoped HTTP ranges over MonoTorrent's verified, seekable stream.</summary>
public sealed class OnlineStreamServer : IAsyncDisposable
{
    readonly Stream source;
    readonly Func<long, int, bool>? isAvailable;
    readonly System.Collections.Concurrent.ConcurrentDictionary<long,long> readers = new();
    long nextReader;
    readonly TcpListener listener = new(IPAddress.Loopback, 0);
    readonly CancellationTokenSource lifetime = new();
    readonly SemaphoreSlim reading = new(1, 1);
    readonly SemaphoreSlim connections = new(8, 8);
    readonly List<Task> requests = [];
    readonly string route;
    readonly string contentType;
    readonly Task accepting;
    public Uri Address { get; }
    public Action<string>? Diagnostic { get; set; }

    public OnlineStreamServer(Stream source, string fileName, Func<long, int, bool>? isAvailable = null)
    {
        this.source = source; this.isAvailable = isAvailable;
        contentType = Path.GetExtension(fileName).ToLowerInvariant() switch {
            ".mkv" => "video/x-matroska", ".mp4" or ".m4v" => "video/mp4",
            ".webm" => "video/webm", ".avi" => "video/x-msvideo", _ => "application/octet-stream"
        };
        route = "/" + Guid.NewGuid().ToString("N") + "/" + Uri.EscapeDataString(fileName);
        listener.Start();
        Address = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}{route}");
        accepting = AcceptAsync();
    }

    async Task AcceptAsync()
    {
        try {
            while (!lifetime.IsCancellationRequested) {
                await connections.WaitAsync(lifetime.Token);
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(lifetime.Token); }
                catch { connections.Release(); throw; }
                lock (requests) { requests.RemoveAll(t => t.IsCompleted); requests.Add(ServeAsync(client)); }
            }
        } catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }

    async Task ServeAsync(TcpClient client)
    {
        using (client) {
            long readerId = 0;
            try {
                client.NoDelay = true;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                var token = timeout.Token;
                await using var network = client.GetStream();
                var header = new List<byte>(); var next = new byte[1];
                while (header.Count < 16384 && await network.ReadAsync(next, token) == 1) {
                    header.Add(next[0]);
                    if (header.Count >= 4 && header[^4] == 13 && header[^3] == 10 && header[^2] == 13 && header[^1] == 10) break;
                }
                var lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");
                var request = lines[0].Split(' ');
                if (request.Length != 3 || request[1] != route || request[0] is not ("GET" or "HEAD")) { await RespondAsync(network, "404 Not Found", 0, "", token); return; }
                var length = source.Length; long start = 0, end = length - 1;
                var range = lines.FirstOrDefault(l => l.StartsWith("Range:", StringComparison.OrdinalIgnoreCase));
                if (range is not null && !TryRange(range[6..].Trim(), length, out start, out end)) {
                    await RespondAsync(network, "416 Range Not Satisfiable", 0, $"Content-Range: bytes */{length}\r\n", token); return;
                }
                // The header is bounded, but waiting for verified pieces is buffering, not EOF.
                // Client disconnect and the server lifetime still cancel every pending read.
                timeout.CancelAfter(Timeout.InfiniteTimeSpan);
                Diagnostic?.Invoke($"HTTP {request[0]} bytes={start}-{end}");
                await RespondAsync(network, range is null ? "200 OK" : "206 Partial Content", end - start + 1,
                    "Accept-Ranges: bytes\r\n" + (range is null ? "" : $"Content-Range: bytes {start}-{end}/{length}\r\n"), token);
                if (request[0] == "HEAD") return;
                readerId = Interlocked.Increment(ref nextReader); readers[readerId] = start;
                var buffer = new byte[64 * 1024];
                while (start <= end) {
                    // A container can probe its index while its video response is still pending.
                    // Yield a stalled verified read so the probe gets its own turn, without closing either response.
                    if (client.Client.Poll(0, SelectMode.SelectRead) && client.Client.Available == 0) return;
                    int count;
                    await reading.WaitAsync(token);
                    try {
                        var requested = (int)Math.Min(buffer.Length, end - start + 1);
                        var newest = readers.Keys.DefaultIfEmpty(readerId).Max();
                        // An old HTTP read-ahead must not keep seeking the torrent picker away
                        // from a newer container/index/seek request that is waiting for pieces.
                        if (isAvailable is not null && readerId != newest && !isAvailable(start, requested)) {
                            count = -1;
                        } else {
                        source.Position = start;
                        using var turn = CancellationTokenSource.CreateLinkedTokenSource(token);
                        turn.CancelAfter(TimeSpan.FromMilliseconds(300));
                        try { count = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, end - start + 1)), turn.Token); }
                        catch (OperationCanceledException) when (!token.IsCancellationRequested) { count = -1; }
                        }
                        if (isAvailable is not null && readerId != newest && readers.TryGetValue(newest, out var pending))
                            source.Position = pending;
                    } finally { reading.Release(); }
                    if (count < 0) { await Task.Delay(25, token); continue; }
                    if (count == 0) break;
                    await network.WriteAsync(buffer.AsMemory(0, count), token);
                    start += count; readers[readerId] = start;
                }
            } catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException) { Diagnostic?.Invoke($"HTTP interrompido: {ex.GetType().Name}: {ex.Message}"); }
            finally {
                if (readerId != 0) readers.TryRemove(readerId, out _);
                connections.Release();
            }
        }
    }

    Task RespondAsync(Stream stream, string status, long length, string extra, CancellationToken token) =>
        stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: {length}\r\nContent-Type: {contentType}\r\nCache-Control: no-store\r\nConnection: close\r\n{extra}\r\n"), token).AsTask();

    public async Task<bool> PrepareIndexAsync(CancellationToken token)
    {
        if(source is not MatroskaStreamingView view)return false;
        await reading.WaitAsync(token);
        try{return await view.PrepareIndexAsync(token);}finally{reading.Release();}
    }

    public static bool TryRange(string value, long length, out long start, out long end)
    {
        start = 0; end = length - 1;
        if (length <= 0 || !value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) || value.Contains(',')) return false;
        var pair = value[6..].Split('-');
        if (pair.Length != 2) return false;
        if (pair[0].Length == 0) {
            if (!long.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out var suffix) || suffix <= 0) return false;
            start = Math.Max(0, length - suffix); return true;
        }
        if (!long.TryParse(pair[0], NumberStyles.None, CultureInfo.InvariantCulture, out start) || start >= length) return false;
        if (pair[1].Length > 0 && (!long.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out end) || end < start)) return false;
        end = Math.Min(end, length - 1); return true;
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel(); listener.Stop();
        await accepting;
        Task[] pending; lock (requests) pending = requests.ToArray();
        await Task.WhenAll(pending);
        await source.DisposeAsync();
        lifetime.Dispose(); reading.Dispose(); connections.Dispose();
    }
}

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace AniT.Testing;

/// <summary>Forwards real pinned TLS, deliberately stalls only the second TCP connection after authentication.</summary>
internal sealed class TheaterTestRelay : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<int, TcpClient> sockets = new();
    private readonly ConcurrentDictionary<int, Task> handlers = new();
    private readonly Task accept;
    private readonly int targetPort, mediaTargetPort;
    private TaskCompletionSource? mediaRelease;
    private int accepted, mediaDelayMs, controlDelayMs, jitterMs, mediaJitterMs;
    public TaskCompletionSource MediaBlocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Accepted => Volatile.Read(ref accepted);
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public TheaterTestRelay(int targetPort, int? mediaTargetPort = null) { this.targetPort = targetPort; this.mediaTargetPort = mediaTargetPort ?? targetPort; listener.Start(); accept = AcceptAsync(); }
    public void ConfigureDelays(int controlMilliseconds, int mediaMilliseconds, int variationMilliseconds, int? mediaVariationMilliseconds = null)
    {
        Interlocked.Exchange(ref controlDelayMs, Math.Clamp(controlMilliseconds, 0, 1000));
        Interlocked.Exchange(ref mediaDelayMs, Math.Clamp(mediaMilliseconds, 0, 1000));
        Interlocked.Exchange(ref jitterMs, Math.Clamp(variationMilliseconds, 0, 1000));
        Interlocked.Exchange(ref mediaJitterMs, Math.Clamp(mediaVariationMilliseconds ?? variationMilliseconds, 0, 1000));
    }
    public void DelayMedia(int milliseconds) => Interlocked.Exchange(ref mediaDelayMs, Math.Clamp(milliseconds, 0, 1000));
    public void PauseMedia() => Volatile.Write(ref mediaRelease, new(TaskCreationOptions.RunContinuationsAsynchronously));
    public void ResumeMedia() => Volatile.Read(ref mediaRelease)?.TrySetResult();
    public void DisconnectMedia() { if (sockets.TryGetValue(2, out var client)) client.Dispose(); }

    private async Task AcceptAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(lifetime.Token).ConfigureAwait(false);
                var id = Interlocked.Increment(ref accepted); sockets[id] = client;
                handlers[id] = RelayAsync(client, id);
            }
        }
        catch (Exception) when (lifetime.IsCancellationRequested) { }
    }

    private async Task RelayAsync(TcpClient client, int id)
    {
        using (client)
        using (var remote = new TcpClient { NoDelay = true })
        {
            try
            {
                client.NoDelay = true;
                await remote.ConnectAsync(IPAddress.Loopback, id == 2 ? mediaTargetPort : targetPort, lifetime.Token).ConfigureAwait(false);
                using var done = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                var up = Pump(client.GetStream(), remote.GetStream(), false, done.Token);
                var down = Pump(remote.GetStream(), client.GetStream(), id == 2, done.Token);
                await Task.WhenAny(up, down).ConfigureAwait(false); done.Cancel();
                await Task.WhenAll(up, down).ConfigureAwait(false);
            }
            catch (Exception) { }
        }
    }

    private async Task Pump(NetworkStream input, NetworkStream output, bool media, CancellationToken token)
    {
        var buffer = new byte[16384];
        int count, sequence = 0;
        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (media && Volatile.Read(ref mediaRelease) is { } blocked && !blocked.Task.IsCompleted)
            {
                MediaBlocked.TrySetResult(); await blocked.Task.WaitAsync(token).ConfigureAwait(false);
            }
            var delay = (media ? Volatile.Read(ref mediaDelayMs) : Volatile.Read(ref controlDelayMs))
                + (++sequence % 4 == 0 ? 2 : sequence % 2) * (media ? Volatile.Read(ref mediaJitterMs) : Volatile.Read(ref jitterMs));
            if (delay > 0) await Task.Delay(delay, token).ConfigureAwait(false);
            await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel(); listener.Stop(); ResumeMedia();
        foreach (var socket in sockets.Values) socket.Dispose();
        try { await accept.ConfigureAwait(false); } catch (Exception) { }
        try { await Task.WhenAll(handlers.Values).ConfigureAwait(false); } catch (Exception) { }
        lifetime.Dispose();
    }
}

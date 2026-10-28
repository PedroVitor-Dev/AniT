using System.Threading.Channels;
using System.Collections.Concurrent;

namespace AniT.Theater;

public enum TheaterMode { PeerSync, DirectStream }

public sealed partial class TheaterRoom
{
    internal const int StreamBlockSize = 1024 * 1024;
    internal const int StreamRequestWindow = 3;
    private readonly StreamAdaptiveBuffer adaptiveBuffer = new();
    private FileStream? streamFile;
    private TheaterStreamProxy? streamProxy;
    private readonly ConcurrentDictionary<long, StreamBlock> streamBlocks = new();
    private long streamRequest;
    private readonly ConcurrentDictionary<long, Task> streamFetches = new();
    private readonly SemaphoreSlim streamGate = new(StreamRequestWindow, StreamRequestWindow);
    private readonly SemaphoreSlim streamStartGate = new(1, 1);
    public long StreamBytesReceived => Interlocked.Read(ref transferredBytes);

    /// <summary>Primes a bounded in-memory cache and exposes only a secret loopback URL to the local MPC-HC.</summary>
    public async Task<Uri> StartStreamingAsync(CancellationToken cancellationToken = default)
    {
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        await streamStartGate.WaitAsync(pending.Token).ConfigureAwait(false);
        try
        {
            if (IsHost || Mode != TheaterMode.DirectStream || !IsConnected) throw new InvalidOperationException("Entre em uma sala de streaming primeiro.");
            if (streamProxy is not null) return streamProxy.Address;
            await EnsureMediaChannelAsync(pending.Token).ConfigureAwait(false);
            var proxy = new TheaterStreamProxy(media.Length, OfferedName, FetchStreamBlockAsync, () => AdaptiveSnapshot.Policy);
            try
            {
                await proxy.PrimeAsync(pending.Token).ConfigureAwait(false);
                pending.Token.ThrowIfCancellationRequested();
                streamProxy = proxy; Changed?.Invoke();
                return proxy.Address;
            }
            catch { await proxy.DisposeAsync().ConfigureAwait(false); throw; }
        }
        finally { streamStartGate.Release(); }
    }

    private async Task<StreamReadBlock> FetchStreamBlockAsync(long offset, int count, CancellationToken cancellationToken)
    {
        await streamGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var block = new StreamBlock(Interlocked.Increment(ref streamRequest), offset, count);
        streamBlocks.TryAdd(block.Request, block);
        var fetching = FetchStreamBlockCoreAsync(block, cancellationToken);
        streamFetches[block.Request] = fetching;
        _ = RemoveFetchAsync(block.Request, fetching);
        return block.Data;
    }

    private async Task RemoveFetchAsync(long request, Task fetching)
    {
        try { await fetching.ConfigureAwait(false); } finally { streamFetches.TryRemove(request, out _); }
    }

    private async Task FetchStreamBlockCoreAsync(StreamBlock block, CancellationToken cancellationToken)
    {
        bool sent = false, measuring = false;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var link = peer ?? throw new IOException("Host desconectado.");
            adaptiveBuffer.RequestStarted(ClockEstimator.NowMs); measuring = true; sent = true;
            await (link.Media ?? throw new IOException("Canal de vídeo desconectado.")).Channel.SendAsync(new("stream-request", Offset: block.Offset, Count: block.Bytes.Length, Request: block.Request), timeout.Token).ConfigureAwait(false);
            await block.Complete.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception error) { block.Complete.TrySetException(error); if (sent) peer?.Dispose(); }
        finally { if (measuring) adaptiveBuffer.RequestFinished(ClockEstimator.NowMs); streamBlocks.TryRemove(block.Request, out _); streamGate.Release(); }
    }

    private void ReceiveStreamChunk(RoomPacket packet)
    {
        if (!streamBlocks.TryGetValue(packet.Request, out var block)) throw new InvalidDataException("Streaming não solicitado.");
        if (block.Complete.Task.IsCompleted || packet.Request != block.Request || packet.Offset != block.Offset + block.Received || packet.Data is not { Length: > 0 and <= PacketChannel.StreamChunkBytes } bytes || bytes.Length > block.Bytes.Length - block.Received)
            throw new InvalidDataException("Trecho de streaming inválido.");
        block.Data.Append(bytes);
        adaptiveBuffer.Received(bytes.Length, ClockEstimator.NowMs);
        ReportProgress(Interlocked.Read(ref transferredBytes) + bytes.Length);
    }

    private async Task ServeStreamAsync(Peer link, ChannelReader<RoomPacket> requests, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var request in requests.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (link.Removed) return;
                var file = streamFile ?? throw new IOException("O arquivo transmitido não está disponível.");
                if (file.Length != media.Length) throw new IOException("O arquivo transmitido mudou.");
                var buffer = new byte[PacketChannel.StreamChunkBytes]; int sent = 0;
                while (sent < request.Count)
                {
                    if (link.Removed) return;
                    var count = await RandomAccess.ReadAsync(file.SafeFileHandle, buffer.AsMemory(0, Math.Min(buffer.Length, request.Count - sent)), request.Offset + sent, cancellationToken).ConfigureAwait(false);
                    if (count == 0) throw new EndOfStreamException();
                    await link.Channel.SendStreamChunkAsync(request.Request, request.Offset + sent, buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    sent += count;
                    ReportProgress(Interlocked.Read(ref transferredBytes) + count);
                }
                await link.Channel.SendAsync(new("stream-end", Request: request.Request), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception) { link.Dispose(); }
    }

    private sealed class StreamBlock(long request, long offset, int count)
    {
        public long Request { get; } = request;
        public long Offset { get; } = offset;
        public StreamReadBlock Data { get; } = new(count);
        public byte[] Bytes => Data.Bytes;
        public int Received => Data.Received;
        public TaskCompletionSource<byte[]> Complete => Data.Completion;
    }
}

using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using AniT.Infrastructure;

namespace AniT.Tests;

public sealed class OnlineStreamingDemandTests
{
    sealed class WaitingStream : MemoryStream
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Reads, Cancelled;
        internal WaitingStream() : base(new byte[200000]) { }
        public override async ValueTask<int> ReadAsync(Memory<byte> bytes, CancellationToken token = default)
        {
            Interlocked.Increment(ref Reads); Started.TrySetResult();
            try { await Ready.Task.WaitAsync(token); }
            catch (OperationCanceledException) { Interlocked.Increment(ref Cancelled); throw; }
            return await base.ReadAsync(bytes, token);
        }
    }

    [Fact]
    public async Task BufferingWithoutNewDemandDoesNotRepeatedlyCancelAndRestartItsRead()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var input = new WaitingStream();
        await using var server = new OnlineStreamServer(input, "test.bin");
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, server.Address);
        request.Headers.Range = new RangeHeaderValue(0, 63);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        await input.Started.Task.WaitAsync(timeout.Token);
        await Task.Delay(800, timeout.Token);
        Assert.Equal(1, input.Reads); Assert.Equal(0, input.Cancelled);
        input.Ready.TrySetResult();
        Assert.Equal(64, (await response.Content.ReadAsByteArrayAsync(timeout.Token)).Length);
    }

    sealed class TwoRegions : MemoryStream
    {
        internal readonly TaskCompletionSource OldStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource NewStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource OldResumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource OldReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource NewReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int oldReads;
        internal TwoRegions() : base(Enumerable.Range(0, 200000).Select(i => (byte)i).ToArray()) { }
        public override async ValueTask<int> ReadAsync(Memory<byte> bytes, CancellationToken token = default)
        {
            if (Position == 0) {
                OldStarted.TrySetResult(); if (Interlocked.Increment(ref oldReads) > 1) OldResumed.TrySetResult();
                await OldReady.Task.WaitAsync(token);
            } else { NewStarted.TrySetResult(); await NewReady.Task.WaitAsync(token); }
            return await base.ReadAsync(bytes, token);
        }
    }

    [Fact]
    public async Task DisconnectingTheNewestBufferingRequestImmediatelyReleasesTheOlderResponse()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var input = new TwoRegions();
        await using var server = new OnlineStreamServer(input, "test.bin", (offset, _) => offset == 0 ? input.OldReady.Task.IsCompleted : input.NewReady.Task.IsCompleted);
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, server.Address);
        request.Headers.Range = new RangeHeaderValue(0, 63);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        await input.OldStarted.Task.WaitAsync(timeout.Token);
        using var newer = new TcpClient();
        await newer.ConnectAsync("127.0.0.1", server.Address.Port, timeout.Token);
        await newer.GetStream().WriteAsync(Encoding.ASCII.GetBytes($"GET {server.Address.PathAndQuery} HTTP/1.1\r\nHost: localhost\r\nRange: bytes=100000-100063\r\n\r\n"), timeout.Token);
        await input.NewStarted.Task.WaitAsync(timeout.Token);
        newer.Client.Shutdown(SocketShutdown.Both); newer.Close();
        await input.OldResumed.Task.WaitAsync(timeout.Token);
        input.OldReady.TrySetResult();
        Assert.Equal(Enumerable.Range(0, 64).Select(i => (byte)i), await response.Content.ReadAsByteArrayAsync(timeout.Token));
    }

    [Theory]
    [InlineData(0, 65536, 32768, 0, 32768, 0, 0)]
    [InlineData(100, 65536, 32768, 32668, 1, 1, 1)]
    [InlineData(100, 65536, 32768, 32667, 2, 0, 1)]
    [InlineData(0, 65536, 32768, 65535, 1, 1, 1)]
    public void ExactPieceBoundariesNeverWaitForAnExtraPiece(long fileOffset, long length, int pieceLength, long offset, int count, int first, int last)
    {
        var requested = new List<int>();
        Assert.True(OnlinePieceRange.IsAvailable(fileOffset, length, pieceLength, offset, count, piece => { requested.Add(piece); return true; }));
        Assert.Equal(Enumerable.Range(first, last - first + 1), requested);
        Assert.False(OnlinePieceRange.IsAvailable(fileOffset, length, pieceLength, offset, count, _ => false));
    }

    [Fact]
    public void InvalidOrOverflowingRangesDoNotQueryTheTorrentBitfield()
    {
        bool Fail(int _) => throw new InvalidOperationException();
        Assert.False(OnlinePieceRange.IsAvailable(0, 10, 4, -1, 1, Fail));
        Assert.False(OnlinePieceRange.IsAvailable(0, 10, 4, 9, 2, Fail));
        Assert.False(OnlinePieceRange.IsAvailable(long.MaxValue, 10, 4, 1, 1, Fail));
        Assert.True(OnlinePieceRange.IsAvailable(0, 10, 4, 10, 0, Fail));
    }
}

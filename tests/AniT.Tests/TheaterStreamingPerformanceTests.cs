using System.Buffers.Binary;
using System.Net.Http.Headers;
using AniT.Theater;

namespace AniT.Tests;

public sealed class TheaterStreamingPerformanceTests
{
    [Fact]
    public async Task BinaryMediaRoundTripsWithInterleavedControlAndNoBase64Expansion()
    {
        var data = new byte[PacketChannel.StreamChunkBytes]; new Random(8).NextBytes(data);
        using var stream = new MemoryStream(); using var channel = new PacketChannel(stream);
        await channel.SendAsync(new("stream-chunk", Request: 8, Offset: 123, Data: data), default);
        Assert.Equal(data.Length + 21, stream.Length);
        await channel.SendAsync(new("state", State: new(1, 20, false, 30)), default);
        stream.Position = 0;
        var chunk = await channel.ReadAsync(default);
        Assert.Equal("stream-chunk", chunk.Type); Assert.Equal(8, chunk.Request); Assert.Equal(123, chunk.Offset); Assert.Equal(data, chunk.Data);
        Assert.False((await channel.ReadAsync(default)).State!.Playing);
        await Assert.ThrowsAsync<InvalidDataException>(() => channel.SendAsync(new("stream-chunk", Request: 1, Data: new byte[PacketChannel.StreamChunkBytes + 1]), default));
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-17)]
    [InlineData(-32786)]
    public async Task BinaryFramesRejectInvalidSizesBeforeAllocatingPayload(int length)
    {
        var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, length);
        using var channel = new PacketChannel(new MemoryStream(header));
        await Assert.ThrowsAsync<InvalidDataException>(() => channel.ReadAsync(default));
    }

    [Theory]
    [InlineData(2, 1, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(1, 1, -1)]
    public async Task BinaryFramesRejectInvalidTypeRequestAndOffset(byte type, long request, long offset)
    {
        var frame = new byte[22]; BinaryPrimitives.WriteInt32BigEndian(frame, -18); frame[4] = type;
        BinaryPrimitives.WriteInt64BigEndian(frame.AsSpan(5), request); BinaryPrimitives.WriteInt64BigEndian(frame.AsSpan(13), offset);
        using var channel = new PacketChannel(new MemoryStream(frame));
        await Assert.ThrowsAsync<InvalidDataException>(() => channel.ReadAsync(default));
    }

    [Fact]
    public async Task SlowReadDoesNotBlockDistantSeekAndConcurrentReadersShareOneFetch()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int firstRequests = 0;
        await using var proxy = new TheaterStreamProxy(8 * 1024 * 1024, "test.mkv", async (offset, count, token) =>
        {
            if (offset == 0) { Interlocked.Increment(ref firstRequests); started.TrySetResult(); await release.Task.WaitAsync(token); }
            return Enumerable.Repeat((byte)(offset == 0 ? 4 : 9), count).ToArray();
        });
        using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        async Task<byte[]> Read(long first)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, proxy.Address); request.Headers.Range = new RangeHeaderValue(first, first + 99);
            using var response = await http.SendAsync(request); response.EnsureSuccessStatusCode(); return await response.Content.ReadAsByteArrayAsync();
        }
        var one = Read(0); await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var two = Read(0);
        try
        {
            Assert.Equal(Enumerable.Repeat((byte)9, 100), await Read(4 * 1024 * 1024));
            Assert.False(one.IsCompleted); Assert.Equal(1, firstRequests);
        }
        finally { release.TrySetResult(); }
        Assert.Equal(Enumerable.Repeat((byte)4, 100), await one); Assert.Equal(await one, await two);
        Assert.Equal(1, firstRequests);
    }

    [Theory]
    [InlineData(9.5, false, 1.04)]
    [InlineData(10.5, false, .96)]
    [InlineData(8, true, 1)]
    public void StreamingUsesRateCorrectionBeforeDiscardingBufferedVideo(double guestPosition, bool seek, double speed)
    {
        var correction = TimelineSync.Correct(new(1, 10, true, 1000), guestPosition, 1000, streaming: true);
        Assert.Equal(seek, correction.Seek); Assert.Equal(speed, correction.Speed, 5);
    }
}

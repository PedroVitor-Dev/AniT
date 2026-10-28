using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using AniT.Theater;
using AniT.Testing;

namespace AniT.Tests;

public sealed class ProgressiveStreamingTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public async Task HttpDeliversValidatedPrefixBeforeTheBlockCompletes()
    {
        var bytes = new byte[TheaterStreamProxy.StartupBlockSize]; new Random(19).NextBytes(bytes);
        var block = new StreamReadBlock(bytes.Length); block.Append(bytes.AsSpan(0, 8192));
        await using var proxy = new TheaterStreamProxy(bytes.Length, "video.mkv", (_, _, _) => Task.FromResult(block));
        using var http = Client(); using var request = Range(proxy.Address, 0, bytes.Length - 1);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        await using var body = await response.Content.ReadAsStreamAsync();
        var prefix = new byte[8192]; await body.ReadExactlyAsync(prefix).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(bytes[..8192], prefix); Assert.False(block.Completion.Task.IsCompleted);
        var rest = new byte[bytes.Length - prefix.Length]; var reading = body.ReadExactlyAsync(rest).AsTask();
        Assert.False(reading.IsCompleted);
        block.Append(bytes.AsSpan(prefix.Length)); block.Completion.SetResult(block.Bytes);
        await reading.WaitAsync(TimeSpan.FromSeconds(3)); Assert.Equal(bytes[8192..], rest);
    }

    [Fact]
    public async Task InterruptedPartialBlockUnblocksReadersAndDoesNotBecomeACompleteCacheEntry()
    {
        var block = new StreamReadBlock(TheaterStreamProxy.StartupBlockSize); block.Append(new byte[8192]);
        var calls = 0;
        await using var proxy = new TheaterStreamProxy(block.Bytes.Length, "video.mkv", (_, count, _) => Task.FromResult(Interlocked.Increment(ref calls) == 1 ? block : StreamReadBlock.Completed(new byte[count])));
        using var http = Client(); using var request = Range(proxy.Address, 0, block.Bytes.Length - 1);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        await using var body = await response.Content.ReadAsStreamAsync();
        var prefix = new byte[8192]; await body.ReadExactlyAsync(prefix);
        var remaining = body.ReadExactlyAsync(new byte[8192]).AsTask();
        block.Completion.TrySetException(new IOException("Synthetic interrupted media"));
        await Assert.ThrowsAnyAsync<IOException>(() => remaining.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.False(proxy.GetReserve(0, block.Bytes.Length).Complete);
        using var retry = await http.SendAsync(Range(proxy.Address, 0, 99)); Assert.Equal(100, (await retry.Content.ReadAsByteArrayAsync()).Length);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task SequentialShortRangesWarmBeyondTheHttpBoundaryWithinTheBudget()
    {
        var calls = new ConcurrentDictionary<long, int>();
        await using var proxy = new TheaterStreamProxy(16 * 1048576, "video.mkv", (offset, count, _) =>
        {
            calls.AddOrUpdate(offset, 1, (_, value) => value + 1); return Task.FromResult(new byte[count]);
        });
        using var http = Client();
        async Task Read(long first, long last) { using var response = await http.SendAsync(Range(proxy.Address, first, last)); Assert.Equal(last - first + 1, (await response.Content.ReadAsByteArrayAsync()).LongLength); }
        await Read(0, 65535); Assert.Single(calls); // An isolated header probe must not warm the episode.
        await Read(65536, 131071);
        await Until(() => proxy.GetReserve(TheaterStreamProxy.StartupBlockSize, 2 * 1048576).Complete);
        Assert.Equal(3, calls.Count); Assert.All(calls.Values, count => Assert.Equal(1, count));
        Assert.False(calls.ContainsKey(TheaterStreamProxy.StartupBlockSize + 2 * 1048576));
        await Read(TheaterStreamProxy.StartupBlockSize, TheaterStreamProxy.StartupBlockSize + 99); Assert.Equal(3, calls.Count);
    }

    [Fact]
    public async Task RealTlsDeliversFirstChunkWithoutWaitingForOneMiBOnAThrottledLink()
    {
        var path = Path.Combine(Path.GetTempPath(), "anit-progressive-" + Guid.NewGuid().ToString("N") + ".mkv");
        var data = new byte[4 * 1048576]; new Random(29).NextBytes(data); await File.WriteAllBytesAsync(path, data);
        try
        {
            await using var host = TheaterRoom.Host(await MediaIdentity.FromFileAsync(path), IPAddress.Loopback, sharedFilePath: path, mode: TheaterMode.DirectStream);
            await using var relay = new TheaterTestRelay(host.Invite!.Port); relay.DelayMedia(10);
            await using var guest = await TheaterRoom.JoinAsync((host.Invite with { Port = relay.Port }).Encode());
            var endpoint = await guest.StartStreamingAsync();
            using var http = Client(); using var request = Range(endpoint, 262144, 262144 + 1048576 - 1);
            var clock = Stopwatch.StartNew(); using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            await using var body = await response.Content.ReadAsStreamAsync();
            var bytes = new byte[1048576]; await body.ReadExactlyAsync(bytes.AsMemory(0, 32768));
            var firstMs = clock.Elapsed.TotalMilliseconds; var receivedAtFirst = guest.StreamBytesReceived;
            Assert.True(receivedAtFirst < 262144 + 1048576, $"Full block arrived before first HTTP bytes: {receivedAtFirst}");
            Assert.Equal(data[262144..(262144 + 32768)], bytes[..32768]);
            await body.ReadExactlyAsync(bytes.AsMemory(32768)); Assert.Equal(data[262144..(262144 + 1048576)], bytes);
            output.WriteLine($"Throttled TLS: first 32 KiB in {firstMs:0} ms; full 1 MiB in {clock.Elapsed.TotalMilliseconds:0} ms; guest received {receivedAtFirst - 262144} bytes at first delivery. Local relay diagnostic.");
            Assert.True(guest.IsConnected && host.IsConnected);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task FailureBeforeAnyBytesWakesTheProgressiveWaiter()
    {
        var block = new StreamReadBlock(1024); var waiting = block.AvailableAsync(0, default);
        block.Completion.SetException(new IOException("Failed before bytes"));
        await Assert.ThrowsAsync<IOException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void ShortProgressiveReadsCannotHideARealPlaybackStall()
    {
        var monitor = new StreamBufferMonitor();
        for (var now = 0; now <= 1300; now += 100)
            Assert.False(monitor.Observe(now, 10, true, new(0, 1048576, Math.Max(0, now - 150)), _ => true).Waiting);
        Assert.True(monitor.Observe(1500, 10, true, new(0, 1048576, 1350), _ => true).Waiting);
        var flowing = new StreamBufferMonitor();
        for (var now = 0; now <= 5000; now += 100)
            Assert.False(flowing.Observe(now, 10 + now / 1000d, true, new(0, 1048576, Math.Max(0, now - 150)), _ => true).Waiting);
    }

    [Fact]
    public async Task PrefetchRefillsThePipelineBeforeTheSecondRequestFinishes()
    {
        var blocks = new ConcurrentDictionary<long, StreamReadBlock>();
        await using var proxy = new TheaterStreamProxy(16 * 1048576, "video.mkv", (offset, count, _) => Task.FromResult(blocks.GetOrAdd(offset, _ => new StreamReadBlock(count))));
        var start = TheaterStreamProxy.StartupBlockSize;
        proxy.EnsureReserve(start, 4 * 1048576);
        await Until(() => blocks.ContainsKey(start) && blocks.ContainsKey(start + 1048576));
        var first = blocks[start]; first.Append(new byte[first.Bytes.Length]); first.Completion.SetResult(first.Bytes);
        await Until(() => blocks.ContainsKey(start + 2 * 1048576));
        Assert.False(blocks[start + 1048576].Completion.Task.IsCompleted);
        Assert.Equal(3, blocks.Count); // Two outstanding speculative requests, not an unbounded episode download.
        Assert.True(proxy.GetReserve(start, 1048576).Complete);
    }

    private static HttpClient Client() => new(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(12) };
    private static HttpRequestMessage Range(Uri endpoint, long first, long last) { var request = new HttpRequestMessage(HttpMethod.Get, endpoint); request.Headers.Range = new RangeHeaderValue(first, last); return request; }
    private static async Task Until(Func<bool> ready) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4)); while (!ready()) await Task.Delay(10, timeout.Token); }
}

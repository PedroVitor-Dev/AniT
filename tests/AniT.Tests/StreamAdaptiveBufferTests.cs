using System.Net;
using System.Net.Http.Headers;
using AniT.Testing;
using AniT.Theater;

namespace AniT.Tests;

public sealed class StreamAdaptiveBufferTests
{
    private static void Transfer(StreamAdaptiveBuffer model, long now, int bytes = 1048576, int milliseconds = 500)
    {
        model.RequestStarted(now); model.Received(bytes, now + milliseconds); model.RequestFinished(now + milliseconds);
    }

    [Fact]
    public void ActiveTransferIsMeasuredAndIdleOrCacheReadsDoNotLowerCapacity()
    {
        var model = new StreamAdaptiveBuffer();
        Transfer(model, 0); Transfer(model, 1000);
        var measured = model.Snapshot(2000, 20, 0);
        Assert.True(measured.Connection.Measured); Assert.Equal(2 * 1048576d, measured.Connection.BytesPerSecond);
        Assert.Equal(measured.Connection.BytesPerSecond, model.Snapshot(100000, 20, 0).Connection.BytesPerSecond);
        model.RequestStarted(100000);
        Assert.Equal(0, model.Snapshot(102000, 20, 0).Connection.BytesPerSecond);
        model.RequestFinished(102000);
        Assert.Equal(measured.Connection.BytesPerSecond, model.Snapshot(103000, 20, 0).Connection.BytesPerSecond);
    }

    [Fact]
    public void LowHeadroomLatencyAndRepeatedWaitsIncreaseBoundedReserveAndLaterDecay()
    {
        var model = new StreamAdaptiveBuffer(); model.ConfigureMedia(10 * 1048576, TimeSpan.FromSeconds(10));
        Transfer(model, 0); Transfer(model, 1000);
        var stable = model.Snapshot(2000, 10, 0);
        Assert.False(stable.Connection.Limited);
        for (var i = 0; i < 10; i++) Transfer(model, 3000 + i * 1000, 131072, 1000);
        var slow = model.Snapshot(13000, 180, 80);
        Assert.True(slow.Connection.Limited); Assert.True(slow.Policy.ReadAheadBytes > stable.Policy.ReadAheadBytes);
        for (var i = 0; i < 10; i++) model.NotifyWait(14000 + i * 100);
        Assert.Equal(StreamAdaptiveBuffer.MaxReserveBytes, model.Snapshot(15000, 180, 80).Policy.ReadAheadBytes);
        var grown = model.Snapshot(15000, 180, 80).Policy.ReadAheadBytes;
        for (var i = 0; i < 10; i++) Transfer(model, 16000 + i * 1000, 1048576, 50);
        var recovered = model.Snapshot(150000, 10, 0);
        Assert.False(recovered.Connection.Limited); Assert.True(recovered.Policy.ReadAheadBytes < grown);
        Assert.InRange(recovered.Policy.RecoveryStableMs, 500, 2500);
    }

    [Fact]
    public void MetricsAreFiniteAndColdStartUsesConservativeDefaults()
    {
        var model = new StreamAdaptiveBuffer();
        var cold = model.Snapshot(0, double.NaN, double.PositiveInfinity);
        Assert.False(cold.Connection.Measured); Assert.True(cold.Connection.IsValid);
        Assert.Equal(2 * 1048576, cold.Policy.ReadAheadBytes); Assert.Equal(750, cold.Policy.RecoveryStableMs);
        model.ConfigureMedia(1, TimeSpan.Zero);
        model.Received(1000, 1000); // Not part of a media request.
        Assert.Equal(0, model.Snapshot(2000, 0, 0).Connection.BytesPerSecond);
        var clock = new ClockEstimator(); clock.Observe(100, 120, 110); clock.Observe(200, 300, 250);
        Assert.Equal(20, clock.RoundTripMs); Assert.Equal(40, clock.JitterMs);
    }

    [Fact]
    public void AdaptiveRecoveryRequiresReserveAndFreezesStabilityWindowForTheCurrentHold()
    {
        var monitor = new StreamBufferMonitor(); var demand = new StreamReadDemand(100, 1000, 1000);
        var policy = new StreamBufferPolicy(4 * 1048576, 900, 1000, 3000, 1500);
        Assert.False(monitor.Observe(1000, 0, true, demand, _ => true, policy).Waiting);
        Assert.False(monitor.Observe(1800, 0, true, demand, _ => true, policy).Waiting);
        Assert.True(monitor.Observe(2000, 0, true, demand, _ => true, policy).Waiting);
        Assert.True(monitor.Observe(2500, 0, false, null, _ => false, policy, _ => false).Waiting);
        Assert.True(monitor.Observe(3000, 0, false, null, _ => false, policy, _ => true).Waiting);
        Assert.True(monitor.Observe(4000, 0, false, null, _ => false, policy with { RecoveryStableMs = 500 }, _ => true).Waiting);
        Assert.False(monitor.Observe(4500, 0, false, null, _ => false, policy, _ => true).Waiting);
    }

    [Fact]
    public async Task ReserveWarmsWhilePausedWithinRequestBudgetAndStopsAtTheBoundedGoalOrEof()
    {
        var requests = new System.Collections.Concurrent.ConcurrentDictionary<long, int>();
        int active = 0, maximum = 0;
        await using var proxy = new TheaterStreamProxy(10 * 1048576, "video.mkv", async (offset, count, token) =>
        {
            var countActive = Interlocked.Increment(ref active);
            int previous; do { previous = maximum; } while (previous < countActive && Interlocked.CompareExchange(ref maximum, countActive, previous) != previous);
            try { requests.AddOrUpdate(offset, 1, (_, value) => value + 1); await Task.Delay(20, token); return new byte[count]; }
            finally { Interlocked.Decrement(ref active); }
        });
        var start = TheaterStreamProxy.StartupBlockSize;
        proxy.EnsureReserve(start, 1048576);
        await Until(() => proxy.GetReserve(start, 1048576).Complete);
        proxy.EnsureReserve(start, 4 * 1048576);
        await Until(() => proxy.GetReserve(start, 4 * 1048576).Complete);
        Assert.Equal(4, requests.Count); Assert.All(requests.Values, value => Assert.Equal(1, value)); Assert.InRange(maximum, 1, 2);
        proxy.ReleaseReserve();
        var tail = start + 9L * 1048576;
        proxy.EnsureReserve(tail, StreamAdaptiveBuffer.MaxReserveBytes);
        await Until(() => proxy.GetReserve(tail, StreamAdaptiveBuffer.MaxReserveBytes).Complete);
        Assert.Equal(10 * 1048576 - tail, proxy.GetReserve(tail, StreamAdaptiveBuffer.MaxReserveBytes).RequiredBytes);
    }

    [Fact]
    public async Task DisposingReserveCancelsBackgroundReads()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proxy = new TheaterStreamProxy(16 * 1048576, "video.mkv", async (_, count, token) =>
        {
            started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return new byte[count];
        });
        proxy.EnsureReserve(262144, StreamAdaptiveBuffer.MaxReserveBytes);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await proxy.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Theory]
    [InlineData(-1, 10, 0, 1048576)]
    [InlineData(double.NaN, 10, 0, 1048576)]
    [InlineData(100, double.PositiveInfinity, 0, 1048576)]
    [InlineData(100, 10, -1, 1048576)]
    [InlineData(100, 10, 0, 8388609)]
    public void InvalidConnectionMeasurementsAreRejected(double rate, double rtt, double jitter, int target)
        => Assert.False(new StreamConnectionInfo(rate, rtt, jitter, target, true, false).IsValid);

    [Fact]
    public async Task RealThrottledTransportChangesMeasuredReserveAndReportsItToHostWithoutCacheInflation()
    {
        var path = Path.Combine(Path.GetTempPath(), "anit-adaptive-" + Guid.NewGuid().ToString("N") + ".mkv");
        var bytes = new byte[8 * 1048576]; new Random(38).NextBytes(bytes); await File.WriteAllBytesAsync(path, bytes);
        try
        {
            await using var host = TheaterRoom.Host(await MediaIdentity.FromFileAsync(path), IPAddress.Loopback, sharedFilePath: path, mode: TheaterMode.DirectStream);
            await using var relay = new TheaterTestRelay(host.Invite!.Port); relay.DelayMedia(25);
            await using var guest = await TheaterRoom.JoinAsync((host.Invite with { Port = relay.Port }).Encode());
            await Until(() => guest.Clock.IsReady);
            var endpoint = await guest.StartStreamingAsync(); guest.ConfigureStreamingMedia(TimeSpan.FromSeconds(8));
            await host.SetReadyAsync(true); await guest.SetReadyAsync(true); await Until(() => host.BothReady);
            var initial = guest.StreamConnection!.TargetBufferBytes;
            using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(12) };
            async Task<byte[]> Read(long first, long last)
            {
                using var message = new HttpRequestMessage(HttpMethod.Get, endpoint); message.Headers.Range = new RangeHeaderValue(first, last);
                using var response = await http.SendAsync(message); response.EnsureSuccessStatusCode(); return await response.Content.ReadAsByteArrayAsync();
            }
            var reading = Read(262144, 262144 + 1048576 - 1);
            while (!reading.IsCompleted) { await guest.ObserveStreamingPlaybackAsync(0, false); await Task.Delay(100); }
            Assert.Equal(bytes[262144..(262144 + 1048576)], await reading);
            await Until(async () => { await guest.ObserveStreamingPlaybackAsync(0, false); return host.StreamConnection is { Measured: true, Limited: true }; });
            var slow = host.StreamConnection!; Assert.True(slow.TargetBufferBytes > initial); Assert.InRange(slow.BytesPerSecond, 1, 5 * 1048576);
            relay.DelayMedia(0);
            await Read(262144 + 1048576, 262144 + 2 * 1048576 - 1);
            await Until(async () => { await guest.ObserveStreamingPlaybackAsync(0, false); return host.StreamConnection is { Measured: true, Limited: false }; });
            Assert.True(host.StreamConnection!.TargetBufferBytes <= slow.TargetBufferBytes);
            var before = guest.StreamConnection!.BytesPerSecond; var transferred = guest.StreamBytesReceived;
            await Read(0, 99);
            Assert.Equal(transferred, guest.StreamBytesReceived); Assert.Equal(before, guest.StreamConnection!.BytesPerSecond);
            Assert.False(guest.HasMedia);
            var closing = host.DisposeAsync().AsTask(); var closingAgain = host.DisposeAsync().AsTask();
            Assert.Same(closing, closingAgain);
            await Task.WhenAll(closing, closingAgain);
            using var writable = File.OpenWrite(path); Assert.True(writable.CanWrite);
        }
        finally { File.Delete(path); }
    }

    private static async Task Until(Func<bool> predicate) => await Until(() => Task.FromResult(predicate()));
    private static async Task Until(Func<Task<bool>> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        while (!await predicate()) await Task.Delay(25, timeout.Token);
    }
}

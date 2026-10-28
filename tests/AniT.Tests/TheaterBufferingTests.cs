using System.Net;
using System.Net.Http.Headers;
using AniT.Testing;
using AniT.Theater;

namespace AniT.Tests;

public sealed class TheaterBufferingTests
{
    [Fact]
    public void MonitorRequiresDemandAndIgnoresManualPauseAndShortWaits()
    {
        var monitor = new StreamBufferMonitor();
        Assert.False(monitor.Observe(1000, 10, true, null, _ => false).Waiting);
        Assert.False(monitor.Observe(9000, 10, true, null, _ => false).Waiting);
        var demand = new StreamReadDemand(1024, 1024, 9000);
        Assert.False(monitor.Observe(9500, 10, true, demand, _ => true).Waiting);
        Assert.False(monitor.Observe(9600, 10, false, demand, _ => true).Waiting);
        Assert.False(monitor.Observe(15000, 10, false, demand, _ => true).Waiting);
    }

    [Fact]
    public void MonitorHoldsStallAndRequiresStableRecoveryEvenAfterHostPauses()
    {
        var monitor = new StreamBufferMonitor(); var demand = new StreamReadDemand(100, 1000, 1000);
        Assert.False(monitor.Observe(1000, 10, true, demand, _ => true).Waiting);
        var held = monitor.Observe(1800, 10, true, demand, _ => true);
        Assert.True(held.Waiting); Assert.Equal(10, held.PositionSeconds);
        Assert.True(monitor.Observe(3000, 10, false, demand, _ => true).Waiting);
        Assert.True(monitor.Observe(3100, 10, false, null, _ => false).Waiting);
        Assert.True(monitor.Observe(3600, 10, false, demand with { SinceMs = 3000 }, _ => false).Waiting);
        Assert.True(monitor.Observe(3700, 10, false, null, _ => false).Waiting);
        Assert.False(monitor.Observe(4450, 10, false, null, _ => false).Waiting);
        monitor.Reset(); Assert.False(monitor.Observe(9000, 0, true, null, _ => false).Waiting);
    }

    [Fact]
    public void MovingPlaybackSurvivesBriefReadsButProlongedDemandHoldsBeforeDecoderDrains()
    {
        var monitor = new StreamBufferMonitor(); var demand = new StreamReadDemand(10, 1000, 1000);
        for (var i = 0; i < 20; i++) Assert.False(monitor.Observe(1000 + i * 100, i / 10d, true, demand, _ => true).Waiting);
        Assert.True(monitor.Observe(3000, 2, true, demand, _ => true).Waiting);
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(1, -1, 1)]
    [InlineData(1, 2, 1)]
    [InlineData(1, 0, 8388609)]
    public void InvalidBufferReportsAreRejected(long revision, int received, int required)
        => Assert.False(new StreamBufferStatus(revision, true, 10, received, required).IsValid);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BufferHoldUsesIndependentControlAndRequiresExplicitHostResume(bool publishPausedAcknowledgment)
    {
        var path = Path.Combine(Path.GetTempPath(), "anit-buffer-test-" + Guid.NewGuid().ToString("N") + ".mkv");
        var bytes = new byte[8 * 1024 * 1024]; new Random(31).NextBytes(bytes); await File.WriteAllBytesAsync(path, bytes);
        try
        {
            await using var host = TheaterRoom.Host(await MediaIdentity.FromFileAsync(path), IPAddress.Loopback, sharedFilePath: path, mode: TheaterMode.DirectStream);
            // A fresh guest must not inherit previous report revisions or a previous hold.
            for (var session = 0; session < 2; session++)
            {
                await using var relay = new TheaterTestRelay(host.Invite!.Port);
                await using var guest = await TheaterRoom.JoinAsync((host.Invite with { Port = relay.Port }).Encode());
                await Until(() => guest.Clock.IsReady);
                var url = await guest.StartStreamingAsync();
                await host.SetReadyAsync(true); await guest.SetReadyAsync(true); await Until(() => host.BothReady);
                await host.PublishAsync(10, true); await Until(() => guest.LatestState?.Playing == true);
                relay.PauseMedia();
                using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false });
                using var request = new HttpRequestMessage(HttpMethod.Get, url); request.Headers.Range = new RangeHeaderValue(4 * 1024 * 1024, 4 * 1024 * 1024 + 99);
                var read = http.SendAsync(request); await relay.MediaBlocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
                try
                {
                    await SampleUntil(guest, true, () => guest.StreamHold && host.StreamHold);
                    Assert.True(host.GuestBuffering); Assert.False(read.IsCompleted);
                    await Assert.ThrowsAsync<InvalidOperationException>(() => guest.ResumeStreamingAsync(10, ClockEstimator.NowMs + 350));
                    await Assert.ThrowsAsync<InvalidOperationException>(() => host.PublishAsync(10, true));
                    await Assert.ThrowsAsync<InvalidOperationException>(() => host.ResumeStreamingAsync(10, ClockEstimator.NowMs + 350));
                    if (publishPausedAcknowledgment)
                    {
                        await host.PublishAsync(10, false); await Until(() => guest.LatestState?.BufferHold == true);
                        Assert.False(guest.LatestState!.Playing);
                    }
                }
                finally { relay.ResumeMedia(); }
                using var response = await read.WaitAsync(TimeSpan.FromSeconds(5)); response.EnsureSuccessStatusCode();
                Assert.Equal(bytes[(4 * 1024 * 1024)..(4 * 1024 * 1024 + 100)], await response.Content.ReadAsByteArrayAsync());
                await SampleUntil(guest, false, () => !guest.GuestBuffering && !host.GuestBuffering);
                Assert.True(host.StreamHold && guest.StreamHold);
                var required = guest.BufferStatus!.RequiredBytes;
                guest.ConfigureStreamingMedia(TimeSpan.FromSeconds(1));
                Assert.Equal(required, guest.StreamConnection!.TargetBufferBytes);
                await Assert.ThrowsAsync<InvalidOperationException>(() => host.PublishAsync(10, true));
                var revision = guest.BufferStatus!.Revision;
                await host.ResumeStreamingAsync(10, ClockEstimator.NowMs + 350);
                await Until(() => !guest.StreamHold && guest.LatestState?.Playing == true);
                Assert.True(guest.LatestState!.BufferResumeRevision >= revision);
                await guest.SetReadyAsync(false); await Until(() => !host.BothReady);
                Assert.False(host.StreamHold); Assert.Null(host.BufferStatus);
                await guest.DisposeAsync(); await Until(() => !host.IsConnected);
            }
        }
        finally { File.Delete(path); }
    }

    private static async Task SampleUntil(TheaterRoom guest, bool playing, Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!predicate()) { await guest.ObserveStreamingPlaybackAsync(10, playing, timeout.Token); await Task.Delay(100, timeout.Token); }
    }
    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }
}

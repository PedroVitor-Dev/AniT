using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using AniT.Testing;
using AniT.Theater;

namespace AniT.Tests;

public sealed class TheaterNetworkProfileTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(20, 5, 3)]
    [InlineData(50, 10, 5)]
    public async Task CommandsStayOrderedDuringVideoOutageThenBytesAndSeeksRecover(int controlDelay, int mediaDelay, int jitter)
    {
        var path = Path.Combine(Path.GetTempPath(), "anit-network-qa-" + Guid.NewGuid().ToString("N") + ".mkv");
        var data = new byte[8 * 1048576]; new Random(47).NextBytes(data); await File.WriteAllBytesAsync(path, data);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var token = deadline.Token;
            await using var host = TheaterRoom.Host(await MediaIdentity.FromFileAsync(path, token), IPAddress.Loopback, sharedFilePath: path, mode: TheaterMode.DirectStream);
            await using var relay = new TheaterTestRelay(host.Invite!.Port); relay.ConfigureDelays(controlDelay, mediaDelay, jitter);
            await using var guest = await TheaterRoom.JoinAsync((host.Invite with { Port = relay.Port }).Encode(), cancellationToken: token);
            await Until(() => guest.Clock.IsReady, token);
            var endpoint = await guest.StartStreamingAsync(token);
            await host.SetReadyAsync(true, token); await guest.SetReadyAsync(true, token); await Until(() => host.BothReady, token);
            using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false });
            async Task<byte[]> Read(long start, long last)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint); request.Headers.Range = new RangeHeaderValue(start, last);
                using var response = await http.SendAsync(request, token); response.EnsureSuccessStatusCode();
                Assert.Equal(start, response.Content.Headers.ContentRange!.From); return await response.Content.ReadAsByteArrayAsync(token);
            }
            relay.PauseMedia();
            var body = Read(262144, 262144 + 1048576 - 1);
            await relay.MediaBlocked.Task.WaitAsync(token);
            var latencies = new List<double>();
            try
            {
                for (var index = 0; index < 8; index++)
                {
                    var position = 10 + index; var playing = index % 2 == 0;
                    var clock = Stopwatch.StartNew();
                    await host.PublishAsync(position, playing, token);
                    await Until(() => guest.LatestState?.PositionSeconds == position, token);
                    latencies.Add(clock.Elapsed.TotalMilliseconds);
                    Assert.Equal(playing, guest.LatestState!.Playing);
                    Assert.False(body.IsCompleted); Assert.True(host.IsConnected && guest.IsConnected);
                }
            }
            finally { relay.ResumeMedia(); }
            Assert.Equal(data[262144..(262144 + 1048576)], await body);
            Assert.Equal(data[(6 * 1048576)..(6 * 1048576 + 100)], await Read(6 * 1048576, 6 * 1048576 + 99));
            Assert.False(guest.HasMedia);
            latencies.Sort();
            output.WriteLine($"Relay read delays control/media/jitter={controlDelay}/{mediaDelay}/{jitter} ms; commands p50={latencies[latencies.Count / 2]:0} ms, max={latencies[^1]:0} ms; bytes verified after outage and distant seek. Not physical packet loss or a two-PC measurement.");
            relay.DisconnectMedia(); await Until(() => !host.IsConnected && !guest.IsConnected, token);
            Assert.Null(guest.LatestState); Assert.False(host.BothReady);
            await using var next = await TheaterRoom.JoinAsync(host.Invite.Encode(), cancellationToken: token);
            await next.StartStreamingAsync(token);
            Assert.True(next.IsConnected && host.IsConnected);
        }
        finally { File.Delete(path); }
    }

    private static async Task Until(Func<bool> condition, CancellationToken token)
    {
        while (!condition()) await Task.Delay(5, token);
    }
}

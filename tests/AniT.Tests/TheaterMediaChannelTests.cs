using AniT.Testing;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Authentication;
using AniT.Theater;

namespace AniT.Tests;

public sealed class TheaterMediaChannelTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public async Task BlockedVideoTransportDoesNotBlockPausePositionOrHeartbeatAndUsesSamePort()
    {
        using var source = new Source();
        await using var host = TheaterRoom.Host(await MediaIdentity.FromFileAsync(source.Path), IPAddress.Loopback, sharedFilePath: source.Path, mode: TheaterMode.DirectStream);
        await using var relay = new TheaterTestRelay(host.Invite!.Port);
        await using var guest = await TheaterRoom.JoinAsync((host.Invite with { Port = relay.Port }).Encode());
        await Until(() => guest.Clock.IsReady);
        var urls = await Task.WhenAll(guest.StartStreamingAsync(), guest.StartStreamingAsync());
        Assert.Equal(urls[0], urls[1]);
        var url = urls[0];
        Assert.Equal(2, relay.Accepted);
        await host.SetReadyAsync(true); await guest.SetReadyAsync(true); await Until(() => host.BothReady);
        relay.PauseMedia();
        using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, url); request.Headers.Range = new RangeHeaderValue(4 * 1024 * 1024, 4 * 1024 * 1024 + 99);
        var read = http.SendAsync(request);
        await relay.MediaBlocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var watch = Stopwatch.StartNew();
            await host.PublishAsync(30, true); await Until(() => guest.LatestState is { PositionSeconds: 30, Playing: true });
            await host.PublishAsync(31, false); await Until(() => guest.LatestState is { PositionSeconds: 31, Playing: false });
            await host.PublishAsync(120, false); await Until(() => guest.LatestState?.PositionSeconds == 120);
            Assert.False(read.IsCompleted);
            output.WriteLine($"Three timeline commands delivered in {watch.Elapsed.TotalMilliseconds:0} ms while encrypted video bytes were deliberately blocked.");
            // Control heartbeat must keep the session alive even with a fully stalled media socket.
            await Task.Delay(TimeSpan.FromSeconds(11));
            Assert.True(host.IsConnected && guest.IsConnected);
            await host.PublishAsync(121, false); await Until(() => guest.LatestState?.PositionSeconds == 121);
            Assert.False(read.IsCompleted);
        }
        finally { relay.ResumeMedia(); }
        using var response = await read.WaitAsync(TimeSpan.FromSeconds(5)); response.EnsureSuccessStatusCode();
        Assert.Equal(source.Bytes[(4 * 1024 * 1024)..(4 * 1024 * 1024 + 100)], await response.Content.ReadAsByteArrayAsync());
        relay.DisconnectMedia();
        await Until(() => !guest.IsConnected && !host.IsConnected);
        await using var replacement = await TheaterRoom.JoinAsync(host.Invite.Encode());
        await replacement.StartStreamingAsync();
        Assert.True(host.IsConnected && replacement.IsConnected);
    }

    [Fact]
    public async Task MediaRequiresCurrentSessionSecretAndRejectsDuplicateChannels()
    {
        using var source = new Source();
        await using var host = TheaterRoom.Host(await MediaIdentity.FromFileAsync(source.Path), IPAddress.Loopback, sharedFilePath: source.Path, mode: TheaterMode.DirectStream);
        using (var unsolicited = await Connect(host.Invite!))
        {
            await unsolicited.Channel.SendAsync(new("media-hello", Token: host.Invite!.Token), default);
            Assert.Equal("reject", (await unsolicited.Channel.ReadAsync(default)).Type);
        }
        string oldKey;
        using (var oldControl = await Connect(host.Invite!))
        {
            await oldControl.Channel.SendAsync(new("hello", Token: host.Invite!.Token), default);
            oldKey = (await oldControl.Channel.ReadAsync(default)).Token!;
            await Until(() => host.IsConnected);
        }
        await Until(() => !host.IsConnected);
        using var control = await Connect(host.Invite!);
        await control.Channel.SendAsync(new("hello", Token: host.Invite!.Token), default);
        var key = (await control.Channel.ReadAsync(default)).Token!;
        Assert.NotEqual(oldKey, key);
        foreach (var invalid in new[] { oldKey, host.Invite.Token, new string('A', 64) })
        {
            using var rejected = await Connect(host.Invite);
            await rejected.Channel.SendAsync(new("media-hello", Token: invalid), default);
            Assert.Equal("reject", (await rejected.Channel.ReadAsync(default)).Type);
            Assert.True(host.IsConnected);
        }
        using var video = await Connect(host.Invite);
        await video.Channel.SendAsync(new("media-hello", Token: key), default);
        Assert.Equal("media-welcome", (await video.Channel.ReadAsync(default)).Type);
        using (var duplicate = await Connect(host.Invite))
        {
            await duplicate.Channel.SendAsync(new("media-hello", Token: key), default);
            Assert.Equal("reject", (await duplicate.Channel.ReadAsync(default)).Type);
        }
        await using var secondGuest = await TheaterRoom.JoinAsync(host.Invite.Encode());
        await Until(() => host.Participants.Count == 3);
        Assert.True(host.IsConnected);
        // The media channel cannot impersonate playback control.
        await video.Channel.SendAsync(new("state", State: new(1, 12, true, ClockEstimator.NowMs)), default);
        await Until(() => host.Participants.Count == 2);
        Assert.True(secondGuest.IsConnected);
        await secondGuest.DisposeAsync(); await Until(() => !host.IsConnected);
        await using var valid = await TheaterRoom.JoinAsync(host.Invite.Encode());
        await valid.StartStreamingAsync();
    }

    [Fact]
    public async Task MediaConnectionRejectsDifferentCertificateEvenWithValidControlConnection()
    {
        using var source = new Source();
        var identity = await MediaIdentity.FromFileAsync(source.Path);
        await using var host = TheaterRoom.Host(identity, IPAddress.Loopback, sharedFilePath: source.Path, mode: TheaterMode.DirectStream);
        await using var other = TheaterRoom.Host(identity, IPAddress.Loopback, sharedFilePath: source.Path, mode: TheaterMode.DirectStream);
        await using var relay = new TheaterTestRelay(host.Invite!.Port, other.Invite!.Port);
        await using var guest = await TheaterRoom.JoinAsync((host.Invite with { Port = relay.Port }).Encode());
        await Assert.ThrowsAsync<AuthenticationException>(() => guest.StartStreamingAsync());
        await Until(() => !host.IsConnected && !guest.IsConnected);
        Assert.False(other.IsConnected); Assert.Equal(0, other.StreamBytesReceived); Assert.Equal(0, guest.StreamBytesReceived);
    }

    [Theory]
    [InlineData("stream-request")]
    [InlineData("stream-chunk")]
    [InlineData("stream-end")]
    public async Task VideoPacketsAreRejectedOnControlConnection(string type)
    {
        using var source = new Source();
        await using var host = TheaterRoom.Host(await MediaIdentity.FromFileAsync(source.Path), IPAddress.Loopback, sharedFilePath: source.Path, mode: TheaterMode.DirectStream);
        using var control = await Connect(host.Invite!);
        await control.Channel.SendAsync(new("hello", Token: host.Invite!.Token), default);
        Assert.Equal("welcome", (await control.Channel.ReadAsync(default)).Type);
        await Until(() => host.IsConnected);
        await control.Channel.SendAsync(new(type, Request: 1, Count: 1, Data: new byte[] { 1 }), default);
        await Until(() => !host.IsConnected);
    }

    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }

    private static async Task<PinnedConnection> Connect(TheaterInvite invite)
    {
        var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(invite.Address, invite.Port);
        var tls = new SslStream(client.GetStream(), false, (_, cert, _, _) => cert is not null && Convert.ToHexString(SHA256.HashData(cert.GetRawCertData())) == invite.CertificateHash);
        await tls.AuthenticateAsClientAsync("AniT Movie Theater");
        return new(client, new PacketChannel(tls));
    }

    private sealed class PinnedConnection(TcpClient client, PacketChannel channel) : IDisposable
    {
        public PacketChannel Channel => channel;
        public void Dispose() { channel.Dispose(); client.Dispose(); }
    }

    private sealed class Source : IDisposable
    {
        private readonly string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "anit-split-stream-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(folder, "episode.mkv");
        public byte[] Bytes { get; } = new byte[8 * 1024 * 1024 + 31];
        public Source() { Directory.CreateDirectory(folder); new Random(21).NextBytes(Bytes); File.WriteAllBytes(Path, Bytes); }
        public void Dispose() => Directory.Delete(folder, true);
    }

}

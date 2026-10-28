using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Authentication;
using System.Text;
using AniT.Theater;

namespace AniT.Tests;

public sealed class TheaterTests
{
    private static MediaIdentity Media(char hash = 'A') => new(1024, new string(hash, 64));

    [Fact]
    public async Task IdentityChecksContentsRatherThanFilename()
    {
        var folder = Path.Combine(Path.GetTempPath(), "anit-theater-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            var first = Path.Combine(folder, "one.mkv"); var second = Path.Combine(folder, "two.mp4");
            await File.WriteAllTextAsync(first, "same episode bytes"); await File.WriteAllTextAsync(second, "same episode bytes");
            var identity = await MediaIdentity.FromFileAsync(first);
            Assert.True(identity.IsValid);
            Assert.True(identity.Matches(await MediaIdentity.FromFileAsync(second)));
            await File.WriteAllTextAsync(second, "different bytes");
            Assert.False(identity.Matches(await MediaIdentity.FromFileAsync(second)));
            Assert.False(identity.Matches(null));
            await File.WriteAllTextAsync(second, "");
            await Assert.ThrowsAsync<InvalidDataException>(() => MediaIdentity.FromFileAsync(second));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void InvitationRoundTripsAndAuthenticatesSecret()
    {
        var invite = new TheaterInvite(IPAddress.Loopback, 45678, new string('A', 64), new string('B', 64));
        Assert.Equal(invite, TheaterInvite.Parse(invite.Encode()));
        Assert.True(invite.AcceptsToken(invite.Token));
        Assert.False(invite.AcceptsToken(new string('C', 64)));
        Assert.False(invite.AcceptsToken(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://example.com")]
    [InlineData("anit-theater://0.0.0.0:45678/A/B")]
    [InlineData("anit-theater://127.0.0.1:0/A/B")]
    [InlineData("anit-theater://example.com:45678/A/B")]
    [InlineData("anit-theater://user@127.0.0.1:45678/A/B")]
    [InlineData("anit-theater://127.0.0.1:45678/A/B?x=1")]
    [InlineData("anit-theater://127.0.0.1:45678/A/B#extra")]
    public void InvalidInvitationsRejected(string value) => Assert.Throws<FormatException>(() => TheaterInvite.Parse(value));

    [Fact]
    public void ClockPrefersLowLatencySamplesAndExpiresOldSamples()
    {
        var clock = new ClockEstimator();
        Assert.False(clock.IsReady);
        clock.Observe(1000, 1020, 1510);
        Assert.True(clock.IsReady); Assert.Equal(500, clock.OffsetMs); Assert.Equal(20, clock.RoundTripMs);
        clock.Observe(2000, 2400, 2800);
        Assert.Equal(500, clock.OffsetMs);
        clock.Observe(500, 400, 600); clock.Observe(0, 6000, 500); clock.Observe(-1, 1, 0);
        Assert.Equal(500, clock.OffsetMs);
        for (var i = 0; i < 8; i++) clock.Observe(3000 + i * 100, 3040 + i * 100, 3620 + i * 100);
        Assert.Equal(600, clock.OffsetMs);
    }

    [Theory]
    [InlineData(true, 10.05, 1100, 10.1, false, 1)]
    [InlineData(true, 9.9, 1100, 10.1, false, 1.02)]
    [InlineData(true, 10.3, 1100, 10.1, false, .98)]
    [InlineData(true, 8, 1100, 10.1, true, 1)]
    [InlineData(false, 9, 1100, 10, true, 1)]
    [InlineData(false, 10, 900, 10, false, 1)]
    public void DriftCorrectionIsBounded(bool playing, double current, long now, double target, bool seek, double speed)
    {
        var state = new TimelineState(1, 10, playing, 1000);
        var result = TimelineSync.Correct(state, current, now);
        Assert.Equal(target, result.TargetSeconds, 5); Assert.Equal(seek, result.Seek); Assert.Equal(speed, result.Speed);
        Assert.Throws<ArgumentException>(() => TimelineSync.Correct(state with { PositionSeconds = double.NaN }, 0, now));
    }

    [Fact]
    public async Task DirectRoomSynchronizesAndAllowsRejoinAfterDisconnect()
    {
        await using var host = TheaterRoom.Host(Media(), IPAddress.Loopback);
        await using (var guest = await JoinDiagnosed(host))
        {
            await Until(() => host.IsConnected && guest.Clock.IsReady);
            await Assert.ThrowsAsync<InvalidOperationException>(() => host.PublishAsync(0, true));
            await Assert.ThrowsAsync<InvalidOperationException>(() => guest.PublishAsync(0, true));
            await host.SetReadyAsync(true); await guest.SetReadyAsync(true);
            await Until(() => host.BothReady && guest.BothReady);
            await guest.ConfirmParticipationAsync(); await Until(() => host.CanApprovePlayback);
            await host.ApprovePlaybackAsync(42, audioTrack: 1, subtitleTrack: -1);
            await Until(() => guest.LatestState?.PositionSeconds == 42);
            Assert.True(guest.LatestState!.Playing);
            Assert.Equal(1, guest.LatestState.AudioTrack); Assert.Equal(-1, guest.LatestState.SubtitleTrack);
            await host.PublishAsync(12, false);
            await Until(() => guest.LatestState?.PositionSeconds == 12);
            Assert.False(guest.LatestState!.Playing);
            await Assert.ThrowsAsync<ArgumentException>(() => host.PublishAsync(double.PositiveInfinity, true));
            await guest.SetReadyAsync(false); await Until(() => !host.RemoteReady);
        }
        await Until(() => !host.IsConnected);
        Assert.False(host.LocalReady);
        await host.BeginPreparationAsync();
        await using var reconnect = await TheaterRoom.JoinAsync(host.Invite!.Encode(), Media());
        await Until(() => host.IsConnected && reconnect.Clock.IsReady);
        await host.SetReadyAsync(true); await reconnect.SetReadyAsync(true); await Until(() => host.BothReady);
        await reconnect.ConfirmParticipationAsync(); await Until(() => host.CanApprovePlayback); await host.ApprovePlaybackAsync(20);
        await host.PublishAsync(20, false); await Until(() => reconnect.LatestState?.PositionSeconds == 20);
    }

    [Fact]
    public async Task DifferentFileAndWrongTokenAreRejectedWithoutClosingHost()
    {
        await using var host = TheaterRoom.Host(Media(), IPAddress.Loopback);
        var wrong = await Assert.ThrowsAsync<InvalidDataException>(() => TheaterRoom.JoinAsync(host.Invite!.Encode(), Media('B')));
        Assert.Contains("arquivos", wrong.Message);
        await Assert.ThrowsAsync<InvalidDataException>(() => TheaterRoom.JoinAsync((host.Invite! with { Token = new string('0', 64) }).Encode(), Media()));
        await using var good = await TheaterRoom.JoinAsync(host.Invite!.Encode(), Media()); await Until(() => host.IsConnected);
    }

    [Fact]
    public async Task CertificatePinCannotBeReplaced()
    {
        await using var host = TheaterRoom.Host(Media(), IPAddress.Loopback);
        await Assert.ThrowsAnyAsync<AuthenticationException>(() => TheaterRoom.JoinAsync((host.Invite! with { CertificateHash = new string('0', 64) }).Encode(), Media()));
        await using var good = await TheaterRoom.JoinAsync(host.Invite!.Encode(), Media()); await Until(() => host.IsConnected);
    }

    [Fact]
    public async Task CancelAndDisposeDoNotLeaveListenerWaitingForHandshake()
    {
        var host = TheaterRoom.Host(Media(), IPAddress.Loopback);
        using var raw = new TcpClient(); await raw.ConnectAsync(IPAddress.Loopback, host.Invite!.Port);
        await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        await host.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TheaterRoom.JoinAsync(new TheaterInvite(IPAddress.Loopback, 45678, new string('A', 64), new string('B', 64)).Encode(), Media(), new CancellationToken(true)));
        Assert.Throws<ArgumentException>(() => TheaterRoom.Host(new(0, "bad"), IPAddress.Loopback));
    }

    [Fact]
    public async Task FramingRejectsOversizedInvalidAndIncompatiblePackets()
    {
        foreach (var length in new[] { -1, 0, 8193 })
        {
            var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, length);
            using var channel = new PacketChannel(new MemoryStream(header));
            await Assert.ThrowsAsync<InvalidDataException>(() => channel.ReadAsync(default));
        }
        using var stream = new MemoryStream(); using var packets = new PacketChannel(stream);
        await packets.SendAsync(new("ready", Ready: true), default); stream.Position = 0;
        Assert.True((await packets.ReadAsync(default)).Ready);
        await Assert.ThrowsAsync<InvalidDataException>(() => packets.SendAsync(new("hello", Token: new string('A', 9000)), default));
        stream.SetLength(0); await packets.SendAsync(new("ready", Version: 8), default); stream.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => packets.ReadAsync(default));
    }

    [Theory]
    [InlineData("state")]
    [InlineData("hello")]
    [InlineData("welcome")]
    [InlineData("download")]
    [InlineData("file-verified")]
    [InlineData("chunk")]
    [InlineData("prepare")]
    [InlineData("confirmed")]
    [InlineData("download-progress")]
    [InlineData("buffer-status")]
    [InlineData("stream-connection")]
    [InlineData("voice-policy")]
    [InlineData("voice-frame")]
    [InlineData("voice-status")]
    [InlineData("roster")]
    [InlineData("removed")]
    [InlineData("removed-ack")]
    public async Task GuestCannotSendHostCommands(string message)
    {
        await using var host = TheaterRoom.Host(Media(), IPAddress.Loopback);
        using var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, host.Invite!.Port);
        using var tls = new SslStream(client.GetStream(), false, (_, cert, _, _) => cert is not null && Convert.ToHexString(SHA256.HashData(cert.GetRawCertData())) == host.Invite.CertificateHash);
        await tls.AuthenticateAsClientAsync("AniT Movie Theater");
        using var channel = new PacketChannel(tls);
        await channel.SendAsync(new("hello", Token: host.Invite.Token, Media: Media()), default);
        Assert.Equal("welcome", (await channel.ReadAsync(default)).Type);
        await Until(() => host.IsConnected);
        await channel.SendAsync(new(message, State: new TimelineState(1, 50, true, ClockEstimator.NowMs), Data: new byte[] { 0 }, ParticipantId: Guid.NewGuid(), Offset: 1, Clock: ClockEstimator.NowMs), default);
        await Until(() => !host.IsConnected);
        Assert.Null(host.LatestState);
        await using var valid = await TheaterRoom.JoinAsync(host.Invite.Encode(), Media()); await Until(() => host.IsConnected);
    }

    private static async Task<TheaterRoom> JoinDiagnosed(TheaterRoom host)
    {
        try { await host.BeginPreparationAsync(); return await TheaterRoom.JoinAsync(host.Invite!.Encode(), Media()); }
        catch (Exception error) { throw new Exception("Host: " + host.LastError, error); }
    }

    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }
}

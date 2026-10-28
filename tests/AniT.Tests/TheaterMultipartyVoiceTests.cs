using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using AniT.Theater;

namespace AniT.Tests;

public sealed class TheaterMultipartyVoiceTests
{
    [Fact]
    public async Task FourGuestsRequireEveryoneReadyAndConfirmedAndKickIsIsolated()
    {
        var media = new MediaIdentity(1024, new string('A', 64));
        await using var host = TheaterRoom.Host(media, IPAddress.Loopback);
        var guests = new List<TheaterRoom>();
        try
        {
            await host.BeginPreparationAsync();
            for (var i = 0; i < 4; i++) guests.Add(await TheaterRoom.JoinAsync(host.Invite!.Encode(), media, displayName: "Pessoa " + i));
            await Until(() => host.Participants.Count == 5 && guests.All(g => g.Participants.Count == 5 && g.Clock.IsReady));
            await Assert.ThrowsAsync<InvalidDataException>(() => TheaterRoom.JoinAsync(host.Invite!.Encode(), media));
            await host.SetReadyAsync(true);
            foreach (var guest in guests.Take(3)) { await guest.SetReadyAsync(true); await guest.ConfirmParticipationAsync(); }
            Assert.False(host.BothReady); Assert.False(host.CanApprovePlayback);
            await Assert.ThrowsAsync<InvalidOperationException>(() => host.ApprovePlaybackAsync(0));
            await guests[3].SetReadyAsync(true); await Until(() => host.BothReady);
            Assert.False(host.CanApprovePlayback);
            await guests[3].ConfirmParticipationAsync(); await Until(() => host.CanApprovePlayback);
            var start = await host.ApprovePlaybackAsync(12);
            await Until(() => guests.All(g => g.LatestState?.HostTimeMs == start));
            await host.PublishAsync(15, false); await Until(() => guests.All(g => g.LatestState is { Playing: false, PositionSeconds: 15 }));
            var kickedId = guests[1].Participants.Single(p => p.DisplayName == "Pessoa 1").Id;
            await host.RemoveParticipantAsync(kickedId);
            await Until(() => !guests[1].IsConnected && guests.Where((_, i) => i != 1).All(g => g.Participants.Count == 4));
            Assert.True(host.LocalReady); Assert.True(host.PreparationStarted); Assert.True(host.BothReady);
            Assert.Equal(TheaterExitReason.RemovedByHost, guests[1].ExitReason);
            await host.ApprovePlaybackAsync(16);
            await Until(() => guests.Where((_, i) => i != 1).All(g => g.LatestState?.PositionSeconds == 16));
            await using var replacement = await TheaterRoom.JoinAsync(host.Invite!.Encode(), media);
            await Until(() => guests[0].Participants.Count == 5 && !guests[0].BothReady);
            Assert.False(host.CanApprovePlayback); Assert.False(host.PlaybackAuthorized);
            await host.DisposeAsync(); await Until(() => guests.All(g => !g.IsConnected) && !replacement.IsConnected);
        }
        finally { foreach (var guest in guests) await guest.DisposeAsync(); }
    }

    [Theory]
    [InlineData(TheaterMode.PeerSync)]
    [InlineData(TheaterMode.DirectStream)]
    public async Task FourGuestsReceiveTheirOwnVideoAndLeavingOnePreservesOthers(TheaterMode mode)
    {
        var folder = Path.Combine(Path.GetTempPath(), "anit-group-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "episode.mkv"); var data = new byte[262144]; new Random(12).NextBytes(data); await File.WriteAllBytesAsync(path, data);
        var guests = new List<TheaterRoom>();
        try
        {
            await using var host = TheaterRoom.Host(await MediaIdentity.FromFileAsync(path), IPAddress.Loopback, sharedFilePath: path, mode: mode);
            if (mode == TheaterMode.PeerSync) await host.BeginPreparationAsync();
            for (var i = 0; i < 4; i++) guests.Add(await TheaterRoom.JoinAsync(host.Invite!.Encode()));
            if (mode == TheaterMode.PeerSync)
            {
                await Task.WhenAll(guests.Select((g, i) => g.DownloadAsync(Path.Combine(folder, i + ".mkv"))));
                await Until(() => host.RemoteHasMedia && host.Participants.All(p => p.HasFile));
                foreach (var i in Enumerable.Range(0, 4)) Assert.Equal(data, await File.ReadAllBytesAsync(Path.Combine(folder, i + ".mkv")));
            }
            else
            {
                var endpoints = await Task.WhenAll(guests.Select(g => g.StartStreamingAsync()));
                using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false });
                await Task.WhenAll(endpoints.Select(async uri => { using var request = new HttpRequestMessage(HttpMethod.Get, uri); request.Headers.Range = new RangeHeaderValue(10000, 12047); using var response = await http.SendAsync(request); Assert.Equal(data[10000..12048], await response.Content.ReadAsByteArrayAsync()); }));
            }
            await guests[0].DisposeAsync(); await Until(() => host.Participants.Count == 4);
            Assert.All(guests.Skip(1), g => Assert.True(g.IsConnected));
        }
        finally { foreach (var guest in guests) await guest.DisposeAsync(); Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task VoiceRelaysRealOpusForFivePeopleWithoutEchoAndEnforcesMuteAndRemoval()
    {
        await using var host = TheaterRoom.Host(new(1024, new string('A', 64)), IPAddress.Loopback);
        var guests = new List<TheaterRoom>(); var received = new ConcurrentDictionary<int, ConcurrentBag<Guid>>();
        for (var i = 0; i < 5; i++) received[i] = new();
        try
        {
            for (var i = 0; i < 4; i++) guests.Add(await TheaterRoom.JoinAsync(host.Invite!.Encode(), displayName: "Voz " + i, media: host.Episode));
            Assert.False(host.VoiceEnabled); Assert.All(guests, g => Assert.False(g.VoiceEnabled));
            var rooms = new[] { host }.Concat(guests).ToArray();
            for (var i = 0; i < 5; i++) { var index = i; rooms[i].VoiceAudioReceived += a => { Assert.Equal(960, a.Samples.Length); received[index].Add(a.ParticipantId); }; }
            var speaking = new ConcurrentBag<Guid>(); var peaks = new ConcurrentDictionary<Guid, int>();
            host.VoiceAudioReceived += a => { peaks[a.ParticipantId] = a.Samples.Max(s => Math.Abs((int)s)); if (host.IsSpeaking(a.ParticipantId)) speaking.Add(a.ParticipantId); };
            await host.StartVoiceAsync(); foreach (var guest in guests) await guest.StartVoiceAsync();
            Assert.All(rooms, g => Assert.True(g.VoiceMuted));
            foreach (var r in rooms) await r.SetVoiceStateAsync(false, false);
            await Until(() => guests.All(g => g.Participants.Count(p => p.VoiceConnected && !p.Muted) == 5));
            var ids = rooms.Select(r => r.Participants.Single(p => p.IsHost == r.IsHost && (r.IsHost || p.DisplayName == "Voz " + guests.IndexOf(r))).Id).ToArray();
            var samples = Enumerable.Range(0, 960).Select(i => (short)(12000 * Math.Sin(i * Math.PI / 30))).ToArray();
            for (var frame = 0; frame < 6; frame++) { foreach (var r in rooms) r.SubmitVoice(samples); await Task.Delay(20); }
            await Until(() => received.All(pair => pair.Value.Distinct().Count() == 4));
            for (var i = 0; i < 5; i++) Assert.DoesNotContain(ids[i], received[i]);
            Assert.True(speaking.Distinct().Count() == 4, "Peaks " + string.Join(",", peaks.Select(p => p.Key + "=" + p.Value)) + "; speaking " + string.Join(",", speaking.Distinct())); await Task.Delay(350); Assert.All(host.Participants, p => Assert.False(p.Speaking));
            await host.SetMicrophoneBlockedAsync(ids[1], true); await Until(() => guests[0].MicrophoneBlocked);
            await Task.Delay(100); foreach (var bag in received.Values) bag.Clear();
            for (var i = 0; i < 5; i++) guests[0].SubmitVoice(samples);
            await Task.Delay(120); Assert.All(received.Values, bag => Assert.Empty(bag));
            await host.SetMicrophoneBlockedAsync(ids[1], false); await Until(() => !guests[0].MicrophoneBlocked);
            await guests[1].SetVoiceStateAsync(true, true); await Until(() => host.Participants.Single(p => p.Id == ids[2]).Deafened);
            foreach (var bag in received.Values) bag.Clear();
            for (var i = 0; i < 4; i++) { host.SubmitVoice(samples); await Task.Delay(20); }
            await Until(() => received[1].Count > 0 && received[3].Count > 0 && received[4].Count > 0); Assert.Empty(received[2]);
            await guests[2].StopVoiceAsync(); Assert.True(guests[2].IsConnected); Assert.False(guests[2].VoiceEnabled);
            await guests[2].StartVoiceAsync(); await Until(() => host.Participants.Single(p => p.Id == ids[3]).VoiceConnected);
            await host.RemoveParticipantAsync(ids[1]); await Until(() => !guests[0].IsConnected);
            Assert.All(guests.Skip(1), g => Assert.True(g.IsConnected));
            await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(4));
            await Until(() => guests.All(g => !g.IsConnected && !g.VoiceEnabled));
        }
        finally { foreach (var guest in guests) await guest.DisposeAsync(); }
    }

    [Fact]
    public async Task MultipleBufferWaitsRequireEveryGuestAndResumeUsesIndividualRevisions()
    {
        var path = Path.Combine(Path.GetTempPath(), "anit-group-buffer-" + Guid.NewGuid().ToString("N") + ".mkv"); await File.WriteAllBytesAsync(path, new byte[1024]);
        var controls = new List<Connection>(); var videos = new List<Connection>();
        try
        {
            await using var host = TheaterRoom.Host(await MediaIdentity.FromFileAsync(path), IPAddress.Loopback, sharedFilePath: path, mode: TheaterMode.DirectStream);
            for (var i = 0; i < 4; i++)
            {
                var control = await Connect(host.Invite!); controls.Add(control);
                await control.Channel.SendAsync(new("hello", Token: host.Invite!.Token), default);
                var welcome = await control.Channel.ReadAsync(default);
                var video = await Connect(host.Invite); videos.Add(video);
                await video.Channel.SendAsync(new("media-hello", Token: welcome.Token), default);
                Assert.Equal("media-welcome", (await video.Channel.ReadAsync(default)).Type);
                await control.Channel.SendAsync(new("ready", Ready: true), default);
            }
            await host.SetReadyAsync(true); await Until(() => host.BothReady);
            await controls[0].Channel.SendAsync(new("buffer-status", Buffer: new(8, true, 10, 10, 100)), default);
            await controls[1].Channel.SendAsync(new("buffer-status", Buffer: new(1, true, 9, 10, 100)), default);
            await Until(() => host.Participants.Count(p => p.Buffering) == 2);
            await host.PublishAsync(9, false);
            await controls[0].Channel.SendAsync(new("buffer-status", Buffer: new(9, false, 10, 100, 100)), default);
            await Until(() => host.Participants.Count(p => p.Buffering) == 1);
            Assert.True(host.GuestBuffering); Assert.Equal(9, host.BufferStatus!.PositionSeconds);
            await Assert.ThrowsAsync<InvalidOperationException>(() => host.ResumeStreamingAsync(9, ClockEstimator.NowMs + 300));
            await controls[1].Channel.SendAsync(new("buffer-status", Buffer: new(2, false, 9, 100, 100)), default);
            await Until(() => !host.GuestBuffering); Assert.True(host.StreamHold);
            await host.ResumeStreamingAsync(9, ClockEstimator.NowMs + 300);
            for (var i = 0; i < controls.Count; i++)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)); RoomPacket packet;
                do { packet = await controls[i].Channel.ReadAsync(timeout.Token); } while (packet.State?.Playing != true);
                Assert.Equal(i == 0 ? 9 : i == 1 ? 2 : 0, packet.State!.BufferResumeRevision);
            }
            Assert.False(host.StreamHold);
        }
        finally { foreach (var link in videos.Concat(controls)) link.Dispose(); File.Delete(path); }
    }

    [Fact]
    public async Task VoiceSecretIsScopedAndServerRejectsSpoofingAndBlockedAudio()
    {
        await using var host = TheaterRoom.Host(new(1024, new string('A', 64)), IPAddress.Loopback);
        await host.StartVoiceAsync(); await host.SetVoiceStateAsync(false, false);
        using var control = await Connect(host.Invite!);
        await control.Channel.SendAsync(new("hello", Token: host.Invite!.Token, Media: host.Episode), default);
        var welcome = await control.Channel.ReadAsync(default); var id = welcome.ParticipantId;
        await Until(() => host.Participants.Count == 2);
        foreach (var invalid in new[] { host.Invite.Token, welcome.Token, new string('F', 64) })
        {
            using var rejected = await Connect(host.Invite);
            await rejected.Channel.SendAsync(new("voice-hello", Token: invalid), default);
            Assert.Equal("reject", (await rejected.Channel.ReadAsync(default)).Type);
        }
        using var voice = await Connect(host.Invite);
        await voice.Channel.SendAsync(new("voice-hello", Token: welcome.VoiceToken), default);
        Assert.Equal("voice-welcome", (await voice.Channel.ReadAsync(default)).Type);
        using (var duplicate = await Connect(host.Invite))
        {
            await duplicate.Channel.SendAsync(new("voice-hello", Token: welcome.VoiceToken), default);
            Assert.Equal("reject", (await duplicate.Channel.ReadAsync(default)).Type);
        }
        await voice.Channel.SendAsync(new("voice-status", Ready: false), default);
        await Until(() => !host.Participants.Single(p => p.Id == id).Muted);
        var arrivals = new ConcurrentBag<Guid>(); host.VoiceAudioReceived += a => arrivals.Add(a.ParticipantId);
        var encoder = Concentus.OpusCodecFactory.CreateEncoder(48000, 1, Concentus.Enums.OpusApplication.OPUS_APPLICATION_VOIP);
        encoder.Bitrate = 24000;
        var pcm = Enumerable.Range(0, 960).Select(i => (short)(12000 * Math.Sin(i * Math.PI / 30))).ToArray();
        long sequence = 0;
        async Task SendFrame(long? timestamp = null)
        {
            var bytes = new byte[512]; var count = encoder.Encode(pcm, 960, bytes, bytes.Length);
            // A forged sender is always replaced by the identity of this control connection.
            await voice.Channel.SendAsync(new("voice-frame", ParticipantId: welcome.HostId, Data: bytes[..count], Offset: ++sequence, Clock: timestamp ?? ClockEstimator.NowMs), default);
        }
        await SendFrame(); await Until(() => arrivals.Count == 1); Assert.All(arrivals, sender => Assert.Equal(id, sender));
        await SendFrame(ClockEstimator.NowMs - 1000); await Task.Delay(50); Assert.Single(arrivals);
        await host.SetMicrophoneBlockedAsync(id, true);
        await SendFrame(); await Task.Delay(80); Assert.Single(arrivals); Assert.False(host.IsSpeaking(id));
        await host.SetMicrophoneBlockedAsync(id, false); await SendFrame(); await Until(() => arrivals.Count == 2);
        await Assert.ThrowsAsync<InvalidDataException>(() => voice.Channel.SendAsync(new("voice-frame", ParticipantId: id, Data: new byte[513], Offset: ++sequence, Clock: ClockEstimator.NowMs), default));
        await voice.Channel.SendAsync(new("voice-frame", ParticipantId: id, Data: new byte[] { 0 }, Offset: ++sequence, Clock: ClockEstimator.NowMs), default);
        await Until(() => !host.Participants.Single(p => p.Id == id).VoiceConnected);
        Assert.True(host.IsConnected); // Bad audio closes only voice, never the video room.
    }

    private static async Task<Connection> Connect(TheaterInvite invite)
    {
        var client = new System.Net.Sockets.TcpClient { NoDelay = true };
        await client.ConnectAsync(invite.Address, invite.Port);
        var tls = new System.Net.Security.SslStream(client.GetStream(), false, (_, cert, _, _) => cert is not null && Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(cert.GetRawCertData())) == invite.CertificateHash);
        await tls.AuthenticateAsClientAsync("AniT Movie Theater");
        return new(client, new PacketChannel(tls));
    }
    private sealed class Connection(System.Net.Sockets.TcpClient client, PacketChannel channel) : IDisposable
    {
        public PacketChannel Channel => channel;
        public void Dispose() { channel.Dispose(); client.Dispose(); }
    }

    [Fact]
    public async Task VoiceBinaryFramesRoundTripWithoutBase64AndRejectInvalidMetadata()
    {
        using var stream = new MemoryStream(); using var channel = new PacketChannel(stream);
        var id = Guid.NewGuid(); var bytes = new byte[60]; new Random(9).NextBytes(bytes);
        await channel.SendAsync(new("voice-frame", ParticipantId: id, Data: bytes, Offset: 3, Clock: 12345, Request: 7), default);
        Assert.Equal(105, stream.Length); stream.Position = 0;
        var packet = await channel.ReadAsync(default);
        Assert.Equal("voice-frame", packet.Type); Assert.Equal(id, packet.ParticipantId); Assert.Equal(3, packet.Offset); Assert.Equal(12345, packet.Clock); Assert.Equal(7, packet.Request); Assert.Equal(bytes, packet.Data);
        stream.SetLength(0);
        stream.Write(new byte[] { 255, 255, 255, 214 }); // -(41 + 1), with an invalid empty identity.
        stream.Write(new byte[42]); stream.GetBuffer()[4] = 2; stream.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => channel.ReadAsync(default));
    }

    [Fact]
    public void CodecPreservesAudiblePcm()
    {
        var encoder = Concentus.OpusCodecFactory.CreateEncoder(48000, 1, Concentus.Enums.OpusApplication.OPUS_APPLICATION_VOIP);
        encoder.Bitrate = 24000;
        var decoder = Concentus.OpusCodecFactory.CreateDecoder(48000, 1);
        var input = Enumerable.Range(0, 960).Select(i => (short)(12000 * Math.Sin(i * Math.PI / 30))).ToArray();
        var bytes = new byte[512]; var output = new short[960];
        for (var i = 0; i < 10; i++) { var count = encoder.Encode(input, 960, bytes, bytes.Length); decoder.Decode(bytes.AsSpan(0, count), output, 960); }
        Assert.True(output.Max(s => Math.Abs((int)s)) > 5000, encoder.GetType().FullName + "/" + decoder.GetType().FullName + " peak " + output.Max(s => Math.Abs((int)s)));
    }

    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }
}

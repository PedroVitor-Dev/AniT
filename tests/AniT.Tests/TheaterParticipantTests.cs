using System.Net;
using AniT.Theater;

namespace AniT.Tests;

public sealed class TheaterParticipantTests
{
    [Theory]
    [InlineData(TheaterMode.PeerSync)]
    [InlineData(TheaterMode.DirectStream)]
    public async Task HostManagesSessionIdentityVoicePermissionAndRevokesInvitationOnRemoval(TheaterMode mode)
    {
        var path = Path.Combine(Path.GetTempPath(), "anit-participant-" + Guid.NewGuid().ToString("N") + ".mkv");
        await File.WriteAllBytesAsync(path, new byte[1048576]);
        try
        {
            var identity = await MediaIdentity.FromFileAsync(path);
            await using var host = TheaterRoom.Host(identity, IPAddress.Loopback, sharedFilePath: path, mode: mode, displayName: "Pedro");
            var oldInvite = host.Invite!.Encode();
            await using var guest = await TheaterRoom.JoinAsync(oldInvite, mode == TheaterMode.PeerSync ? identity : null, displayName: "Amiga\nAniT");
            await Until(() => host.Participants.Count == 2);
            if (mode == TheaterMode.DirectStream) await guest.StartStreamingAsync();
            var member = host.Participants.Single(p => !p.IsHost);
            Assert.Equal("AmigaAniT", member.DisplayName); Assert.NotEqual(Guid.Empty, member.Id);
            Assert.Equal(member.Id, guest.Participants.Single(p => !p.IsHost).Id);
            Assert.Equal("Pedro", guest.Participants.Single(p => p.IsHost).DisplayName);
            Assert.True(host.IsVoiceTransmissionAllowed(member.Id));
            await host.SetMicrophoneBlockedAsync(member.Id, true); await Until(() => guest.MicrophoneBlocked);
            Assert.False(host.IsVoiceTransmissionAllowed(member.Id));
            await Assert.ThrowsAsync<InvalidOperationException>(() => guest.SetMicrophoneBlockedAsync(member.Id, false));
            await Assert.ThrowsAsync<InvalidOperationException>(() => guest.RemoveParticipantAsync(host.Participants[0].Id));
            Assert.True(guest.MicrophoneBlocked);
            await host.SetMicrophoneBlockedAsync(member.Id, false); await Until(() => !guest.MicrophoneBlocked);
            Assert.True(host.IsVoiceTransmissionAllowed(member.Id));
            await Assert.ThrowsAsync<InvalidOperationException>(() => host.RemoveParticipantAsync(host.Participants[0].Id));
            await host.RemoveParticipantAsync(member.Id); await Until(() => !guest.IsConnected);
            Assert.Equal(TheaterExitReason.RemovedByHost, guest.ExitReason); Assert.Single(host.Participants);
            Assert.False(host.IsVoiceTransmissionAllowed(member.Id)); Assert.NotEqual(oldInvite, host.Invite.Encode());
            await Assert.ThrowsAsync<InvalidDataException>(() => TheaterRoom.JoinAsync(oldInvite, identity));
            await using var next = await TheaterRoom.JoinAsync(host.Invite.Encode(), mode == TheaterMode.PeerSync ? identity : null, displayName: "Nova pessoa");
            await Until(() => host.Participants.Count == 2);
            var replacement = host.Participants.Single(p => !p.IsHost);
            Assert.NotEqual(member.Id, replacement.Id); Assert.False(replacement.MicrophoneBlocked);
            await Assert.ThrowsAsync<InvalidOperationException>(() => host.RemoveParticipantAsync(member.Id));
            await Assert.ThrowsAsync<InvalidOperationException>(() => host.SetMicrophoneBlockedAsync(member.Id, true));
            Assert.True(next.IsConnected); Assert.True(File.Exists(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NamesAreBoundedAndDoNotContainControlCharacters()
    {
        Assert.Equal("Convidado", TheaterRoom.ParticipantName("\n\t", "Convidado"));
        Assert.Equal(60, TheaterRoom.ParticipantName(new string('x', 200), "Convidado").Length);
        Assert.Equal("a_b", TheaterRoom.ParticipantName(" a_b ", "Convidado"));
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }
}

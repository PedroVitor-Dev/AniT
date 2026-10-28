using System.Net;
using AniT.Theater;

namespace AniT.Tests;

public sealed class TheaterPreparationTests
{
    private static MediaIdentity Media => new(1024, new string('A', 64));

    [Fact]
    public async Task HostOwnsReleaseAndApprovalGuestOnlyConfirmsAfterOpening()
    {
        await using var host = TheaterRoom.Host(Media, IPAddress.Loopback);
        await using var guest = await TheaterRoom.JoinAsync(host.Invite!.Encode(), Media);
        await Until(() => guest.Clock.IsReady);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.SetReadyAsync(true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => guest.BeginPreparationAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => guest.ConfirmParticipationAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.ApprovePlaybackAsync(0));
        await host.BeginPreparationAsync(); await Until(() => guest.PreparationStarted);
        Assert.Equal(host.PreparationStartedAtMs, guest.PreparationStartedAtMs);
        await Assert.ThrowsAsync<InvalidOperationException>(() => guest.ConfirmParticipationAsync());
        await host.SetReadyAsync(true); await guest.SetReadyAsync(true); await Until(() => host.BothReady);
        Assert.False(host.PlaybackAuthorized); Assert.False(host.CanApprovePlayback);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.PublishAsync(0, true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => guest.ApprovePlaybackAsync(0));
        await guest.ConfirmParticipationAsync(); await Until(() => host.CanApprovePlayback);
        Assert.False(host.PlaybackAuthorized); Assert.Null(guest.LatestState);
        var before = ClockEstimator.NowMs; var start = await host.ApprovePlaybackAsync(12); var after = ClockEstimator.NowMs;
        Assert.InRange(start, before + 5000, after + 5000);
        await Until(() => guest.PlaybackAuthorized);
        Assert.Equal(start, guest.LatestState!.HostTimeMs); Assert.Equal(12, guest.LatestState.PositionSeconds);
        Assert.Equal(start, guest.ApprovedStartAtMs);
    }

    [Fact]
    public async Task LosingReadinessRevokesApprovalAndRequiresAnotherConfirmation()
    {
        await using var host = TheaterRoom.Host(Media, IPAddress.Loopback); await host.BeginPreparationAsync();
        await using var guest = await TheaterRoom.JoinAsync(host.Invite!.Encode(), Media);
        await Until(() => guest.Clock.IsReady);
        await host.SetReadyAsync(true); await guest.SetReadyAsync(true); await Until(() => host.BothReady);
        await guest.ConfirmParticipationAsync(); await Until(() => host.CanApprovePlayback); await host.ApprovePlaybackAsync(0);
        await Until(() => guest.PlaybackAuthorized);
        await guest.SetReadyAsync(false); await Until(() => !host.RemoteReady);
        Assert.False(host.PlaybackAuthorized); Assert.False(guest.PlaybackAuthorized);
        Assert.False(host.GuestConfirmed); Assert.False(guest.GuestConfirmed);
        await guest.SetReadyAsync(true); await Until(() => host.BothReady);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.ApprovePlaybackAsync(0));
        await guest.ConfirmParticipationAsync(); await Until(() => host.CanApprovePlayback);
        await host.ApprovePlaybackAsync(0); await Until(() => guest.PlaybackAuthorized);
        await guest.DisposeAsync(); await Until(() => !host.IsConnected);
        Assert.False(host.PreparationStarted); Assert.False(host.GuestConfirmed); Assert.False(host.PlaybackAuthorized);
        await using var again = await TheaterRoom.JoinAsync(host.Invite.Encode(), Media);
        Assert.False(again.PreparationStarted);
    }

    [Fact]
    public async Task DownloadWaitsForHostAndReportsReceiveVerifyAndVerifiedStages()
    {
        var folder = Path.Combine(Path.GetTempPath(), "anit-preparation-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var source = Path.Combine(folder, "source.mkv"); var destination = Path.Combine(folder, "received.mkv");
            await File.WriteAllBytesAsync(source, new byte[1024 * 1024 + 7]);
            var identity = await MediaIdentity.FromFileAsync(source);
            await using var host = TheaterRoom.Host(identity, IPAddress.Loopback, sharedFilePath: source);
            await using var guest = await TheaterRoom.JoinAsync(host.Invite!.Encode());
            var phases = new System.Collections.Concurrent.ConcurrentQueue<EpisodeTransferPhase>();
            host.Changed += () => phases.Enqueue(host.DownloadProgress.Phase);
            await Assert.ThrowsAsync<InvalidOperationException>(() => guest.DownloadAsync(destination));
            Assert.False(File.Exists(destination)); Assert.Equal(0, guest.TransferredBytes);
            await host.BeginPreparationAsync(); await Until(() => guest.PreparationStarted);
            await guest.DownloadAsync(destination); await Until(() => host.RemoteHasMedia);
            Assert.Contains(EpisodeTransferPhase.Receiving, phases); Assert.Contains(EpisodeTransferPhase.Verifying, phases);
            Assert.Equal(EpisodeTransferPhase.Verified, host.DownloadProgress.Phase); Assert.Equal(identity.Length, host.DownloadProgress.Bytes);
            Assert.True(host.TransferFinishedAtMs >= host.TransferStartedAtMs && host.TransferStartedAtMs > 0);
            Assert.False(host.CanApprovePlayback); Assert.False(host.PlaybackAuthorized);
        }
        finally { Directory.Delete(folder, true); }
    }

    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }
}

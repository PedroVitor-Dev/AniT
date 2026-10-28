using System.Net;
using AniT.Theater;

namespace AniT.Tests;

public sealed class TheaterTransferTests
{
    [Fact]
    public async Task AuthorizedEpisodeDownloadsVerifiesThenUnlocksReadinessAndPlayback()
    {
        using var files = new TestFiles();
        var source = await files.SourceAsync(1024 * 1024 + 37);
        var identity = await MediaIdentity.FromFileAsync(source);
        await using var host = TheaterRoom.Host(identity, IPAddress.Loopback, sharedFilePath: source);
        await using var guest = await JoinForTransferAsync(host);
        await Until(() => host.IsConnected && guest.Clock.IsReady);
        Assert.False(guest.HasMedia); Assert.False(host.RemoteHasMedia);
        Assert.True(guest.SharingAvailable); Assert.Equal("episode.mkv", guest.OfferedName);
        await host.SetReadyAsync(true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => guest.SetReadyAsync(true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.PublishAsync(0, true));
        var destination = files.PathFor("download.mkv");
        Assert.Equal(destination, await guest.DownloadAsync(destination).WaitAsync(TimeSpan.FromSeconds(15)));
        await Until(() => host.RemoteHasMedia);
        Assert.True(guest.HasMedia); Assert.Equal(identity.Length, host.TransferredBytes); Assert.Equal(identity.Length, guest.TransferredBytes);
        Assert.True(identity.Matches(await MediaIdentity.FromFileAsync(destination)));
        Assert.Empty(Directory.GetFiles(files.Folder, ".anit-part-*"));
        Assert.False(host.BothReady); // Download OK is separate from a player actually prepared.
        await guest.SetReadyAsync(true); await Until(() => host.BothReady);
        await guest.ConfirmParticipationAsync(); await Until(() => host.CanApprovePlayback); await host.ApprovePlaybackAsync(8);
        await host.PublishAsync(8, true); await Until(() => guest.LatestState?.PositionSeconds == 8);
        await Assert.ThrowsAsync<InvalidOperationException>(() => guest.DownloadAsync(files.PathFor("duplicate.mkv")));
    }

    [Fact]
    public async Task SharingRequiresExplicitHostApprovalAndMatchingCopiesNeedNoTransfer()
    {
        using var files = new TestFiles(); var source = await files.SourceAsync(1024);
        var identity = await MediaIdentity.FromFileAsync(source);
        await using var privateRoom = TheaterRoom.Host(identity, IPAddress.Loopback);
        await Assert.ThrowsAsync<InvalidDataException>(() => TheaterRoom.JoinAsync(privateRoom.Invite!.Encode()));
        await using var original = await TheaterRoom.JoinAsync(privateRoom.Invite!.Encode(), identity);
        Assert.True(original.HasMedia); Assert.False(original.SharingAvailable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => original.DownloadAsync(files.PathFor("forbidden.mkv")));
        await using var shared = TheaterRoom.Host(identity, IPAddress.Loopback, sharedFilePath: source);
        await using var identical = await TheaterRoom.JoinAsync(shared.Invite!.Encode(), identity);
        await Until(() => shared.RemoteHasMedia);
        Assert.True(identical.HasMedia); Assert.Equal(0, shared.TransferredBytes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => shared.DownloadAsync(files.PathFor("host.mkv")));
    }

    [Fact]
    public async Task WrongGuestCopyCanBeReplacedByDownloadingSharedEpisode()
    {
        using var files = new TestFiles(); var source = await files.SourceAsync(20001);
        var identity = await MediaIdentity.FromFileAsync(source);
        await using var host = TheaterRoom.Host(identity, IPAddress.Loopback, sharedFilePath: source);
        await using var guest = await JoinForTransferAsync(host, new MediaIdentity(42, new string('A', 64)));
        Assert.False(guest.HasMedia);
        var destination = await guest.DownloadAsync(files.PathFor("right-copy.mkv"));
        Assert.True(identity.Matches(await MediaIdentity.FromFileAsync(destination)));
        await Until(() => host.RemoteHasMedia);
    }

    [Fact]
    public async Task ExistingDestinationIsNeverOverwrittenAndGuestCanChooseAnother()
    {
        using var files = new TestFiles(); var source = await files.SourceAsync(12345);
        await using var host = TheaterRoom.Host(await MediaIdentity.FromFileAsync(source), IPAddress.Loopback, sharedFilePath: source);
        await using var guest = await JoinForTransferAsync(host);
        var existing = files.PathFor("existing.mkv"); await File.WriteAllTextAsync(existing, "keep me");
        await Assert.ThrowsAsync<IOException>(() => guest.DownloadAsync(existing));
        Assert.Equal("keep me", await File.ReadAllTextAsync(existing)); Assert.True(guest.IsConnected);
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => guest.DownloadAsync(files.PathFor("missing/episode.mkv")));
        Assert.False(guest.IsDownloading);
        await guest.DownloadAsync(files.PathFor("new.mkv")); await Until(() => host.RemoteHasMedia);
    }

    [Fact]
    public async Task ChangedHostContentCannotProduceAnApprovedOrPublishedFile()
    {
        using var files = new TestFiles(); var source = await files.SourceAsync(65537);
        var identity = await MediaIdentity.FromFileAsync(source);
        await using var host = TheaterRoom.Host(identity, IPAddress.Loopback, sharedFilePath: source);
        await using var guest = await JoinForTransferAsync(host);
        var bytes = await File.ReadAllBytesAsync(source); bytes[0] ^= 255; await File.WriteAllBytesAsync(source, bytes);
        var destination = files.PathFor("changed.mkv");
        await Assert.ThrowsAnyAsync<IOException>(() => guest.DownloadAsync(destination).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(File.Exists(destination)); Assert.False(guest.HasMedia); Assert.False(host.RemoteHasMedia);
        Assert.Empty(Directory.GetFiles(files.Folder, ".anit-part-*"));
    }

    [Fact]
    public async Task CancellationRemovesPartialFileAndCleanRejoinCanDownload()
    {
        using var files = new TestFiles(); var source = await files.SourceAsync(4 * 1024 * 1024);
        var identity = await MediaIdentity.FromFileAsync(source);
        await using var host = TheaterRoom.Host(identity, IPAddress.Loopback, sharedFilePath: source);
        await using (var guest = await JoinForTransferAsync(host))
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            guest.Changed += () => { if (guest.TransferredBytes > 0) cancel.Cancel(); };
            var destination = files.PathFor("cancelled.mkv");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => guest.DownloadAsync(destination, cancel.Token));
            Assert.False(File.Exists(destination)); Assert.False(guest.HasMedia); Assert.False(guest.IsDownloading);
            Assert.Empty(Directory.GetFiles(files.Folder, ".anit-part-*"));
        }
        await Until(() => !host.IsConnected);
        await using var reconnect = await JoinForTransferAsync(host);
        await reconnect.DownloadAsync(files.PathFor("retry.mkv")).WaitAsync(TimeSpan.FromSeconds(15));
        await Until(() => host.RemoteHasMedia);
    }

    [Fact]
    public async Task HostDisconnectRemovesIncompleteDownloadAndDoesNotMarkGuestReady()
    {
        using var files = new TestFiles(); var source = await files.SourceAsync(4 * 1024 * 1024);
        await using var host = TheaterRoom.Host(await MediaIdentity.FromFileAsync(source), IPAddress.Loopback, sharedFilePath: source);
        await using var guest = await JoinForTransferAsync(host);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        guest.Changed += () => { if (guest.TransferredBytes > 0) started.TrySetResult(); };
        var destination = files.PathFor("interrupted.mkv"); var download = guest.DownloadAsync(destination);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(8)); await host.DisposeAsync();
        await Assert.ThrowsAnyAsync<IOException>(() => download.WaitAsync(TimeSpan.FromSeconds(8)));
        Assert.False(File.Exists(destination)); Assert.False(guest.LocalReady); Assert.False(guest.HasMedia);
        Assert.Empty(Directory.GetFiles(files.Folder, ".anit-part-*"));
    }

    private static async Task<TheaterRoom> JoinForTransferAsync(TheaterRoom host, MediaIdentity? media = null)
    {
        await host.BeginPreparationAsync(); return await TheaterRoom.JoinAsync(host.Invite!.Encode(), media);
    }

    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }
    private sealed class TestFiles : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "anit-transfer-test-" + Guid.NewGuid().ToString("N"));
        public TestFiles() => Directory.CreateDirectory(Folder);
        public string PathFor(string name) => Path.Combine(Folder, name);
        public async Task<string> SourceAsync(int size)
        {
            var path = PathFor("episode.mkv"); var bytes = new byte[size]; new Random(17).NextBytes(bytes); await File.WriteAllBytesAsync(path, bytes); return path;
        }
        public void Dispose() => Directory.Delete(Folder, true);
    }
}

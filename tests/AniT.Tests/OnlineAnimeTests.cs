using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using AniT.Core;
using AniT.Infrastructure;
using Microsoft.EntityFrameworkCore;
using MonoTorrent;
using MonoTorrent.Client;

namespace AniT.Tests;

public sealed class OnlineAnimeTests
{
    [Theory]
    [InlineData("bytes=0-9", 100, true, 0, 9)]
    [InlineData("bytes=50-", 100, true, 50, 99)]
    [InlineData("bytes=-12", 100, true, 88, 99)]
    [InlineData("bytes=90-999", 100, true, 90, 99)]
    [InlineData("bytes=100-", 100, false, 0, 0)]
    [InlineData("bytes=5-2", 100, false, 0, 0)]
    [InlineData("bytes=0-1,8-9", 100, false, 0, 0)]
    [InlineData("bytes=-0", 100, false, 0, 0)]
    public void RangesAreBounded(string range, long length, bool valid, long expectedStart, long expectedEnd)
    {
        Assert.Equal(valid, OnlineStreamServer.TryRange(range, length, out var start, out var end));
        if (valid) { Assert.Equal(expectedStart, start); Assert.Equal(expectedEnd, end); }
    }

    [Theory]
    [InlineData("../evil.mkv")]
    [InlineData("season/../../evil.mkv")]
    [InlineData("C:\\evil.mkv")]
    [InlineData("episode.mkv:stream")]
    public void TorrentPathsCannotEscapeRoot(string path) => Assert.Throws<InvalidDataException>(() => OnlineAnimeService.ContainedPath(Path.GetTempPath(), path));

    [Fact]
    public async Task HttpProxySupportsSeekHeadAndRejectsOtherPaths()
    {
        var bytes = Enumerable.Range(0, 300000).Select(i => (byte)i).ToArray();
        await using var server = new OnlineStreamServer(new MemoryStream(bytes), "teste.mkv");
        using var client = new HttpClient();
        using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, server.Address));
        Assert.Equal(bytes.Length, head.Content.Headers.ContentLength);
        using var request = new HttpRequestMessage(HttpMethod.Get, server.Address);
        request.Headers.Range = new RangeHeaderValue(100, 300);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(bytes[100..301], await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(new Uri(server.Address, "/unknown"))).StatusCode);
        using var invalid = new HttpRequestMessage(HttpMethod.Get, server.Address);
        invalid.Headers.TryAddWithoutValidation("Range", "bytes=999999-");
        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, (await client.SendAsync(invalid)).StatusCode);
    }

    [Fact]
    public async Task MetadataProbeDoesNotAbortPendingVideoResponse()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var source = new DelayedVideoStream();
        await using var server = new OnlineStreamServer(source, "test.mkv");
        using var client = new HttpClient();
        using var mainRequest = new HttpRequestMessage(HttpMethod.Get, server.Address);
        mainRequest.Headers.Range = new RangeHeaderValue(0, 63);
        using var main = await client.SendAsync(mainRequest, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        await source.FirstRead.Task.WaitAsync(deadline.Token);
        using var probeRequest = new HttpRequestMessage(HttpMethod.Get, server.Address);
        probeRequest.Headers.Range = new RangeHeaderValue(100000, 100063);
        using var probe = await client.SendAsync(probeRequest, deadline.Token);
        Assert.Equal(64, (await probe.Content.ReadAsByteArrayAsync(deadline.Token)).Length);
        source.Release.TrySetResult();
        Assert.Equal(64, (await main.Content.ReadAsByteArrayAsync(deadline.Token)).Length);
    }

    sealed class DelayedVideoStream : MemoryStream
    {
        public TaskCompletionSource FirstRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DelayedVideoStream() : base(new byte[200000]) { }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (Position == 0) { FirstRead.TrySetResult(); await Release.Task.WaitAsync(token); }
            return await base.ReadAsync(buffer, token);
        }
    }

    static EngineSettings OfflineSettings(string cache, int port = 0, int rate = 0) => new EngineSettingsBuilder {
        CacheDirectory = cache, DhtEndPoint = null, AllowLocalPeerDiscovery = false, AllowPortForwarding = false,
        ListenEndPoints = new Dictionary<string, IPEndPoint> { ["ipv4"] = new(IPAddress.Loopback, port) },
        MaximumDownloadRate = rate, UsePartialFiles = true
    }.ToSettings();

    static async Task<string> CreateTorrentAsync(string root, int count, int bytesPerEpisode)
    {
        var source = Path.Combine(root, "Original"); Directory.CreateDirectory(source);
        for (int i = 1; i <= count; i++) {
            var data = new byte[bytesPerEpisode]; new Random(i).NextBytes(data);
            await File.WriteAllBytesAsync(Path.Combine(source, $"Authorized Test - {i:00}.mkv"), data);
        }
        await File.WriteAllTextAsync(Path.Combine(source, "ignored.exe"), "not a video");
        var creator = new TorrentCreator { PieceLength = 32 * 1024 };
        var torrent = await creator.CreateAsync(new TorrentFileSource(source));
        var path = Path.Combine(root, "authorized.torrent");
        await File.WriteAllBytesAsync(path, torrent.Encode()); return path;
    }

    [Fact]
    public async Task BatchImportsTwelveEpisodesOnceAndResumesWithSameWatchIdentity()
    {
        var root = Path.Combine(Path.GetTempPath(), "AniT.Online.Tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "test.db"); var state = Path.Combine(root, "state");
        var source = await CreateTorrentAsync(root, 12, 64 * 1024);
        string id; Guid episodeId;
        await using (var service = new OnlineAnimeService(dbPath, state, OfflineSettings(Path.Combine(root, "cache")))) {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAsync(source, root, "Autorizado", false));
            id = await service.AddAsync(source, root, "Autorizado", true);
            var row = Assert.Single(await service.SnapshotAsync()); Assert.Equal(12, row.Episodes.Count);
            Assert.All(row.Episodes, e => Assert.True(e.IsCloud));
            using var db = AniTDatabase.Create(dbPath);
            Assert.Equal(12, await db.Episodes.CountAsync()); Assert.Equal(12, await db.MediaFiles.CountAsync());
            Assert.Equal(Enumerable.Range(1,12), await db.Episodes.OrderBy(e => e.Number).Select(e => e.Number).ToArrayAsync());
            var first = await db.Episodes.OrderBy(e => e.Number).FirstAsync(); episodeId = first.Id;
            first.Status = WatchStatus.Watching;
            first.PlaybackProgress = new PlaybackProgress { EpisodeId = first.Id, PositionSeconds = 120, DurationSeconds = 1400 };
            db.PlaybackProgresses.Add(first.PlaybackProgress);
            await db.SaveChangesAsync();
            await service.SetPausedAsync(id, true);
            Assert.True(Assert.Single(await service.SnapshotAsync()).Paused);
            // An ordinary scan of a containing folder must not duplicate partial files.
            var parent = new LibraryRoot { Path = root }; db.LibraryRoots.Add(parent); await db.SaveChangesAsync();
            var result = await new LibraryScanner(db).ScanAsync(parent);
            Assert.DoesNotContain(await db.MediaFiles.Where(f => f.LibraryRootId == parent.Id).ToListAsync(), f => f.RelativePath.StartsWith("AniT Online"));
            Assert.DoesNotContain(await db.MediaFiles.ToListAsync(), f => f.OnlineTorrentId == id && f.Availability == MediaFileAvailability.Missing);
        }
        await using (var service = new OnlineAnimeService(dbPath, state, OfflineSettings(Path.Combine(root, "cache")))) {
            var row = Assert.Single(await service.SnapshotAsync()); Assert.Equal(id, row.Id); Assert.True(row.Paused);
            using var db = AniTDatabase.Create(dbPath);
            var episode = await db.Episodes.Include(e => e.PlaybackProgress).SingleAsync(e => e.Id == episodeId);
            Assert.Equal(WatchStatus.Watching, episode.Status); Assert.Equal(120, episode.PlaybackProgress!.PositionSeconds);
        }
    }

    [Fact]
    public async Task ActualTorrentStreamReadsVerifiedBytesBeforeDownloadCompletes()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var root = Path.Combine(Path.GetTempPath(), "AniT.Online.Tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var source = await CreateTorrentAsync(root, 1, 8 * 1024 * 1024);
        var seedPort = FreePort();
        using var seedEngine = new ClientEngine(OfflineSettings(Path.Combine(root, "seed-cache"), seedPort));
        var seeder = await seedEngine.AddAsync(await Torrent.LoadAsync(source), root);
        await seeder.HashCheckAsync(false); await seeder.StartAsync();
        await using var service = new OnlineAnimeService(Path.Combine(root,"test.db"), Path.Combine(root,"state"), OfflineSettings(Path.Combine(root,"cache"), rate: 512 * 1024));
        var id = await service.AddAsync(source, root, "Autorizado", true, deadline.Token);
        var engine = (ClientEngine)typeof(OnlineAnimeService).GetField("engine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        var downloader = engine.Torrents.Single();
        await downloader.AddPeerAsync(new PeerInfo(new Uri($"ipv4://127.0.0.1:{seedPort}")));
        using var db = AniTDatabase.Create(Path.Combine(root,"test.db"));
        var file = await db.MediaFiles.SingleAsync();
        var address = await service.OpenStreamAsync(file, deadline.Token);
        Assert.True(downloader.Progress < 100, "Playback must start before full download");
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, address); request.Headers.Range = new RangeHeaderValue(0,65535);
        using var response = await client.SendAsync(request, deadline.Token); response.EnsureSuccessStatusCode();
        var expected = await File.ReadAllBytesAsync(Path.Combine(root,"Original","Authorized Test - 01.mkv"));
        Assert.Equal(expected[..65536], await response.Content.ReadAsByteArrayAsync(deadline.Token));
        await engine.UpdateSettingsAsync(new EngineSettingsBuilder(engine.Settings) { MaximumDownloadRate = 0 }.ToSettings());
        OnlineTransfer row;
        do { await Task.Delay(50, deadline.Token); row = Assert.Single(await service.SnapshotAsync(deadline.Token)); } while (row.Episodes.Any(e => e.IsCloud));
        var completed = await db.MediaFiles.AsNoTracking().SingleAsync();
        Assert.False(completed.IsCloud); Assert.Equal(file.EpisodeId, completed.EpisodeId);
        Assert.True(File.Exists(downloader.Files[completed.OnlineFileIndex].DownloadCompleteFullPath));
        using var tailRequest = new HttpRequestMessage(HttpMethod.Get, address); tailRequest.Headers.Range = new RangeHeaderValue(expected.Length - 1024, expected.Length - 1);
        using var tail = await client.SendAsync(tailRequest, deadline.Token); tail.EnsureSuccessStatusCode();
        Assert.Equal(expected[^1024..], await tail.Content.ReadAsByteArrayAsync(deadline.Token));
        await seeder.StopAsync();
    }

    [Fact]
    public async Task ImportIntoExistingAnimeKeepsItsEpisodesAndRatings()
    {
        var root = Path.Combine(Path.GetTempPath(), "AniT.Online.Tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "library.db"); Guid id;
        using (var db = AniTDatabase.Create(path)) {
            var episode = new Episode { Number = 1, Rating = 5, Status = WatchStatus.Completed };
            id = episode.Id;
            db.Anime.Add(new Anime { Title = "Existente", Seasons = { new Season { Number = 1, Episodes = { episode } } } });
            await db.SaveChangesAsync();
        }
        var source = await CreateTorrentAsync(root, 2, 65536);
        await using var service = new OnlineAnimeService(path, Path.Combine(root,"state"), OfflineSettings(Path.Combine(root,"cache")));
        await service.AddAsync(source, root, "Existente", true);
        using var check = AniTDatabase.Create(path);
        Assert.Equal(1, await check.Anime.CountAsync()); Assert.Equal(2, await check.Episodes.CountAsync());
        Assert.Equal(5, (await check.Episodes.SingleAsync(e => e.Id == id)).Rating);
        Assert.Equal(WatchStatus.Completed, (await check.Episodes.SingleAsync(e => e.Id == id)).Status);
    }

    [Fact]
    public async Task VersionFourDatabaseGainsOnlineColumnsWithoutLosingHistory()
    {
        var root = Path.Combine(Path.GetTempPath(), "AniT.Online.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root,"old.db"); Guid id;
        using (var db = AniTDatabase.Create(path)) {
            var episode = new Episode { Number = 1, Status = WatchStatus.Completed, Rating = 4 }; id = episode.Id;
            db.Anime.Add(new Anime { Title = "Preservado", Seasons = { new Season { Number = 1, Episodes = { episode } } } });
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE MediaFiles DROP COLUMN OnlineTorrentId;");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE MediaFiles DROP COLUMN OnlineFileIndex;");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE MediaFiles DROP COLUMN IsCloud;");
            await db.Database.ExecuteSqlRawAsync("UPDATE AniTSchemaInfo SET Version = 4;");
        }
        using var check = AniTDatabase.Create(path);
        var preserved = await check.Episodes.SingleAsync(e => e.Id == id);
        Assert.Equal(4, preserved.Rating); Assert.Equal(WatchStatus.Completed, preserved.Status);
        Assert.Empty(await check.MediaFiles.Where(f => f.IsCloud).ToListAsync());
    }

    static int FreePort() { var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
}

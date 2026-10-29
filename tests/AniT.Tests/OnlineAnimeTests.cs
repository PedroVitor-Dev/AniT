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

    [Fact]
    public async Task PendingReadAheadDoesNotStealPriorityFromNewContainerRequest()
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var source=new CompetingReads();
        await using var server=new OnlineStreamServer(source,"test.mkv",(offset,count)=>offset==0 ? source.MainAvailable.Task.IsCompleted : source.ProbeAvailable.Task.IsCompleted);
        using var client=new HttpClient();using var mainRequest=new HttpRequestMessage(HttpMethod.Get,server.Address);
        mainRequest.Headers.Range=new RangeHeaderValue(0,63);
        using var main=await client.SendAsync(mainRequest,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
        await source.MainStarted.Task.WaitAsync(timeout.Token);
        using var probeRequest=new HttpRequestMessage(HttpMethod.Get,server.Address);probeRequest.Headers.Range=new RangeHeaderValue(100000,100063);
        var probeResponse=client.SendAsync(probeRequest,timeout.Token);
        await source.ProbeStarted.Task.WaitAsync(timeout.Token);
        var previous=source.MainReads;await Task.Delay(750,timeout.Token);
        Assert.Equal(previous,source.MainReads);
        source.ProbeAvailable.TrySetResult();using var probe=await probeResponse;
        Assert.Equal(64,(await probe.Content.ReadAsByteArrayAsync(timeout.Token)).Length);
        source.MainAvailable.TrySetResult();Assert.Equal(64,(await main.Content.ReadAsByteArrayAsync(timeout.Token)).Length);
    }

    sealed class CompetingReads : MemoryStream
    {
        public TaskCompletionSource MainStarted {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ProbeStarted {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource MainAvailable {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ProbeAvailable {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int MainReads;
        public CompetingReads():base(new byte[100064]){}
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default)
        {
            if(Position==0) {Interlocked.Increment(ref MainReads);MainStarted.TrySetResult();await MainAvailable.Task.WaitAsync(token);}
            else {ProbeStarted.TrySetResult();await ProbeAvailable.Task.WaitAsync(token);}
            return await base.ReadAsync(buffer,token);
        }
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
        // Simulate a previous session's persisted source. No manual peer injection
        // into the manager: playback discovery must recover the source itself.
        var hints = new OnlinePeerCache(Path.Combine(root, "state", "recent-peers.json"));
        hints.Remember(OnlineAnimeService.PeerCacheKey(downloader.InfoHashes), new Uri($"ipv4://127.0.0.1:{seedPort}"));
        await hints.FlushAsync();
        using var db = AniTDatabase.Create(Path.Combine(root,"test.db"));
        var file = await db.MediaFiles.SingleAsync();
        var metrics = new System.Collections.Concurrent.ConcurrentBag<OnlinePlaybackMetric>();
        var trace = new OnlinePlaybackTrace(metrics.Add);
        var address = await service.OpenStreamAsync(file, deadline.Token, trace: trace);
        Assert.Contains(metrics, item => item.Name == "recent_peers_loaded");
        Assert.Contains(metrics, item => item.Name == "header_verified");
        Assert.Contains(metrics, item => item.Name == "http_stream_ready");
        Assert.Contains(metrics, item => item.Name == "first_data_observed");
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
        trace.Finish("controlled_cold_complete");
        var cold = metrics.Single(item => item.Kind == "summary");
        Assert.True(cold.Values["http_stream_ready_ms"] >= cold.Values["header_verified_ms"]);
        await service.PausePlaybackAsync(address);
        var warmMetrics = new System.Collections.Concurrent.ConcurrentBag<OnlinePlaybackMetric>();
        var warmTrace = new OnlinePlaybackTrace(warmMetrics.Add);
        await service.OpenStreamAsync(file, deadline.Token, trace: warmTrace); warmTrace.Finish("controlled_warm_complete");
        Assert.Equal(100, warmMetrics.Single(item => item.Name == "file_selected").Values["cached_percent"]);
        Assert.DoesNotContain(warmMetrics.LastOrDefault(item => item.Kind == "summary")!.Values.Keys, key => key == "first_video_frame_signal_ms");
        var output = Environment.GetEnvironmentVariable("ANIT_TRACE_TEST_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output)) {
            Directory.CreateDirectory(output);
            await File.WriteAllLinesAsync(Path.Combine(output, "controlled-cold.jsonl"), metrics.OrderBy(item => item.Sequence).Select(item => System.Text.Json.JsonSerializer.Serialize(item)));
            await File.WriteAllLinesAsync(Path.Combine(output, "controlled-warm.jsonl"), warmMetrics.OrderBy(item => item.Sequence).Select(item => System.Text.Json.JsonSerializer.Serialize(item)));
        }
        await seeder.StopAsync();
    }

    [Fact]
    public async Task SwitchingEpisodesKeepsTheTorrentAndExistingPeerConnectionRunning()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var root = Path.Combine(Path.GetTempPath(), "AniT.Online.Tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var source = await CreateTorrentAsync(root, 2, 8 * 1024 * 1024);
        var port = FreePort();
        using var seedEngine = new ClientEngine(OfflineSettings(Path.Combine(root, "seed"), port));
        var seed = await seedEngine.AddAsync(await Torrent.LoadAsync(source), root);
        await seed.HashCheckAsync(false); await seed.StartAsync();
        await using var service = new OnlineAnimeService(Path.Combine(root, "db"), Path.Combine(root, "state"), OfflineSettings(Path.Combine(root, "cache"), rate: 256 * 1024));
        await service.AddAsync(source, root, "Autorizado", true, deadline.Token);
        var engine = (ClientEngine)typeof(OnlineAnimeService).GetField("engine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        var manager = engine.Torrents.Single();
        await manager.AddPeerAsync(new PeerInfo(new Uri($"ipv4://127.0.0.1:{port}")));
        using var db = AniTDatabase.Create(Path.Combine(root, "db"));
        var files = await db.MediaFiles.OrderBy(f => f.OnlineFileIndex).ToArrayAsync();
        await service.OpenStreamAsync(files[0], deadline.Token);
        Assert.True(manager.OpenConnections > 0);
        var stopped = false;
        manager.TorrentStateChanged += (_, change) => { if (change.NewState is TorrentState.Stopped or TorrentState.Stopping) stopped = true; };
        await service.OpenStreamAsync(files[1], deadline.Token);
        Assert.False(stopped, "Switching an episode must not shut down the torrent/DHT and disconnect its peers");
        Assert.True(manager.OpenConnections > 0);
        var row = Assert.Single(await service.SnapshotAsync(deadline.Token));
        Assert.Single(row.Episodes, e => e.IsSelected);
        Assert.Equal(files[1].EpisodeId, row.Episodes.Single(e => e.IsSelected).EpisodeId);
    }

    [Fact]
    public async Task SlowTrackersDoNotPreventLaterAlternativesAndCancellationStopsDiscovery()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var reachedLast = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queries = Enumerable.Range(0, 16).Select(index => (Func<CancellationToken, Task>)(async token => {
            if (index == 15) reachedLast.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        })).ToArray();
        var task = QueryTrackers(queries, cancel.Token);
        await reachedLast.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task TrackerTimeoutAndFailureDoNotBlockTheRemainingSources()
    {
        var reachedLast = false; var messages = new List<string>();
        Func<CancellationToken, Task>[] queries = [
            token => Task.Delay(Timeout.Infinite, token),
            _ => Task.FromException(new IOException("Unreachable source")),
            _ => { reachedLast = true; return Task.CompletedTask; }
        ];
        await QueryTrackers(queries, CancellationToken.None, message => { lock (messages) messages.Add(message); }).WaitAsync(TimeSpan.FromSeconds(6));
        Assert.True(reachedLast);
        Assert.Equal(2, messages.Count);
    }

    static Task QueryTrackers(IReadOnlyList<Func<CancellationToken, Task>> queries, CancellationToken token, Action<string>? diagnostic = null)
        => (Task)typeof(OnlineAnimeService).GetMethod("QueryTrackersAsync", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [queries, diagnostic ?? (_ => { }), token, TimeSpan.FromMilliseconds(150)])!;

    sealed class InlinePreparationProgress(Action<OnlinePreparationProgress> observe) : IProgress<OnlinePreparationProgress>
    {
        public void Report(OnlinePreparationProgress value) => observe(value);
    }

    [Fact]
    public async Task ListingRestoredCatalogDoesNotCreateManagersOrWaitForLegacyMagnets()
    {
        var root = Path.Combine(Path.GetTempPath(), "AniT.Lazy.Tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var state = Path.Combine(root, "state"); Directory.CreateDirectory(state);
        var jobs = Enumerable.Range(0, 80).Select(i => new OnlineJob("legacy-" + i,
            "magnet:?xt=urn:btih:" + new string('C', 40), Path.Combine(root, "job-" + i), "Legacy", false, DateTimeOffset.UtcNow, 0)).ToArray();
        await File.WriteAllTextAsync(Path.Combine(state, "downloads.json"), System.Text.Json.JsonSerializer.Serialize(jobs));
        await using var service = new OnlineAnimeService(Path.Combine(root, "db"), state, OfflineSettings(Path.Combine(root, "cache")));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var rows = await service.SnapshotAsync(deadline.Token);
        Assert.Equal(80, rows.Count); Assert.All(rows, row => { Assert.True(row.Paused); Assert.True(row.WaitingForSelection); });
        var engine = (ClientEngine)typeof(OnlineAnimeService).GetField("engine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        Assert.Empty(engine.Torrents); Assert.Null(service.LastError);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetPausedAsync("legacy-0", false, deadline.Token));
        await service.SetPausedAsync("legacy-0", true, deadline.Token); Assert.Empty(engine.Torrents);
    }

    [Fact]
    public async Task SelectedCachedEpisodeOpensWithoutRestoringUnrelatedJobsAndCancellationCanRetry()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var root = Path.Combine(Path.GetTempPath(), "AniT.Lazy.Tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var state = Path.Combine(root, "state"); var database = Path.Combine(root, "db");
        var source = await CreateTorrentAsync(root, 1, 65536);
        string id; MediaFile file;
        await using (var initial = new OnlineAnimeService(database, state, OfflineSettings(Path.Combine(root, "cache")))) {
            id = await initial.AddAsync(source, root, "Autorizado", true, deadline.Token);
            using var db = AniTDatabase.Create(database); file = await db.MediaFiles.SingleAsync(deadline.Token);
            var engine = (ClientEngine)typeof(OnlineAnimeService).GetField("engine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(initial)!;
            var path = engine.Torrents.Single().Files[file.OnlineFileIndex].DownloadCompleteFullPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(Path.Combine(root, "Original", file.FileName), path);
        }
        var jobs = System.Text.Json.JsonSerializer.Deserialize<List<OnlineJob>>(await File.ReadAllTextAsync(Path.Combine(state, "downloads.json")))!;
        jobs.Insert(0, new OnlineJob("unrelated", "magnet:?xt=urn:btih:" + new string('D', 40), Path.Combine(root, "unrelated"), "Legacy", false, DateTimeOffset.UtcNow, 0));
        await File.WriteAllTextAsync(Path.Combine(state, "downloads.json"), System.Text.Json.JsonSerializer.Serialize(jobs));
        await using var service = new OnlineAnimeService(database, state, OfflineSettings(Path.Combine(root, "cache")));
        var coldEngine = (ClientEngine)typeof(OnlineAnimeService).GetField("engine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAsync(source, root, "Duplicado inativo", true, deadline.Token));
        Assert.Empty(coldEngine.Torrents);
        var stages = new List<OnlinePreparationProgress>();
        using (var cancelled = new CancellationTokenSource()) {
            var progress = new InlinePreparationProgress(value => { stages.Add(value); if (value.Stage == "Selecionando somente este episódio…") cancelled.Cancel(); });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.OpenStreamAsync(file, cancelled.Token, progress));
        }
        var restoredEngine = (ClientEngine)typeof(OnlineAnimeService).GetField("engine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        var manager = Assert.Single(restoredEngine.Torrents);
        Assert.Equal(TorrentState.Stopped, manager.State); Assert.All(manager.Files, item => Assert.Equal(Priority.DoNotDownload, item.Priority));
        var afterCancel = System.Text.Json.JsonSerializer.Deserialize<List<OnlineJob>>(await File.ReadAllTextAsync(Path.Combine(state, "downloads.json")))!.Single(job => job.Id == id);
        Assert.True(afterCancel.Paused); Assert.Null(afterCancel.SelectedFileIndex);
        Assert.Contains(stages, stage => !stage.TransferStarted && stage.Stage == "Lendo sua lista de episódios…");
        var uri = await service.OpenStreamAsync(file, deadline.Token, new InlinePreparationProgress(stages.Add));
        Assert.Equal("127.0.0.1", uri.Host); Assert.Single(restoredEngine.Torrents);
        Assert.Equal(100, manager.Files[file.OnlineFileIndex].BitField.PercentComplete);
        Assert.Equal(2, (await service.SnapshotAsync(deadline.Token)).Count);
        Assert.True(File.Exists(manager.Files[file.OnlineFileIndex].DownloadCompleteFullPath));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAsync(source, root, "Duplicado", true, deadline.Token));
    }

    [Fact]
    public async Task ChosenMetadataFailureKeepsCatalogAndCanRetryAfterSourceIsRestored()
    {
        var root = Path.Combine(Path.GetTempPath(), "AniT.Lazy.Tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var state = Path.Combine(root, "state"); var database = Path.Combine(root, "db");
        var source = await CreateTorrentAsync(root, 1, 65536); MediaFile file;
        await using (var initial = new OnlineAnimeService(database, state, OfflineSettings(Path.Combine(root, "cache")))) {
            await initial.AddAsync(source, root, "Autorizado", true);
            using var db = AniTDatabase.Create(database); file = await db.MediaFiles.SingleAsync();
        }
        var manifest = Path.Combine(state, "downloads.json");
        var jobs = System.Text.Json.JsonSerializer.Deserialize<List<OnlineJob>>(await File.ReadAllTextAsync(manifest))!;
        var missing = Path.Combine(root, "temporarily-missing.torrent"); jobs[0] = jobs[0] with { Source = missing };
        await File.WriteAllTextAsync(manifest, System.Text.Json.JsonSerializer.Serialize(jobs));
        await using var service = new OnlineAnimeService(database, state, OfflineSettings(Path.Combine(root, "cache")));
        await Assert.ThrowsAnyAsync<IOException>(() => service.OpenStreamAsync(file));
        Assert.NotNull(service.LastError); Assert.Single(await service.SnapshotAsync());
        var engine = (ClientEngine)typeof(OnlineAnimeService).GetField("engine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        Assert.Empty(engine.Torrents);
        File.Copy(source, missing);
        using var cancelled = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var progress = new InlinePreparationProgress(value => { if (value.Stage == "Selecionando somente este episódio…") cancelled.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.OpenStreamAsync(file, cancelled.Token, progress));
        Assert.Null(service.LastError); Assert.Single(engine.Torrents);
        Assert.Equal(TorrentState.Stopped, engine.Torrents.Single().State);
        Assert.Single((await service.SnapshotAsync()).Single().Episodes);
    }

    [Fact]
    public async Task FailedOrCancelledManifestReadDoesNotPoisonNextRestore()
    {
        var root = Path.Combine(Path.GetTempPath(), "AniT.Lazy.Tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var state = Path.Combine(root, "state"); Directory.CreateDirectory(state);
        var path = Path.Combine(state, "downloads.json"); await File.WriteAllTextAsync(path, "not json");
        await using var service = new OnlineAnimeService(Path.Combine(root, "db"), state, OfflineSettings(Path.Combine(root, "cache")));
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => service.RestoreAsync());
        await File.WriteAllTextAsync(path, "[]");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RestoreAsync(cancelled.Token));
        Assert.Empty(await service.SnapshotAsync());
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

    [Fact]
    public void MagnetWithSpacesUnicodeAndEncodedParametersKeepsHashAndTrackers()
    {
        const string hash = "0123456789abcdef0123456789abcdef01234567";
        var raw = $"magnet:?xt=urn%3Abtih%3A{hash}&dn=[Group] Season 2 | TEST×SERIES&tr=http://localhost:7777/announce&tr=udp://localhost:80/announce";
        var normalized = OnlineAnimeService.NormalizeMagnet(raw);
        var magnet = MagnetLink.Parse(normalized);
        Assert.Equal(hash, magnet.InfoHashes.V1!.ToHex().ToLowerInvariant());
        Assert.Equal("[Group] Season 2 | TEST×SERIES", magnet.Name);
        Assert.Equal(2, magnet.AnnounceUrls.Count);
        Assert.Equal(normalized, OnlineAnimeService.NormalizeMagnet(normalized));
    }

    [Fact]
    public async Task EpisodesWaitUntilOpenedAndOnlySelectedVideoDownloads()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var root = Path.Combine(Path.GetTempPath(), "AniT.OnDemand.Tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var source = await CreateTorrentAsync(root, 3, 2 * 1024 * 1024);
        var seedPort = FreePort();
        using var seedEngine = new ClientEngine(OfflineSettings(Path.Combine(root,"seed-cache"), seedPort));
        var seed = await seedEngine.AddAsync(await Torrent.LoadAsync(source), root);
        await seed.HashCheckAsync(false); await seed.StartAsync();
        var dbPath = Path.Combine(root,"db.sqlite"); var state = Path.Combine(root,"state");
        Guid firstId;
        await using (var service = new OnlineAnimeService(dbPath, state, OfflineSettings(Path.Combine(root,"cache"), rate: 256 * 1024))) {
            var id = await service.AddAsync(source, root, "Autorizado", true, deadline.Token);
            var engine = (ClientEngine)typeof(OnlineAnimeService).GetField("engine",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(service)!;
            var manager = engine.Torrents.Single();
            Assert.Equal(TorrentState.Stopped, manager.State);
            Assert.All(manager.Files, f => Assert.Equal(Priority.DoNotDownload, f.Priority));
            Assert.Equal(0, manager.Monitor.DataBytesReceived);
            Assert.True(Assert.Single(await service.SnapshotAsync()).WaitingForSelection);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetPausedAsync(id,false));
            using var db = AniTDatabase.Create(dbPath);
            var files = await db.MediaFiles.OrderBy(f=>f.OnlineFileIndex).ToListAsync(); firstId=files[0].EpisodeId;
            await manager.AddPeerAsync(new PeerInfo(new Uri($"ipv4://127.0.0.1:{seedPort}")));
            var firstAddress = await service.OpenStreamAsync(files[0],deadline.Token);
            Assert.Equal(Priority.Normal,manager.Files[files[0].OnlineFileIndex].Priority);
            Assert.All(manager.Files.Where((_,i)=>i!=files[0].OnlineFileIndex),f=>Assert.Equal(Priority.DoNotDownload,f.Priority));
            var active=Assert.Single(await service.SnapshotAsync()); Assert.True(active.Episodes.Single(e=>e.EpisodeId==firstId).IsSelected);
            Assert.All(active.Episodes.Where(e=>e.EpisodeId!=firstId), e=>Assert.False(e.IsSelected));
            _ = await service.OpenStreamAsync(files[2],deadline.Token);
            await service.PausePlaybackAsync(firstAddress);
            Assert.False(Assert.Single(await service.SnapshotAsync()).Paused);
            Assert.Equal(Priority.Normal,manager.Files[files[2].OnlineFileIndex].Priority);
            Assert.All(manager.Files.Where((_,i)=>i!=files[2].OnlineFileIndex),f=>Assert.Equal(Priority.DoNotDownload,f.Priority));
            await service.PausePlaybackAsync();
            Assert.True(Assert.Single(await service.SnapshotAsync()).Paused);
        }
        // Legacy manifests can say "not paused" without having a selected episode.
        var manifests = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(state,"downloads.json")))!.AsArray();
        manifests[0]!["Paused"]=false; manifests[0]!.AsObject().Remove("SelectedFileIndex");
        await File.WriteAllTextAsync(Path.Combine(state,"downloads.json"),manifests.ToJsonString());
        await using (var restored = new OnlineAnimeService(dbPath,state,OfflineSettings(Path.Combine(root,"cache")))) {
            var row = Assert.Single(await restored.SnapshotAsync()); Assert.True(row.WaitingForSelection); Assert.True(row.Paused);
            var engine=(ClientEngine)typeof(OnlineAnimeService).GetField("engine",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(restored)!;
            Assert.Empty(engine.Torrents); // Restoring the catalog no longer constructs an inactive manager.
            Assert.Contains(row.Episodes,e=>e.EpisodeId==firstId);
        }
        await seed.StopAsync();
    }


    [Fact]
    public async Task StartupDoesNotWaitForUnavailableTailWithFourMiBPieces()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var root=Path.Combine(Path.GetTempPath(),"AniT.Online.Startup",Guid.NewGuid().ToString("N"));
        var original=Path.Combine(root,"Original"); Directory.CreateDirectory(original);
        var path=Path.Combine(original,"Authorized - 01.mkv");
        var data=new byte[16*1024*1024];new Random(17).NextBytes(data);await File.WriteAllBytesAsync(path,data,timeout.Token);
        var torrent=await new TorrentCreator { PieceLength=4*1024*1024 }.CreateAsync(new TorrentFileSource(original));
        var metadata=Path.Combine(root,"test.torrent");await File.WriteAllBytesAsync(metadata,torrent.Encode(),timeout.Token);
        // This peer has the beginning but neither of the final two pieces. Old tail-first
        // prebuffer cannot finish; the new path must serve a verified prefix regardless.
        Array.Clear(data,8*1024*1024,8*1024*1024);await File.WriteAllBytesAsync(path,data,timeout.Token);
        var port=FreePort();using var seedEngine=new ClientEngine(OfflineSettings(Path.Combine(root,"seed"),port));
        var seed=await seedEngine.AddAsync(await Torrent.LoadAsync(metadata),original);await seed.HashCheckAsync(false);await seed.StartAsync();
        Assert.True(seed.Bitfield[0]);Assert.False(seed.Bitfield[2]);Assert.False(seed.Bitfield[3]);
        await using var service=new OnlineAnimeService(Path.Combine(root,"db.sqlite"),Path.Combine(root,"state"),OfflineSettings(Path.Combine(root,"client")));
        await service.AddAsync(metadata,root,"Autorizado",true,timeout.Token);
        var engine=(ClientEngine)typeof(OnlineAnimeService).GetField("engine",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(service)!;
        await engine.Torrents.Single().AddPeerAsync(new PeerInfo(new Uri($"ipv4://127.0.0.1:{port}")));
        using var db=AniTDatabase.Create(Path.Combine(root,"db.sqlite"));var file=await db.MediaFiles.SingleAsync(timeout.Token);
        var address=await service.OpenStreamAsync(file,timeout.Token);
        using var client=new HttpClient();using var request=new HttpRequestMessage(HttpMethod.Get,address);
        request.Headers.Range=new RangeHeaderValue(0,1023);
        var response=await client.SendAsync(request,timeout.Token);
        Assert.Equal(data[..1024],await response.Content.ReadAsByteArrayAsync(timeout.Token));
        Assert.False(engine.Torrents.Single().Bitfield[3]);
        await seed.StopAsync();
    }

    [Fact]
    public async Task AutomaticImportCleansTitleOrdersEpisodesAndKeepsExistingLibraryIdentity()
    {
        var root=Path.Combine(Path.GetTempPath(),"AniT.Online.Automatic",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var original=Path.Combine(root,"SPY.x.FAMILY.S02.1080p.WEBRip.MULTi.x265-group");Directory.CreateDirectory(original);
        foreach(var number in new[]{12,3,1})await File.WriteAllBytesAsync(Path.Combine(original,$"[Group] SPY.x.FAMILY.S02E{number:00}.1080p.WEBRip.x265-releasegroup.mkv"),new byte[4096]);
        var torrent=await new TorrentCreator{PieceLength=32768}.CreateAsync(new TorrentFileSource(original));var metadata=Path.Combine(root,"test.torrent");await File.WriteAllBytesAsync(metadata,torrent.Encode());
        var database=Path.Combine(root,"db.sqlite");
        Guid animeId;
        using(var db=AniTDatabase.Create(database)) {var anime=new Anime {Title="Spy x Family"};animeId=anime.Id;db.Anime.Add(anime);await db.SaveChangesAsync();}
        await using var service=new OnlineAnimeService(database,Path.Combine(root,"state"),OfflineSettings(Path.Combine(root,"cache")));
        await service.AddAsync(metadata,root,"",true);
        using(var db=AniTDatabase.Create(database)) {
            var anime=Assert.Single(await db.Anime.ToListAsync());Assert.Equal(animeId,anime.Id);
            Assert.All(await db.Episodes.ToListAsync(),episode=>Assert.StartsWith("Episódio ",episode.Title));
            Assert.All(await db.Seasons.ToListAsync(),season=>Assert.Equal(2,season.Number));
        }
        var snapshot=Assert.Single(await service.SnapshotAsync());Assert.Equal(new[]{"S02E01","S02E03","S02E12"},snapshot.Episodes.Select(episode=>episode.Title));
        Assert.True(snapshot.WaitingForSelection);
    }

    static int FreePort() { var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
}

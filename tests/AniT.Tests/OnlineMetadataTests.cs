using System.Diagnostics;
using System.Net;
using System.Reflection;
using AniT.Infrastructure;
using MonoTorrent;
using MonoTorrent.Client;
using MonoTorrent.Trackers;
using ReusableTasks;

namespace AniT.Tests;

public sealed class OnlineMetadataTests
{
    static EngineSettings Offline(string directory, int port = 0) => new EngineSettingsBuilder {
        CacheDirectory=directory,DhtEndPoint=null,AllowLocalPeerDiscovery=false,AllowPortForwarding=false,
        ListenEndPoints=new Dictionary<string,IPEndPoint>{{"ipv4",new(IPAddress.Loopback,port)}}
    }.ToSettings();
    static string Root() { var path=Path.Combine(Path.GetTempPath(),"AniT.Metadata.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path; }
    static MagnetLink Missing()=>MagnetLink.Parse("magnet:?xt=urn:btih:"+new string('a',40));
    sealed class Reports(Action<OnlineImportProgress> action):IProgress<OnlineImportProgress> {public void Report(OnlineImportProgress value)=>action(value);}

    [Fact]
    public async Task TrackerThatIgnoresCancellationCannotHoldTheMetadataLookup()
    {
        var stalled=new TaskCompletionSource<AnnounceResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner=new Tracker((_,_)=>stalled.Task);
        var bounded=new BoundedMetadataTracker(inner,TimeSpan.FromMilliseconds(80));
        await Assert.ThrowsAsync<TimeoutException>(()=>bounded.AnnounceAsync(new AnnounceRequest(Missing().InfoHashes),CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
        stalled.TrySetResult(new AnnounceResponse(TrackerState.Ok));
    }
    [Fact]
    public async Task OfflineTrackerResponseAllowsTheLibraryToTryItsNextAlternative()
    {
        var tracker=new BoundedMetadataTracker(new Tracker((_,_)=>Task.FromResult(new AnnounceResponse(TrackerState.Offline))));
        await Assert.ThrowsAsync<IOException>(()=>tracker.AnnounceAsync(new AnnounceRequest(Missing().InfoHashes),CancellationToken.None).AsTask());
    }
    [Fact]
    public async Task NoSourcesEndsWithATimeoutInsteadOfAnUnboundedCleanupWait()
    {
        var root=Root();var reports=new List<OnlineImportProgress>();
        await Assert.ThrowsAsync<TimeoutException>(()=>OnlineMetadataResolver.ResolveAsync(Missing(),Offline(root),new Reports(reports.Add),CancellationToken.None,TimeSpan.FromMilliseconds(150)).WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Contains(reports,p=>p.IsIndeterminate&&p.Detail.Contains("0 conexões"));
    }
    [Fact]
    public async Task CancelImportReleasesTheCatalogAndAllowsAnotherImport()
    {
        var root=Root();using var cancel=new CancellationTokenSource();
        await using var service=new OnlineAnimeService(Path.Combine(root,"db"),Path.Combine(root,"state"),Offline(Path.Combine(root,"cache")));
        var reports=new Reports(p=>{if(p.IsIndeterminate)cancel.Cancel();});
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>service.AddAsync(Missing().ToV1String(),root,"",true,cancel.Token,reports).WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Empty(await service.SnapshotAsync().WaitAsync(TimeSpan.FromSeconds(2)));
        var engine=(ClientEngine)typeof(OnlineAnimeService).GetField("engine",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(service)!;
        Assert.Empty(engine.Torrents);
        var source=Path.Combine(root,"Authorized - 01.mkv");await File.WriteAllBytesAsync(source,new byte[65536]);
        var data=await new TorrentCreator().CreateAsync(new TorrentFileSource(source));var metadata=Path.Combine(root,"authorized.torrent");await File.WriteAllBytesAsync(metadata,data.Encode());
        await service.AddAsync(metadata,root,"Autorizado",true).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(await service.SnapshotAsync());
    }
    [Fact]
    public async Task LocalPeerReturnsMetadataWithoutDownloadingAnyEpisode()
    {
        var root=Root();var source=Path.Combine(root,"Authorized - 01.mkv");await File.WriteAllBytesAsync(source,new byte[65536]);
        var encoded=await new TorrentCreator().CreateAsync(new TorrentFileSource(source));var torrent=Torrent.Load(encoded.Encode());
        using var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
        using var seeder=new ClientEngine(Offline(Path.Combine(root,"seed-cache"),port));
        var seed=await seeder.AddAsync(torrent,root);await seed.HashCheckAsync(false);await seed.StartAsync();
        var magnet=MagnetLink.Parse("magnet:?xt=urn:btih:"+torrent.InfoHashes.V1!.ToHex()+"&tr=http%3A%2F%2Fmetadata.invalid%2Fannounce");
        var factory=Factories.Default.WithTrackerCreator("http",_=>new Tracker((request,_)=>Task.FromResult(new AnnounceResponse(TrackerState.Ok,new(){{request.InfoHashes.V1!,new List<PeerInfo>{new(new Uri($"ipv4://127.0.0.1:{port}"))}}}))));
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var bytes=await OnlineMetadataResolver.ResolveAsync(magnet,Offline(Path.Combine(root,"metadata-cache")),null,timeout.Token,sourceFactories:factory);
        Assert.Equal(torrent.InfoHashes,Torrent.Load(bytes.ToArray()).InfoHashes);
        Assert.Equal(0,seed.Monitor.DataBytesSent);
        await seed.StopAsync(TimeSpan.FromMilliseconds(100));
    }
    sealed class Tracker(Func<AnnounceRequest,CancellationToken,Task<AnnounceResponse>> announce):ITracker
    {
        public bool CanScrape=>false;public TimeSpan MinUpdateInterval=>TimeSpan.FromMinutes(1);public TimeSpan UpdateInterval=>TimeSpan.FromMinutes(10);public TimeSpan TimeSinceLastAnnounce=>TimeSpan.MaxValue;
        public TrackerState Status=>TrackerState.Ok;public Uri Uri=>new("http://metadata.invalid/announce");public string WarningMessage=>"";public string FailureMessage=>"";
        public async ReusableTask<AnnounceResponse> AnnounceAsync(AnnounceRequest request,CancellationToken token)=>await announce(request,token);
        public ReusableTask<ScrapeResponse> ScrapeAsync(ScrapeRequest request,CancellationToken token)=>throw new NotSupportedException();
    }
}

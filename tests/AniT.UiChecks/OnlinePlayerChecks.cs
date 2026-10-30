using System.IO;
using System.Net;
using System.Reflection;
using System.Windows;
using AniT.Core;
using AniT.Infrastructure;
using AniT.Player;
using Microsoft.EntityFrameworkCore;
using MonoTorrent;
using MonoTorrent.Client;

internal static class OnlinePlayerChecks
{
    sealed class BlockedVideo(Stream inner):Stream
    {
        public TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default){Started.TrySetResult();await Task.Delay(Timeout.Infinite,token);return 0;}
        public override bool CanRead=>true;public override bool CanSeek=>true;public override bool CanWrite=>false;public override long Length=>inner.Length;
        public override long Position{get=>inner.Position;set=>inner.Position=value;}public override long Seek(long offset,SeekOrigin origin)=>inner.Seek(offset,origin);
        public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();public override void Flush(){}public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
        protected override void Dispose(bool disposing){if(disposing)inner.Dispose();base.Dispose(disposing);}
    }
    static async Task WaitForPosition(MpcHcPlayer player,double minimum,CancellationToken token,double maximum=double.PositiveInfinity)
    {
        while(true) {
            token.ThrowIfCancellationRequested();
            try { var position=(await player.GetPositionAsync(token)).TotalSeconds; if(position>=minimum && position<=maximum)return; }
            catch(OperationCanceledException)when(!token.IsCancellationRequested) { }
            catch(TimeoutException)when(!token.IsCancellationRequested) { }
            catch(IOException)when(!token.IsCancellationRequested) { }
            await Task.Delay(150,token);
        }
    }
    public static int Run(Application app, string? fixture = null, bool resume = false)
    {
        int result = 1;
        var window = new Window { Width = 100, Height = 100, Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        window.Loaded += async (_, _) => {
            try {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(150)); var token = timeout.Token;
                var root = Path.Combine(Path.GetTempPath(), "AniT.Online.Native", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
                var original = Path.Combine(root, "Original"); Directory.CreateDirectory(original);
                var path = Path.Combine(original,"Authorized - 01" + (fixture is null ? ".avi" : ".mkv"));
                if (fixture is null) SyntheticTheaterMedia.WriteAvi(path, 600); else {
                    File.Copy(fixture, path);
                    File.Copy(fixture, Path.Combine(original,"Authorized - 02.mkv"));
                    File.Copy(fixture, Path.Combine(original,"Authorized - 03.mkv"));
                }
                var torrent = await new TorrentCreator { PieceLength = 64 * 1024 }.CreateAsync(new TorrentFileSource(original));
                var metadata = Path.Combine(root,"test.torrent"); await File.WriteAllBytesAsync(metadata,torrent.Encode(),token);
                var tcp = new System.Net.Sockets.TcpListener(IPAddress.Loopback,0); tcp.Start(); var port=((IPEndPoint)tcp.LocalEndpoint).Port;tcp.Stop();
                EngineSettings Settings(string name, int listen, int rate) => new EngineSettingsBuilder {
                    CacheDirectory=Path.Combine(root,name),DhtEndPoint=null,AllowLocalPeerDiscovery=false,AllowPortForwarding=false,
                    ListenEndPoints=new Dictionary<string,IPEndPoint>{{"ipv4",new(IPAddress.Loopback,listen)}}, MaximumDownloadRate=rate,UsePartialFiles=true
                }.ToSettings();
                using var seedEngine=new ClientEngine(Settings("seed",port,0));
                var seed=await seedEngine.AddAsync(await Torrent.LoadAsync(metadata),fixture is null ? original : root);await seed.HashCheckAsync(false);await seed.StartAsync();
                await using var service=new OnlineAnimeService(Path.Combine(root,"db.sqlite"),Path.Combine(root,"state"),Settings("client",0,fixture is null ? 4*1024*1024 : 512*1024));
                service.Diagnostic = message => Console.WriteLine(message);
                await service.AddAsync(metadata,root,"Autorizado",true,token);
                var engine=(ClientEngine)typeof(OnlineAnimeService).GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(service)!;
                await using var relay=new TorrentPeerRelay(port,fixture is null ? 4*1024*1024 : 1024*1024);
                var manager=engine.Torrents.Single(); await manager.AddPeerAsync(new PeerInfo(new Uri($"ipv4://127.0.0.1:{relay.Port}")));
                using var db=AniTDatabase.Create(Path.Combine(root,"db.sqlite"));var file=await db.MediaFiles.OrderBy(f=>f.OnlineFileIndex).LastAsync(token);
                Console.WriteLine($"Seeder complete: {seed.Complete}, path: {seed.Files[0].FullPath}, downloader: {manager.State}");
                if (!seed.Complete) throw new Exception("The synthetic seeder did not find its own generated file");
                using var statsCancellation=CancellationTokenSource.CreateLinkedTokenSource(token);
                var stats=Task.Run(async()=> { while(!statsCancellation.IsCancellationRequested) { Console.WriteLine($"Native torrent: {manager.State} {manager.Progress:0.0}% peers={manager.OpenConnections} rate={manager.Monitor.DownloadRate} error={manager.Error?.Reason}");try {await Task.Delay(3000,statsCancellation.Token);}catch(OperationCanceledException){break;} } });
                var address=await Task.Run(()=>service.OpenStreamAsync(file,token));
                statsCancellation.Cancel(); await stats;
                using var player=new MpcHcPlayer(Path.Combine(AppContext.BaseDirectory,"Player","MPC-HC","mpc-hc64.exe"),true,onlineStreaming:Environment.GetEnvironmentVariable("ANIT_TEST_USE_MPV")=="1",prepareOnlineSeek:service.PrepareSeekIndexAsync);
                if(Environment.GetEnvironmentVariable("ANIT_TEST_EMBEDDED")=="1")player.ConfigureOnlinePlayer=native=>{
                    var surface=new AniT.App.OnlinePlayerWindow(native,"Validação de torrent local autorizado"){ShowActivated=false};surface.Show();native.AttachVideoWindow(surface.VideoHandle);
                };
                player.Diagnostic += (_, message) => Console.WriteLine(message);
                var startup=System.Diagnostics.Stopwatch.StartNew();
                var savedAtEnd=Environment.GetEnvironmentVariable("ANIT_TEST_RESUME_AT_END")=="1";
                var savedPosition=savedAtEnd ? TimeSpan.FromSeconds(double.Parse(Environment.GetEnvironmentVariable("ANIT_TEST_END_POSITION_SECONDS") ?? "210",System.Globalization.CultureInfo.InvariantCulture)) : resume ? TimeSpan.FromSeconds(69.435) : (TimeSpan?)null;
                await player.PlayStreamAsync(address,savedPosition,MpcHcPlaybackOptions.Default,token);
                await WaitForPosition(player,resume && !savedAtEnd ? 70.635 : 1.2,token,savedAtEnd ? 8 : double.PositiveInfinity);
                if(savedAtEnd)Console.WriteLine($"Legacy end checkpoint restarted and advanced from the beginning in {startup.Elapsed.TotalSeconds:0.0}s.");
                var progressAtFirstPlayback=manager.Files[file.OnlineFileIndex].BitField.PercentComplete;
                if(progressAtFirstPlayback>=100)throw new Exception("Test must start before the selected episode finishes downloading");
                if(manager.Files.Where((_,i)=>i!=file.OnlineFileIndex).Any(f=>f.Priority!=Priority.DoNotDownload)) throw new Exception("Another episode was selected while playing");
                if (Environment.GetEnvironmentVariable("ANIT_TEST_STARTUP_ONLY") == "1") {
                    Console.WriteLine($"Online native partial startup passed: selected episode {progressAtFirstPlayback:0.0}% downloaded; other episodes unselected.");
                    await seed.StopAsync(); result=0; return;
                }
                await player.PauseAsync(token);await Task.Delay(300,token);
                if(await player.GetStateAsync(token)!=PlayerState.Paused)throw new Exception("Native pause did not apply");
                if (fixture is not null) {
                    var target=Math.Min(150,(player.Duration?.TotalSeconds ?? 160)-10);
                    Volatile.Write(ref relay.BytesPerSecond,4096);
                    var restoreRate=Task.Run(async()=> { await Task.Delay(3000,token); Volatile.Write(ref relay.BytesPerSecond,1024*1024); });
                    var seeking=System.Diagnostics.Stopwatch.StartNew();
                    var pulses=0;
                    Task seek;
                    seek=player.SeekAsync(TimeSpan.FromSeconds(target),token);
                    while(!seek.IsCompleted) { await Task.WhenAny(seek,Task.Delay(50,token)); pulses++; token.ThrowIfCancellationRequested(); }
                    await seek;
                    if(pulses==0)throw new Exception("Seek should yield the UI while remote data is fetched");
                    player.Resume();
                    await WaitForPosition(player,target+.2,token,target+6);
                    await restoreRate;
                    if(manager.Files[file.OnlineFileIndex].BitField.AllTrue)throw new Exception("Forward seek must complete while the episode is still incomplete");
                    Console.WriteLine($"Forward seek to {target:0}s under throttle passed in {seeking.Elapsed.TotalSeconds:0.0}s; UI pulses={pulses}.");
                }
                await player.SeekAsync(TimeSpan.FromSeconds(12),token);player.Resume();
                await WaitForPosition(player,12.2,token,18);
                Console.WriteLine($"Seek under throttle passed: {manager.Files[file.OnlineFileIndex].BitField.PercentComplete:0.0}% of selected episode downloaded.");
                Volatile.Write(ref relay.BytesPerSecond,0);
                await engine.UpdateSettingsAsync(new EngineSettingsBuilder(engine.Settings) { MaximumDownloadRate=0 }.ToSettings());
                OnlineTransfer snapshot;
                do { await Task.Delay(100,token); snapshot=(await Task.Run(()=>service.SnapshotAsync(token))).Single(); } while(snapshot.Episodes.Single(e=>e.EpisodeId==file.EpisodeId).IsCloud);
                if(fixture is not null && snapshot.Episodes.Where(e=>e.EpisodeId!=file.EpisodeId).Any(e=>!e.IsCloud)) throw new Exception("The other episodes should remain pending");
                await player.SeekAsync(TimeSpan.FromSeconds(20),token);player.Resume();await WaitForPosition(player,20.3,token);
                if(Environment.GetEnvironmentVariable("ANIT_TEST_USE_MPV")=="1") {
                    var closed=new TaskCompletionSource<PlaybackPositionChangedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
                    player.PlaybackClosed+=(_,progress)=>closed.TrySetResult(progress);
                    var owned=(MpvOnlinePlayer)typeof(MpcHcPlayer).GetField("onlinePlayer",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(player)!;
                    try {await owned.CommandAsync(["quit"],token);}catch(IOException){}
                    var last=await closed.Task.WaitAsync(TimeSpan.FromSeconds(5),token);
                    if(!last.IsPositionKnown || last.Position.TotalSeconds<20)throw new Exception("Closing mpv must preserve a confirmed checkpoint");
                    await player.PlayStreamAsync(address,null,new MpcHcPlaybackOptions(1.25,false,"ja","off"),token);
                    await WaitForPosition(player,1.2,token,8);
                    Console.WriteLine("PASS mpv close preserves checkpoint and reopening starts a fresh working session.");
                }
                if(Environment.GetEnvironmentVariable("ANIT_TEST_EMBEDDED")=="1") {
                    player.Dispose();var blocked=new BlockedVideo(File.OpenRead(path));await using var stalled=new OnlineStreamServer(blocked,"waiting.mkv");
                    using var closingPlayer=new MpcHcPlayer(Path.Combine(AppContext.BaseDirectory,"Player","MPC-HC","mpc-hc64.exe"),independent:true,onlineStreaming:true);
                    AniT.App.OnlinePlayerWindow? loadingWindow=null;MpvOnlinePlayer? loadingEngine=null;
                    closingPlayer.ConfigureOnlinePlayer=native=>{loadingEngine=native;loadingWindow=new AniT.App.OnlinePlayerWindow(native,"Teste de cancelamento de abertura"){ShowActivated=false};loadingWindow.Show();native.AttachVideoWindow(loadingWindow.VideoHandle);};
                    var opening=closingPlayer.PlayStreamAsync(stalled.Address,null,MpcHcPlaybackOptions.Default,token);
                    await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(10),token);loadingWindow!.Close();
                    try{await opening;throw new Exception("A closed preparation must not report successful playback");}catch(PlaybackPreparationCancelledException){}
                    if(loadingEngine!.IsRunning)throw new Exception("Canceled preparation left its owned player running");
                    Console.WriteLine("PASS user-close during blocked startup cancels cleanly and terminates owned player.");
                }
                Console.WriteLine($"Online native player passed: play while downloading ({progressAtFirstPlayback:0.0}% of selected episode at first playback), pause, forward seek (MKV), backward seek to 12s under throttle, resume, finish download and keep playing at 20s. Synthetic media only.");
                await seed.StopAsync(); result=0;
            } catch(Exception ex) { Console.Error.WriteLine(ex); }
            finally { window.Close();app.Shutdown(); }
        };
        app.Run(window);return result;
    }
}

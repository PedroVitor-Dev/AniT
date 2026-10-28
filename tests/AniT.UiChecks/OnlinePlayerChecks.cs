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
    static async Task WaitForPosition(MpcHcPlayer player,double minimum,CancellationToken token)
    {
        while(true) {
            token.ThrowIfCancellationRequested();
            try { if((await player.GetPositionAsync(token)).TotalSeconds>=minimum)return; }
            catch(OperationCanceledException)when(!token.IsCancellationRequested) { }
            await Task.Delay(150,token);
        }
    }
    public static int Run(Application app, string? fixture = null, bool resume = false)
    {
        int result = 1;
        var window = new Window { Width = 100, Height = 100, Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        window.Loaded += async (_, _) => {
            try {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(75)); var token = timeout.Token;
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
                var manager=engine.Torrents.Single(); await manager.AddPeerAsync(new PeerInfo(new Uri($"ipv4://127.0.0.1:{port}")));
                using var db=AniTDatabase.Create(Path.Combine(root,"db.sqlite"));var file=await db.MediaFiles.OrderBy(f=>f.OnlineFileIndex).LastAsync(token);
                Console.WriteLine($"Seeder complete: {seed.Complete}, path: {seed.Files[0].FullPath}, downloader: {manager.State}");
                if (!seed.Complete) throw new Exception("The synthetic seeder did not find its own generated file");
                using var statsCancellation=CancellationTokenSource.CreateLinkedTokenSource(token);
                var stats=Task.Run(async()=> { while(!statsCancellation.IsCancellationRequested) { Console.WriteLine($"Native torrent: {manager.State} {manager.Progress:0.0}% peers={manager.OpenConnections} rate={manager.Monitor.DownloadRate} error={manager.Error?.Reason}");try {await Task.Delay(3000,statsCancellation.Token);}catch(OperationCanceledException){break;} } });
                var address=await Task.Run(()=>service.OpenStreamAsync(file,token));
                statsCancellation.Cancel(); await stats;
                using var player=new MpcHcPlayer(Path.Combine(AppContext.BaseDirectory,"Player","MPC-HC","mpc-hc64.exe"),true);
                player.Diagnostic += (_, message) => Console.WriteLine(message);
                await player.PlayStreamAsync(address, resume ? TimeSpan.FromSeconds(69.435) : null, MpcHcPlaybackOptions.Default, token);
                await WaitForPosition(player,resume ? 70.635 : 1.2,token);
                if(manager.Progress>=100)throw new Exception("Test must exercise playback during download");
                await player.PauseAsync(token);await Task.Delay(300,token);
                if(await player.GetStateAsync(token)!=PlayerState.Paused)throw new Exception("Native pause did not apply");
                await player.SeekAsync(TimeSpan.FromSeconds(12),token);player.Resume();
                await WaitForPosition(player,12.2,token);
                var startedAtProgress=manager.Progress;
                await engine.UpdateSettingsAsync(new EngineSettingsBuilder(engine.Settings) { MaximumDownloadRate=0 }.ToSettings());
                OnlineTransfer snapshot;
                do { await Task.Delay(100,token); snapshot=(await Task.Run(()=>service.SnapshotAsync(token))).Single(); } while(snapshot.Episodes.Any(e=>e.IsCloud));
                await player.SeekAsync(TimeSpan.FromSeconds(20),token);player.Resume();await WaitForPosition(player,20.3,token);
                Console.WriteLine($"Online native MPC-HC passed: play while downloading ({startedAtProgress:0.0}%), pause, seek to 12s, resume, finish download and keep playing at 20s. Synthetic media only.");
                await seed.StopAsync(); result=0;
            } catch(Exception ex) { Console.Error.WriteLine(ex); }
            finally { window.Close();app.Shutdown(); }
        };
        app.Run(window);return result;
    }
}

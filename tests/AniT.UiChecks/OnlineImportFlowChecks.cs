using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AniT.App;
using AniT.Infrastructure;
using Microsoft.EntityFrameworkCore;
using MonoTorrent;
using MonoTorrent.Client;

internal static class OnlineImportFlowChecks
{
    public static int Run(Application app)
    {
        var result = 1; var host = new Window { Width = 50, Height = 50, Opacity = 0, ShowInTaskbar = false };
        host.Loaded += async (_, _) =>
        {
            try
            {
                var root = Path.Combine(Path.GetTempPath(), "AniT.Online.Import", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
                var original = Path.Combine(root, "Original"); Directory.CreateDirectory(original);
                foreach (var i in new[] { 12, 2, 1 }) await File.WriteAllBytesAsync(Path.Combine(original, $"[Release] Aozora.S01E{i:00}.1080p.x265.mkv"), new byte[4096]);
                var torrent = await new TorrentCreator { PieceLength = 32768 }.CreateAsync(new TorrentFileSource(original)); var source = Path.Combine(root, "test.torrent"); await File.WriteAllBytesAsync(source, torrent.Encode());
                var database = Path.Combine(root, "db.sqlite");
                await using var service = new OnlineAnimeService(database, Path.Combine(root, "state"), new EngineSettingsBuilder { CacheDirectory = Path.Combine(root, "cache"), DhtEndPoint = null, AllowLocalPeerDiscovery = false, AllowPortForwarding = false }.ToSettings());
                var image = new PngBitmapEncoder(); image.Frames.Add(BitmapFrame.Create(BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgr32, null, new byte[256], 32))); using var bytes = new MemoryStream(); image.Save(bytes);
                using var client = new HttpClient(new MetadataHandler(bytes.ToArray()));
                var flags = BindingFlags.Static | BindingFlags.NonPublic;
                typeof(App).GetField("databasePath", flags)!.SetValue(null, database);
                typeof(App).GetField("online", flags)!.SetValue(null, service);
                typeof(App).GetField("animeCoverProvider", flags)!.SetValue(null, new AnimeCoverProvider(Path.Combine(root, "covers"), client));
                var imported = await App.ImportOnlineAnimeAsync(source, root, new NoopProgress(), CancellationToken.None);
                using var db = AniTDatabase.Create(database); var anime = await db.Anime.SingleAsync();
                if (imported.Title != "Clear Sky" || imported.Episodes != 3 || imported.ArtworkPending) throw new Exception("Import did not resolve canonical title, episodes and artwork: " + imported);
                if (anime.Title != "Clear Sky" || string.IsNullOrWhiteSpace(anime.CoverPath) || !File.Exists(anime.CoverPath)) throw new Exception("Metadata was not persisted before completing");
                if (await db.MediaFiles.AnyAsync(file => !file.IsCloud)) throw new Exception("Import unexpectedly downloaded a video");
                var second = Path.Combine(root, "Second"); Directory.CreateDirectory(second); await File.WriteAllBytesAsync(Path.Combine(second, "Another.Sky.S01E01.mkv"), new byte[4096]);
                var secondTorrent = await new TorrentCreator { PieceLength = 32768 }.CreateAsync(new TorrentFileSource(second)); var secondSource = Path.Combine(root, "second.torrent"); await File.WriteAllBytesAsync(secondSource, secondTorrent.Encode());
                using var cancelled = new CancellationTokenSource();
                var completedWithoutArtwork = await App.ImportOnlineAnimeAsync(secondSource, root, new CommitCancellation(cancelled), cancelled.Token);
                if (!completedWithoutArtwork.ArtworkPending || completedWithoutArtwork.Episodes != 1) throw new Exception("Skipping artwork after commit lost the import");
                var third = Path.Combine(root, "Third"); Directory.CreateDirectory(third); await File.WriteAllBytesAsync(Path.Combine(third, "Aozora.S01E04.mkv"), new byte[4096]);
                var thirdTorrent = await new TorrentCreator { PieceLength = 32768 }.CreateAsync(new TorrentFileSource(third)); var thirdSource = Path.Combine(root, "third.torrent"); await File.WriteAllBytesAsync(thirdSource, thirdTorrent.Encode());
                await App.ImportOnlineAnimeAsync(thirdSource, root, new NoopProgress(), CancellationToken.None);
                using var final = AniTDatabase.Create(database);
                if (await final.Anime.CountAsync() != 2) throw new Exception("Canonical renaming lost the filename alias and duplicated the anime");
                if ((await service.SnapshotAsync()).Any(snapshot => !snapshot.WaitingForSelection)) throw new Exception("Import started a season download");
                Console.WriteLine("PASS online import: isolated torrent -> parsed name -> mocked AniList -> saved cover and canonical title -> ordered library; no video download."); result = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { host.Close(); app.Shutdown(); }
        };
        host.Show(); app.Run(); return result;
    }
    sealed class CommitCancellation(CancellationTokenSource token) : IProgress<OnlineImportProgress> { public void Report(OnlineImportProgress progress) { if (progress.Percent >= 65) token.Cancel(); } }
    sealed class NoopProgress : IProgress<OnlineImportProgress> { public void Report(OnlineImportProgress progress) { } }
    sealed class MetadataHandler(byte[] image) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.Host == "graphql.anilist.co") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""
                {"data":{"Media":{"title":{"romaji":"Aozora","english":"Clear Sky","native":"青空"},"coverImage":{"extraLarge":"https://unit.test/cover.png","large":null},"bannerImage":null,"description":"Uma aventura sobre amizade.","averageScore":80,"genres":["Adventure"],"seasonYear":2026,"studios":{"nodes":[{"name":"Test Studio"}]}}}}
                """, Encoding.UTF8, "application/json") });
            if (request.RequestUri!.Host == "unit.test") { var content = new ByteArrayContent(image); content.Headers.ContentType = new("image/png"); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }); }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}

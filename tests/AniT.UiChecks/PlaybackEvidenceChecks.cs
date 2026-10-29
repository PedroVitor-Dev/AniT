using System.IO;
using System.Reflection;
using AniT.App;
using AniT.Core;
using AniT.Infrastructure;
using AniT.Infrastructure.Achievements;
using Microsoft.EntityFrameworkCore;

internal static class PlaybackEvidenceChecks
{
    public static void Run() => Task.Run(async () => {
        var path = Path.Combine(Path.GetTempPath(), "AniT.PlaybackEvidence", Guid.NewGuid().ToString("N"), "test.db");
        var id = Guid.NewGuid();
        using (var db = AniTDatabase.Create(path)) {
            db.Anime.Add(new Anime { Title = "Autorizado", Seasons = { new Season { Number = 1, Episodes = {
                new Episode { Id = id, Number = 1, Status = WatchStatus.Watching,
                    PlaybackProgress = new PlaybackProgress { EpisodeId = id, PositionSeconds = 69.435, DurationSeconds = 1421 } }
            } } } });
            await db.SaveChangesAsync();
        }
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        typeof(App).GetField("databasePath",flags)!.SetValue(null,path);
        typeof(App).GetField("activeEpisodeId",flags)!.SetValue(null,id);
        typeof(App).GetField("activePlaybackIsOnlineStream",flags)!.SetValue(null,true);
        typeof(App).GetField("activePlaybackSettings",flags)!.SetValue(null,AniTSystemSettings.Default);
        typeof(App).GetField("lastProgressWrite",flags)!.SetValue(null,DateTimeOffset.UtcNow.AddMinutes(-3));
        typeof(App).GetProperty("Achievements")!.SetValue(null,new AchievementService(()=>AniTDatabase.Create(path)));
        var persist = typeof(App).GetMethod("PersistProgressAsync",flags)!;
        await (Task)persist.Invoke(null,new object[]{new PlaybackPositionChangedEventArgs(TimeSpan.Zero,null,false),true})!;
        using (var db = AniTDatabase.Create(path)) {
            if ((await db.PlaybackProgresses.SingleAsync()).PositionSeconds != 69.435) throw new Exception("Unknown close overwrote resume point");
        }
        await (Task)typeof(App).GetMethod("CompleteActiveEpisodeAsync",flags)!.Invoke(null,null)!;
        using (var db = AniTDatabase.Create(path)) {
            if ((await db.Episodes.SingleAsync()).Status != WatchStatus.Watching) throw new Exception("Unconfirmed EOS completed episode");
        }
        await (Task)persist.Invoke(null,new object[]{new PlaybackPositionChangedEventArgs(TimeSpan.Zero,null),true})!;
        using (var db = AniTDatabase.Create(path)) {
            var saved=await db.PlaybackProgresses.SingleAsync();
            if (saved.PositionSeconds != 0 || saved.DurationSeconds != 1421) throw new Exception("Reported seek to zero was replaced by elapsed time");
            if (await db.AchievementMetrics.AnyAsync()) throw new Exception("Unplayed buffering time generated achievement metrics");
        }
        Console.WriteLine("PASS: unknown position preserves resume; unconfirmed EOF does not complete; reported zero stays zero; no buffering watch time or achievements.");
    }).GetAwaiter().GetResult();
}

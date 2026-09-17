using AniT.Core;
using AniT.Infrastructure;
using AniT.Infrastructure.Achievements;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniT.Tests;

public sealed class AchievementPersistenceTests
{
    [Fact]
    public async Task LegacyDatabase_GainsAchievementSchemaAndKeepsUnlocksIdempotent()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AniT.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "achievements.db");

        try
        {
            using (var context = AniTDatabase.Create(databasePath))
            {
                context.Anime.Add(new Anime
                {
                    Title = "Teste de jornada",
                    Seasons =
                    {
                        new Season
                        {
                            Number = 1,
                            Episodes =
                            {
                                new Episode
                                {
                                    Number = 1,
                                    Status = WatchStatus.Completed,
                                    WatchedAt = DateTimeOffset.Now,
                                    PlaybackProgress = new PlaybackProgress
                                    {
                                        PositionSeconds = 24 * 60,
                                        DurationSeconds = 24 * 60,
                                        LastPlayedAt = DateTimeOffset.Now
                                    }
                                }
                            }
                        }
                    }
                });
                context.SaveChanges();
            }

            var service = new AchievementService(() => AniTDatabase.Create(databasePath));
            var first = await service.RecalculateAsync();
            var second = await service.RecalculateAsync();

            Assert.Contains(first, item => item.Definition.Id == 1 && item.IsUnlocked);
            Assert.Contains(second, item => item.Definition.Id == 1 && item.IsUnlocked);

            using var reopened = AniTDatabase.Create(databasePath);
            Assert.Equal(100, await reopened.UserAchievements.CountAsync());
            Assert.Equal(1, await reopened.AchievementHistory.CountAsync(item => item.AchievementId == 1));

            var connection = reopened.Database.GetDbConnection();
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Version FROM AniTSchemaInfo WHERE Id = 1;";
            Assert.Equal(4, Convert.ToInt32(command.ExecuteScalar()));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}

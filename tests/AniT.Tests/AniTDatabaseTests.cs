using AniT.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniT.Tests;

public sealed class AniTDatabaseTests
{
    [Fact]
    public void Create_AddsMetadataAndReviewColumnsToExistingDatabase()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AniT.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "legacy.db");

        try
        {
            using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE Anime (Id TEXT NOT NULL PRIMARY KEY, Title TEXT NOT NULL);";
                command.ExecuteNonQuery();
            }

            using var context = AniTDatabase.Create(databasePath);
            var migratedConnection = context.Database.GetDbConnection();
            migratedConnection.Open();
            using var checkCommand = migratedConnection.CreateCommand();
            checkCommand.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Anime') WHERE name IN ('EnglishTitle', 'CriticScore', 'ReviewNotes');";

            Assert.Equal(3, Convert.ToInt32(checkCommand.ExecuteScalar()));
            migratedConnection.Close();
            context.Dispose();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Create_PersistsPersonalReviewAndPublicScore()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AniT.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "reviews.db");
        var animeId = Guid.NewGuid();

        try
        {
            using (var context = AniTDatabase.Create(databasePath))
            {
                context.Anime.Add(new global::AniT.Core.Anime
                {
                    Id = animeId,
                    Title = "Frieren",
                    CriticScore = 89,
                    Rating = 9.5,
                    ReviewNotes = "Uma jornada muito bonita."
                });
                context.SaveChanges();
            }

            using var reopenedContext = AniTDatabase.Create(databasePath);
            var savedAnime = reopenedContext.Anime.Single(item => item.Id == animeId);
            Assert.Equal(89, savedAnime.CriticScore);
            Assert.Equal(9.5, savedAnime.Rating);
            Assert.Equal("Uma jornada muito bonita.", savedAnime.ReviewNotes);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}

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
                command.CommandText = """
                    CREATE TABLE Anime (Id TEXT NOT NULL PRIMARY KEY, Title TEXT NOT NULL);
                    CREATE TABLE Episodes (Id TEXT NOT NULL PRIMARY KEY);
                    """;
                command.ExecuteNonQuery();
            }

            using var context = AniTDatabase.Create(databasePath);
            var migratedConnection = context.Database.GetDbConnection();
            migratedConnection.Open();
            using var checkCommand = migratedConnection.CreateCommand();
            checkCommand.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Anime') WHERE name IN ('EnglishTitle', 'CriticScore', 'ReviewNotes');";

            Assert.Equal(3, Convert.ToInt32(checkCommand.ExecuteScalar()));
            checkCommand.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Episodes') WHERE name = 'ReviewNotes';";
            Assert.Equal(1, Convert.ToInt32(checkCommand.ExecuteScalar()));
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
    public void Create_PersistsEpisodeReviewAndPublicAnimeScore()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AniT.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "reviews.db");
        var animeId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();

        try
        {
            using (var context = AniTDatabase.Create(databasePath))
            {
                var anime = new global::AniT.Core.Anime
                {
                    Id = animeId,
                    Title = "Frieren",
                    CriticScore = 89
                };
                anime.Seasons.Add(new global::AniT.Core.Season
                {
                    Number = 1,
                    Episodes =
                    {
                        new global::AniT.Core.Episode
                        {
                            Id = episodeId,
                            Number = 1,
                            Rating = 5,
                            ReviewNotes = "Um episódio muito bonito."
                        }
                    }
                });
                context.Anime.Add(anime);
                context.SaveChanges();
            }

            using var reopenedContext = AniTDatabase.Create(databasePath);
            var savedAnime = reopenedContext.Anime.Single(item => item.Id == animeId);
            var savedEpisode = reopenedContext.Episodes.Single(item => item.Id == episodeId);
            Assert.Equal(89, savedAnime.CriticScore);
            Assert.Equal(5, savedEpisode.Rating);
            Assert.Equal("Um episódio muito bonito.", savedEpisode.ReviewNotes);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Create_MigratesLegacyOneToOneMediaFileWithoutLosingItsEpisodeLink()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AniT.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "legacy-files.db");
        var mediaFileId = Guid.NewGuid().ToString();
        var episodeId = Guid.NewGuid().ToString();
        var rootId = Guid.NewGuid().ToString();
        try
        {
            using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = $"""
                    CREATE TABLE Anime (Id TEXT NOT NULL PRIMARY KEY, Title TEXT NOT NULL);
                    CREATE TABLE Episodes (Id TEXT NOT NULL PRIMARY KEY, ReviewNotes TEXT NULL);
                    CREATE TABLE LibraryRoots (Id TEXT NOT NULL PRIMARY KEY, Path TEXT NOT NULL, DisplayName TEXT NOT NULL, AddedAt TEXT NOT NULL);
                    CREATE TABLE MediaFiles (
                        Id TEXT NOT NULL PRIMARY KEY,
                        EpisodeId TEXT NOT NULL,
                        LibraryRootId TEXT NOT NULL,
                        RelativePath TEXT NOT NULL,
                        SizeInBytes INTEGER NOT NULL,
                        LastModifiedAt TEXT NOT NULL,
                        QuickHash TEXT NULL
                    );
                    CREATE UNIQUE INDEX IX_MediaFiles_EpisodeId ON MediaFiles (EpisodeId);
                    INSERT INTO Episodes (Id) VALUES ('{episodeId}');
                    INSERT INTO LibraryRoots (Id, Path, DisplayName, AddedAt) VALUES ('{rootId}', 'D:\\Anime', 'Anime', '2025-01-01');
                    INSERT INTO MediaFiles (Id, EpisodeId, LibraryRootId, RelativePath, SizeInBytes, LastModifiedAt)
                    VALUES ('{mediaFileId}', '{episodeId}', '{rootId}', 'Frieren 01.mkv', 42, '2025-01-01');
                    """;
                command.ExecuteNonQuery();
            }

            using var context = AniTDatabase.Create(databasePath);
            var connectionAfter = context.Database.GetDbConnection();
            connectionAfter.Open();
            using var check = connectionAfter.CreateCommand();
            check.CommandText = $"SELECT COUNT(*) FROM MediaFiles WHERE Id = '{mediaFileId}' AND EpisodeId = '{episodeId}';";
            Assert.Equal(1, Convert.ToInt32(check.ExecuteScalar()));
            check.CommandText = $"SELECT IsPreferred FROM MediaFiles WHERE Id = '{mediaFileId}';";
            Assert.Equal(1L, Convert.ToInt64(check.ExecuteScalar()));
            check.CommandText = "SELECT [unique] FROM pragma_index_list('MediaFiles') WHERE name = 'IX_MediaFiles_EpisodeId';";
            Assert.Equal(0L, Convert.ToInt64(check.ExecuteScalar()));
            Assert.True(File.Exists(databasePath + ".pre-smart-library.bak"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}

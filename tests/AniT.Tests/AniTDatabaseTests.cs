using AniT.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniT.Tests;

public sealed class AniTDatabaseTests
{
    [Fact]
    public void Create_AddsEnglishTitleColumnToExistingDatabase()
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
            checkCommand.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Anime') WHERE name = 'EnglishTitle';";

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
}

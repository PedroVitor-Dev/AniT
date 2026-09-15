using System.Data;
using Microsoft.EntityFrameworkCore;

namespace AniT.Infrastructure;

public static class AniTDatabase
{
    public static AniTDbContext Create(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var options = new DbContextOptionsBuilder<AniTDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        var context = new AniTDbContext(options);
        context.Database.EnsureCreated();
        EnsureAnimeColumn(context, "EnglishTitle", "TEXT NULL");
        EnsureAnimeColumn(context, "CriticScore", "REAL NULL");
        EnsureAnimeColumn(context, "ReviewNotes", "TEXT NULL");
        return context;
    }

    private static void EnsureAnimeColumn(AniTDbContext context, string columnName, string definition)
    {
        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) connection.Open();
        try
        {
            using var checkCommand = connection.CreateCommand();
            checkCommand.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Anime') WHERE name = $columnName;";
            var parameter = checkCommand.CreateParameter();
            parameter.ParameterName = "$columnName";
            parameter.Value = columnName;
            checkCommand.Parameters.Add(parameter);
            var columnExists = Convert.ToInt32(checkCommand.ExecuteScalar()) > 0;
            if (columnExists) return;

            using var migrationCommand = connection.CreateCommand();
            migrationCommand.CommandText = $"ALTER TABLE Anime ADD COLUMN {columnName} {definition};";
            migrationCommand.ExecuteNonQuery();
        }
        finally
        {
            if (shouldClose) connection.Close();
        }
    }
}

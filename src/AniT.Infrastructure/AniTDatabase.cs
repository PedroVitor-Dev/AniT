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
        EnsureColumn(context, "Anime", "EnglishTitle", "TEXT NULL");
        EnsureColumn(context, "Anime", "CriticScore", "REAL NULL");
        EnsureColumn(context, "Anime", "ReviewNotes", "TEXT NULL");
        EnsureColumn(context, "Episodes", "ReviewNotes", "TEXT NULL");
        return context;
    }

    private static void EnsureColumn(AniTDbContext context, string tableName, string columnName, string definition)
    {
        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) connection.Open();
        try
        {
            using var tableCommand = connection.CreateCommand();
            tableCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $tableName;";
            var tableParameter = tableCommand.CreateParameter();
            tableParameter.ParameterName = "$tableName";
            tableParameter.Value = tableName;
            tableCommand.Parameters.Add(tableParameter);
            if (Convert.ToInt32(tableCommand.ExecuteScalar()) == 0) return;

            using var checkCommand = connection.CreateCommand();
            checkCommand.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{tableName}') WHERE name = $columnName;";
            var parameter = checkCommand.CreateParameter();
            parameter.ParameterName = "$columnName";
            parameter.Value = columnName;
            checkCommand.Parameters.Add(parameter);
            var columnExists = Convert.ToInt32(checkCommand.ExecuteScalar()) > 0;
            if (columnExists) return;

            using var migrationCommand = connection.CreateCommand();
            migrationCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};";
            migrationCommand.ExecuteNonQuery();
        }
        finally
        {
            if (shouldClose) connection.Close();
        }
    }
}

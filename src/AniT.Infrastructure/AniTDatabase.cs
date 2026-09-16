using System.Data;
using Microsoft.EntityFrameworkCore;

namespace AniT.Infrastructure;

public static class AniTDatabase
{
    private const int CurrentSchemaVersion = 3;

    public static AniTDbContext Create(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        CreateLegacyBackupOnce(databasePath);
        var options = new DbContextOptionsBuilder<AniTDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        var context = new AniTDbContext(options);
        context.Database.EnsureCreated();
        EnsureLegacyColumns(context);
        EnsureSmartLibrarySchema(context);
        return context;
    }

    private static void CreateLegacyBackupOnce(string databasePath)
    {
        if (!File.Exists(databasePath) || new FileInfo(databasePath).Length == 0) return;
        var backupPath = databasePath + ".pre-smart-library.bak";
        if (!File.Exists(backupPath)) File.Copy(databasePath, backupPath, overwrite: false);
    }

    private static void EnsureLegacyColumns(AniTDbContext context)
    {
        EnsureColumn(context, "Anime", "EnglishTitle", "TEXT NULL");
        EnsureColumn(context, "Anime", "CriticScore", "REAL NULL");
        EnsureColumn(context, "Anime", "ReviewNotes", "TEXT NULL");
        EnsureColumn(context, "Episodes", "ReviewNotes", "TEXT NULL");
    }

    private static void EnsureSmartLibrarySchema(AniTDbContext context)
    {
        if (GetSchemaVersion(context) >= CurrentSchemaVersion) return;
        EnsureColumn(context, "LibraryRoots", "IncludeSubfolders", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(context, "LibraryRoots", "IsEnabled", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(context, "LibraryRoots", "LastScanAt", "TEXT NULL");
        EnsureColumn(context, "LibraryRoots", "LastUnavailableAt", "TEXT NULL");

        EnsureColumn(context, "MediaFiles", "FileName", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(context, "MediaFiles", "Extension", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(context, "MediaFiles", "Resolution", "TEXT NULL");
        EnsureColumn(context, "MediaFiles", "ReleaseGroup", "TEXT NULL");
        EnsureColumn(context, "MediaFiles", "Language", "TEXT NULL");
        EnsureColumn(context, "MediaFiles", "ImportedAt", "TEXT NOT NULL DEFAULT '0001-01-01T00:00:00+00:00'");
        EnsureColumn(context, "MediaFiles", "LastSeenAt", "TEXT NOT NULL DEFAULT '0001-01-01T00:00:00+00:00'");
        EnsureColumn(context, "MediaFiles", "MissingSince", "TEXT NULL");
        EnsureColumn(context, "MediaFiles", "Availability", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(context, "MediaFiles", "IsPreferred", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(context, "MediaFiles", "DuplicateOfMediaFileId", "TEXT NULL");

        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) connection.Open();
        try
        {
            using var transaction = connection.BeginTransaction();
            Execute(connection, transaction, """
                CREATE TABLE IF NOT EXISTS AnimeAliases (
                    Id TEXT NOT NULL PRIMARY KEY,
                    AnimeId TEXT NOT NULL,
                    Alias TEXT NOT NULL,
                    NormalizedAlias TEXT NOT NULL,
                    Source INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    FOREIGN KEY (AnimeId) REFERENCES Anime (Id) ON DELETE CASCADE
                );
                """);
            Execute(connection, transaction, """
                CREATE TABLE IF NOT EXISTS LibraryReviewItems (
                    Id TEXT NOT NULL PRIMARY KEY,
                    LibraryRootId TEXT NOT NULL,
                    RelativePath TEXT NOT NULL,
                    FileName TEXT NOT NULL,
                    SizeInBytes INTEGER NOT NULL,
                    LastModifiedAt TEXT NOT NULL,
                    QuickHash TEXT NULL,
                    CandidateTitle TEXT NULL,
                    NormalizedTitle TEXT NULL,
                    SuggestedSeasonNumber INTEGER NULL,
                    SuggestedEpisodeNumber REAL NULL,
                    SuggestedAnimeId TEXT NULL,
                    Confidence REAL NOT NULL,
                    Reasons TEXT NOT NULL,
                    Resolution TEXT NULL,
                    ReleaseGroup TEXT NULL,
                    Language TEXT NULL,
                    Status INTEGER NOT NULL,
                    DiscoveredAt TEXT NOT NULL,
                    LastSeenAt TEXT NOT NULL,
                    FOREIGN KEY (LibraryRootId) REFERENCES LibraryRoots (Id) ON DELETE CASCADE
                );
                """);
            if (TableExists(connection, transaction, "MediaFiles"))
            {
                Execute(connection, transaction, "UPDATE MediaFiles SET IsPreferred = 1 WHERE EpisodeId IN (SELECT EpisodeId FROM MediaFiles GROUP BY EpisodeId HAVING COUNT(*) = 1);");
                Execute(connection, transaction, "UPDATE MediaFiles SET ImportedAt = LastModifiedAt WHERE ImportedAt = '0001-01-01T00:00:00+00:00';");
                Execute(connection, transaction, "UPDATE MediaFiles SET LastSeenAt = LastModifiedAt WHERE LastSeenAt = '0001-01-01T00:00:00+00:00';");
                Execute(connection, transaction, "DROP INDEX IF EXISTS IX_MediaFiles_EpisodeId;");
                Execute(connection, transaction, "CREATE INDEX IF NOT EXISTS IX_MediaFiles_EpisodeId ON MediaFiles (EpisodeId);");
                Execute(connection, transaction, "CREATE INDEX IF NOT EXISTS IX_MediaFiles_QuickHash ON MediaFiles (QuickHash);");
                Execute(connection, transaction, "CREATE UNIQUE INDEX IF NOT EXISTS IX_MediaFiles_LibraryRootId_RelativePath ON MediaFiles (LibraryRootId, RelativePath);");
            }
            Execute(connection, transaction, "CREATE INDEX IF NOT EXISTS IX_AnimeAliases_NormalizedAlias ON AnimeAliases (NormalizedAlias);");
            Execute(connection, transaction, "CREATE UNIQUE INDEX IF NOT EXISTS IX_AnimeAliases_AnimeId_NormalizedAlias ON AnimeAliases (AnimeId, NormalizedAlias);");
            Execute(connection, transaction, "CREATE UNIQUE INDEX IF NOT EXISTS IX_LibraryReviewItems_LibraryRootId_RelativePath ON LibraryReviewItems (LibraryRootId, RelativePath);");
            Execute(connection, transaction, "CREATE INDEX IF NOT EXISTS IX_LibraryReviewItems_QuickHash ON LibraryReviewItems (QuickHash);");
            Execute(connection, transaction, "CREATE TABLE IF NOT EXISTS AniTSchemaInfo (Id INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1), Version INTEGER NOT NULL);");
            Execute(connection, transaction, $"INSERT INTO AniTSchemaInfo (Id, Version) VALUES (1, {CurrentSchemaVersion}) ON CONFLICT(Id) DO UPDATE SET Version = excluded.Version;");
            transaction.Commit();
        }
        finally
        {
            if (shouldClose) connection.Close();
        }
    }

    private static int GetSchemaVersion(AniTDbContext context)
    {
        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) connection.Open();
        try
        {
            using var exists = connection.CreateCommand();
            exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'AniTSchemaInfo';";
            if (Convert.ToInt32(exists.ExecuteScalar()) == 0) return 0;
            using var version = connection.CreateCommand();
            version.CommandText = "SELECT Version FROM AniTSchemaInfo WHERE Id = 1;";
            return Convert.ToInt32(version.ExecuteScalar() ?? 0);
        }
        finally
        {
            if (shouldClose) connection.Close();
        }
    }

    private static void Execute(System.Data.Common.DbConnection connection, System.Data.Common.DbTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static bool TableExists(System.Data.Common.DbConnection connection, System.Data.Common.DbTransaction transaction, string tableName)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $tableName;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$tableName";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
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
            if (Convert.ToInt32(checkCommand.ExecuteScalar()) > 0) return;

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

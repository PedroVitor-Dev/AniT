using Microsoft.Data.Sqlite;

namespace AniT.Infrastructure;

public sealed record AniTDatabaseDiagnostic(
    bool IsHealthy,
    string IntegrityResult,
    int ForeignKeyProblems,
    long DatabaseSizeBytes,
    DateTimeOffset CheckedAtUtc,
    bool RepairWasRun = false);

public static class AniTDatabaseMaintenance
{
    public static async Task<AniTDatabaseDiagnostic> DiagnoseAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        databasePath = Path.GetFullPath(databasePath);
        if (!File.Exists(databasePath)) throw new FileNotFoundException("O banco de dados do AniT não foi encontrado.", databasePath);

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        return await DiagnoseConnectionAsync(connection, databasePath, false, cancellationToken);
    }

    public static async Task<AniTDatabaseDiagnostic> RepairAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        databasePath = Path.GetFullPath(databasePath);
        if (!File.Exists(databasePath)) throw new FileNotFoundException("O banco de dados do AniT não foi encontrado.", databasePath);

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "PRAGMA wal_checkpoint(TRUNCATE);", cancellationToken);
        await ExecuteAsync(connection, "REINDEX;", cancellationToken);
        await ExecuteAsync(connection, "PRAGMA optimize;", cancellationToken);
        await ExecuteAsync(connection, "VACUUM;", cancellationToken);
        return await DiagnoseConnectionAsync(connection, databasePath, true, cancellationToken);
    }

    private static async Task<AniTDatabaseDiagnostic> DiagnoseConnectionAsync(
        SqliteConnection connection,
        string databasePath,
        bool repaired,
        CancellationToken cancellationToken)
    {
        await using var integrityCommand = connection.CreateCommand();
        integrityCommand.CommandText = "PRAGMA integrity_check;";
        var integrityRows = new List<string>();
        await using (var reader = await integrityCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken)) integrityRows.Add(reader.GetString(0));
        }

        await using var foreignKeyCommand = connection.CreateCommand();
        foreignKeyCommand.CommandText = "PRAGMA foreign_key_check;";
        var foreignKeyProblems = 0;
        await using (var reader = await foreignKeyCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken)) foreignKeyProblems++;
        }

        var integrity = integrityRows.Count == 0 ? "sem resposta" : string.Join("; ", integrityRows.Take(5));
        var healthy = integrityRows.Count == 1
                      && string.Equals(integrityRows[0], "ok", StringComparison.OrdinalIgnoreCase)
                      && foreignKeyProblems == 0;
        return new AniTDatabaseDiagnostic(healthy, integrity, foreignKeyProblems,
            new FileInfo(databasePath).Length, DateTimeOffset.UtcNow, repaired);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

using Microsoft.Data.Sqlite;

var databasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AniT", "Data", "anit.db");
await using var connection = new SqliteConnection($"Data Source={databasePath}");
await connection.OpenAsync();
await using var command = connection.CreateCommand();
command.CommandText = """
    SELECT e.Id, e.Number, e.Status, p.PositionSeconds, p.DurationSeconds, p.LastPlayedAt
    FROM Episodes e
    LEFT JOIN PlaybackProgresses p ON p.EpisodeId = e.Id
    ORDER BY p.LastPlayedAt DESC;
    """;
await using var reader = await command.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    Console.WriteLine($"episode={reader.GetString(0)} number={reader.GetInt32(1)} status={reader.GetInt32(2)} position={(reader.IsDBNull(3) ? "<null>" : reader.GetDouble(3))} duration={(reader.IsDBNull(4) ? "<null>" : reader.GetDouble(4))} playedAt={(reader.IsDBNull(5) ? "<null>" : reader.GetString(5))}");
}

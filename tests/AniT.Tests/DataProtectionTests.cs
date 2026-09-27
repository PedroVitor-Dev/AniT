using AniT.Infrastructure;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace AniT.Tests;

public sealed class DataProtectionTests
{
    [Fact]
    public void DataBackupPreferences_RoundTripWithoutLosingAutomationChoices()
    {
        var expected = AniTSystemSettings.Default with
        {
            AutomaticBackupEnabled = true,
            AutomaticBackupFrequency = AutomaticBackupFrequency.Weekly,
            AutomaticBackupRetention = 12,
            AutomaticBackupDirectory = Path.Combine(Path.GetTempPath(), "AniT Backups"),
            PersonalDataExportFormat = PersonalDataExportFormat.Csv,
            PrepareFutureSync = true
        };

        var actual = JsonSerializer.Deserialize<AniTSystemSettings>(JsonSerializer.Serialize(expected));

        Assert.NotNull(actual);
        Assert.True(actual.AutomaticBackupEnabled);
        Assert.Equal(AutomaticBackupFrequency.Weekly, actual.AutomaticBackupFrequency);
        Assert.Equal(12, actual.AutomaticBackupRetention);
        Assert.Equal(expected.AutomaticBackupDirectory, actual.AutomaticBackupDirectory);
        Assert.Equal(PersonalDataExportFormat.Csv, actual.PersonalDataExportFormat);
        Assert.True(actual.PrepareFutureSync);
    }

    [Theory]
    [InlineData(AutomaticBackupFrequency.Daily, 23, false)]
    [InlineData(AutomaticBackupFrequency.Daily, 24, true)]
    [InlineData(AutomaticBackupFrequency.Weekly, 167, false)]
    [InlineData(AutomaticBackupFrequency.Weekly, 168, true)]
    [InlineData(AutomaticBackupFrequency.Monthly, 719, false)]
    [InlineData(AutomaticBackupFrequency.Monthly, 720, true)]
    public void AutomaticBackupSchedule_OnlyRunsWhenFrequencyIsDue(AutomaticBackupFrequency frequency, int elapsedHours, bool expected)
    {
        var now = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, AutomaticBackupService.IsDue(frequency, now.AddHours(-elapsedHours), now));
    }

    [Fact]
    public async Task DatabaseMaintenance_DiagnosesAndOptimizesHealthyDatabase()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AniT.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "maintenance.db");
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE Sample (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL); INSERT INTO Sample(Name) VALUES ('AniT');";
                await command.ExecuteNonQueryAsync();
            }

            var diagnostic = await AniTDatabaseMaintenance.DiagnoseAsync(databasePath);
            var repaired = await AniTDatabaseMaintenance.RepairAsync(databasePath);

            Assert.True(diagnostic.IsHealthy);
            Assert.True(repaired.IsHealthy);
            Assert.True(repaired.RepairWasRun);
            Assert.Equal(0, repaired.ForeignKeyProblems);
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch { }
        }
    }
}

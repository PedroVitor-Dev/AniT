using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using AniT.Core;
using AniT.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniT.Tests;

public sealed class AniTBackupServiceTests
{
    [Fact]
    public async Task ExportAndRestore_PreservesStateArtworkAndRebasesManagedPaths()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "AniTBackupTests", Guid.NewGuid().ToString("N"));
        var sourceRoot = Path.Combine(testRoot, "source", "AniT");
        var targetRoot = Path.Combine(testRoot, "target", "AniT");
        var archivePath = Path.Combine(testRoot, "journey.anitbackup");
        try
        {
            var sourceDatabasePath = Path.Combine(sourceRoot, "Data", "anit.db");
            var sourceCover = Path.Combine(sourceRoot, "Covers", "cutey.jpg");
            Directory.CreateDirectory(Path.GetDirectoryName(sourceCover)!);
            await File.WriteAllBytesAsync(sourceCover, [1, 2, 3, 4, 5]);
            Directory.CreateDirectory(Path.Combine(sourceRoot, "Data"));
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "Data", "profile.json"), $$"""
                { "DisplayName": "Ratão", "AvatarPath": "{{sourceCover.Replace("\\", "\\\\")}}", "BannerPath": null }
                """);
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "Data", "profiles.json"), $$"""
                { "ActiveProfileId": "00000000-0000-0000-0000-000000000001", "Profiles": [ { "ProfileId": "00000000-0000-0000-0000-000000000001", "DisplayName": "Ratão", "AvatarPath": "{{sourceCover.Replace("\\", "\\\\")}}" } ] }
                """);
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "Data", "notification-state.json"), """
                { "LastWeeklySummaryAt": "2026-09-21T09:00:00-03:00" }
                """);
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "Data", "automatic-backup-state.json"), """
                { "LastSuccessfulBackupUtc": "2026-09-21T09:00:00Z", "BackupPath": "C:\\old\\backup.anitbackup" }
                """);
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, "Data", "system-settings.json"), $$"""
                { "ArtworkDirectory": "{{sourceRoot.Replace("\\", "\\\\")}}\\Covers", "AutomaticBackupDirectory": "C:\\Users\\old\\Documents\\AniT Backups" }
                """);
            var secondaryProfileDatabase = Path.Combine(sourceRoot, "Data", "Profiles", Guid.NewGuid().ToString("N"), "anit.db");
            Directory.CreateDirectory(Path.GetDirectoryName(secondaryProfileDatabase)!);
            await File.WriteAllBytesAsync(secondaryProfileDatabase, [7, 4, 2, 9]);
            await using (var source = AniTDatabase.Create(sourceDatabasePath))
            {
                var anime = new Anime { Title = "Cutey Honey", CoverPath = sourceCover, IsFavorite = true };
                var season = new Season { Anime = anime, AnimeId = anime.Id, Number = 1 };
                season.Episodes.Add(new Episode { Season = season, SeasonId = season.Id, Number = 1, Status = WatchStatus.Watching, Rating = 5 });
                anime.Seasons.Add(season);
                source.Anime.Add(anime);
                await source.SaveChangesAsync();
            }

            var service = new AniTBackupService();
            var manifest = await service.ExportAsync(sourceRoot, sourceDatabasePath, archivePath, "1.0-test");
            Assert.Equal(1, manifest.Summary.AnimeCount);
            Assert.Equal(1, manifest.Summary.EpisodeCount);
            Assert.Equal(1, manifest.Summary.WatchedEpisodeCount);
            Assert.True(manifest.IncludesArtwork);
            Assert.False(manifest.IncludesMediaFiles);

            var inspection = await service.InspectAsync(archivePath);
            Assert.Equal("AniT Portable Backup", inspection.Manifest.Format);
            Assert.True(inspection.ArchiveSize > 0);

            var targetDatabasePath = Path.Combine(targetRoot, "Data", "anit.db");
            await using var targetContext = AniTDatabase.Create(targetDatabasePath);
            targetContext.Anime.Add(new Anime { Title = "Será substituído" });
            await targetContext.SaveChangesAsync();
            var result = await service.RestoreAsync(
                archivePath,
                targetRoot,
                targetDatabasePath,
                "1.0-test",
                async () => { await targetContext.DisposeAsync(); });

            Assert.True(File.Exists(result.RecoveryBackupPath));
            await using (var restored = AniTDatabase.Create(targetDatabasePath))
            {
                var restoredAnime = await restored.Anime.SingleAsync();
                Assert.Equal("Cutey Honey", restoredAnime.Title);
                Assert.Equal(Path.Combine(targetRoot, "Covers", "cutey.jpg"), restoredAnime.CoverPath);
                Assert.True(File.Exists(restoredAnime.CoverPath));
            }
            Assert.Contains(targetRoot.Replace("\\", "\\\\"), await File.ReadAllTextAsync(Path.Combine(targetRoot, "Data", "profile.json")));
            Assert.Contains(targetRoot.Replace("\\", "\\\\"), await File.ReadAllTextAsync(Path.Combine(targetRoot, "Data", "profiles.json")));
            Assert.Contains("2026-09-21", await File.ReadAllTextAsync(Path.Combine(targetRoot, "Data", "notification-state.json")));
            Assert.False(File.Exists(Path.Combine(targetRoot, "Data", "automatic-backup-state.json")));
            var restoredSettings = await File.ReadAllTextAsync(Path.Combine(targetRoot, "Data", "system-settings.json"));
            Assert.Contains(targetRoot.Replace("\\", "\\\\"), restoredSettings);
            Assert.Contains("AniT Backups", restoredSettings);
            Assert.DoesNotContain("Users\\\\old", restoredSettings);
            Assert.Single(Directory.EnumerateFiles(Path.Combine(targetRoot, "Data", "Profiles"), "anit.db", SearchOption.AllDirectories));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Inspect_RejectsAnArchiveWhosePayloadWasChanged()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "AniTBackupTests", Guid.NewGuid().ToString("N"));
        var sourceRoot = Path.Combine(testRoot, "source", "AniT");
        var databasePath = Path.Combine(sourceRoot, "Data", "anit.db");
        var archivePath = Path.Combine(testRoot, "tampered.anitbackup");
        try
        {
            await using (var context = AniTDatabase.Create(databasePath))
            {
                context.Anime.Add(new Anime { Title = "Original" });
                await context.SaveChangesAsync();
            }
            var service = new AniTBackupService();
            await service.ExportAsync(sourceRoot, databasePath, archivePath, "1.0-test");

            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update))
            {
                var entry = archive.GetEntry("payload/Data/anit.db")!;
                entry.Delete();
                var replacement = archive.CreateEntry("payload/Data/anit.db");
                await using var stream = replacement.Open();
                await stream.WriteAsync(new byte[] { 9, 8, 7, 6 });
            }

            var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.InspectAsync(archivePath));
            Assert.Contains("backup", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Inspect_RejectsArchivePathTraversal()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "AniTBackupTests", Guid.NewGuid().ToString("N"));
        var dataRoot = Path.Combine(testRoot, "AniT");
        var databasePath = Path.Combine(dataRoot, "Data", "anit.db");
        var archivePath = Path.Combine(testRoot, "traversal.anitbackup");
        try
        {
            await using (var context = AniTDatabase.Create(databasePath))
            {
                context.Anime.Add(new Anime { Title = "Safe" });
                await context.SaveChangesAsync();
            }
            var service = new AniTBackupService();
            await service.ExportAsync(dataRoot, databasePath, archivePath, "1.0-test");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update))
            {
                var entry = archive.CreateEntry("../outside.txt");
                await using var stream = entry.Open();
                await stream.WriteAsync(new byte[] { 1 });
            }

            var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.InspectAsync(archivePath));
            Assert.Contains("inseguro", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(testRoot, "outside.txt")));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Inspect_RejectsDuplicateManifest()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "AniTBackupTests", Guid.NewGuid().ToString("N"));
        var dataRoot = Path.Combine(testRoot, "AniT");
        var databasePath = Path.Combine(dataRoot, "Data", "anit.db");
        var archivePath = Path.Combine(testRoot, "duplicate-manifest.anitbackup");
        try
        {
            await using (var context = AniTDatabase.Create(databasePath))
            {
                context.Anime.Add(new Anime { Title = "Safe" });
                await context.SaveChangesAsync();
            }
            var service = new AniTBackupService();
            await service.ExportAsync(dataRoot, databasePath, archivePath, "1.0-test");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update))
                archive.CreateEntry("MANIFEST.JSON");

            var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.InspectAsync(archivePath));
            Assert.Contains("manifesto", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Inspect_RejectsHealthySqliteThatIsNotAnAniTDatabase()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "AniTBackupTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        var databasePath = Path.Combine(testRoot, "foreign.db");
        var archivePath = Path.Combine(testRoot, "foreign.anitbackup");
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE OtherApp (Id INTEGER PRIMARY KEY);";
                await command.ExecuteNonQueryAsync();
            }
            SqliteConnection.ClearAllPools();
            var bytes = await File.ReadAllBytesAsync(databasePath);
            var file = new AniTBackupFile("payload/Data/anit.db", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            var manifest = new AniTBackupManifest("AniT Portable Backup", 1, DateTimeOffset.UtcNow, "1.0-test", testRoot,
                new AniTBackupSummary(0, 0, 0, 0), false, false, [file]);
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(databasePath, file.Path);
                var manifestEntry = archive.CreateEntry("manifest.json");
                await using var stream = manifestEntry.Open();
                await JsonSerializer.SerializeAsync(stream, manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            }

            var error = await Assert.ThrowsAsync<InvalidDataException>(() => new AniTBackupService().InspectAsync(archivePath));
            Assert.Contains("reconhecido", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
        }
    }
}

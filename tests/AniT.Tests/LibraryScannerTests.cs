using AniT.Core;
using AniT.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniT.Tests;

public sealed class LibraryScannerTests
{
    [Fact]
    public async Task ScanAsync_ReportsProgressAndAddsDiscoveredEpisode()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AniT.Tests", Guid.NewGuid().ToString("N"));
        var libraryPath = Path.Combine(directory, "Library");
        var animePath = Path.Combine(libraryPath, "Frieren");
        Directory.CreateDirectory(animePath);
        await File.WriteAllBytesAsync(Path.Combine(animePath, "Frieren - 01.mkv"), [0x00]);

        var databasePath = Path.Combine(directory, "anit.db");
        try
        {
            await using var context = AniTDatabase.Create(databasePath);
            var root = new LibraryRoot { DisplayName = "Anime", Path = libraryPath };
            context.LibraryRoots.Add(root);
            await context.SaveChangesAsync();

            var reports = new List<LibraryScanProgress>();
            var progress = new InlineProgress<LibraryScanProgress>(reports.Add);
            var result = await new LibraryScanner(context).ScanAsync(root, progress);

            Assert.Equal(1, result.FilesFound);
            Assert.Equal(1, result.EpisodesAdded);
            Assert.Single(reports);
            Assert.Equal(1, reports[0].FilesProcessed);
            Assert.Equal(1, reports[0].TotalFiles);
            Assert.Equal("Frieren - 01.mkv", reports[0].CurrentFile);
            Assert.Equal(1, await context.Episodes.CountAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

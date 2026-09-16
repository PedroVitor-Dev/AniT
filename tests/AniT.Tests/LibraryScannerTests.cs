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

    [Fact]
    public async Task ScanAsync_RecognizesMovedFileByFingerprintAndKeepsLogicalEpisode()
    {
        await WithLibraryAsync(async (context, root, libraryPath) =>
        {
            var sourceFolder = Path.Combine(libraryPath, "Downloads");
            Directory.CreateDirectory(sourceFolder);
            var source = Path.Combine(sourceFolder, "Frieren - 07.mkv");
            await File.WriteAllBytesAsync(source, Enumerable.Range(0, 200_000).Select(index => (byte)(index % 251)).ToArray());
            var scanner = new LibraryScanner(context);
            await scanner.ScanAsync(root);
            var original = await context.MediaFiles.AsNoTracking().SingleAsync();
            var episodeId = original.EpisodeId;

            var destinationFolder = Path.Combine(libraryPath, "Frieren", "Season 01");
            Directory.CreateDirectory(destinationFolder);
            var destination = Path.Combine(destinationFolder, "Frieren S01E07.mkv");
            File.Move(source, destination);
            var result = await scanner.ScanAsync(root);

            var moved = await context.MediaFiles.AsNoTracking().SingleAsync();
            Assert.Equal(original.Id, moved.Id);
            Assert.Equal(episodeId, moved.EpisodeId);
            Assert.Equal(1, result.FilesMoved);
            Assert.EndsWith("Frieren S01E07.mkv", moved.RelativePath);
        });
    }

    [Fact]
    public async Task ScanAsync_DistinguishesPhysicalDuplicateFromDifferentVersion()
    {
        await WithLibraryAsync(async (context, root, libraryPath) =>
        {
            var animePath = Path.Combine(libraryPath, "Frieren");
            Directory.CreateDirectory(animePath);
            var bytes = Enumerable.Range(0, 150_000).Select(index => (byte)(index % 239)).ToArray();
            await File.WriteAllBytesAsync(Path.Combine(animePath, "Frieren - 07 [1080p].mkv"), bytes);
            var scanner = new LibraryScanner(context);
            await scanner.ScanAsync(root);

            await File.WriteAllBytesAsync(Path.Combine(animePath, "Frieren - 07 copy.mkv"), bytes);
            var duplicateResult = await scanner.ScanAsync(root);
            Assert.Equal(1, duplicateResult.PhysicalDuplicates);
            Assert.Equal(2, await context.MediaFiles.CountAsync());
            Assert.Equal(1, await context.MediaFiles.CountAsync(file => file.DuplicateOfMediaFileId != null));

            await File.WriteAllBytesAsync(Path.Combine(animePath, "Frieren - 07 [720p].mkv"), [1, 3, 5, 7, 9]);
            await scanner.ScanAsync(root);
            Assert.Equal(3, await context.MediaFiles.CountAsync());
            Assert.Single(await context.Episodes.ToListAsync());
        });
    }

    [Fact]
    public async Task ScanAsync_OfflineRootDoesNotDeleteOrMarkFilesMissing()
    {
        await WithLibraryAsync(async (context, root, libraryPath) =>
        {
            var animePath = Path.Combine(libraryPath, "Frieren");
            Directory.CreateDirectory(animePath);
            await File.WriteAllBytesAsync(Path.Combine(animePath, "Frieren - 01.mkv"), [1, 2, 3]);
            var scanner = new LibraryScanner(context);
            await scanner.ScanAsync(root);
            Directory.Delete(libraryPath, true);

            var result = await scanner.ScanAsync(root);

            Assert.True(result.RootUnavailable);
            Assert.Single(await context.MediaFiles.ToListAsync());
            Assert.Equal(MediaFileAvailability.RootUnavailable, (await context.MediaFiles.SingleAsync()).Availability);
        });
    }

    [Fact]
    public async Task ScanAsync_SendsDecimalAndMultiEpisodeFilesToReview()
    {
        await WithLibraryAsync(async (context, root, libraryPath) =>
        {
            await File.WriteAllBytesAsync(Path.Combine(libraryPath, "Frieren - 12.5.mkv"), [1]);
            await File.WriteAllBytesAsync(Path.Combine(libraryPath, "Frieren - 01-02.mkv"), [2]);

            var result = await new LibraryScanner(context).ScanAsync(root);

            Assert.Equal(2, result.NeedsReview);
            Assert.Equal(2, await context.LibraryReviewItems.CountAsync());
            Assert.Empty(await context.MediaFiles.ToListAsync());
        });
    }

    [Fact]
    public async Task ScanAsync_RecognizesMoveBetweenConfiguredRootsAfterReconciliation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AniT.Tests", Guid.NewGuid().ToString("N"));
        var firstPath = Path.Combine(directory, "Downloads");
        var secondPath = Path.Combine(directory, "Anime");
        Directory.CreateDirectory(firstPath);
        Directory.CreateDirectory(secondPath);
        try
        {
            var source = Path.Combine(firstPath, "Frieren - 07.mkv");
            var destination = Path.Combine(secondPath, "Frieren - 07.mkv");
            await File.WriteAllBytesAsync(source, Enumerable.Range(0, 180_000).Select(index => (byte)(index % 241)).ToArray());
            await using var context = AniTDatabase.Create(Path.Combine(directory, "anit.db"));
            var firstRoot = new LibraryRoot { DisplayName = "Downloads", Path = firstPath };
            var secondRoot = new LibraryRoot { DisplayName = "Anime", Path = secondPath };
            context.AddRange(firstRoot, secondRoot);
            await context.SaveChangesAsync();
            var scanner = new LibraryScanner(context);
            await scanner.ScanAsync(firstRoot);
            var original = await context.MediaFiles.AsNoTracking().SingleAsync();

            File.Move(source, destination);
            await scanner.ScanAsync(firstRoot);
            var result = await scanner.ScanAsync(secondRoot);

            var moved = await context.MediaFiles.AsNoTracking().SingleAsync();
            Assert.Equal(original.Id, moved.Id);
            Assert.Equal(original.EpisodeId, moved.EpisodeId);
            Assert.Equal(secondRoot.Id, moved.LibraryRootId);
            Assert.Equal(1, result.FilesMoved);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static async Task WithLibraryAsync(Func<AniTDbContext, LibraryRoot, string, Task> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AniT.Tests", Guid.NewGuid().ToString("N"));
        var libraryPath = Path.Combine(directory, "Library");
        Directory.CreateDirectory(libraryPath);
        try
        {
            await using var context = AniTDatabase.Create(Path.Combine(directory, "anit.db"));
            var root = new LibraryRoot { DisplayName = "Anime", Path = libraryPath };
            context.LibraryRoots.Add(root);
            await context.SaveChangesAsync();
            await action(context, root, libraryPath);
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

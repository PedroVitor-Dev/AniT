using AniT.Core;
using AniT.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniT.Tests;

public sealed class SmartLibraryServiceTests
{
    [Fact]
    public async Task QuickFingerprint_IsStableAcrossRenameAndSensitiveToChangedContent()
    {
        var directory = CreateDirectory();
        try
        {
            var original = Path.Combine(directory, "original.mkv");
            var renamed = Path.Combine(directory, "renamed.mkv");
            var changed = Path.Combine(directory, "changed.mkv");
            var bytes = Enumerable.Range(0, 300_000).Select(index => (byte)(index % 251)).ToArray();
            await File.WriteAllBytesAsync(original, bytes);
            await File.WriteAllBytesAsync(renamed, bytes);
            bytes[150_000] ^= 0xFF;
            await File.WriteAllBytesAsync(changed, bytes);
            var service = new QuickFileFingerprintService();

            var first = await service.ComputeQuickFingerprintAsync(original);
            var second = await service.ComputeQuickFingerprintAsync(renamed);
            var third = await service.ComputeQuickFingerprintAsync(changed);

            Assert.Equal(first, second);
            Assert.NotEqual(first, third);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ManualReview_CreatesPhysicalFileAndLearnsAlias()
    {
        var directory = CreateDirectory();
        try
        {
            await using var context = AniTDatabase.Create(Path.Combine(directory, "anit.db"));
            var root = new LibraryRoot { Path = directory, DisplayName = "Anime" };
            var anime = new Anime { Title = "Sousou no Frieren" };
            var review = new LibraryReviewItem
            {
                LibraryRoot = root,
                RelativePath = "Frieren Beyond Journey - 07.mkv",
                FileName = "Frieren Beyond Journey - 07.mkv",
                CandidateTitle = "Frieren Beyond Journey",
                NormalizedTitle = "frieren beyond journey",
                SuggestedSeasonNumber = 1,
                SuggestedEpisodeNumber = 7,
                SizeInBytes = 10,
                LastModifiedAt = DateTimeOffset.UtcNow
            };
            context.AddRange(root, anime, review);
            await context.SaveChangesAsync();

            await new LibraryReviewService(context).ResolveAsync(review.Id, anime.Id, 1, 7, learnAlias: true);

            var file = await context.MediaFiles.Include(item => item.Episode).SingleAsync();
            Assert.Equal(7, file.Episode!.Number);
            Assert.Equal(LibraryReviewStatus.Resolved, (await context.LibraryReviewItems.SingleAsync()).Status);
            Assert.Contains(await context.AnimeAliases.ToListAsync(), alias => alias.NormalizedAlias == "frieren beyond journey" && alias.Source == AnimeAliasSource.FilenameLearned);
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Organizer_BuildsPreviewWithSidecarAndDoesNotMoveAnything()
    {
        var directory = CreateDirectory();
        var sourceRoot = Path.Combine(directory, "source");
        var destinationRoot = Path.Combine(directory, "organized");
        Directory.CreateDirectory(sourceRoot);
        try
        {
            var video = Path.Combine(sourceRoot, "[Group] Frieren - 07.mkv");
            var subtitle = Path.Combine(sourceRoot, "[Group] Frieren - 07.srt");
            await File.WriteAllBytesAsync(video, [1, 2, 3]);
            await File.WriteAllTextAsync(subtitle, "subtitle");
            await using var context = AniTDatabase.Create(Path.Combine(directory, "anit.db"));
            var root = new LibraryRoot { Path = sourceRoot, DisplayName = "source" };
            var anime = new Anime { Title = "Sousou no Frieren" };
            var season = new Season { Anime = anime, Number = 1 };
            var episode = new Episode { Season = season, Number = 7 };
            var file = new MediaFile
            {
                Episode = episode,
                LibraryRoot = root,
                RelativePath = Path.GetFileName(video),
                FileName = Path.GetFileName(video),
                Extension = ".mkv",
                SizeInBytes = 3,
                LastModifiedAt = File.GetLastWriteTimeUtc(video),
                Resolution = "1080p"
            };
            context.Add(file);
            await context.SaveChangesAsync();

            var plan = await new LibraryOrganizer(context).BuildPlanAsync([file.Id], destinationRoot);

            var operation = Assert.Single(plan.Operations);
            Assert.False(operation.HasConflict);
            Assert.Single(operation.Sidecars);
            Assert.True(File.Exists(video));
            Assert.True(File.Exists(subtitle));
            Assert.False(File.Exists(operation.DestinationPath));
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task PreferredVersion_ChangesWithoutTouchingEpisodeProgress()
    {
        var directory = CreateDirectory();
        try
        {
            await using var context = AniTDatabase.Create(Path.Combine(directory, "anit.db"));
            var root = new LibraryRoot { Path = directory, DisplayName = "Anime" };
            var episode = new Episode { Season = new Season { Anime = new Anime { Title = "Frieren" }, Number = 1 }, Number = 7 };
            var first = CreateVersion(root, episode, "one.mkv", preferred: true);
            var second = CreateVersion(root, episode, "two.mkv", preferred: false);
            episode.PlaybackProgress = new PlaybackProgress { PositionSeconds = 1122, DurationSeconds = 1440 };
            context.AddRange(first, second);
            await context.SaveChangesAsync();

            await new LibraryReviewService(context).SetPreferredAsync(second.Id);
            context.ChangeTracker.Clear();

            Assert.True((await context.MediaFiles.SingleAsync(item => item.Id == second.Id)).IsPreferred);
            Assert.False((await context.MediaFiles.SingleAsync(item => item.Id == first.Id)).IsPreferred);
            Assert.Equal(1122, (await context.PlaybackProgresses.SingleAsync()).PositionSeconds);
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
    }

    private static MediaFile CreateVersion(LibraryRoot root, Episode episode, string path, bool preferred) => new()
    {
        Episode = episode,
        LibraryRoot = root,
        RelativePath = path,
        FileName = path,
        Extension = ".mkv",
        SizeInBytes = 1,
        LastModifiedAt = DateTimeOffset.UtcNow,
        IsPreferred = preferred
    };

    private static string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "AniT.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

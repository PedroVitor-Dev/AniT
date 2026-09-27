using AniT.Core;
using AniT.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniT.Tests;

public sealed class CatalogCorrectionServiceTests
{
    [Theory]
    [InlineData("NumberOnly", "01")]
    [InlineData("EpisodePrefix", "E01")]
    [InlineData("SeasonEpisode", "S02E01")]
    public void EpisodeCode_UsesConfiguredFormat(string format, string expected)
    {
        Assert.Equal(expected, EpisodeDisplayName.Code(2, 1, format));
    }

    [Fact]
    public async Task MergeAnime_PreservesFilesProgressFavoriteAndSourceTitleAsAlias()
    {
        var directory = CreateDirectory();
        try
        {
            await using var context = AniTDatabase.Create(Path.Combine(directory, "anit.db"));
            var root = new LibraryRoot { Path = directory, DisplayName = "Anime" };
            var target = new Anime { Title = "Título principal" };
            var targetEpisode = new Episode
            {
                Season = new Season { Anime = target, Number = 1 },
                Number = 1,
                PlaybackProgress = new PlaybackProgress { PositionSeconds = 20, DurationSeconds = 100 }
            };
            var source = new Anime { Title = "Nome alternativo", IsFavorite = true };
            var sourceEpisode = new Episode
            {
                Season = new Season { Anime = source, Number = 1 },
                Number = 1,
                Status = WatchStatus.Watching,
                PlaybackProgress = new PlaybackProgress { PositionSeconds = 80, DurationSeconds = 100 }
            };
            var file = new MediaFile
            {
                Episode = sourceEpisode,
                LibraryRoot = root,
                RelativePath = "Nome alternativo 01.mkv",
                FileName = "Nome alternativo 01.mkv",
                Extension = ".mkv",
                SizeInBytes = 10,
                LastModifiedAt = DateTimeOffset.UtcNow
            };
            context.AddRange(targetEpisode, sourceEpisode, file);
            await context.SaveChangesAsync();

            await new CatalogCorrectionService(context).MergeAnimeAsync(source.Id, target.Id);
            context.ChangeTracker.Clear();

            var merged = await context.Anime
                .Include(anime => anime.Aliases)
                .Include(anime => anime.Seasons).ThenInclude(season => season.Episodes).ThenInclude(episode => episode.MediaFiles)
                .Include(anime => anime.Seasons).ThenInclude(season => season.Episodes).ThenInclude(episode => episode.PlaybackProgress)
                .SingleAsync();
            var episode = Assert.Single(Assert.Single(merged.Seasons).Episodes);
            Assert.True(merged.IsFavorite);
            Assert.Equal(WatchStatus.Watching, episode.Status);
            Assert.Equal(80, episode.PlaybackProgress!.PositionSeconds);
            Assert.Single(episode.MediaFiles);
            Assert.Contains(merged.Aliases, alias => alias.Alias == "Nome alternativo");
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task SplitEpisodes_MovesSelectedEpisodeWithFileAndProgress()
    {
        var directory = CreateDirectory();
        try
        {
            await using var context = AniTDatabase.Create(Path.Combine(directory, "anit.db"));
            var root = new LibraryRoot { Path = directory, DisplayName = "Anime" };
            var anime = new Anime { Title = "Título misturado" };
            var season = new Season { Anime = anime, Number = 1 };
            var first = new Episode { Season = season, Number = 1 };
            var second = new Episode
            {
                Season = season,
                Number = 2,
                PlaybackProgress = new PlaybackProgress { PositionSeconds = 45, DurationSeconds = 100 }
            };
            var file = new MediaFile
            {
                Episode = second,
                LibraryRoot = root,
                RelativePath = "Outro anime 02.mkv",
                FileName = "Outro anime 02.mkv",
                Extension = ".mkv",
                SizeInBytes = 10,
                LastModifiedAt = DateTimeOffset.UtcNow
            };
            context.AddRange(first, second, file);
            await context.SaveChangesAsync();

            var createdId = await new CatalogCorrectionService(context).SplitEpisodesAsync(anime.Id, [second.Id], "Outro anime");
            context.ChangeTracker.Clear();

            var original = await context.Anime.Include(item => item.Seasons).ThenInclude(item => item.Episodes).SingleAsync(item => item.Id == anime.Id);
            var created = await context.Anime
                .Include(item => item.Seasons).ThenInclude(item => item.Episodes).ThenInclude(item => item.MediaFiles)
                .Include(item => item.Seasons).ThenInclude(item => item.Episodes).ThenInclude(item => item.PlaybackProgress)
                .SingleAsync(item => item.Id == createdId);
            Assert.Equal(1, Assert.Single(Assert.Single(original.Seasons).Episodes).Number);
            var moved = Assert.Single(Assert.Single(created.Seasons).Episodes);
            Assert.Equal(2, moved.Number);
            Assert.Equal(45, moved.PlaybackProgress!.PositionSeconds);
            Assert.Single(moved.MediaFiles);
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
    }

    private static string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "AniT.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

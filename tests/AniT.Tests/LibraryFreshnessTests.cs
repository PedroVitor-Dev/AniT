using AniT.Core;

namespace AniT.Tests;

public sealed class LibraryFreshnessTests
{
    [Fact]
    public void IsNew_UsesTheMostRecentAvailableMediaImport()
    {
        var now = new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
        var anime = CreateAnime(now.AddYears(-1), now.AddHours(-47));

        Assert.True(LibraryFreshness.IsNew(anime, now));
        Assert.Equal(now.AddHours(-47), LibraryFreshness.GetLatestImportAt(anime));
    }

    [Fact]
    public void IsNew_ExpiresAtExactlyTwoDays()
    {
        var now = new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
        var anime = CreateAnime(now.AddYears(-1), now.AddDays(-2));

        Assert.False(LibraryFreshness.IsNew(anime, now));
    }

    [Fact]
    public void IsNew_FallsBackToAnimeCreationWhenThereAreNoAvailableFiles()
    {
        var now = new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
        var anime = new Anime { Title = "Frieren", CreatedAt = now.AddHours(-12) };

        Assert.True(LibraryFreshness.IsNew(anime, now));
        Assert.Equal(anime.CreatedAt, LibraryFreshness.GetLatestImportAt(anime));
    }

    private static Anime CreateAnime(DateTimeOffset createdAt, DateTimeOffset importedAt)
    {
        var anime = new Anime { Title = "Frieren", CreatedAt = createdAt };
        var season = new Season { AnimeId = anime.Id, Number = 1, Anime = anime };
        var episode = new Episode { SeasonId = season.Id, Number = 1, Season = season };
        episode.MediaFiles.Add(new MediaFile
        {
            EpisodeId = episode.Id,
            LibraryRootId = Guid.NewGuid(),
            RelativePath = "Frieren/Frieren - 01.mkv",
            ImportedAt = importedAt,
            Availability = MediaFileAvailability.Available
        });
        season.Episodes.Add(episode);
        anime.Seasons.Add(season);
        return anime;
    }
}

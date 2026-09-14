using AniT.Core;

namespace AniT.Tests;

public sealed class EpisodeFileNameParserTests
{
    [Theory]
    [InlineData(@"D:\Anime\Frieren\Season 01\Frieren - 01.mkv", "Frieren", 1, 1)]
    [InlineData(@"D:\Anime\Naruto\S02\Naruto S02E12 [1080p].mkv", "Naruto", 2, 12)]
    [InlineData(@"D:\Anime\Odd Taxi\[SubsPlease] Odd Taxi - 03 (1080p).mkv", "Odd Taxi", 1, 3)]
    [InlineData(@"D:\Anime\Solo Leveling\Solo Leveling EP07.mkv", "Solo Leveling", 1, 7)]
    public void Parses_common_anime_episode_names(string path, string expectedTitle, int expectedSeason, int expectedEpisode)
    {
        var parsed = EpisodeFileNameParser.TryParse(path, @"D:\Anime", out var identity);

        Assert.True(parsed);
        Assert.Equal(expectedTitle, identity.Title);
        Assert.Equal(expectedSeason, identity.SeasonNumber);
        Assert.Equal(expectedEpisode, identity.EpisodeNumber);
    }

    [Fact]
    public void Rejects_file_without_episode_number()
    {
        var parsed = EpisodeFileNameParser.TryParse(@"D:\Anime\Frieren\opening.mkv", @"D:\Anime", out _);

        Assert.False(parsed);
    }
}

using AniT.Core;

namespace AniT.Tests;

public sealed class EpisodeFileNameParserTests
{
    [Theory]
    [InlineData(@"D:\Anime\Frieren\Season 01\Frieren - 01.mkv", "Frieren", 1, 1)]
    [InlineData(@"D:\Anime\Naruto\S02\Naruto S02E12 [1080p].mkv", "Naruto", 2, 12)]
    [InlineData(@"D:\Anime\Odd Taxi\[SubsPlease] Odd Taxi - 03 (1080p).mkv", "Odd Taxi", 1, 3)]
    [InlineData(@"D:\Anime\Solo Leveling\Solo Leveling EP07.mkv", "Solo Leveling", 1, 7)]
    [InlineData(@"D:\Anime\Sousou no Frieren\[SubsPlease] Sousou no Frieren - 07 (1080p) [A82F91C3].mkv", "Sousou no Frieren", 1, 7)]
    [InlineData(@"D:\Anime\Sousou.no.Frieren.E07.1080p.WEB-DL.mkv", "Sousou no Frieren", 1, 7)]
    [InlineData(@"D:\Anime\Frieren Beyond Journeys End S01E07.mkv", "Frieren Beyond Journeys End", 1, 7)]
    [InlineData(@"D:\Anime\Frieren - 07v2.mkv", "Frieren", 1, 7)]
    [InlineData(@"D:\Anime\Frieren.S1E7.1080p.mkv", "Frieren", 1, 7)]
    [InlineData(@"D:\Anime\Frieren.01x07.mkv", "Frieren", 1, 7)]
    [InlineData(@"D:\Anime\86 - 01.mkv", "86", 1, 1)]
    [InlineData(@"D:\Anime\Re Zero - 01.mkv", "Re Zero", 1, 1)]
    [InlineData(@"D:\Anime\Fate stay night - 01.mkv", "Fate stay night", 1, 1)]
    [InlineData(@"D:\Anime\Blue Lock - 01.mkv", "Blue Lock", 1, 1)]
    [InlineData(@"D:\Anime\Dr. Stone - 01.mkv", "Dr Stone", 1, 1)]
    [InlineData(@"D:\Anime\K-On! - 01.mkv", "K-On!", 1, 1)]
    [InlineData(@"D:\Anime\Oshi no Ko - 01.mkv", "Oshi no Ko", 1, 1)]
    [InlineData(@"D:\Anime\One Piece - 1000.mkv", "One Piece", 1, 1000)]
    [InlineData(@"D:\Anime\Boku no Hero Academia S06E10.mkv", "Boku no Hero Academia", 6, 10)]
    [InlineData(@"D:\Anime\Shingeki no Kyojin The Final Season Part 2 - 03.mkv", "Shingeki no Kyojin The Final Season Part 2", 1, 3)]
    public void Parses_common_anime_episode_names(string path, string expectedTitle, int expectedSeason, int expectedEpisode)
    {
        var parsed = EpisodeFileNameParser.TryParse(path, @"D:\Anime", out var identity);

        Assert.True(parsed);
        Assert.Equal(expectedTitle, identity.Title);
        Assert.Equal(expectedSeason, identity.SeasonNumber);
        Assert.Equal(expectedEpisode, identity.EpisodeNumber);
    }

    [Fact]
    public void Uses_parent_folder_when_filename_only_contains_episode()
    {
        var parsed = EpisodeFileNameParser.TryParse(@"D:\Anime\Sousou no Frieren\Season 01\07.mkv", @"D:\Anime", out var identity);

        Assert.True(parsed);
        Assert.Equal("Sousou no Frieren", identity.Title);
        Assert.Equal(7, identity.EpisodeNumber);
    }

    [Theory]
    [InlineData(@"D:\Anime\Frieren - 12.5.mkv", ParsedEpisodeKind.Decimal)]
    [InlineData(@"D:\Anime\Frieren - OVA.mkv", ParsedEpisodeKind.Special)]
    [InlineData(@"D:\Anime\Frieren - 01-02.mkv", ParsedEpisodeKind.MultiEpisode)]
    [InlineData(@"D:\Anime\Frieren S01E01-E02.mkv", ParsedEpisodeKind.MultiEpisode)]
    public void Difficult_episode_kinds_are_sent_to_review(string path, ParsedEpisodeKind expectedKind)
    {
        var parsed = EpisodeFileNameParser.Parse(path, @"D:\Anime");

        Assert.Equal(expectedKind, parsed.Kind);
        Assert.False(parsed.CanAutoImport);
    }

    [Fact]
    public void Extracts_release_metadata_without_destroying_title()
    {
        var parsed = EpisodeFileNameParser.Parse(@"D:\Anime\[EMBER] Frieren - 07 [1080p HEVC x265] [PT-BR].mkv", @"D:\Anime");

        Assert.Equal("Frieren", parsed.CandidateTitle);
        Assert.Equal("EMBER", parsed.ReleaseGroup);
        Assert.Equal("1080p", parsed.Resolution);
        Assert.Equal("PT-BR", parsed.Language);
    }

    [Theory]
    [InlineData("Sousou no Frieren")]
    [InlineData("sousou no frieren")]
    [InlineData("Sousou.no.Frieren")]
    [InlineData("Sousou_no_Frieren")]
    [InlineData("Sousou-no-Frieren")]
    public void Normalizer_equates_common_title_separators(string title)
    {
        Assert.Equal("sousou no frieren", AnimeTitleNormalizer.Normalize(title));
    }

    [Fact]
    public void Rejects_file_without_episode_number_or_special_marker()
    {
        var parsed = EpisodeFileNameParser.TryParse(@"D:\Anime\Frieren\opening theme.mkv", @"D:\Anime", out _);

        Assert.False(parsed);
    }
}

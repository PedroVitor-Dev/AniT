using AniT.Core;

namespace AniT.Tests;

public sealed class AnimeMatcherTests
{
    private static readonly Guid FrierenId = Guid.NewGuid();
    private static readonly AnimeMatchEntry Frieren = new(
        FrierenId,
        "Sousou no Frieren",
        "Frieren: Beyond Journey's End",
        ["Frieren Beyond Journeys End"]);

    [Fact]
    public void Exact_alias_is_an_explainable_automatic_match()
    {
        var parsed = EpisodeFileNameParser.Parse(@"D:\Anime\Frieren.Beyond.Journeys.End.S01E07.mkv", @"D:\Anime");
        var result = new AnimeMatcher().Match(parsed, [Frieren]);

        Assert.True(result.CanAutoMatch);
        Assert.Equal(FrierenId, result.Best!.AnimeId);
        Assert.Contains(AnimeMatchReason.ExactAlias, result.Best.Reasons);
        Assert.True(result.Best.Confidence >= AnimeMatchThresholds.Automatic);
    }

    [Fact]
    public void Close_unique_title_is_suggested_but_not_silently_forced_when_below_threshold()
    {
        var parsed = EpisodeFileNameParser.Parse(@"D:\Anime\Frieren Beyond Journey S01E07.mkv", @"D:\Anime");
        var result = new AnimeMatcher().Match(parsed, [Frieren]);

        Assert.Equal(FrierenId, result.Best!.AnimeId);
        Assert.False(result.IsAmbiguous);
        Assert.True(result.Best.Confidence >= AnimeMatchThresholds.ReviewSuggested);
    }

    [Fact]
    public void Similar_candidates_are_marked_ambiguous()
    {
        var parsed = EpisodeFileNameParser.Parse(@"D:\Anime\Fate Stay S01E01.mkv", @"D:\Anime");
        var result = new AnimeMatcher().Match(parsed,
        [
            new(Guid.NewGuid(), "Fate Stay Night", null, []),
            new(Guid.NewGuid(), "Fate Stay Zero", null, [])
        ]);

        Assert.True(result.IsAmbiguous);
        Assert.False(result.CanAutoMatch);
    }

    [Fact]
    public void Unrelated_title_has_no_candidate()
    {
        var parsed = EpisodeFileNameParser.Parse(@"D:\Anime\Blue Lock - 01.mkv", @"D:\Anime");
        var result = new AnimeMatcher().Match(parsed, [Frieren]);

        Assert.Null(result.Best);
    }
}

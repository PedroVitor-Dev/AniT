using AniT.Core;

namespace AniT.Tests;

public sealed class AnimeSearchTests
{
    [Theory]
    [InlineData("magi lumiere", "Kabushikigaisha Magi-Lumière 2nd Season", null)]
    [InlineData("magical inc", "Kabushikigaisha Magi-Lumière 2nd Season", "Magilumiere Magical Girls Inc. Season 2")]
    [InlineData("azur slow", "Azur Lane Bisoku Zenshin! Ni!!", "Anime AzurLane: Slow Ahead!")]
    [InlineData("ZENSHIN azur", "Azur Lane Bisoku Zenshin! Ni!!", "Anime AzurLane: Slow Ahead!")]
    public void Matches_titles_ignoring_accents_punctuation_case_and_term_order(
        string query,
        string title,
        string? englishTitle)
    {
        Assert.True(AnimeSearch.Matches(query, title, englishTitle));
    }

    [Fact]
    public void Empty_query_matches_every_item()
    {
        Assert.True(AnimeSearch.Matches("  ", "Any title", null));
    }

    [Fact]
    public void All_search_terms_must_exist()
    {
        Assert.False(AnimeSearch.Matches("magi frieren", "Kabushikigaisha Magi-Lumière", "Magilumiere Magical Girls Inc."));
    }
}

using AniT.Core;

namespace AniT.Infrastructure;

public static class OrganizationPreferences
{
    public static string PreferredTitle(Anime anime, AnimeTitlePreference preference)
    {
        var portuguese = anime.Aliases
            .Where(alias => alias.Source == AnimeAliasSource.Manual)
            .Select(alias => alias.Alias)
            .FirstOrDefault();
        return preference switch
        {
            AnimeTitlePreference.Portuguese => First(portuguese, anime.Title, anime.EnglishTitle, anime.OriginalTitle),
            AnimeTitlePreference.English => First(anime.EnglishTitle, anime.Title, anime.OriginalTitle),
            AnimeTitlePreference.Japanese => First(anime.OriginalTitle, anime.Title, anime.EnglishTitle),
            _ => First(anime.Title, anime.EnglishTitle, anime.OriginalTitle)
        };
    }

    public static string EpisodeCode(int seasonNumber, int episodeNumber, EpisodeNumberDisplayFormat format) =>
        EpisodeDisplayName.Code(seasonNumber, episodeNumber, format.ToString());

    public static string RenameTemplate(EpisodeNumberDisplayFormat format) => format switch
    {
        EpisodeNumberDisplayFormat.EpisodePrefix => "{AnimeTitle} - E{Episode:00}",
        EpisodeNumberDisplayFormat.SeasonEpisode => "{AnimeTitle} - S{Season:00}E{Episode:00}",
        _ => "{AnimeTitle} - {Episode:00}"
    };

    public static string LibrarySortKey(LibraryDefaultSort sort) => sort switch
    {
        LibraryDefaultSort.Title => "title",
        LibraryDefaultSort.ReleaseYearNewest => "year-desc",
        LibraryDefaultSort.ReleaseYearOldest => "year-asc",
        LibraryDefaultSort.Score => "score",
        LibraryDefaultSort.Progress => "progress",
        LibraryDefaultSort.Favorites => "favorites",
        _ => "recent"
    };

    private static string First(params string?[] values) =>
        values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();
}

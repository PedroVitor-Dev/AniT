namespace AniT.Core;

public static class EpisodeDisplayName
{
    public static string Format(string? animeTitle, int episodeNumber)
        => Format(animeTitle, 1, episodeNumber, "NumberOnly");

    public static string Format(string? animeTitle, int seasonNumber, int episodeNumber, string format)
    {
        var title = string.IsNullOrWhiteSpace(animeTitle) ? "Anime" : animeTitle.Trim();
        return $"{title} {Code(seasonNumber, episodeNumber, format)}";
    }

    public static string Code(int seasonNumber, int episodeNumber, string format) => format switch
    {
        "EpisodePrefix" => $"E{episodeNumber:00}",
        "SeasonEpisode" => $"S{seasonNumber:00}E{episodeNumber:00}",
        _ => $"{episodeNumber:00}"
    };
}

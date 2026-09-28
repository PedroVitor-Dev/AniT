using System.Linq.Expressions;

namespace AniT.Core;

/// <summary>
/// Defines which logical catalog entries still belong to the local library.
/// Missing files stay in the database so history and manual relinking are preserved,
/// but they must not keep stale titles visible in the normal catalog.
/// </summary>
public static class LibraryCatalogPresence
{
    public static Expression<Func<Episode, bool>> EpisodeFilter { get; } = episode =>
        episode.MediaFiles.Any(file => file.Availability != MediaFileAvailability.Missing);

    public static Expression<Func<Anime, bool>> AnimeFilter { get; } = anime =>
        anime.Seasons.Any(season => season.Episodes.Any(episode => episode.MediaFiles.Any(file =>
            file.Availability != MediaFileAvailability.Missing)));

    public static bool IsPresent(Anime anime) => anime.Seasons.Any(season => season.Episodes.Any(IsPresent));

    public static bool IsPresent(Episode episode) => episode.MediaFiles.Any(file =>
        file.Availability != MediaFileAvailability.Missing);
}

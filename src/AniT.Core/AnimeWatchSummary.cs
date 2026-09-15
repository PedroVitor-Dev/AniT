namespace AniT.Core;

public sealed record AnimeWatchSummary(int TotalEpisodes, int CompletedEpisodes, int InProgressEpisodes)
{
    public bool IsCompleted => TotalEpisodes > 0 && CompletedEpisodes == TotalEpisodes;

    public static AnimeWatchSummary Create(IEnumerable<Episode> episodes)
    {
        var episodeList = episodes.ToList();
        return new AnimeWatchSummary(
            episodeList.Count,
            episodeList.Count(episode => episode.Status == WatchStatus.Completed),
            episodeList.Count(IsInProgress));
    }

    public static bool IsInProgress(Episode episode) =>
        episode.Status != WatchStatus.Completed
        && (episode.Status == WatchStatus.Watching
            || episode.PlaybackProgress is { PositionSeconds: > 0 });
}

using AniT.Core;

namespace AniT.Tests;

public sealed class AnimeWatchSummaryTests
{
    [Fact]
    public void CompletedEpisodeWithSavedProgressDoesNotRemainInProgress()
    {
        var episode = new Episode
        {
            Status = WatchStatus.Completed,
            PlaybackProgress = new PlaybackProgress
            {
                PositionSeconds = 840,
                DurationSeconds = 1440
            }
        };

        var summary = AnimeWatchSummary.Create([episode]);

        Assert.True(summary.IsCompleted);
        Assert.Equal(1, summary.CompletedEpisodes);
        Assert.Equal(0, summary.InProgressEpisodes);
    }
}

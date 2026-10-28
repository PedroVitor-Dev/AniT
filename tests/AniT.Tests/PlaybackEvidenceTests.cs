using AniT.Core;
namespace AniT.Tests;
public sealed class PlaybackEvidenceTests
{
    [Theory]
    [InlineData(null, 1421d, false)]
    [InlineData(0d, 1421d, false)]
    [InlineData(69d, 1421d, false)]
    [InlineData(1418d, 1421d, true)]
    [InlineData(1421d, 1421d, true)]
    [InlineData(5d, null, false)]
    [InlineData(0d, 0d, false)]
    public void CompletionRequiresReportedPositionNearKnownEnd(double? position, double? duration, bool expected) =>
        Assert.Equal(expected, PlaybackEvidence.IsNearEnd(position is null ? null : TimeSpan.FromSeconds(position.Value), duration is null ? null : TimeSpan.FromSeconds(duration.Value), 5));
}

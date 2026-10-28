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
    [Theory]
    [InlineData(1421.056, 1421.056, null)]
    [InlineData(1420.5, 1421.056, null)]
    [InlineData(1500, 1421.056, null)]
    [InlineData(69.435, 1421.056, 69.435)]
    [InlineData(1300d, 1421.056, 1300d)]
    [InlineData(69.435, null, 69.435)]
    [InlineData(0, 1421.056, null)]
    public void ResumeNeverOpensAtTheEnd(double position,double? duration,double? expected)
    {
        var actual=PlaybackEvidence.ResumePosition(TimeSpan.FromSeconds(position),duration is null ? null : TimeSpan.FromSeconds(duration.Value));
        Assert.Equal(expected,actual?.TotalSeconds);
    }
}

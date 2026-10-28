namespace AniT.Core;

/// <summary>A truncated network response is not evidence that an episode finished.</summary>
public static class PlaybackEvidence
{
    public static bool IsNearEnd(TimeSpan? position, TimeSpan? duration, int checkpointIntervalSeconds)
    {
        if (position is null || duration is null || position <= TimeSpan.Zero || duration <= TimeSpan.Zero) return false;
        var tolerance = TimeSpan.FromSeconds(Math.Min(Math.Clamp(checkpointIntervalSeconds, 2, 60) + 2, duration.Value.TotalSeconds * .1));
        return position >= duration - tolerance;
    }
}

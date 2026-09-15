namespace AniT.Core;

public enum PlayerState
{
    Stopped,
    Playing,
    Paused
}

public interface IMediaPlayer
{
    event EventHandler<PlaybackPositionChangedEventArgs>? PositionChanged;
    event EventHandler? PlaybackEnded;
    event EventHandler<PlaybackPositionChangedEventArgs>? PlaybackClosed;
    Task PlayAsync(MediaFile file, TimeSpan? position, CancellationToken cancellationToken = default);
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);
    Task<TimeSpan> GetPositionAsync(CancellationToken cancellationToken = default);
    Task<PlayerState> GetStateAsync(CancellationToken cancellationToken = default);
}

public sealed record PlaybackPositionChangedEventArgs(TimeSpan Position, TimeSpan? Duration);

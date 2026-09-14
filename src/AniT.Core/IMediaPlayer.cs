namespace AniT.Core;

public enum PlayerState
{
    Stopped,
    Playing,
    Paused
}

public interface IMediaPlayer
{
    Task PlayAsync(MediaFile file, TimeSpan? position, CancellationToken cancellationToken = default);
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);
    Task<TimeSpan> GetPositionAsync(CancellationToken cancellationToken = default);
    Task<PlayerState> GetStateAsync(CancellationToken cancellationToken = default);
}

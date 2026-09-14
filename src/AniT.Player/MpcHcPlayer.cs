using AniT.Core;

namespace AniT.Player;

/// <summary>Boundary for the bundled MPC-HC integration. Slave API support is added in the next player milestone.</summary>
public sealed class MpcHcPlayer : IMediaPlayer
{
    public Task PlayAsync(MediaFile file, TimeSpan? position, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("A integração MPC-HC ainda não foi configurada.");

    public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<TimeSpan> GetPositionAsync(CancellationToken cancellationToken = default) => Task.FromResult(TimeSpan.Zero);
    public Task<PlayerState> GetStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(PlayerState.Stopped);
}

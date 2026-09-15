using AniT.Core;
using System.IO;

namespace AniT.Player;

/// <summary>Controls the bundled MPC-HC instance through its documented /slave WM_COPYDATA API.</summary>
public sealed class MpcHcPlayer : IMediaPlayer, IDisposable
{
    private readonly MpcHcBridge bridge = new();
    private readonly string executablePath;
    private PlayerState state = PlayerState.Stopped;

    public event EventHandler<PlaybackPositionChangedEventArgs>? PositionChanged;
    public event EventHandler? PlaybackEnded;

    public MpcHcPlayer(string executablePath)
    {
        this.executablePath = executablePath;
        bridge.PositionReceived += (_, position) => PositionChanged?.Invoke(this, new PlaybackPositionChangedEventArgs(position, bridge.Duration));
        bridge.EndOfStream += (_, _) => PlaybackEnded?.Invoke(this, EventArgs.Empty);
        bridge.StateChanged += (_, newState) => state = newState;
    }

    public async Task PlayAsync(MediaFile file, TimeSpan? position, CancellationToken cancellationToken = default)
    {
        var root = file.LibraryRoot?.Path ?? throw new InvalidOperationException("A raiz da biblioteca não foi carregada para este episódio.");
        var path = Path.Combine(root, file.RelativePath);
        if (!File.Exists(path)) throw new FileNotFoundException("O arquivo do episódio não foi encontrado.", path);

        await bridge.StartAsync(executablePath, cancellationToken);
        bridge.OpenFile(path, position);
    }

    public Task PauseAsync(CancellationToken cancellationToken = default) { bridge.Pause(); return Task.CompletedTask; }
    public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) { bridge.Seek(position); return Task.CompletedTask; }
    public Task<TimeSpan> GetPositionAsync(CancellationToken cancellationToken = default) => bridge.GetPositionAsync(cancellationToken);
    public Task<PlayerState> GetStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(state);
    public void Dispose() => bridge.Dispose();
}

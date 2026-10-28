using AniT.Core;
using System.IO;

namespace AniT.Player;

public sealed record MpcHcPlaybackOptions(
    double Speed,
    bool StartFullscreen,
    string PreferredAudioLanguage,
    string PreferredSubtitleLanguage)
{
    public static MpcHcPlaybackOptions Default { get; } = new(1.0, false, "auto", "auto");
}

/// <summary>Controls the bundled MPC-HC instance through its documented /slave WM_COPYDATA API.</summary>
public sealed class MpcHcPlayer : IMediaPlayer, IDisposable
{
    private readonly MpcHcBridge bridge;
    private readonly string executablePath;
    private PlayerState state = PlayerState.Stopped;

    public event EventHandler<PlaybackPositionChangedEventArgs>? PositionChanged;
    public event EventHandler? PlaybackEnded;
    public event EventHandler<PlaybackPositionChangedEventArgs>? PlaybackClosed;
    public event EventHandler<string>? Diagnostic;

    public MpcHcPlayer(string executablePath, bool independent = false, bool viewerOnly = false)
    {
        if (viewerOnly && !independent) throw new ArgumentException("A tela de exibição exige uma instância independente.");
        bridge = new MpcHcBridge(independent, viewerOnly);
        this.executablePath = executablePath;
        bridge.PositionReceived += (_, position) => PositionChanged?.Invoke(this, new PlaybackPositionChangedEventArgs(position, bridge.Duration));
        bridge.EndOfStream += (_, _) => PlaybackEnded?.Invoke(this, EventArgs.Empty);
        bridge.Disconnected += (_, position) => PlaybackClosed?.Invoke(this, new PlaybackPositionChangedEventArgs(position ?? TimeSpan.Zero, bridge.Duration, position.HasValue));
        bridge.StateChanged += (_, newState) => state = newState;
        bridge.Diagnostic += (_, message) => Diagnostic?.Invoke(this, message);
    }

    public async Task PlayAsync(MediaFile file, TimeSpan? position, CancellationToken cancellationToken = default)
        => await PlayAsync(file, position, MpcHcPlaybackOptions.Default, cancellationToken);

    public async Task PlayAsync(MediaFile file, TimeSpan? position, MpcHcPlaybackOptions options, CancellationToken cancellationToken = default)
    {
        var root = file.LibraryRoot?.Path ?? throw new InvalidOperationException("A raiz da biblioteca não foi carregada para este episódio.");
        var path = Path.Combine(root, file.RelativePath);
        if (!File.Exists(path)) throw new FileNotFoundException("O arquivo do episódio não foi encontrado.", path);

        await bridge.StartAsync(executablePath, cancellationToken);
        bridge.OpenFile(path, position, options);
    }

    public async Task PlayStreamAsync(Uri address, TimeSpan? position, MpcHcPlaybackOptions options, CancellationToken cancellationToken = default)
    {
        if (address.Scheme != "http" || address.Host != "127.0.0.1" || address.Port <= 0)
            throw new ArgumentException("O player só aceita o streaming local do AniT.");
        await bridge.StartAsync(executablePath, cancellationToken);
        bridge.OpenFile(address.AbsoluteUri, position, options);
    }

    public async Task PrepareLocalFileAsync(string path, CancellationToken cancellationToken = default, Action<IntPtr>? onConnected = null)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("O arquivo do episódio não foi encontrado.", path);
        await bridge.StartAsync(executablePath, cancellationToken);
        onConnected?.Invoke(bridge.WindowHandle);
        await bridge.PrepareFileAsync(path, cancellationToken);
    }

    public async Task PrepareStreamAsync(Uri address, CancellationToken cancellationToken = default, Action<IntPtr>? onConnected = null)
    {
        if (address.Scheme != "http" || address.Host != "127.0.0.1" || address.Port <= 0) throw new ArgumentException("O player só aceita o endpoint local da sala.");
        await bridge.StartAsync(executablePath, cancellationToken);
        onConnected?.Invoke(bridge.WindowHandle);
        await bridge.PrepareFileAsync(address.AbsoluteUri, cancellationToken, TimeSpan.FromSeconds(45));
    }

    public void SetPlaybackPermission(bool allowed) => bridge.SetPlaybackPermission(allowed);
    public void Resume() => bridge.Play();
    public void SetSpeed(double speed) => bridge.SetSpeed(speed);
    public TimeSpan? Duration => bridge.Duration;
    public TimeSpan? LastReportedPosition => bridge.LastReportedPosition;
    public int? AudioTrack => bridge.AudioTrack;
    public int? SubtitleTrack => bridge.SubtitleTrack;
    public string? CurrentFilePath => bridge.CurrentFilePath;
    public void RefreshTracks() => bridge.RefreshTracks();
    public void SetTracks(int? audio, int? subtitle) => bridge.SetTracks(audio, subtitle);

    public Task PauseAsync(CancellationToken cancellationToken = default) { bridge.Pause(); return Task.CompletedTask; }
    public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) => bridge.SeekAsync(position, cancellationToken);
    public Task<TimeSpan> GetPositionAsync(CancellationToken cancellationToken = default) => bridge.GetPositionAsync(cancellationToken);
    public Task<PlayerState> GetStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(state);
    public void Dispose() => bridge.Dispose();
}

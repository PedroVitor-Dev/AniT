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
    public OnlinePlaybackTrace? OnlineTrace { get; set; }
    public Action<MpvOnlinePlayer>? ConfigureOnlinePlayer { get; set; }
    private readonly MpcHcBridge bridge;
    private readonly string executablePath;
    private readonly bool onlineStreaming;
    private readonly Func<Uri,CancellationToken,Task<bool>>? prepareOnlineSeek;
    private MpvOnlinePlayer? onlinePlayer;
    private bool onlineActive;
    private PlayerState state = PlayerState.Stopped;

    public event EventHandler<PlaybackPositionChangedEventArgs>? PositionChanged;
    public event EventHandler? PlaybackEnded;
    public event EventHandler<PlaybackPositionChangedEventArgs>? PlaybackClosed;
    public event EventHandler<string>? Diagnostic;

    public MpcHcPlayer(string executablePath, bool independent = false, bool viewerOnly = false, bool onlineStreaming = false, Func<Uri,CancellationToken,Task<bool>>? prepareOnlineSeek = null)
    {
        if (viewerOnly && !independent) throw new ArgumentException("A tela de exibição exige uma instância independente.");
        this.onlineStreaming = onlineStreaming; this.prepareOnlineSeek = prepareOnlineSeek;
        bridge = new MpcHcBridge(independent, viewerOnly);
        this.executablePath = executablePath;
        bridge.PositionReceived += (_, position) => { if (!onlineActive) PositionChanged?.Invoke(this, new PlaybackPositionChangedEventArgs(position, bridge.Duration)); };
        bridge.EndOfStream += (_, _) => { if (!onlineActive) PlaybackEnded?.Invoke(this, EventArgs.Empty); };
        bridge.Disconnected += (_, position) => { if (!onlineActive) PlaybackClosed?.Invoke(this, new PlaybackPositionChangedEventArgs(position ?? TimeSpan.Zero, bridge.Duration, position.HasValue)); };
        bridge.StateChanged += (_, newState) => state = newState;
        bridge.Diagnostic += (_, message) => Diagnostic?.Invoke(this, message);
    }

    public async Task PlayAsync(MediaFile file, TimeSpan? position, CancellationToken cancellationToken = default)
        => await PlayAsync(file, position, MpcHcPlaybackOptions.Default, cancellationToken);

    public async Task PlayAsync(MediaFile file, TimeSpan? position, MpcHcPlaybackOptions options, CancellationToken cancellationToken = default)
    {
        onlineActive = false; onlinePlayer?.Dispose(); onlinePlayer = null;
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
        if (onlineStreaming) {
            if (bridge.WindowHandle != IntPtr.Zero) bridge.Pause();
            if (onlinePlayer is null) {
                onlinePlayer = new MpvOnlinePlayer(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executablePath)!, "..", "MPV", "mpv.exe")), prepareOnlineSeek);
                onlinePlayer.PositionChanged += (_, progress) => PositionChanged?.Invoke(this, progress);
                onlinePlayer.PlaybackEnded += (_, _) => PlaybackEnded?.Invoke(this, EventArgs.Empty);
                onlinePlayer.PlaybackClosed += (_, progress) => PlaybackClosed?.Invoke(this, progress);
                onlinePlayer.Diagnostic += (_, message) => Diagnostic?.Invoke(this, "[ONLINE MPV] " + message);
            }
            onlineActive = true;
            try {
                if (!onlinePlayer.IsRunning) ConfigureOnlinePlayer?.Invoke(onlinePlayer);
                await onlinePlayer.PlayStreamAsync(address, position, options, cancellationToken, OnlineTrace);
            }
            catch { OnlineTrace?.Finish(cancellationToken.IsCancellationRequested ? "cancelled" : "player_failed"); onlinePlayer.Dispose(); onlinePlayer = null; onlineActive = false; throw; }
            return;
        }
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

    public void CancelOnlinePreparation() => onlinePlayer?.CancelPendingPreparation();
    public void SetPlaybackPermission(bool allowed) => bridge.SetPlaybackPermission(allowed);
    public void Resume() { if (onlineActive) onlinePlayer!.Resume(); else bridge.Play(); }
    public void SetSpeed(double speed) { if (onlineActive) onlinePlayer!.SetSpeed(speed); else bridge.SetSpeed(speed); }
    public TimeSpan? Duration => onlineActive ? onlinePlayer?.Duration : bridge.Duration;
    public TimeSpan? LastReportedPosition => onlineActive ? onlinePlayer?.LastReportedPosition : bridge.LastReportedPosition;
    public int? AudioTrack => bridge.AudioTrack;
    public int? SubtitleTrack => bridge.SubtitleTrack;
    public string? CurrentFilePath => onlineActive ? onlinePlayer?.CurrentFilePath : bridge.CurrentFilePath;
    public void RefreshTracks() => bridge.RefreshTracks();
    public void SetTracks(int? audio, int? subtitle) => bridge.SetTracks(audio, subtitle);

    public Task PauseAsync(CancellationToken cancellationToken = default) { if (onlineActive) return onlinePlayer!.PauseAsync(cancellationToken); bridge.Pause(); return Task.CompletedTask; }
    public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) => onlineActive ? onlinePlayer!.SeekAsync(position, cancellationToken) : bridge.SeekAsync(position, cancellationToken);
    public Task<TimeSpan> GetPositionAsync(CancellationToken cancellationToken = default) => onlineActive ? onlinePlayer!.GetPositionAsync(cancellationToken) : bridge.GetPositionAsync(cancellationToken);
    public Task<PlayerState> GetStateAsync(CancellationToken cancellationToken = default) => onlineActive ? onlinePlayer!.GetStateAsync(cancellationToken) : Task.FromResult(state);
    public void Dispose() { onlinePlayer?.Dispose(); bridge.Dispose(); }
}

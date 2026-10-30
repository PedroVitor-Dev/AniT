using System.Globalization;
using System.Diagnostics;
using System.Text.Json;
using AniT.Core;

namespace AniT.Player;

public sealed record OnlinePlayerSnapshot(TimeSpan? Position, TimeSpan? Duration, bool Paused,
    bool Buffering, bool Seeking, bool VideoReady, double CacheSeconds, double Volume, bool Muted);

public sealed partial class MpvOnlinePlayer
{
    IntPtr videoParent;
    long lastPointer;
    double volume = 100;
    bool muted;
    OnlinePlayerSnapshot snapshot = new(null, null, true, false, false, false, 0, 100, false);
    public double? SupplyRatio => bufferPlan.SupplyRatio;
    public OnlinePlayerSnapshot Snapshot => Volatile.Read(ref snapshot);
    public event EventHandler<string>? UserInput;
    public event EventHandler? Disposed;
    public void AttachVideoWindow(IntPtr parent)
    {
        if (IsRunning) throw new InvalidOperationException("A superfície do vídeo deve ser definida antes de iniciar o player.");
        if (parent == IntPtr.Zero) throw new ArgumentException("A superfície do vídeo não está pronta.", nameof(parent));
        videoParent = parent;
        position = duration = null; videoReady = buffering = seeking = false; paused = true;
        ResetTracks(); ResetBufferObservation(); PublishSnapshot();
    }
    internal static string WindowId(IntPtr parent) => unchecked((uint)parent.ToInt64()).ToString(CultureInfo.InvariantCulture);
    internal const string HostBindings = """
        MBTN_RIGHT script-message anit-ui menu
        b script-message anit-ui moments
        a script-message anit-ui audio
        s script-message anit-ui subtitles
        MENU script-message anit-ui menu
        Shift+F10 script-message anit-ui menu
        MBTN_LEFT script-message anit-ui click
        MBTN_LEFT_DBL script-message anit-ui double-click
        MOUSE_MOVE script-message anit-ui pointer
        MOUSE_ENTER script-message anit-ui pointer
        SPACE script-message anit-ui pause
        p script-message anit-ui pause
        f script-message anit-ui fullscreen
        F11 script-message anit-ui fullscreen
        ESC script-message anit-ui escape
        LEFT script-message anit-ui back
        RIGHT script-message anit-ui forward
        UP script-message anit-ui volume-up
        DOWN script-message anit-ui volume-down
        m script-message anit-ui mute
        Ctrl+w script-message anit-ui close
        Alt+F4 script-message anit-ui close
        """;
    internal void ReceiveHostInput(JsonElement root)
    {
        if (videoParent == IntPtr.Zero || !root.TryGetProperty("args", out var args) || args.ValueKind != JsonValueKind.Array || args.GetArrayLength() != 2
            || args[0].ValueKind != JsonValueKind.String || args[0].GetString() != "anit-ui" || args[1].ValueKind != JsonValueKind.String) return;
        var action = args[1].GetString();
        if (action == "pointer") {
            var now = Stopwatch.GetTimestamp();
            if (Stopwatch.GetElapsedTime(Interlocked.Read(ref lastPointer), now).TotalMilliseconds < 33) return;
            Interlocked.Exchange(ref lastPointer, now);
        }
        if (action is "click" or "double-click" or "pointer" or "pause" or "fullscreen" or "escape" or "back" or "forward" or "volume-up" or "volume-down" or "mute" or "close" or "menu" or "audio" or "subtitles" or "moments")
            Dispatch(() => UserInput?.Invoke(this, action));
    }
    void PublishSnapshot() => Volatile.Write(ref snapshot, new(position, duration, UserPaused, buffering, seeking, videoReady,
        Math.Max(0, BufferSample().Seconds ?? 0), volume, muted));
    public Task TogglePauseAsync(CancellationToken token = default) => ChangeUserPauseAsync(null,token);
    public async Task SetVolumeAsync(double value, CancellationToken token = default)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        await CommandAsync(["set_property", "volume", Math.Clamp(value, 0, 100)], token);
    }
    public async Task ToggleMuteAsync(CancellationToken token = default) => await CommandAsync(["cycle", "mute"], token);
    /// <summary>A user close saves a known checkpoint before terminating the owned process.</summary>
    public void ClosePlayback()
    {
        if (closing) return;
        var progress = new PlaybackPositionChangedEventArgs(position ?? TimeSpan.Zero, duration, videoReady && position.HasValue);
        Dispose();
        PlaybackClosed?.Invoke(this, progress);
    }
}

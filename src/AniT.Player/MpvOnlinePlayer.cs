using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using AniT.Core;

namespace AniT.Player;

/// <summary>Owned streaming-only mpv process, controlled through its documented JSON IPC.</summary>
public sealed partial class MpvOnlinePlayer : IDisposable
{
    readonly string executable;
    OnlinePlaybackTrace? telemetry;
    bool seeking, frameInfoReady, playbackRestarted;
    double? cacheSeconds, outputDrops, decoderDrops, avSync;
    void ObservePlayback()
    {
        PublishSnapshot();
        if (videoReady && frameInfoReady && playbackRestarted) telemetry?.Mark("first_video_frame_signal");
        telemetry?.PlaybackState(buffering, paused, seeking);
        telemetry?.Sample("player", ("position_seconds", position?.TotalSeconds ?? double.NaN),
            ("cache_seconds", cacheSeconds ?? double.NaN), ("output_dropped_frames", outputDrops ?? double.NaN),
            ("decoder_dropped_frames", decoderDrops ?? double.NaN), ("av_sync_seconds", avSync ?? double.NaN),
            ("paused", paused ? 1 : 0), ("buffering", buffering ? 1 : 0), ("seeking", seeking ? 1 : 0));
    }
    async Task ObserveDiagnosticsAsync(int expectedGeneration, CancellationToken token)
    {
        var expectedTrace = telemetry;
        var index = 100;
        foreach (var property in new[] { "video-frame-info", "frame-drop-count", "decoder-frame-drop-count", "avsync" }) {
            if (token.IsCancellationRequested || generation != expectedGeneration) return;
            try { await CommandAsync(["observe_property", ++index, property], token); }
            catch { if (generation == expectedGeneration) expectedTrace?.Event("player_metric_unavailable", ("property_index", index)); }
        }
    }
    readonly Func<Uri, CancellationToken, Task<bool>>? prepareSeek;
    bool usingIndex;
    bool awaitingFileStart;
    public bool SeekIndexReady => usingIndex;
    CancellationTokenSource? fileLifetime;
    readonly SemaphoreSlim indexing = new(1, 1);
    readonly object warmingSync = new();
    CancellationTokenSource? warmingIndex;
    bool indexFetching;
    readonly SynchronizationContext? context = SynchronizationContext.Current
        ?? (System.Windows.Application.Current is { } application ? new System.Windows.Threading.DispatcherSynchronizationContext(application.Dispatcher) : null);
    readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> pending = new();
    readonly SemaphoreSlim writing = new(1, 1);
    CancellationTokenSource? lifetime;
    Process? process;
    NamedPipeClientStream? pipe;
    StreamWriter? writer;
    TaskCompletionSource? loaded;
    int nextRequest;
    int generation;
    bool closing, paused, buffering, videoReady;
    TimeSpan? position, duration;
    public TimeSpan? Duration => duration;
    public TimeSpan? LastReportedPosition => videoReady ? position : null;
    public string? CurrentFilePath { get; private set; }
    public bool IsRunning => process is { HasExited: false };
    public event EventHandler<PlaybackPositionChangedEventArgs>? PositionChanged;
    public event EventHandler? PlaybackEnded;
    public event EventHandler<PlaybackPositionChangedEventArgs>? PlaybackClosed;
    public event EventHandler<string>? Diagnostic;
    public MpvOnlinePlayer(string executable, Func<Uri, CancellationToken, Task<bool>>? prepareSeek = null) { this.executable = executable; this.prepareSeek = prepareSeek; }
    void Dispatch(Action action)
    {
        var expected = generation;
        if (context is null) { if (!closing) action(); }
        else context.Post(_ => { if (!closing && expected == generation) action(); }, null);
    }
    void Trace(string message) => Dispatch(() => Diagnostic?.Invoke(this, message));

    public async Task PlayStreamAsync(Uri address, TimeSpan? resume, MpcHcPlaybackOptions options, CancellationToken token, OnlinePlaybackTrace? trace = null)
    {
        if (address.Scheme != "http" || address.Host != "127.0.0.1") throw new ArgumentException("O player aceita somente o streaming local do AniT.");
        if (!File.Exists(executable)) throw new FileNotFoundException("O motor de streaming do Beta não foi encontrado. Atualize o AniT.", executable);
        WasClosedByUser=false;
        if (!ReferenceEquals(telemetry, trace)) telemetry?.Finish("replaced");
        trace?.Mark("player_requested");
        if (!IsRunning) { DisposeCore(false); telemetry = trace; trace?.Mark("process_start_requested"); await StartAsync(token); trace?.Mark("ipc_ready"); }
        else telemetry = trace;
        fileLifetime?.Cancel(); fileLifetime?.Dispose(); fileLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime!.Token);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token, fileLifetime.Token); token = operation.Token;
        var fileGeneration = ++generation;
        lock(pauseSync) indexReloading=false;
        ResetBufferObservation(); playbackSpeed = options.Speed; ResetTracks();
        seeking = frameInfoReady = playbackRestarted = false; cacheSeconds = outputDrops = decoderDrops = avSync = null;
        usingIndex = false; position = duration = null; videoReady = false; paused = true; buffering = false; CurrentFilePath = address.AbsoluteUri; PublishSnapshot();
        telemetry?.Mark("loadfile_requested");
        loaded = new(TaskCreationOptions.RunContinuationsAsynchronously); awaitingFileStart = true;
        await CommandAsync(["set_property", "pause", true], token);
        await CommandAsync(["set_property", "speed", options.Speed], token);
        await CommandAsync(["set_property", "fullscreen", videoParent == IntPtr.Zero && options.StartFullscreen], token);
        await CommandAsync(["set_property", "alang", options.PreferredAudioLanguage == "auto" ? "" : Language(options.PreferredAudioLanguage)], token);
        await CommandAsync(["set_property", "sid", options.PreferredSubtitleLanguage == "off" ? "no" : "auto"], token);
        await CommandAsync(["set_property", "slang", options.PreferredSubtitleLanguage is "auto" or "off" ? "" : Language(options.PreferredSubtitleLanguage)], token);
        await CommandAsync(["set_property", "aid", "auto"], token);
        await CommandAsync(["set_property", "sub-delay", 0], token);
        if(InitialSubtitleAppearance is {} subtitleStyle)await ApplySubtitleStyleCore(subtitleStyle.Normalize(),token);
        await ResetBufferOptionsAsync(token);
        await CommandAsync(["loadfile", address.AbsoluteUri, "replace", -1, new Dictionary<string, string> { { "index", Path.GetExtension(address.AbsolutePath).ToLowerInvariant() is ".mkv" or ".webm" ? "recreate" : "default" }, { "pause", "yes" } }], token);
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(120), token);
        var length = await CommandAsync(["get_property", "duration"], token);
        if (length.ValueKind == JsonValueKind.Number) duration = TimeSpan.FromSeconds(length.GetDouble());
        var target = PlaybackEvidence.ResumePosition(resume, duration);
        if (target is not null) await SeekAsync(target.Value, token);
        await CommandAsync(["set_property", "pause", false], token);
        token.ThrowIfCancellationRequested();
        _ = ControlBufferAsync(fileGeneration, fileLifetime.Token);
        if (!usingIndex && prepareSeek is not null) _ = WarmIndexAsync(address, fileLifetime.Token);
    }
    static string Language(string value) => value switch { "pt-br" => "pt-br,por,pt", "ja" => "ja,jpn", "en" => "en,eng", "es" => "es,spa", _ => value };
    async Task StartAsync(CancellationToken token)
    {
        closing = false; generation++; lifetime = new(); var name = "AniT.Online." + Guid.NewGuid().ToString("N");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "--no-config", "--no-terminal", "--idle=yes", "--force-window=immediate", "--title=Online Anime Player · AniT", "--pause=yes", "--cache=yes", "--cache-pause-initial=no", "--cache-pause-wait=1", "--demuxer-max-bytes=33554432", "--demuxer-max-back-bytes=8388608", "--cache-secs=10", "--demuxer-readahead-secs=10", "--network-timeout=120", "--demuxer-mkv-probe-video-duration=no", "--input-ipc-server=\\\\.\\pipe\\" + name }) start.ArgumentList.Add(arg);
        if (videoParent != IntPtr.Zero) {
            start.ArgumentList.Add("--wid=" + WindowId(videoParent));
            start.ArgumentList.Add("--input-doubleclick-time=" + MpvVideoHost.DoubleClickMilliseconds);
            foreach (var option in new[] { "--drag-and-drop=no", "--osc=no", "--osd-level=0", "--input-builtin-bindings=no", "--input-default-bindings=no", "--window-dragging=no" }) start.ArgumentList.Add(option);
        }
        process = Process.Start(start) ?? throw new IOException("Não foi possível iniciar o motor de streaming.");
        Trace($"mpv iniciado (PID {process.Id}).");
        telemetry?.Mark("process_started");
        pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        try { await pipe.ConnectAsync(10000, token); } catch { telemetry?.Finish(token.IsCancellationRequested ? "cancelled" : "ipc_failed"); Dispose(); throw; }
        writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
        _ = ReadLoopAsync(pipe, lifetime.Token);
        _ = WatchExitAsync(process, lifetime);
        var index = 0; foreach (var property in new[] { "time-pos", "duration", "pause", "paused-for-cache", "video-out-params", "demuxer-cache-state", "demuxer-cache-duration", "speed", "volume", "mute", "track-list", "aid", "sid", "sub-delay" }) await CommandAsync(["observe_property", ++index, property], token);
        await SetVolumeAsync(InitialVolume, token);
        await CommandAsync(["set_property", "mute", InitialMuted], token);
        if (videoParent != IntPtr.Zero) {
            await CommandAsync(["define-section", "anit-host", HostBindings, "force"], token);
            await CommandAsync(["enable-section", "anit-host", "allow-hide-cursor"], token);
        }
    }
    async Task ReadLoopAsync(NamedPipeClientStream connection, CancellationToken token)
    {
        try
        {
            using var reader = new StreamReader(connection, Encoding.UTF8, false, 4096, leaveOpen: true);
            while (!token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(token); if (line is null) break; token.ThrowIfCancellationRequested();
                using var document = JsonDocument.Parse(line); var root = document.RootElement;
                if (root.TryGetProperty("request_id", out var id) && pending.TryRemove(id.GetInt32(), out var result))
                {
                    if (root.TryGetProperty("error", out var error) && error.GetString() != "success") result.TrySetException(new IOException("mpv: " + error.GetString()));
                    else result.TrySetResult(root.TryGetProperty("data", out var data) ? data.Clone() : default);
                }
                if (!root.TryGetProperty("event", out var e)) continue;
                switch (e.GetString())
                {
                    case "client-message": ReceiveHostInput(root); break;
                    case "start-file": awaitingFileStart = false; break;
                    case "file-loaded": loaded?.TrySetResult(); if (!awaitingFileStart) { telemetry?.Mark("container_loaded"); if (telemetry is not null) _ = ObserveDiagnosticsAsync(generation, fileLifetime?.Token ?? token); } Trace("Contêiner carregado pelo mpv."); break;
                    case "seek": if (!awaitingFileStart) { ResetBufferObservation(); seeking = true; telemetry?.Event("seek_requested"); ObservePlayback(); } break;
                    case "playback-restart": if (!awaitingFileStart) { seeking = false; playbackRestarted = true; telemetry?.Event("playback_restarted"); ObservePlayback(); } break;
                    case "property-change":
                        if (awaitingFileStart || !root.TryGetProperty("data", out var value)) break;
                        switch (root.GetProperty("name").GetString())
                        {
                            case "time-pos" when value.ValueKind == JsonValueKind.Number:
                                position = TimeSpan.FromSeconds(value.GetDouble());
                                if (videoReady) { var args = new PlaybackPositionChangedEventArgs(position.Value, duration); Dispatch(() => PositionChanged?.Invoke(this, args)); }
                                break;
                            case "duration" when value.ValueKind == JsonValueKind.Number: duration = TimeSpan.FromSeconds(value.GetDouble()); break;
                            case "pause" when value.ValueKind is JsonValueKind.True or JsonValueKind.False: paused = value.GetBoolean(); break;
                            case "paused-for-cache" when value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                                var waiting = value.GetBoolean();
                                if (waiting && !buffering && !paused && !seeking && videoReady && playbackRestarted && !cacheState.Eof)
                                    Interlocked.Increment(ref cacheUnderruns);
                                buffering = waiting; break;
                            case "video-frame-info" when value.ValueKind == JsonValueKind.Object && value.TryGetProperty("picture-type", out var pictureType) && pictureType.ValueKind == JsonValueKind.String: frameInfoReady = !string.IsNullOrEmpty(pictureType.GetString()); break;
                            case "demuxer-cache-duration": cacheSeconds = BufferNumber(value); cacheSecondsTimestamp = Stopwatch.GetTimestamp(); break;
                            case "demuxer-cache-state": cacheState = MpvCacheState.Parse(value); break;
                            case "track-list": case "aid": case "sid": case "sub-delay": ObserveTracks(root.GetProperty("name").GetString()!, value); break;
                            case "volume": if (BufferNumber(value) is { } level) volume = Math.Clamp(level, 0, 100); break;
                            case "mute" when value.ValueKind is JsonValueKind.True or JsonValueKind.False: muted = value.GetBoolean(); break;
                            case "speed": if (BufferNumber(value) is > 0 and var speed) playbackSpeed = speed; PublishTracks(); break;
                            case "frame-drop-count" when value.ValueKind == JsonValueKind.Number: outputDrops = value.GetDouble(); break;
                            case "decoder-frame-drop-count" when value.ValueKind == JsonValueKind.Number: decoderDrops = value.GetDouble(); break;
                            case "avsync" when value.ValueKind == JsonValueKind.Number: avSync = value.GetDouble(); break;
                            case "video-out-params" when value.ValueKind == JsonValueKind.Object && value.TryGetProperty("crop-w", out _): videoReady = true; telemetry?.Mark("video_output_configured"); Trace("Saída de vídeo configurada: " + value.GetRawText()); break;
                        }
                        ObservePlayback();
                        break;
                    case "end-file":
                        if (awaitingFileStart) break;
                        if (root.TryGetProperty("reason", out var ending) && (ending.GetString() is "error" or "eof") && loaded is { Task.IsCompleted: false })
                            loaded.TrySetException(new IOException("O streaming terminou antes de carregar o vídeo."));
                        if (root.TryGetProperty("reason", out var reason) && reason.GetString() == "eof") { telemetry?.Finish("eof"); Dispatch(() => PlaybackEnded?.Invoke(this, EventArgs.Empty)); }
                        else if (reason.ValueKind == JsonValueKind.String && reason.GetString() == "error") { telemetry?.Finish("player_error"); loaded?.TrySetException(new IOException("O vídeo não pôde ser aberto pelo motor de streaming.")); } break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or JsonException) { if (!token.IsCancellationRequested) Trace("Conexão com mpv: " + ex.Message); }
        finally { if (ReferenceEquals(pipe, connection)) { foreach (var item in pending.Values) item.TrySetException(new IOException("O player de streaming foi desconectado.")); pending.Clear(); loaded?.TrySetException(new IOException("O player foi fechado durante a abertura.")); } }
    }
    async Task WatchExitAsync(Process owned, CancellationTokenSource session)
    {
        try { await owned.WaitForExitAsync(); try { session.Cancel(); } catch (ObjectDisposedException) { } if (!closing && ReferenceEquals(process, owned)) { telemetry?.Finish("player_closed"); var args = new PlaybackPositionChangedEventArgs(position ?? TimeSpan.Zero, duration, videoReady && position.HasValue); Dispatch(() => PlaybackClosed?.Invoke(this, args)); } }
        catch (InvalidOperationException) { }
    }
    public async Task<JsonElement> CommandAsync(object[] command, CancellationToken token = default)
    {
        var output = writer ?? throw new IOException("O player de streaming não está conectado.");
        var id = Interlocked.Increment(ref nextRequest); var response = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously); pending[id] = response;
        try { await writing.WaitAsync(token); try { await output.WriteLineAsync(JsonSerializer.Serialize(new { command, request_id = id }).AsMemory(), token); } finally { writing.Release(); } return await response.Task.WaitAsync(TimeSpan.FromSeconds(5), token); }
        finally { pending.TryRemove(id, out _); }
    }
    public Task PauseAsync(CancellationToken token = default) => ChangeUserPauseAsync(true,token);
    public void CancelPendingPreparation() => fileLifetime?.Cancel();

    async Task WarmIndexAsync(Uri address, CancellationToken token)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(token);
        lock (warmingSync) { warmingIndex?.Cancel(); warmingIndex = request; }
        token = request.Token;
        try
        {
            while (true)
            {
                await Task.Delay(500, token);
                if (bufferPlan.CanPrepareIndex) break;
            }
            await indexing.WaitAsync(token);
            try
            {
                if (usingIndex || prepareSeek is null) return;
                bool ready;
                lock (warmingSync) indexFetching = true;
                try { ready = await prepareSeek(address, token); }
                finally { lock (warmingSync) indexFetching = false; }
                token.ThrowIfCancellationRequested();
                if (!ready) return;
                // Reopen only after there is a playback cushion; native OSC/keyboard seeks
                // then use the real cues too, without having to go through AniT's wrapper.
                var current = await GetPositionAsync(token);
                telemetry?.Event("index_reload_begin");
                await ReloadIndexedAsync(address.AbsoluteUri,current,token);
                Trace("Índice pronto também para os controles do player.");
            }
            finally { indexing.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Trace("Índice em segundo plano: " + ex.Message); }
        finally { lock (warmingSync) { if (ReferenceEquals(warmingIndex, request)) warmingIndex = null; } }
    }

    public async Task SeekAsync(TimeSpan target, CancellationToken token = default)
    {
        // An explicit seek must not queue behind a speculative index fetch that is buffering.
        lock (warmingSync) warmingIndex?.Cancel();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(token, fileLifetime?.Token ?? lifetime?.Token ?? CancellationToken.None);
        token = request.Token;
        await indexing.WaitAsync(token);
        try { await SeekCoreAsync(target, token); } finally { indexing.Release(); }
    }

    async Task SeekCoreAsync(TimeSpan target, CancellationToken token)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token, fileLifetime?.Token ?? lifetime?.Token ?? CancellationToken.None); token = operation.Token;
        if (!usingIndex && prepareSeek is not null && CurrentFilePath is { } path && await prepareSeek(new Uri(path), token))
        {
            await ReloadIndexedAsync(path,target,token);
        }
        else await CommandAsync(["seek", target.TotalSeconds, "absolute+exact"], token);
    }
    public async Task<TimeSpan> GetPositionAsync(CancellationToken token = default)
    {
        if (!videoReady) throw new IOException("Aguardando o primeiro quadro do vídeo.");
        var result = await CommandAsync(["get_property", "time-pos"], token);
        if (result.ValueKind != JsonValueKind.Number) throw new IOException("Posição indisponível durante o buffering.");
        var value = TimeSpan.FromSeconds(result.GetDouble()); Trace($"Posição MPV: {value.TotalSeconds:0.###}s."); return value;
    }
    public Task<PlayerState> GetStateAsync(CancellationToken token = default) => Task.FromResult(!IsRunning ? PlayerState.Stopped : UserPaused || buffering ? PlayerState.Paused : PlayerState.Playing);
    public void Resume() => ResumeRequested();
    public void SetSpeed(double speed) => Forget(["set_property", "speed", speed]);
    async void Forget(object[] command) { try { await CommandAsync(command); } catch (Exception ex) { Trace(ex.Message); } }
    public void Dispose() => DisposeCore(true);
    void DisposeCore(bool notify)
    {
        telemetry?.Finish("disposed");
        lock (warmingSync) warmingIndex?.Cancel();
        closing = true; fileLifetime?.Cancel(); fileLifetime?.Dispose(); fileLifetime = null; lifetime?.Cancel();
        foreach (var request in pending.Values) request.TrySetCanceled(); pending.Clear(); loaded?.TrySetCanceled();
        pipe?.Dispose(); pipe = null;
        // StreamWriter.Dispose may flush into a pipe that the player has already closed.
        try { writer?.Dispose(); } catch (IOException) { } catch (InvalidOperationException) { }
        writer = null;
        var owned = process; process = null; if (owned is not null) { try { if (!owned.HasExited) owned.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } owned.Dispose(); }
        lifetime?.Dispose(); lifetime = null;
        if (notify) Disposed?.Invoke(this, EventArgs.Empty);
    }
}

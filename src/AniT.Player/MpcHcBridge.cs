using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace AniT.Player;

internal sealed class MpcHcBridge : IDisposable
{
    private const int WmCopyData = 0x004A;
    private const int CmdConnect = unchecked((int)0x50000000);
    private const int CmdState = unchecked((int)0x50000001);
    private const int CmdPlayMode = unchecked((int)0x50000002);
    private const int CmdNowPlaying = unchecked((int)0x50000003);
    private const int CmdListSubtitleTracks = unchecked((int)0x50000004);
    private const int CmdListAudioTracks = unchecked((int)0x50000005);
    private const int CmdCurrentPosition = unchecked((int)0x50000007);
    private const int CmdNotifySeek = unchecked((int)0x50000008);
    private const int CmdEndOfStream = unchecked((int)0x50000009);
    private const int CmdDisconnect = unchecked((int)0x5000000B);
    private const int CmdOpenFile = unchecked((int)0xA0000000);
    private const int CmdPlay = unchecked((int)0xA0000004);
    private const int CmdPause = unchecked((int)0xA0000005);
    private const int CmdSetPosition = unchecked((int)0xA0002000);
    private const int CmdSetAudioTrack = unchecked((int)0xA0002004);
    private const int CmdSetSubtitleTrack = unchecked((int)0xA0002005);
    private const int CmdGetSubtitleTracks = unchecked((int)0xA0003000);
    private const int CmdGetAudioTracks = unchecked((int)0xA0003001);
    private const int CmdGetCurrentPosition = unchecked((int)0xA0003004);
    private const int CmdToggleFullscreen = unchecked((int)0xA0004000);
    private const int CmdSetSpeed = unchecked((int)0xA0004008);

    private readonly HwndSource hostWindow;
    private readonly bool independent;
    private readonly bool viewerOnly;
    private Process? ownedProcess;
    private TaskCompletionSource? filePrepared;
    private bool pauseOnLoad;
    private bool playbackAllowed = true;
    private bool disposed;
    private TaskCompletionSource<IntPtr>? connection;
    private TaskCompletionSource<TimeSpan>? positionRequest;
    private readonly SemaphoreSlim positionGate = new(1, 1);
    private readonly SemaphoreSlim seekGate = new(1, 1);
    private IntPtr playerWindow;
    private TimeSpan? resumePosition;
    private bool resumeSeekReapplied;
    private TimeSpan? duration;
    private TimeSpan? lastPosition;
    public TimeSpan? LastReportedPosition => lastPosition;
    private MpcHcPlaybackOptions playbackOptions = MpcHcPlaybackOptions.Default;
    private bool playbackOptionsApplied;
    private bool fullscreenApplied;
    private bool playing;
    private double requestedSpeed = 1;
    private double? appliedSpeed;

    public event EventHandler<TimeSpan>? PositionReceived;
    public event EventHandler? EndOfStream;
    public event EventHandler<Core.PlayerState>? StateChanged;
    public event EventHandler<string>? NowPlaying;
    public event EventHandler<TimeSpan?>? Disconnected;
    public event EventHandler<string>? Diagnostic;

    public MpcHcBridge(bool independent = false, bool viewerOnly = false)
    {
        this.independent = independent;
        this.viewerOnly = viewerOnly;
        var parameters = new HwndSourceParameters("AniT.MpcHcHost")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
            Width = 0,
            Height = 0
        };
        hostWindow = new HwndSource(parameters);
        hostWindow.AddHook(WindowProcedure);
    }

    public async Task StartAsync(string executablePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(executablePath)) throw new FileNotFoundException("O executável integrado do MPC-HC não foi encontrado.", executablePath);
        if (playerWindow != IntPtr.Zero) return;
        if (independent) executablePath = await TheaterPlayerRuntime.PrepareAsync(executablePath, viewerOnly, cancellationToken);
        var pendingConnection = new TaskCompletionSource<IntPtr>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection = pendingConnection;

        var process = Process.Start(new ProcessStartInfo(executablePath, $"{(independent ? "/new /nocrashreporter " : "")}{(viewerOnly ? "/viewpreset 1 /minimized /nofocus " : independent ? "/viewpreset 3 " : "")}/slave {hostWindow.Handle}") { UseShellExecute = false });
        if (process is null) throw new InvalidOperationException("Não foi possível iniciar o MPC-HC.");
        if (independent) ownedProcess = process;
        Trace($"MPC-HC iniciado; aguardando conexão (PID {process.Id}).");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var registration = timeout.Token.Register(() => pendingConnection.TrySetCanceled(timeout.Token));
        playerWindow = await pendingConnection.Task;
        Trace($"Conectado ao HWND {playerWindow}.");
    }

    public void OpenFile(string path, TimeSpan? startPosition, MpcHcPlaybackOptions? options = null)
    {
        resumePosition = startPosition;
        resumeSeekReapplied = false;
        lastPosition = null;
        duration = null;
        AudioTrack = SubtitleTrack = null;
        CurrentFilePath = null;
        playbackOptions = options ?? MpcHcPlaybackOptions.Default;
        requestedSpeed = playbackOptions.Speed; appliedSpeed = null; playing = false;
        playbackOptionsApplied = false;
        Trace($"Abrindo arquivo: {path}; posição para retomar: {startPosition?.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) ?? "0"}s.");
        Send(CmdOpenFile, path);
    }

    public async Task PrepareFileAsync(string path, CancellationToken cancellationToken, TimeSpan? preparationTimeout = null)
    {
        filePrepared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pauseOnLoad = true;
        try
        {
            OpenFile(path, TimeSpan.Zero, MpcHcPlaybackOptions.Default);
            await filePrepared.Task.WaitAsync(preparationTimeout ?? TimeSpan.FromSeconds(15), cancellationToken);
            await Task.Delay(100, cancellationToken);
            Pause();
            await SeekAsync(TimeSpan.Zero, cancellationToken);
            RefreshTracks();
        }
        finally { pauseOnLoad = false; filePrepared = null; }
    }

    public void SetSpeed(double speed)
    {
        if (!double.IsFinite(speed) || speed is < .5 or > 2) throw new ArgumentOutOfRangeException(nameof(speed));
        requestedSpeed = speed;
        ApplyPendingSpeed();
    }

    private void ApplyPendingSpeed()
    {
        // MPC-HC's CMD_SETSPEED implicitly starts playback, even when the rate is unchanged.
        // Queue rate changes while paused and send them once, only after playback resumes.
        if (!playbackAllowed || !playing || pauseOnLoad || appliedSpeed == requestedSpeed) return;
        appliedSpeed = requestedSpeed;
        Send(CmdSetSpeed, requestedSpeed.ToString("0.##", CultureInfo.InvariantCulture));
    }

    public void Pause() => Send(CmdPause);
    public void SetPlaybackPermission(bool allowed)
    {
        var changed = playbackAllowed != allowed;
        playbackAllowed = allowed;
        if (!allowed && playerWindow != IntPtr.Zero && (changed || playing)) Pause();
    }

    public void Play() { if (!playbackAllowed) return; Send(CmdPlay); ApplyPendingSpeed(); }
    public async Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(disposed, this);
        var target = playerWindow;
        if (target == IntPtr.Zero) throw new InvalidOperationException("O MPC-HC ainda não está conectado ao AniT.");
        var sender = hostWindow.Handle;
        Trace($"Enviando seek: {position.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)}s.");
        // Seeking can synchronously wait for the splitter to read a remote range. Keep the
        // notification window pumping while MPC-HC processes it; never retry a sent seek.
        await seekGate.WaitAsync(cancellationToken);
        if (disposed || playerWindow != target) {
            seekGate.Release();
            throw new InvalidOperationException("O MPC-HC foi desconectado antes da busca.");
        }
        var sending = Task.Run(() => {
            try { SendTo(target, sender, CmdSetPosition,
                position.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture), 45000, waitForBuffering: true); }
            finally { seekGate.Release(); }
        });
        // The worker owns the COPYDATA buffers until SendMessageTimeout actually returns,
        // even if the caller stops waiting. Observe a later failure after cancellation.
        _ = sending.ContinueWith(task => _ = task.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        await sending.WaitAsync(cancellationToken);
    }

    public async Task<TimeSpan> GetPositionAsync(CancellationToken cancellationToken)
    {
        await positionGate.WaitAsync(cancellationToken);
        var pending = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            positionRequest = pending;
            Trace("Solicitando posição atual.");
            Send(CmdGetCurrentPosition);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            using var registration = timeout.Token.Register(() => pending.TrySetCanceled(timeout.Token));
            return await pending.Task;
        }
        finally
        {
            if (ReferenceEquals(positionRequest, pending)) positionRequest = null;
            positionGate.Release();
        }
    }

    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmCopyData) return IntPtr.Zero;
        var data = Marshal.PtrToStructure<CopyDataStruct>(lParam);
        var command = unchecked((int)data.Data.ToInt64());
        var payload = data.DataPointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(data.DataPointer, data.DataLength / sizeof(char))?.TrimEnd('\0') ?? string.Empty;
        HandleNotification(command, payload);
        handled = true;
        return new IntPtr(1);
    }

    private void HandleNotification(int command, string payload)
    {
        switch (command)
        {
            case CmdConnect:
                if (long.TryParse(payload, NumberStyles.Integer, CultureInfo.InvariantCulture, out var handle)) connection?.TrySetResult(new IntPtr(handle));
                break;
            case CmdState:
                Trace($"Estado de carregamento: {payload}.");
                break;
            case CmdPlayMode:
                playing = payload == "0";
                StateChanged?.Invoke(this, payload switch { "0" => global::AniT.Core.PlayerState.Playing, "1" => global::AniT.Core.PlayerState.Paused, _ => global::AniT.Core.PlayerState.Stopped });
                if ((pauseOnLoad || !playbackAllowed) && playing) Pause();
                else ApplyPendingSpeed();
                break;
            case CmdNowPlaying:
                var parts = SplitFields(payload);
                if (parts.Length >= 5) CurrentFilePath = parts[3];
                if (parts.Length >= 5 && double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)) duration = TimeSpan.FromSeconds(seconds);
                Trace($"Arquivo carregado; duração: {duration?.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) ?? "desconhecida"}s.");
                ScheduleResumeSeekAfterGraphInitialization();
                ApplyPlaybackOptions();
                if (pauseOnLoad) { Pause(); filePrepared?.TrySetResult(); }
                NowPlaying?.Invoke(this, payload);
                break;
            case CmdListAudioTracks:
                AudioTrack = int.TryParse(payload.Split('|')[^1], out var audioIndex) && audioIndex >= 0 ? audioIndex : null;
                SelectPreferredTrack(payload, playbackOptions.PreferredAudioLanguage, CmdSetAudioTrack, allowDisabled: false);
                break;
            case CmdListSubtitleTracks:
                SubtitleTrack = int.TryParse(payload.Split('|')[^1], out var subtitleIndex) && subtitleIndex >= -1 ? subtitleIndex : null;
                SelectPreferredTrack(payload, playbackOptions.PreferredSubtitleLanguage, CmdSetSubtitleTrack, allowDisabled: true);
                break;
            case CmdCurrentPosition:
            case CmdNotifySeek:
                if (double.TryParse(payload, NumberStyles.Float, CultureInfo.InvariantCulture, out var current))
                {
                    var value = TimeSpan.FromSeconds(current);
                    lastPosition = value;
                    Trace($"Posição recebida: {current.ToString("0.###", CultureInfo.InvariantCulture)}s.");
                    positionRequest?.TrySetResult(value);
                    PositionReceived?.Invoke(this, value);
                }
                break;
            case CmdEndOfStream:
                EndOfStream?.Invoke(this, EventArgs.Empty);
                break;
            case CmdDisconnect:
                Trace($"MPC-HC desconectado; última posição: {lastPosition?.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) ?? "indisponível"}s.");
                Disconnected?.Invoke(this, lastPosition);
                filePrepared?.TrySetException(new IOException("O player foi fechado durante a preparação."));
                playing = false; appliedSpeed = null;
                playerWindow = IntPtr.Zero;
                connection = null;
                fullscreenApplied = false;
                break;
        }
    }

    private static string[] SplitFields(string payload)
    {
        var fields = new System.Collections.Generic.List<string>();
        var text = new System.Text.StringBuilder();
        for (var i = 0; i < payload.Length; i++)
        {
            if (payload[i] == '\\' && i + 1 < payload.Length && payload[i + 1] == '|') { text.Append('|'); i++; }
            else if (payload[i] == '|') { fields.Add(text.ToString()); text.Clear(); }
            else text.Append(payload[i]);
        }
        fields.Add(text.ToString());
        return fields.ToArray();
    }

    private void ApplyPlaybackOptions()
    {
        if (playbackOptionsApplied) return;
        playbackOptionsApplied = true;
        SetSpeed(playbackOptions.Speed);
        if (playbackOptions.StartFullscreen && !fullscreenApplied)
        {
            Send(CmdToggleFullscreen);
            fullscreenApplied = true;
        }
        if (!playbackOptions.PreferredAudioLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase)) Send(CmdGetAudioTracks);
        if (playbackOptions.PreferredSubtitleLanguage.Equals("off", StringComparison.OrdinalIgnoreCase)) Send(CmdSetSubtitleTrack, "-1");
        else if (!playbackOptions.PreferredSubtitleLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase)) Send(CmdGetSubtitleTracks);
    }

    private void SelectPreferredTrack(string payload, string language, int command, bool allowDisabled)
    {
        var parts = payload.Split('|');
        if (parts.Length <= 1 || parts[0] is "-1" or "-2") return;
        var trackCount = parts.Length - 1; // the final value is the active track index
        var aliases = LanguageAliases(language);
        var selected = Enumerable.Range(0, trackCount)
            .FirstOrDefault(index => aliases.Any(alias => parts[index].Contains(alias, StringComparison.OrdinalIgnoreCase)), -1);
        if (selected >= 0) Send(command, selected.ToString(CultureInfo.InvariantCulture));
        else if (allowDisabled && language.Equals("off", StringComparison.OrdinalIgnoreCase)) Send(command, "-1");
    }

    private static string[] LanguageAliases(string language) => language.ToLowerInvariant() switch
    {
        "pt-br" => ["pt-br", "pt_br", "portuguese", "português", "brazil", "por"],
        "ja" => ["japanese", "japonês", "jpn", "[ja]", "_ja"],
        "en" => ["english", "inglês", "eng", "[en]", "_en"],
        "es" => ["spanish", "español", "espanhol", "spa", "[es]", "_es"],
        _ => [language]
    };

    private void ScheduleResumeSeekAfterGraphInitialization()
    {
        // Wait for NOWPLAYING: MLS_LOADED does not include the duration yet and its
        // graph may still reset an early seek. Validate legacy checkpoints before sending.
        if (resumeSeekReapplied || resumePosition is not { } position || position <= TimeSpan.Zero) return;
        resumeSeekReapplied = true;
        var validated = global::AniT.Core.PlaybackEvidence.ResumePosition(position, duration);
        if (validated is null) {
            Trace("Ponto de retomada no final do episódio; iniciando do começo.");
            position = TimeSpan.Zero;
        } else position = validated.Value;
        var expectedWindow = playerWindow;
        _ = ReapplySeekAsync(expectedWindow, position);
    }

    private async Task ReapplySeekAsync(IntPtr expectedWindow, TimeSpan position)
    {
        // NOWPLAYING is emitted after the graph is ready. A very short yield lets MPC-HC
        // complete its own notification work without leaving the opening frame visible.
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        if (playerWindow != expectedWindow || playerWindow == IntPtr.Zero) return;
        Trace($"Reaplicando seek após inicialização: {position.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)}s.");
        await SeekAfterNotificationAsync(position);
    }

    private async Task SeekAfterNotificationAsync(TimeSpan position)
    {
        try { await SeekAsync(position); }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or ObjectDisposedException)
        { Trace($"Não foi possível aplicar a posição inicial: {ex.Message}"); }
    }

    private void Trace(string message) => Diagnostic?.Invoke(this, message);

    private void Send(int command, string payload = "")
    {
        if (playerWindow == IntPtr.Zero) throw new InvalidOperationException("O MPC-HC ainda não está conectado ao AniT.");
        SendTo(playerWindow, hostWindow.Handle, command, payload, independent ? 1500u : 0u);
    }

    private static void SendTo(IntPtr target, IntPtr sender, int command, string payload, uint timeout, bool waitForBuffering = false)
    {
        var text = payload + '\0';
        var dataPointer = Marshal.StringToHGlobalUni(text);
        var structPointer = Marshal.AllocHGlobal(Marshal.SizeOf<CopyDataStruct>());
        try
        {
            // dwData is an unsigned pointer-sized field. Commands such as CMD_OPENFILE have the high bit set;
            // zero-extension is required on x64 or MPC-HC will not recognize the command.
            Marshal.StructureToPtr(new CopyDataStruct { Data = new IntPtr(unchecked((long)(uint)command)), DataLength = text.Length * sizeof(char), DataPointer = dataPointer }, structPointer, false);
            if (timeout > 0)
            {
                if (SendMessageTimeout(target, WmCopyData, sender, structPointer, waitForBuffering ? 0u : 2u, timeout, out _) == IntPtr.Zero)
                    throw new TimeoutException("O MPC-HC não respondeu ao comando dentro do prazo.");
            }
            else SendMessage(target, WmCopyData, sender, structPointer);
        }
        finally
        {
            Marshal.FreeHGlobal(structPointer);
            Marshal.FreeHGlobal(dataPointer);
        }
    }

    public int? AudioTrack { get; private set; }
    public int? SubtitleTrack { get; private set; }
    public string? CurrentFilePath { get; private set; }
    public void RefreshTracks() { Send(CmdGetAudioTracks); Send(CmdGetSubtitleTracks); }
    public void SetTracks(int? audio, int? subtitle)
    {
        if (audio is >= 0 and <= 1024 && audio != AudioTrack) { Send(CmdSetAudioTrack, audio.Value.ToString(CultureInfo.InvariantCulture)); AudioTrack = audio; }
        if (subtitle is >= -1 and <= 1024 && subtitle != SubtitleTrack) { Send(CmdSetSubtitleTrack, subtitle.Value.ToString(CultureInfo.InvariantCulture)); SubtitleTrack = subtitle; }
    }
    public TimeSpan? Duration => duration;
    public IntPtr WindowHandle => playerWindow;

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (ownedProcess is not null)
        {
            try
            {
                if (!ownedProcess.HasExited)
                {
                    if (playerWindow != IntPtr.Zero) PostMessage(playerWindow, 0x0010, IntPtr.Zero, IntPtr.Zero);
                    else if (!ownedProcess.CloseMainWindow()) ownedProcess.Kill();
                }
            }
            catch (InvalidOperationException) { }
            var closing = ownedProcess;
            ownedProcess = null;
            if (independent) _ = FinishClosingOwnedProcessAsync(closing);
            else closing.Dispose();
        }
        filePrepared?.TrySetCanceled();
        positionRequest?.TrySetCanceled();
        hostWindow.RemoveHook(WindowProcedure);
        hostWindow.Dispose();
    }

    private static async Task FinishClosingOwnedProcessAsync(Process process)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                // Only /new Movie Theater instances created and owned by this bridge reach this path.
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        finally { process.Dispose(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyDataStruct
    {
        public IntPtr Data;
        public int DataLength;
        public IntPtr DataPointer;
    }

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, int message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}

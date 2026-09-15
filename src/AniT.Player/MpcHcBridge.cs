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
    private const int CmdCurrentPosition = unchecked((int)0x50000007);
    private const int CmdNotifySeek = unchecked((int)0x50000008);
    private const int CmdEndOfStream = unchecked((int)0x50000009);
    private const int CmdDisconnect = unchecked((int)0x5000000B);
    private const int CmdOpenFile = unchecked((int)0xA0000000);
    private const int CmdPlay = unchecked((int)0xA0000004);
    private const int CmdPause = unchecked((int)0xA0000005);
    private const int CmdSetPosition = unchecked((int)0xA0002000);
    private const int CmdGetCurrentPosition = unchecked((int)0xA0003004);

    private readonly HwndSource hostWindow;
    private TaskCompletionSource<IntPtr>? connection;
    private TaskCompletionSource<TimeSpan>? positionRequest;
    private IntPtr playerWindow;
    private TimeSpan? pendingStartPosition;
    private TimeSpan? duration;
    private TimeSpan? lastPosition;

    public event EventHandler<TimeSpan>? PositionReceived;
    public event EventHandler? EndOfStream;
    public event EventHandler<Core.PlayerState>? StateChanged;
    public event EventHandler<string>? NowPlaying;
    public event EventHandler<TimeSpan?>? Disconnected;
    public event EventHandler<string>? Diagnostic;

    public MpcHcBridge()
    {
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
        var pendingConnection = new TaskCompletionSource<IntPtr>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection = pendingConnection;

        var process = Process.Start(new ProcessStartInfo(executablePath, $"/slave {hostWindow.Handle}") { UseShellExecute = false });
        if (process is null) throw new InvalidOperationException("Não foi possível iniciar o MPC-HC.");
        Trace($"MPC-HC iniciado; aguardando conexão (PID {process.Id}).");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var registration = timeout.Token.Register(() => pendingConnection.TrySetCanceled(timeout.Token));
        playerWindow = await pendingConnection.Task;
        Trace($"Conectado ao HWND {playerWindow}.");
    }

    public void OpenFile(string path, TimeSpan? startPosition)
    {
        pendingStartPosition = startPosition;
        lastPosition = null;
        duration = null;
        Trace($"Abrindo arquivo: {path}; posição para retomar: {startPosition?.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) ?? "0"}s.");
        Send(CmdOpenFile, path);
    }

    public void Pause() => Send(CmdPause);
    public void Play() => Send(CmdPlay);
    public void Seek(TimeSpan position)
    {
        Trace($"Enviando seek: {position.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)}s.");
        Send(CmdSetPosition, position.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
    }

    public async Task<TimeSpan> GetPositionAsync(CancellationToken cancellationToken)
    {
        positionRequest = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        Trace("Solicitando posição atual.");
        Send(CmdGetCurrentPosition);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        using var registration = timeout.Token.Register(() => positionRequest.TrySetCanceled(timeout.Token));
        return await positionRequest.Task;
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
                if (payload == "2") ApplyPendingSeek();
                break;
            case CmdPlayMode:
                StateChanged?.Invoke(this, payload switch { "0" => global::AniT.Core.PlayerState.Playing, "1" => global::AniT.Core.PlayerState.Paused, _ => global::AniT.Core.PlayerState.Stopped });
                break;
            case CmdNowPlaying:
                var parts = payload.Split('|');
                if (parts.Length >= 5 && double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)) duration = TimeSpan.FromSeconds(seconds);
                Trace($"Arquivo carregado; duração: {duration?.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) ?? "desconhecida"}s.");
                ApplyPendingSeek();
                NowPlaying?.Invoke(this, payload);
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
                playerWindow = IntPtr.Zero;
                connection = null;
                break;
        }
    }

    private void ApplyPendingSeek()
    {
        if (pendingStartPosition is not { } position || position <= TimeSpan.Zero) return;
        Seek(position);
        pendingStartPosition = null;
    }

    private void Trace(string message) => Diagnostic?.Invoke(this, message);

    private void Send(int command, string payload = "")
    {
        if (playerWindow == IntPtr.Zero) throw new InvalidOperationException("O MPC-HC ainda não está conectado ao AniT.");
        var text = payload + '\0';
        var dataPointer = Marshal.StringToHGlobalUni(text);
        var structPointer = Marshal.AllocHGlobal(Marshal.SizeOf<CopyDataStruct>());
        try
        {
            // dwData is an unsigned pointer-sized field. Commands such as CMD_OPENFILE have the high bit set;
            // zero-extension is required on x64 or MPC-HC will not recognize the command.
            Marshal.StructureToPtr(new CopyDataStruct { Data = new IntPtr(unchecked((long)(uint)command)), DataLength = text.Length * sizeof(char), DataPointer = dataPointer }, structPointer, false);
            SendMessage(playerWindow, WmCopyData, hostWindow.Handle, structPointer);
        }
        finally
        {
            Marshal.FreeHGlobal(structPointer);
            Marshal.FreeHGlobal(dataPointer);
        }
    }

    public TimeSpan? Duration => duration;

    public void Dispose()
    {
        hostWindow.RemoveHook(WindowProcedure);
        hostWindow.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyDataStruct
    {
        public IntPtr Data;
        public int DataLength;
        public IntPtr DataPointer;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}

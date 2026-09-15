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
    private readonly TaskCompletionSource<IntPtr> connection = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<TimeSpan>? positionRequest;
    private IntPtr playerWindow;
    private TimeSpan? pendingStartPosition;
    private TimeSpan? duration;

    public event EventHandler<TimeSpan>? PositionReceived;
    public event EventHandler? EndOfStream;
    public event EventHandler<Core.PlayerState>? StateChanged;
    public event EventHandler<string>? NowPlaying;

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

        var process = Process.Start(new ProcessStartInfo(executablePath, $"/slave {hostWindow.Handle}") { UseShellExecute = false });
        if (process is null) throw new InvalidOperationException("Não foi possível iniciar o MPC-HC.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var registration = timeout.Token.Register(() => connection.TrySetCanceled(timeout.Token));
        playerWindow = await connection.Task;
    }

    public void OpenFile(string path, TimeSpan? startPosition)
    {
        pendingStartPosition = startPosition;
        Send(CmdOpenFile, path);
    }

    public void Pause() => Send(CmdPause);
    public void Play() => Send(CmdPlay);
    public void Seek(TimeSpan position) => Send(CmdSetPosition, position.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));

    public async Task<TimeSpan> GetPositionAsync(CancellationToken cancellationToken)
    {
        positionRequest = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
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
                if (long.TryParse(payload, NumberStyles.Integer, CultureInfo.InvariantCulture, out var handle)) connection.TrySetResult(new IntPtr(handle));
                break;
            case CmdState:
                if (payload == "2" && pendingStartPosition is { } position) { Seek(position); pendingStartPosition = null; }
                break;
            case CmdPlayMode:
                StateChanged?.Invoke(this, payload switch { "0" => global::AniT.Core.PlayerState.Playing, "1" => global::AniT.Core.PlayerState.Paused, _ => global::AniT.Core.PlayerState.Stopped });
                break;
            case CmdNowPlaying:
                var parts = payload.Split('|');
                if (parts.Length >= 5 && double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)) duration = TimeSpan.FromSeconds(seconds);
                NowPlaying?.Invoke(this, payload);
                break;
            case CmdCurrentPosition:
            case CmdNotifySeek:
                if (double.TryParse(payload, NumberStyles.Float, CultureInfo.InvariantCulture, out var current))
                {
                    var value = TimeSpan.FromSeconds(current);
                    positionRequest?.TrySetResult(value);
                    PositionReceived?.Invoke(this, value);
                }
                break;
            case CmdEndOfStream:
                EndOfStream?.Invoke(this, EventArgs.Empty);
                break;
            case CmdDisconnect:
                playerWindow = IntPtr.Zero;
                break;
        }
    }

    private void Send(int command, string payload = "")
    {
        if (playerWindow == IntPtr.Zero) throw new InvalidOperationException("O MPC-HC ainda não está conectado ao AniT.");
        var text = payload + '\0';
        var dataPointer = Marshal.StringToHGlobalUni(text);
        var structPointer = Marshal.AllocHGlobal(Marshal.SizeOf<CopyDataStruct>());
        try
        {
            Marshal.StructureToPtr(new CopyDataStruct { Data = new IntPtr(command), DataLength = text.Length * sizeof(char), DataPointer = dataPointer }, structPointer, false);
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

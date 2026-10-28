using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AniT.Player;

/// <summary>A native video-only window, isolated from WPF's DPI context. Normal input is blocked while room IPC remains available.</summary>
public sealed class MpcReadOnlyView : IDisposable
{
    private const long Child = 0x40000000, Visible = 0x10000000, Popup = 0x80000000, Caption = 0x00C00000, ThickFrame = 0x00040000, Minimized = 0x20000000;
    private const int StyleIndex = -16;
    private static readonly ConcurrentDictionary<IntPtr, MpcReadOnlyView> views = new();
    private static readonly WindowProc procedure = Dispatch;
    private static readonly IntPtr instance = GetModuleHandle(null);
    private const string ClassName = "AniT.MovieTheater.VideoOnly";
    private readonly IntPtr playerWindow, owner;
    private IntPtr window, container;
    private long originalStyle;
    private bool fullscreen, closing;
    private NativeRect previousBounds;
    public event EventHandler? Closed;
    public IntPtr WindowHandle => window;
    public bool IsInputBlocked => container != IntPtr.Zero && !IsWindowEnabled(container);
    public bool HasPlayerMenu => GetMenu(playerWindow) != IntPtr.Zero;

    static MpcReadOnlyView()
    {
        var type = new WindowClass { Size = (uint)Marshal.SizeOf<WindowClass>(), Style = 8, Procedure = Marshal.GetFunctionPointerForDelegate(procedure), Instance = instance, Cursor = LoadCursor(IntPtr.Zero, new IntPtr(32512)), Background = GetStockObject(4), ClassName = ClassName };
        if (RegisterClassEx(ref type) == 0 && Marshal.GetLastWin32Error() != 1410) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public MpcReadOnlyView(IntPtr playerWindow, IntPtr owner) { this.playerWindow = playerWindow; this.owner = owner; }

    public void Show()
    {
        if (window != IntPtr.Zero) { ShowWindow(window, 5); return; }
        if (!IsWindow(playerWindow)) throw new InvalidOperationException("O player foi fechado antes de preparar a tela.");
        var previous = SetThreadDpiAwarenessContext(GetWindowDpiAwarenessContext(playerWindow));
        try
        {
            var monitor = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() }; GetMonitorInfo(MonitorFromWindow(owner, 2), ref monitor);
            var width = Math.Min(960, Math.Max(320, monitor.Work.Right - monitor.Work.Left - 60)); var height = Math.Min(600, Math.Max(200, monitor.Work.Bottom - monitor.Work.Top - 60));
            window = CreateWindowEx(0, ClassName, "Movie Theater · Tela do participante", 0x00CF0000, monitor.Work.Left + 30, monitor.Work.Top + 30, width, height, owner, IntPtr.Zero, instance, IntPtr.Zero);
            if (window == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível criar a tela do participante.");
            views[window] = this;
            container = CreateWindowEx(0, ClassName, "", unchecked((int)(Child | Visible | 0x02000000 | 0x08000000)), 0, 0, 1, 1, window, IntPtr.Zero, instance, IntPtr.Zero);
            if (container == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível criar a superfície de vídeo.");
            originalStyle = GetWindowLongPtr(playerWindow, StyleIndex).ToInt64();
            EnableWindow(playerWindow, false);
            SetWindowLongPtr(playerWindow, StyleIndex, new IntPtr((originalStyle & ~(Popup | Caption | ThickFrame | Minimized)) | Child | Visible));
            SetParent(playerWindow, container);
            if (GetParent(playerWindow) != container) throw new Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível incorporar o vídeo.");
            SetMenu(playerWindow, IntPtr.Zero); ShowWindow(playerWindow, 4);
            ShowWindow(window, 5); RefreshBounds();
        }
        catch { Close(); throw; }
        finally { if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous); }
    }

    public void RefreshBounds()
    {
        if (window == IntPtr.Zero || container == IntPtr.Zero || !GetClientRect(window, out var rect)) return;
        EnableWindow(playerWindow, false); SetMenu(playerWindow, IntPtr.Zero);
        MoveWindow(container, 0, 0, Math.Max(1, rect.Right), Math.Max(1, rect.Bottom), true);
        MoveWindow(playerWindow, 0, 0, Math.Max(1, rect.Right), Math.Max(1, rect.Bottom), true);
    }
    public void ToggleFullscreen()
    {
        if (window == IntPtr.Zero) return;
        if (!fullscreen)
        {
            GetWindowRect(window, out previousBounds);
            var monitor = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() }; GetMonitorInfo(MonitorFromWindow(window, 2), ref monitor);
            SetWindowLongPtr(window, StyleIndex, new IntPtr(Popup | Visible));
            SetWindowPos(window, IntPtr.Zero, monitor.Bounds.Left, monitor.Bounds.Top, monitor.Bounds.Right - monitor.Bounds.Left, monitor.Bounds.Bottom - monitor.Bounds.Top, 0x0024);
        }
        else
        {
            SetWindowLongPtr(window, StyleIndex, new IntPtr(0x00CF0000 | Visible));
            SetWindowPos(window, IntPtr.Zero, previousBounds.Left, previousBounds.Top, previousBounds.Right - previousBounds.Left, previousBounds.Bottom - previousBounds.Top, 0x0024);
        }
        fullscreen = !fullscreen; RefreshBounds();
    }
    public void Close()
    {
        if (window == IntPtr.Zero || closing) return;
        closing = true;
        var handle = window;
        if (container != IntPtr.Zero && IsWindow(playerWindow) && GetParent(playerWindow) == container)
        {
            ShowWindow(playerWindow, 0); SetParent(playerWindow, IntPtr.Zero); SetWindowLongPtr(playerWindow, StyleIndex, new IntPtr(originalStyle));
        }
        views.TryRemove(handle, out _); DestroyWindow(handle); window = container = IntPtr.Zero;
        Closed?.Invoke(this, EventArgs.Empty);
    }
    public void Dispose() => Close();

    private static IntPtr Dispatch(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (!views.TryGetValue(hwnd, out var view)) return DefWindowProc(hwnd, message, wParam, lParam);
        switch (message)
        {
            case 0x0010: view.Close(); return IntPtr.Zero;
            case 0x0005: view.RefreshBounds(); break;
            case 0x0203: view.ToggleFullscreen(); return IntPtr.Zero;
            case 0x0100:
            case 0x0104:
                var key = wParam.ToInt32();
                if (key == 0x7A || key == 0x0D && message == 0x0104) view.ToggleFullscreen();
                else if (key == 0x1B && view.fullscreen) view.ToggleFullscreen();
                else if (key == 0x73 && message == 0x0104) return DefWindowProc(hwnd, message, wParam, lParam);
                else if (key is >= 0xAD and <= 0xAF) return DefWindowProc(hwnd, message, wParam, lParam);
                return IntPtr.Zero;
            case 0x0319:
                var command = (lParam.ToInt64() >> 16) & 0x7FF;
                if (command is (>= 11 and <= 14) or (>= 46 and <= 52)) return new IntPtr(1);
                break;
            case 0x02E0:
                var bounds = Marshal.PtrToStructure<NativeRect>(lParam);
                SetWindowPos(hwnd, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, 0x0014); view.RefreshBounds(); return IntPtr.Zero;
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WindowClass { public uint Size, Style; public IntPtr Procedure; public int ClassExtra, WindowExtra; public IntPtr Instance, Icon, Cursor, Background; public string? MenuName; public string ClassName; public IntPtr SmallIcon; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public uint Size; public NativeRect Bounds, Work; public uint Flags; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WindowClass type);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(int exStyle, string className, string title, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int type);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetParent(IntPtr window, IntPtr parent);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool EnableWindow(IntPtr window, bool enabled);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern IntPtr GetMenu(IntPtr window);
    [DllImport("user32.dll")] private static extern bool SetMenu(IntPtr window, IntPtr menu);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}

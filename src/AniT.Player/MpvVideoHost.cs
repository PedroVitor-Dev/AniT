using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace AniT.Player;

public sealed class VideoPointerEventArgs(string action, uint timestamp, int x, int y) : EventArgs
{
    public string Action { get; } = action;
    public uint Timestamp { get; } = timestamp;
    public int X { get; } = x;
    public int Y { get; } = y;
}

/// <summary>Owns the native parent surface, including mouse input when the embedded child is disabled.</summary>
public sealed class MpvVideoHost : HwndHost
{
    public event EventHandler<VideoPointerEventArgs>? PointerInput;
    public bool PointerHidden { get; private set; }
    Point? lastPointer;
    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        // SS_NOTIFY prevents STATIC from returning HTTRANSPARENT and losing mouse input.
        var handle = CreateWindowEx(0, "static", "AniT video", 0x40000000 | 0x10000000 | 0x02000000 | 0x04000000 | 0x00000100,
            0, 0, 1, 1, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new HandleRef(this, handle);
    }
    public bool IsPointerOver {
        get { if (Handle == IntPtr.Zero || !GetCursorPos(out var point)) return false;
            var target = WindowFromPoint(point); return target == Handle || IsChild(Handle, target); }
    }
    public void SetPointerHidden(bool hidden)
    {
        PointerHidden = hidden;
        if (IsPointerOver) SetCursor(hidden ? IntPtr.Zero : LoadCursor(IntPtr.Zero, new IntPtr(32512)));
    }
    void Notify(string action, Point point)
    {
        var input = new VideoPointerEventArgs(action, unchecked((uint)Environment.TickCount), point.X, point.Y);
        // Do not resize a cross-process child during its synchronous parent notification.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (Handle != IntPtr.Zero) PointerInput?.Invoke(this, input); }));
    }
    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        var mouse = msg == 0x210 ? (int)(wParam.ToInt64() & 0xffff) : msg;
        if (mouse is 0x201 or 0x203 or 0x204 or 0x206) {
            var point = new Point { X = unchecked((short)(lParam.ToInt64() & 0xffff)), Y = unchecked((short)((lParam.ToInt64() >> 16) & 0xffff)) };
            ClientToScreen(hwnd, ref point);
            SetPointerHidden(false);
            Notify(mouse is 0x204 or 0x206 ? "menu" : "press", point);
            if (msg != 0x210) handled = true;
            return IntPtr.Zero;
        }
        if (msg == 0x200 && GetCursorPos(out var position)) {
            if (lastPointer is not { } previous || previous.X != position.X || previous.Y != position.Y) {
                lastPointer = position; SetPointerHidden(false); Notify("pointer", position);
            }
        }
        if (msg == 0x20) {
            SetCursor(PointerHidden ? IntPtr.Zero : LoadCursor(IntPtr.Zero, new IntPtr(32512)));
            handled = true; return new IntPtr(1);
        }
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }
    protected override void DestroyWindowCore(HandleRef handle) { SetPointerHidden(false); DestroyWindow(handle.Handle); }
    [StructLayout(LayoutKind.Sequential)] struct Point { public int X, Y; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] static extern bool IsChild(IntPtr parent, IntPtr child);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
    [DllImport("user32.dll")] static extern IntPtr SetCursor(IntPtr cursor);
    [DllImport("user32.dll")] static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursor);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateWindowEx(int extendedStyle, string className, string title, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr handle);
}

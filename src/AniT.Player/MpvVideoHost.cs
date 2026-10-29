using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace AniT.Player;

/// <summary>Owns only the native parent surface. mpv owns its child and GPU renderer.</summary>
public sealed class MpvVideoHost : HwndHost
{
    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        var handle = CreateWindowEx(0, "static", "AniT video", 0x40000000 | 0x10000000 | 0x02000000 | 0x04000000,
            0, 0, 1, 1, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new HandleRef(this, handle);
    }
    protected override void DestroyWindowCore(HandleRef handle) => DestroyWindow(handle.Handle);
    internal static uint DoubleClickMilliseconds => GetDoubleClickTime();
    [DllImport("user32.dll")] static extern uint GetDoubleClickTime();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateWindowEx(int extendedStyle, string className, string title, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr handle);
}

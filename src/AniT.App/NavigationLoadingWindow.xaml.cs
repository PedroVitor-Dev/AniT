using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace AniT.App;

public partial class NavigationLoadingWindow : Window
{
    public NavigationLoadingWindow(Window source)
    {
        InitializeComponent();
        Match(source);
    }

    internal void PrepareFor(Window source) => Match(source);

    private void Match(Window source)
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowState = WindowState.Normal;

        var bounds = GetWindowBounds(source);

        if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0)
        {
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
        }
    }

    private static Rect GetWindowBounds(Window source)
    {
        var handle = new WindowInteropHelper(source).Handle;
        if (handle != IntPtr.Zero && GetWindowRect(handle, out var rectangle))
        {
            var dpi = VisualTreeHelper.GetDpi(source);
            return new Rect(
                rectangle.Left / dpi.DpiScaleX,
                rectangle.Top / dpi.DpiScaleY,
                (rectangle.Right - rectangle.Left) / dpi.DpiScaleX,
                (rectangle.Bottom - rectangle.Top) / dpi.DpiScaleY);
        }

        return source.WindowState == WindowState.Normal
            ? new Rect(source.Left, source.Top, source.ActualWidth, source.ActualHeight)
            : source.RestoreBounds;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rectangle);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

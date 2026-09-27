using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;

namespace AniT.App;

public partial class AniTNotificationToastWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    public AniTNotificationToastWindow(string title, string message)
    {
        InitializeComponent();
        DataContext = new NotificationToastModel(title, message);
        SourceInitialized += (_, _) => ApplyWindowChrome();
    }

    public async Task ShowToastAsync(CancellationToken cancellationToken = default)
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 26;
        Top = area.Bottom - Height - 26;
        Show();
        var reduceMotion = global::AniT.Infrastructure.AniTSystemSettingsStore.Load().ReduceMotion;
        var enter = TimeSpan.FromMilliseconds(reduceMotion ? 100 : 230);
        ToastBorder.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, enter));
        if (!reduceMotion)
            ToastTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(18, 0, enter) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        await Task.Delay(4200, cancellationToken);
        var exit = TimeSpan.FromMilliseconds(reduceMotion ? 100 : 220);
        ToastBorder.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, exit));
        await Task.Delay(exit, cancellationToken);
        Close();
    }

    private void ApplyWindowChrome()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExNoActivate | WsExToolWindow);
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr handle, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr handle, int index, int newStyle);

    private sealed record NotificationToastModel(string Title, string Message);
}

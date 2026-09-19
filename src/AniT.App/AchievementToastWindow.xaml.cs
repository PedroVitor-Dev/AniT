using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AniT.Core.Achievements;

namespace AniT.App;

public partial class AchievementToastWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private readonly AchievementSettings settings;

    public AchievementToastWindow(AchievementUnlock unlock, AchievementSettings settings)
    {
        InitializeComponent();
        this.settings = settings;
        DataContext = new ToastModel(
            unlock.Definition.IconPath,
            unlock.Definition.Name,
            unlock.Points,
            AchievementLabels.Rarity(unlock.Definition.Rarity),
            new SolidColorBrush(ColorFor(unlock.Definition.Rarity)));
        var color = ColorFor(unlock.Definition.Rarity);
        ToastBorder.BorderBrush = new SolidColorBrush(color);
        SourceInitialized += (_, _) => ApplyWindowChrome();
    }

    public async Task ShowToastAsync(CancellationToken cancellationToken = default)
    {
        PositionWindow();
        Show();

        if (settings.ReduceAnimations)
        {
            ToastScale.ScaleX = ToastScale.ScaleY = 1;
            ToastTranslate.Y = 0;
            ToastBorder.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }
        else
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            ToastBorder.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
            ToastScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease });
            ToastScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease });
            ToastTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease });
        }

        await Task.Delay(DataContext is ToastModel { Points: 500 } ? 6000 : 4000, cancellationToken);
        var exit = TimeSpan.FromMilliseconds(settings.ReduceAnimations ? 180 : 250);
        ToastBorder.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, exit));
        if (!settings.ReduceAnimations)
            ToastTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 15, exit));
        await Task.Delay(exit, cancellationToken);
        Close();
    }

    private void PositionWindow()
    {
        var area = SystemParameters.WorkArea;
        const double gap = 28;
        switch (settings.Position)
        {
            case AchievementToastPosition.BottomRight:
                Left = area.Right - Width - gap;
                Top = area.Bottom - Height - gap;
                break;
            case AchievementToastPosition.TopRight:
                Left = area.Right - Width - gap;
                Top = area.Top + gap;
                break;
            default:
                Left = area.Left + (area.Width - Width) / 2;
                Top = area.Bottom - Height - gap;
                break;
        }
    }

    private void ApplyWindowChrome()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExNoActivate | WsExToolWindow);
        var region = CreateRoundRectRgn(0, 0, (int)Math.Ceiling(Width) + 1, (int)Math.Ceiling(Height) + 1, 48, 48);
        if (region != IntPtr.Zero && SetWindowRgn(handle, region, true) == 0)
            DeleteObject(region);
    }

    private static Color ColorFor(AchievementRarity rarity) => rarity switch
    {
        AchievementRarity.Uncommon => Color.FromRgb(83, 222, 166),
        AchievementRarity.Rare => Color.FromRgb(73, 198, 255),
        AchievementRarity.Epic => Color.FromRgb(180, 124, 255),
        AchievementRarity.Secret => Color.FromRgb(120, 107, 190),
        AchievementRarity.Legendary => Color.FromRgb(255, 205, 79),
        AchievementRarity.SupremeLegendary => Color.FromRgb(255, 220, 91),
        _ => Color.FromRgb(147, 180, 210)
    };

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr handle, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr handle, int index, int newStyle);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr handle, IntPtr region, bool redraw);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    private sealed record ToastModel(string IconPath, string Name, int Points, string RarityLabel, Brush AccentBrush);
}

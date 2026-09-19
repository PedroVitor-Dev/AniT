using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace AniT.App;

public partial class ProfileAvatarButton : UserControl
{
    private string? displayedPath;

    public ProfileAvatarButton() => InitializeComponent();

    private void UserControl_Loaded(object sender, RoutedEventArgs e) => RefreshAvatar();

    private void UserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) RefreshAvatar();
    }

    private void RefreshAvatar()
    {
        var configuredPath = ProfileSettingsStore.Load().AvatarPath;
        var path = !string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath)
            ? configuredPath
            : "pack://application:,,,/Assets/Profile/1.png";
        if (string.Equals(displayedPath, path, StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = path.StartsWith("pack:", StringComparison.OrdinalIgnoreCase)
                ? new Uri(path, UriKind.Absolute)
                : new Uri(Path.GetFullPath(path), UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            AvatarBrush.ImageSource = image;
            displayedPath = path;
        }
        catch
        {
            displayedPath = null;
            AvatarBrush.ImageSource = new BitmapImage(new Uri("pack://application:,,,/Assets/Profile/1.png", UriKind.Absolute));
        }
    }

    private void Profile_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } owner) AppNavigation.Profile(owner);
    }
}

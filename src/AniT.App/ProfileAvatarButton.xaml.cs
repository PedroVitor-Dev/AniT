using System.IO;
using System.Windows;
using System.Windows.Controls;

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
            AvatarBrush.ImageSource = ComfortableImageSource.Load(
                path.StartsWith("pack:", StringComparison.OrdinalIgnoreCase) ? path : Path.GetFullPath(path),
                128);
            displayedPath = path;
        }
        catch
        {
            displayedPath = null;
            AvatarBrush.ImageSource = ComfortableImageSource.Load("Assets/Profile/1.png", 128);
        }
    }

    private void Profile_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } owner) AppNavigation.Profile(owner);
    }
}

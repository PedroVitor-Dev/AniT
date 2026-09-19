using System.Windows;
using System.Windows.Controls;

namespace AniT.App;

public partial class SystemSettingsNavButton : UserControl
{
    public SystemSettingsNavButton() => InitializeComponent();

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } owner) AppNavigation.Settings(owner);
    }
}

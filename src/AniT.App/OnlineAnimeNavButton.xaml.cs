using System.Windows;
using System.Windows.Controls;
namespace AniT.App;
public partial class OnlineAnimeNavButton : UserControl
{
 public OnlineAnimeNavButton() => InitializeComponent();
 private void Open_Click(object sender, RoutedEventArgs e) { if (Window.GetWindow(this) is { } owner) AppNavigation.OpenOnline(owner); }
}

using System.Windows;
using System.Windows.Controls;

namespace AniT.App;

public partial class DashboardPoster : UserControl
{
    public DashboardPoster() => InitializeComponent();
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardCard card && Window.GetWindow(this) is DashboardWindow owner)
            AppNavigation.OpenAnimeDetails(owner, card.AnimeId);
    }
    private void Cover_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Image image && Window.GetWindow(this) is DashboardWindow owner) owner.ApplyCoverAppearance(image);
    }
    private void Artwork_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Border border) RoundedPanelClip.Update(border);
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AniT.App;

/// <summary>Layout in logical DIPs after interface scaling. Shared by the real window and its fixture.</summary>
internal static class DashboardLayout
{
    public static void Update(Window window)
    {
        if (window.FindName("MainContentHost") is not FrameworkElement host || host.ActualWidth <= 0) return;
        var width = host.ActualWidth;
        var hero = (Grid)window.FindName("HeroLayout");
        var art = (Border)window.FindName("HeroArtwork");
        var showArt = width >= 820;
        hero.ColumnDefinitions[1].Width = new GridLength(showArt ? (width >= 1250 ? 400 : 300) : 0);
        art.Visibility = showArt ? Visibility.Visible : Visibility.Collapsed;
        var rail = (StackPanel)window.FindName("RightRail");
        var railColumn = (ColumnDefinition)window.FindName("RightRailColumn");
        var sections = (StackPanel)window.FindName("HomeSectionsHost");
        var wide = width >= 1180;
        railColumn.Width = new GridLength(wide ? 300 : 0);
        Grid.SetColumn(rail, wide ? 1 : 0); Grid.SetRow(rail, wide ? 0 : 1); Grid.SetColumnSpan(rail, wide ? 1 : 2);
        sections.Margin = wide ? new Thickness(0,0,24,0) : new Thickness(0);
        rail.Margin = wide ? new Thickness(0) : new Thickness(0,28,0,0);
        var highlights = (Grid)window.FindName("HighlightsSection");
        var top = (StackPanel)window.FindName("TopRatedGroup");
        var split = width - (wide ? 324 : 0) >= 800;
        highlights.ColumnDefinitions[1].Width = new GridLength(split ? 24 : 0);
        highlights.ColumnDefinitions[2].Width = split ? new GridLength(1,GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(top, split ? 2 : 0); Grid.SetRow(top, split ? 0 : 1);
        top.Margin = split ? new Thickness(0) : new Thickness(0,28,0,0);
        foreach (var border in Descendants(window).OfType<Border>())
            if (RoundedPanelClip.IsManaged(border)) RoundedPanelClip.Update(border);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) { var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var item in Descendants(child))yield return item; }
    }
}

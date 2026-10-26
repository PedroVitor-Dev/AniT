using System.Windows;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Controls;

namespace AniT.App;

internal static class CatalogLayout
{
    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}
    }
    public static void UpdateLibraryHeader(Window window)
    {
        if(window.FindName("LibraryHeaderGrid") is not Grid grid || window.FindName("HeaderActionsPanel") is not WrapPanel actions)return;
        var compact=grid.ActualWidth<940;
        Grid.SetRow(actions,compact?1:0);Grid.SetColumn(actions,compact?0:1);Grid.SetColumnSpan(actions,compact?2:1);
        actions.HorizontalAlignment=compact?HorizontalAlignment.Left:HorizontalAlignment.Right;
        actions.Margin=compact?new Thickness(0,16,0,0):new Thickness(0);
        if(window.FindName("FilterScroller") is ScrollViewer filters && window.Content is FrameworkElement root)
            filters.MaxHeight=System.Math.Max(160,System.Math.Min(300,root.ActualHeight*.3));
    }
    public static void UpdateDetails(Window window)
    {
        if (window.FindName("DetailsContentHost") is not FrameworkElement host || host.ActualWidth<=0) return;
        var width=host.ActualWidth;
        var hero=(Grid)window.FindName("DetailsHeroLayout");
        var artwork=(Border)window.FindName("HeroArtwork");
        var copy=(FrameworkElement)window.FindName("HeroCopy");
        var showArt=width>=800;
        hero.ColumnDefinitions[1].Width=new GridLength(showArt?280:0);
        artwork.Visibility=showArt?Visibility.Visible:Visibility.Collapsed;
        copy.MaxWidth=double.PositiveInfinity;
        var body=(Grid)window.FindName("DetailsBody");
        var rail=(StackPanel)window.FindName("DetailsRightRail");
        var wide=width>=1180;
        ((ColumnDefinition)window.FindName("RightRailColumn")).Width=new GridLength(wide?300:0);
        body.ColumnDefinitions[1].Width=new GridLength(wide?24:0);
        Grid.SetRow(rail,wide?0:1);Grid.SetColumn(rail,wide?2:0);Grid.SetColumnSpan(rail,wide?1:3);
        rail.Visibility=Visibility.Visible;
        var synopsis=(Grid)window.FindName("SynopsisLayout");
        var synopsisArt=(Border)window.FindName("SynopsisArtwork");
        var sideBySide=width>=1000;
        synopsis.ColumnDefinitions[1].Width=new GridLength(sideBySide?24:0);
        synopsis.ColumnDefinitions[2].Width=new GridLength(sideBySide?360:0);
        Grid.SetRow(synopsisArt,sideBySide?0:1);Grid.SetColumn(synopsisArt,sideBySide?2:0);Grid.SetColumnSpan(synopsisArt,sideBySide?1:3);
        synopsisArt.Margin=sideBySide?new Thickness(0):new Thickness(0,20,0,0);
        rail.Margin=wide?new Thickness(0):new Thickness(0,24,0,0);
    }
}

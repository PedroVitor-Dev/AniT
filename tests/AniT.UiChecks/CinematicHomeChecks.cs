using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AniT.App;
using AniT.Infrastructure;
using AniT.Presentation;

internal static class CinematicHomeChecks
{
    public static void Run(Application app)
    {
        AppearanceManager.Initialize(app, AniTSystemSettings.Default);
        var combinations = 0;
        foreach (var populated in new[] { false, true })
        {
            var window = HomeVisualChecks.Preview(populated, stressText: true);
            window.MinWidth=580; window.MinHeight=420;
            window.ShowActivated=false; window.ShowInTaskbar=false; window.Opacity=0;
            window.Show(); Pump();
            foreach(var theme in new[]{AppearanceThemeMode.Dark,AppearanceThemeMode.Light})
            foreach(var scale in new[]{80,100,125})
            foreach(var text in new[]{85,100,140})
            foreach(var size in new[]{(853d,480d),(1060d,700d),(1280d,720d),(1920d,1080d),(3440d,1440d)})
            {
                window.Width=size.Item1; window.Height=size.Item2;
                var settings=AniTSystemSettings.Default with {ThemeMode=theme,InterfaceScalePercent=scale,TextScalePercent=text};
                HomeVisualChecks.ApplyPreviewAppearance(window,settings); Pump(); window.UpdateLayout();
                var scroller=(ScrollViewer)window.FindName("HomeScroller");
                Require(scroller.ExtentWidth<=scroller.ViewportWidth+1,"No horizontal overflow");
                var title=(TextBlock)window.FindName("HeroTitleText");
                Require(double.IsPositiveInfinity(title.MaxHeight)&&title.TextWrapping==TextWrapping.Wrap,"Hero title expands instead of clipping");
                var action=(Control)window.FindName("HeroPrimaryAction");
                var hero=(Border)window.FindName("HeroBanner");
                var bounds=action.TransformToAncestor(hero).TransformBounds(new Rect(action.RenderSize));
                Require(bounds.Right<=hero.ActualWidth+1&&bounds.Bottom<=hero.ActualHeight+1,"Primary action remains inside banner");
                Require(action.IsEnabled&&action.FocusVisualStyle is not null,"Empty and populated hero actions are keyboard accessible");
                Require(((Border)window.FindName("RecentEmptyText")).Visibility==(populated?Visibility.Collapsed:Visibility.Visible),"Empty library guidance matches data");
                foreach(var label in Descendants(window).OfType<TextBlock>().Where(t=>t.IsVisible&&t.IsEnabled&&!string.IsNullOrWhiteSpace(t.Text)))
                {
                    if(label.Foreground is not SolidColorBrush foreground)continue;
                    var background=Background(label);
                    if(background is { } color) Require(CinematicTheme.Contrast(foreground.Color,color)>=4.5,$"Text contrast: {theme}/{label.Text}; foreground={foreground.Color}; background={color}");
                }
                combinations++;
            }
            foreach(var radius in new[]{0,16,32})
            {
                HomeVisualChecks.ApplyPreviewAppearance(window,AniTSystemSettings.Default with {CornerRadius=radius}); Pump();
                var art=(Border)window.FindName("HeroArtwork");
                Require(art.Clip is null&&!art.ClipToBounds,"Artwork outline does not clip itself");
            }
            window.Close();
        }
        Console.WriteLine($"PASS: {combinations} cinematic Home empty/populated/theme/scale/text/viewport combinations; contrast, banner actions, wrapping, scrolling and panel outlines.");
    }
    private static Color? Background(DependencyObject item)
    {
        for(DependencyObject? current=item;current is not null;current=VisualTreeHelper.GetParent(current))
        {
            Brush? brush=current switch {Border b=>b.Background,Panel p=>p.Background,Window w=>w.Background,_=>null};
            if(brush is SolidColorBrush solid&&solid.Color.A==255)return solid.Color;
        }
        return null;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}
    }
    private static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}

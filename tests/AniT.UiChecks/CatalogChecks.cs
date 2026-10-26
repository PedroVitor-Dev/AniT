using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using AniT.App;
using AniT.Infrastructure;
using AniT.Presentation;

internal static class CatalogChecks
{
    const string Art="pack://application:,,,/AniT;component/Assets/Home/anime-horizon.png";
    public static Window Preview(bool details, int count=24)
    {
        var xml=XDocument.Load(Path.Combine(AppContext.BaseDirectory,"Fixtures",details?"AnimeDetailsWindow.xaml":"LibraryWindow.xaml"));
        XNamespace x="http://schemas.microsoft.com/winfx/2006/xaml";
        var root=xml.Root!;root.Attribute(x+"Class")?.Remove();
        root.SetAttributeValue(XNamespace.Xmlns+"local","clr-namespace:AniT.App;assembly=AniT");
        foreach(var e in root.Descendants().Where(e=>e.Name.NamespaceName=="clr-namespace:AniT.App"))e.Name=XName.Get(e.Name.LocalName,"clr-namespace:AniT.App;assembly=AniT");
        root.Descendants().Where(e=>e.Name.LocalName=="EventSetter").Remove();
        foreach(var a in root.DescendantsAndSelf().Attributes().ToArray())
        {
            if(a.Name.NamespaceName=="clr-namespace:AniT.App") {var p=a.Parent!;var n=XName.Get(a.Name.LocalName,"clr-namespace:AniT.App;assembly=AniT");var v=a.Value;a.Remove();p.SetAttributeValue(n,v);continue;}
            if(a.Name.NamespaceName.Length==0&&Regex.IsMatch(a.Value,@"^\w+_\w+$")){a.Remove();continue;}
            if(a.Value=="Styles/HomeTheme.xaml")a.Value="pack://application:,,,/AniT;component/Styles/HomeTheme.xaml";
            else if(a.Value.StartsWith("Assets/"))a.Value="pack://application:,,,/AniT;component/"+a.Value;
            else if(a.Value.StartsWith("{local:PageMascot ")||a.Value.StartsWith("{local:ComfortableImage "))a.Value=Art;
        }
        foreach(var e in root.Descendants().Where(e=>e.Name.LocalName=="ProfileAvatarButton").ToArray())e.ReplaceWith(new XElement(root.Name.Namespace+"Border"));
        foreach(var e in root.Descendants().Where(e=>e.Name.LocalName is "CinematicHeader" or "CinematicSidebar"))e.SetAttributeValue("IsPreview","True");
        var w=(Window)XamlReader.Parse(xml.ToString());
        w.Tag=new CinematicTheme(w);w.WindowState=WindowState.Normal;w.Width=1280;w.Height=800;w.MinWidth=720;w.MinHeight=520;
        w.Title=details?"AniT · Detalhes cinematográficos":"AniT · Biblioteca cinematográfica";
        w.Resources["AniT.LibraryCardWidth"]=184d;w.Resources["AniT.LibraryPosterHeight"]=245d;
        if(details)w.DataContext=new {AnimeTitle="Além do horizonte: uma jornada extraordinária entre ilhas e histórias",EnglishTitleDisplay="Beyond the horizon",WatchStateLabel="Em andamento",EpisodeCountLabel="12 episódios",CriticFiveLabel="4,5",Summary="Uma aventura sobre amizade e descobertas.",CanPlayNext=true,PlayNextLabel="Continuar assistindo",FavoriteLabel="Favoritar",CoverPath=Art,SynopsisArtworkPath=Art,Synopsis="Cada nova história começa com o primeiro passo. Uma tripulação encontra novos caminhos pelo oceano.",Episodes=Enumerable.Range(1,12).Select(i=>new EpisodeItem(Guid.NewGuid(),i.ToString("00"),"Uma jornada que começa além do horizonte",AniT.Core.WatchStatus.NotStarted,0,null,null,1,1)).ToArray(),LocalAverageLabel="Sem notas",RatedEpisodeCountLabel="0 episódios avaliados",LocalReviewSummary="Avalie os episódios para acompanhar sua experiência."};
        else
        {
            var items=Items(count);w.Resources["Fixture.Items"]=items;w.DataContext=new {PosterRows=LibraryPosterRows.Build(items,1000,184)};
            ((FrameworkElement)w.FindName("EmptyState")).Visibility=count==0?Visibility.Visible:Visibility.Collapsed;
        }
        return w;
    }
    static AnimeLibraryItem[] Items(int count)=>Enumerable.Range(1,count).Select(i=>new AnimeLibraryItem(Guid.NewGuid(),$"Jornada {i}: além do horizonte",null,"J","12 episódios","Em andamento",42,Art,i%3==0,false,null,2026,null,null,null,4.5,LibraryCatalogStatus.Watching,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow)).ToArray();
    public static void Apply(Window w,bool details,AppearanceOptions options)
    {
        ((CinematicTheme)w.Tag).Apply(options);
        foreach(var host in CatalogLayout.Descendants(w).OfType<IAppearanceHost>())host.ApplyAppearance(options);
        w.UpdateLayout();
        if(!details){var list=(ListBox)w.FindName("LibraryPosterList");list.ItemsSource=LibraryPosterRows.Build((AnimeLibraryItem[])w.Resources["Fixture.Items"],list.ActualWidth-20,184);}
        foreach(var border in CatalogLayout.Descendants(w).OfType<Border>().Where(b=>Equals(b.Tag,"Artwork")))RoundedPanelClip.Update(border);
        if(details)CatalogLayout.UpdateDetails(w);else CatalogLayout.UpdateLibraryHeader(w);
        w.UpdateLayout();
    }
    public static void Run(Application app)
    {
        AppearanceManager.Initialize(app,AniTSystemSettings.Default);
        var cases=0;
        foreach(var details in new[]{false,true})
        {
            var w=Preview(details);w.Opacity=0;w.ShowActivated=false;w.ShowInTaskbar=false;w.Show();Pump();
            foreach(var theme in new[]{VisualTheme.Dark,VisualTheme.Light})
            foreach(var scale in new[]{80,100,125})
            foreach(var text in new[]{85,100,140})
            foreach(var size in new[]{(1060d,700d),(1280d,800d),(1920d,1080d)})
            {
                w.Width=size.Item1;w.Height=size.Item2;Apply(w,details,new(theme,InterfaceScale:scale,TextScale:text));Pump();
                if(details){var t=(TextBlock)w.FindName("HeroTitleText");Require(t.TextWrapping==TextWrapping.Wrap&&double.IsPositiveInfinity(t.MaxHeight),"Title must expand");Require(((TextBlock)w.FindName("SynopsisText")).ActualWidth>180,"Synopsis remains readable");}
                else {var list=(ListBox)w.FindName("LibraryPosterList");Require(VirtualizingPanel.GetIsVirtualizing(list),"Library virtualization");Require(list.ActualHeight>50,"Library viewport remains usable");}
                foreach(var scroller in CatalogLayout.Descendants(w).OfType<ScrollViewer>().Where(s=>s.IsVisible&&s.HorizontalScrollBarVisibility==ScrollBarVisibility.Disabled))Require(scroller.ExtentWidth<=scroller.ViewportWidth+1,$"Horizontal overflow: {details} {theme} {scale} {text} {size} {scroller.Name} {scroller.ExtentWidth}/{scroller.ViewportWidth}");
                cases++;
            }
            w.Close();Pump();
        }
        var data=Items(2000);
        foreach(var width in new[]{220d,650d,1200d})Require(LibraryPosterRows.Build(data,width,184).SelectMany(r=>r.Items).Select(i=>i.Id).SequenceEqual(data.Select(i=>i.Id)),"Grouping preserves every item and order");
        var timer=Stopwatch.StartNew();var large=Preview(false,2000);large.Opacity=0;large.ShowActivated=false;large.ShowInTaskbar=false;large.Show();Apply(large,false,new());Pump();
        var box=(ListBox)large.FindName("LibraryPosterList");var realized=CatalogLayout.Descendants(box).OfType<ListBoxItem>().Count();
        Require(realized>0&&realized<40,$"Only visible rows materialized: {realized}");
        box.ScrollIntoView(box.Items[box.Items.Count-1]);Pump();Require(box.ItemContainerGenerator.ContainerFromIndex(box.Items.Count-1)!=null,"Last row reachable");
        Console.WriteLine($"PASS: {cases} catalog theme/scale/text/viewport cases; 2000 titles, {box.Items.Count} rows, {realized} initial realized rows; initial layout {timer.ElapsedMilliseconds}ms (diagnostic only); ordered grouping and last-row access.");large.Close();
    }
    static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}

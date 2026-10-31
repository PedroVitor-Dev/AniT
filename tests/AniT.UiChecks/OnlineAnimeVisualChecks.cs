using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AniT.App;
using AniT.Infrastructure;

internal static class OnlineAnimeVisualChecks
{
    public static void Run()
    {
        var count = 0;
        var output = Path.Combine(Path.GetTempPath(), "AniT.Online.VisualChecks"); Directory.CreateDirectory(output);
        foreach (var theme in new[] { AppearanceThemeMode.Dark, AppearanceThemeMode.Light })
        foreach (var textScale in new[] { 100, 140 })
        foreach (var size in new[] { (1366d, 680d), (820d, 640d), (600d, 520d) })
        foreach (var state in new[] { "consent", "torrent", "magnet" }) {
            AppearanceManager.Apply(AniTSystemSettings.Default with { ThemeMode=theme,TextScalePercent=textScale });
            var window=new OnlineAnimeWindow(true) { Width=size.Item1,Height=size.Item2,WindowStartupLocation=WindowStartupLocation.Manual,Left=-20000,Top=-20000,ShowInTaskbar=false,ShowActivated=false };
            if(state!="consent") {
                ((Border)window.FindName("ConsentPanel")).Visibility=Visibility.Collapsed;
                ((StackPanel)window.FindName("Workspace")).Visibility=Visibility.Visible;
                ((RadioButton)window.FindName(state=="magnet"?"MagnetChoice":"TorrentChoice")).IsChecked=true;
            }
            window.Show();Flush();window.UpdateLayout();
            if(window.FindName("TitleBox") is not null||window.FindName("FolderBox") is not null||window.FindName("DownloadsList") is not null)throw new Exception("Import must not expose name, destination or a second library");
            if(state=="consent") {
                var check=(CheckBox)window.FindName("ConsentCheck");
                var label=(TextBlock)check.Content;
                var expected=((SolidColorBrush)window.FindResource("AniT.Brush.TextPrimary")).Color;
                if(((SolidColorBrush)label.Foreground).Color!=expected)throw new Exception("Consent text does not use the theme foreground");
            }
            if(state=="magnet"&&((Button)window.FindName("PickTorrentButton")).Visibility!=Visibility.Collapsed)throw new Exception("Magnet mode must not show file controls");
            foreach(var button in Descendants(window).OfType<Button>().Where(item=>item.IsVisible)) {
                var bounds=button.TransformToAncestor(window).TransformBounds(new Rect(button.RenderSize));
                if(button.ActualHeight<10||bounds.Left<0||bounds.Right>window.ActualWidth+1)throw new Exception("Import control clipped: "+button.Content);
            }
            if(textScale==100&&size.Item1==1366)Save(window,Path.Combine(output,$"online-{theme}-{state}.png"));
            window.Close();Flush();count++;
        }
        foreach(var theme in new[]{AppearanceThemeMode.Dark,AppearanceThemeMode.Light})
        foreach(var scale in new[]{100,140})
        foreach(var complete in new[]{false,true}) {
            AppearanceManager.Apply(AniTSystemSettings.Default with { ThemeMode=theme,TextScalePercent=scale });
            using var cancellation=new CancellationTokenSource();
            var popup=new OnlineImportProgressWindow(cancellation,true){WindowStartupLocation=WindowStartupLocation.Manual,Left=-20000,Top=-20000,ShowActivated=false};
            popup.Show();Flush();
            popup.Report(new(8,"Buscando a lista de episódios","0 conexões · 00:12 de espera\nBuscando participantes via DHT e trackers do link.\nSomente os metadados; nenhum episódio está sendo baixado.",true));
            if(!Descendants(popup).OfType<ProgressBar>().Single().IsIndeterminate)throw new Exception("Metadata discovery must not show a fake completion percentage");
            popup.Report(new(72,"Identificando o anime","Uma temporada autorizada com um nome que pode ocupar mais de uma linha"));
            if(Descendants(popup).OfType<ProgressBar>().Single().IsIndeterminate)throw new Exception("Catalog progress must return to measured stages");
            if(complete)popup.Complete("Minha temporada autorizada",12,false);
            popup.UpdateLayout();
            var action=Descendants(popup).OfType<Button>().Single();
            var rect=action.TransformToAncestor(popup).TransformBounds(new Rect(action.RenderSize));
            if(rect.Bottom>popup.ActualHeight||rect.Right>popup.ActualWidth)throw new Exception("Popup action clipped");
            if(!complete&&action.Content?.ToString()!="Concluir agora")throw new Exception("Committed catalog must allow skipping artwork without canceling the import");
            if(scale==100)Save(popup,Path.Combine(output,$"online-{theme}-import-{complete}.png"));
            popup.Close();Flush();count++;
        }
        foreach (var theme in new[] { AppearanceThemeMode.Dark, AppearanceThemeMode.Light })
        foreach (var textScale in new[] { 100, 140 }) {
            AppearanceManager.Apply(AniTSystemSettings.Default with { ThemeMode = theme, TextScalePercent = textScale });
            using var cancellation = new CancellationTokenSource();
            var buffer = new OnlineBufferWindow(cancellation) { Left = -20000, Top = -20000, ShowInTaskbar = false };
            buffer.Show(); Flush();
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            IProgress<OnlinePreparationProgress> progress = new Progress<OnlinePreparationProgress>(buffer.Report);
            SynchronizationContext.SetSynchronizationContext(previous);
            if (!Descendants(buffer).OfType<TextBlock>().Any(t => t.Text.Contains("Organizando a abertura"))) throw new Exception("Preparation must describe work before peers are queried");
            progress.Report(new OnlinePreparationProgress(0, 0, 0, 0, null, Stage: "Lendo sua lista de episódios…", TransferStarted: false)); Flush();
            if (!Descendants(buffer).OfType<TextBlock>().Any(t => t.Text.Contains("Lendo sua lista"))) throw new Exception("Preparation must show initial phase");
            if (!Descendants(buffer).OfType<TextBlock>().Any(t => t.Text.Contains("de espera"))) throw new Exception("Preparation must show elapsed time");
            Task.Run(() => progress.Report(new OnlinePreparationProgress(0, 0, 0, 0, null))).GetAwaiter().GetResult(); Flush();
            if (!Descendants(buffer).OfType<TextBlock>().Any(t => t.Text.Contains("Procurando fontes"))) throw new Exception("Preparation must explain missing peers");
            Task.Run(() => progress.Report(new OnlinePreparationProgress(1000000, 500000, 3, 1.2, null))).GetAwaiter().GetResult(); Flush();
            var cancel = Descendants(buffer).OfType<Button>().Single(b => Equals(b.Content, "Cancelar"));
            var bounds = cancel.TransformToAncestor(buffer).TransformBounds(new Rect(cancel.RenderSize));
            if (bounds.Bottom > buffer.ActualHeight || bounds.Left < 0 || bounds.Right > buffer.ActualWidth) throw new Exception("Preparation cancel is clipped");
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!cancellation.IsCancellationRequested) throw new Exception("Preparation cancel does not cancel");
            buffer.Close(); Flush(); count++;
        }
        Console.WriteLine($"Online Anime Player: {count} layouts passed. Screenshots: {output}");
    }
    static void Save(Window window,string path)
    {
        var image=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(window);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(path);encoder.Save(file);
    }
    static IEnumerable<DependencyObject> Descendants(DependencyObject parent) { for (var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var item in Descendants(child))yield return item;} }
    static void Flush() { var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame); }
}

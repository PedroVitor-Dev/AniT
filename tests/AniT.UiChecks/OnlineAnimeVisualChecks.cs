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
        foreach (var size in new[] { (1366d, 680d), (1040d, 700d), (640d, 520d) })
        foreach (var state in new[] { "consent", "downloads" }) {
            AppearanceManager.Apply(AniTSystemSettings.Default with { ThemeMode = theme, TextScalePercent = textScale });
            var window = new OnlineAnimeWindow(true) { Width = size.Item1, Height = size.Item2, Left = -20000, Top = -20000, ShowInTaskbar = false };
            if (state == "downloads") {
                ((Border)window.FindName("ConsentPanel")).Visibility = Visibility.Collapsed;
                ((StackPanel)window.FindName("Workspace")).Visibility = Visibility.Visible;
                ((TextBlock)window.FindName("EmptyText")).Visibility = Visibility.Collapsed;
                ((TextBox)window.FindName("FolderBox")).Text = @"D:\Animes autorizados";
                var episodes = Enumerable.Range(1,12).Select(i => new OnlineEpisode(Guid.NewGuid(), $"S01E{i:00}", $"Um anime autorizado com nome longo - {i:00}.mkv", i == 1 ? 100 : i == 2 ? 42 : 0, 350 * 1024 * 1024, i != 1, i == 2)).ToArray();
                ((ItemsControl)window.FindName("DownloadsList")).ItemsSource = new[] { new OnlineDownloadRow(new OnlineTransfer("test", "Minha temporada autorizada", "Baixando", 22, 2048 * 1024, 4, false, episodes)) };
            }
            window.Show(); Flush(); window.UpdateLayout();
            foreach (var button in Descendants(window).OfType<Button>().Where(b => b.IsVisible)) {
                if (button.ActualWidth < 10 || button.ActualHeight < 10) throw new Exception($"Invisible control: {button.Content}");
                var rect = button.TransformToAncestor(window).TransformBounds(new Rect(button.RenderSize));
                if (rect.Left < -1 || rect.Right > window.ActualWidth + 1) throw new Exception($"Horizontal clipping: {button.Content} {theme} {textScale} {size} {rect}");
            }
            if (textScale == 100 && size.Item1 == 1366) {
                if (state == "downloads") { var scroll = Descendants(window).OfType<ScrollViewer>().First(); scroll.ScrollToVerticalOffset(520); Flush(); }
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight,96,96,PixelFormats.Pbgra32); bitmap.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output,$"online-{theme}-{state}.png")); encoder.Save(file);
            }
            window.Close(); Flush(); count++;
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
    static IEnumerable<DependencyObject> Descendants(DependencyObject parent) { for (var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var item in Descendants(child))yield return item;} }
    static void Flush() { var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame); }
}

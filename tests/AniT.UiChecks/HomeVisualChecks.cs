using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using AniT.App;
using AniT.Infrastructure;

internal static class HomeVisualChecks
{
    public static void Run()
    {
        foreach (var corner in new[] { 0, 8, 16, 24 })
        foreach (var scale in new[] { 100, 125 })
        {
            AppearanceManager.Apply(AniTSystemSettings.Default with { CornerRadius = corner, InterfaceScalePercent = scale });
            var child = new Grid { Background = Brushes.White, Margin = new Thickness(-24, -16, -24, -36) };
            var border = new Border
            {
                Width = 222.5, Height = 144.5, BorderBrush = Brushes.Cyan, BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(18), Background = Brushes.Navy, Child = child
            };
            border.SizeChanged += (_, _) => RoundedPanelClip.Update(border);
            var window = new Window { Content = border, Opacity = 0, ShowActivated = false, ShowInTaskbar = false, SizeToContent = SizeToContent.WidthAndHeight };
            window.Show();
            Pump();
            AppearanceManager.ApplyToWindow(window);
            AssertClip(border, child);
            border.Height += 25.25;
            window.UpdateLayout();
            Pump();
            AssertClip(border, child);
            window.Close();
        }

        var resources = new ResourceDictionary { Source = new Uri("pack://application:,,,/AniT;component/Styles/HomeTheme.xaml") };
        var mascot = new Image { Style = (Style)resources["HomeSidebarMascot"], Source = new BitmapImage(new Uri("pack://application:,,,/AniT;component/Assets/ghost_menu.png")) };
        var bitmap = (BitmapSource)mascot.Source;
        var pixel = new byte[4];
        var rgba = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        rgba.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
        Require(pixel[3] == 0, "Mascot asset must keep its transparent canvas.");
        Require(mascot.Stretch == Stretch.Uniform,
            "Mascot must retain its natural aspect ratio.");
        var preview = Preview();
        preview.Opacity = 0; preview.ShowActivated = false; preview.ShowInTaskbar = false;
        preview.Show(); Pump();
        Require(System.Windows.PresentationSource.FromVisual(preview) is System.Windows.Interop.HwndSource source
            && source.CompositionTarget.RenderMode == System.Windows.Interop.RenderMode.SoftwareOnly,
            "Home must use stable per-window rendering without a process-wide override.");
        var hero = (Border)preview.FindName("HeroArtwork");
        Require(hero.BorderThickness == new Thickness(0) && hero.Background is SolidColorBrush brush && brush.Color.A == 0,
            "The built-in mascot must not have an anime cover frame.");
        foreach (var theme in new[] { AppearanceThemeMode.Dark, AppearanceThemeMode.Light })
        foreach (var text in new[] { 100, 140 })
        {
            preview.Width = 1366; preview.Height = 720;
            AppearanceManager.Apply(AniTSystemSettings.Default with { ThemeMode = theme, TextScalePercent = text });
            preview.UpdateLayout(); Pump();
            var support = (Button)preview.FindName("SupportProjectButton");
            var layout = (FrameworkElement)preview.Content;
            var bounds = support.TransformToAncestor(layout).TransformBounds(new Rect(support.RenderSize));
            Require(bounds.Top >= 0 && bounds.Bottom <= layout.ActualHeight + 1, "Support card stays inside sidebar with enlarged text");
            if (text == 100)
            {
                var capture = new RenderTargetBitmap((int)layout.ActualWidth, (int)layout.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                preview.Opacity = 1; capture.Render(layout); preview.Opacity = 0; var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(capture));
                var folder = Path.Combine(Path.GetTempPath(), "AniT.Support.Preview"); Directory.CreateDirectory(folder);
                using var output = File.Create(Path.Combine(folder, theme + ".png")); encoder.Save(output);
            }
        }
        preview.Close();
        Console.WriteLine("PASS: Home panels preserve their strokes after resizing and corner/scale changes; mascot retains transparent rendering.");
    }

    private static void AssertClip(Border border, UIElement child)
    {
        Require(border.Clip is null && !border.ClipToBounds, "The panel must not clip its own outline.");
        Require(child.Clip is RectangleGeometry, "Panel content must have its own rounded clip.");
        var clip = (RectangleGeometry)child.Clip;
        var bottomRight = child.TranslatePoint(clip.Rect.BottomRight, border);
        Require(Math.Abs(bottomRight.Y - (border.ActualHeight - border.BorderThickness.Bottom)) < 0.01,
            "The content clip must follow the full current panel height.");
        Require(Math.Abs(clip.RadiusX - Math.Max(0, border.CornerRadius.TopLeft - border.BorderThickness.Left)) < 0.01,
            "The content clip must follow the actual configured radius.");
    }

    // The real Home XAML, with navigation/data event handlers removed, is safe to inspect
    // without opening profiles, scanning folders or initializing App's database services.
    public static Window Preview()
    {
        var xml = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "DashboardWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var root = xml.Root!;
        root.Attribute(x + "Class")?.Remove();
        root.SetAttributeValue(XNamespace.Xmlns + "local", "clr-namespace:AniT.App;assembly=AniT");
        foreach (var element in root.Descendants().Where(e => e.Name.NamespaceName == "clr-namespace:AniT.App"))
            element.Name = XName.Get(element.Name.LocalName, "clr-namespace:AniT.App;assembly=AniT");
        foreach (var attribute in root.DescendantsAndSelf().Attributes().Where(a => a.Name.NamespaceName == "clr-namespace:AniT.App").ToArray())
        {
            var parent = attribute.Parent!;
            var name = XName.Get(attribute.Name.LocalName, "clr-namespace:AniT.App;assembly=AniT");
            var value = attribute.Value;
            attribute.Remove(); parent.SetAttributeValue(name, value);
        }
        root.Elements().Where(e => e.Name.LocalName == "Window.Triggers").Remove();
        foreach (var attribute in root.DescendantsAndSelf().Attributes().ToArray())
        {
            if (attribute.Name.NamespaceName.Length == 0 && Regex.IsMatch(attribute.Value, @"^\w+_\w+$")) { attribute.Remove(); continue; }
            if (attribute.Value == "Styles/HomeTheme.xaml") attribute.Value = "pack://application:,,,/AniT;component/Styles/HomeTheme.xaml";
            else if (attribute.Value.StartsWith("Assets/")) attribute.Value = "pack://application:,,,/AniT;component/" + attribute.Value;
            else if (attribute.Value.StartsWith("{local:PageMascot ")) attribute.Value = "pack://application:,,,/AniT;component/Assets/ghost_menu.png";
            else if (attribute.Value.StartsWith("{local:ComfortableImage ")) attribute.Value = "pack://application:,,,/AniT;component/Assets/logo-fire.png";
        }
        foreach (var avatar in root.Descendants().Where(e => e.Name.LocalName == "ProfileAvatarButton").ToArray())
            avatar.ReplaceWith(new XElement(root.Name.Namespace + "Border", new XAttribute("Width", "44"), new XAttribute("Height", "44")));
        var window = (Window)XamlReader.Parse(xml.ToString());
        window.WindowState = WindowState.Normal;
        window.Width = 1600; window.Height = 900;
        window.Title = "AniT · Prévia isolada da Home";
        window.DataContext = new
        {
            HeroIsMascot = true, HeroCoverPath = "pack://application:,,,/AniT;component/Assets/normal-sf.png",
            HeroTitle = "Sua próxima história", HeroSubtitle = "Assista a um episódio para começar",
            HeroQuote = "A imaginação abre portas onde não havia paredes.", HeroProgressLabel = "Pronto para começar",
            LibraryAnimeCount = "1", LibraryEpisodeCount = "12", LibraryCompletedCount = "0",
            SessionMessage = "Escolha uma história na biblioteca e aproveite."
        };
        window.Loaded += (_, _) =>
        {
            foreach (var border in Descendants(window).OfType<Border>().Where(b => double.TryParse(b.Tag?.ToString(), System.Globalization.CultureInfo.InvariantCulture, out _)))
            {
                border.SizeChanged += (_, _) => RoundedPanelClip.Update(border);
                RoundedPanelClip.Update(border);
            }
            ((UIElement)window.FindName("HeroBanner")).Opacity = 1;
            ((UIElement)window.FindName("MainBody")).Opacity = 1;
            foreach (var name in new[] { "ContinueEmptyText", "RecentEmptyText", "FavoritesEmptyText", "TopRatedEmptyText" })
                if (window.FindName(name) is UIElement element) element.Visibility = Visibility.Visible;
        };
        return window;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

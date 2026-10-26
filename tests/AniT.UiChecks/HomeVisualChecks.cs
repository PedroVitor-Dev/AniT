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
    public static Window Preview(bool populated = false, bool stressText = false)
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
        foreach (var shell in root.Descendants().Where(e => e.Name.LocalName is "CinematicSidebar" or "CinematicHeader")) shell.SetAttributeValue("IsPreview", "True");
        var wpf = root.Name.Namespace;
        root.AddFirst(new XElement(wpf + "Window.Resources", new XElement(wpf + "ResourceDictionary",
            new XElement(wpf + "ResourceDictionary.MergedDictionaries",
                new XElement(wpf + "ResourceDictionary", new XAttribute("Source", "pack://application:,,,/AniT.Presentation;component/Themes/Components.xaml")),
                new XElement(wpf + "ResourceDictionary", new XAttribute("Source", "pack://application:,,,/AniT;component/Styles/CinematicHome.xaml"))))));
        var window = (Window)XamlReader.Parse(xml.ToString());
        var theme = new AniT.Presentation.CinematicTheme(window);
        window.Tag = theme;
        window.WindowState = WindowState.Normal;
        window.Width = 1600; window.Height = 900;
        window.Title = "AniT · Prévia isolada da Home";
        const string art = "pack://application:,,,/AniT;component/Assets/Home/anime-horizon.png";
        var cards = populated ? new[] {
            new DashboardCard(Guid.NewGuid(), Guid.NewGuid(), stressText ? "Além do horizonte: uma jornada extraordinária entre ilhas, mares e as histórias que ainda vamos viver" : "Além do horizonte", "Temporada 1 · Episódio 08", "12 episódios na sua estante", art, 63, "63% assistido", "★ 4,8", true),
            new DashboardCard(Guid.NewGuid(), null, "O verão das pequenas descobertas", "Temporada 2 · Episódio 03", "Uma nova aventura", art, 21, "21% assistido", "★ 4,6", false)
        } : Array.Empty<DashboardCard>();
        window.DataContext = new
        {
            HeroIsMascot = !populated, HeroCoverPath = art, HeroArtworkPath = art,
            HeroTitle = populated ? cards[0].Title : "Sua próxima história começa aqui.",
            HeroSubtitle = populated ? "Temporada 1 · Episódio 08 · Continue de onde parou" : "Adicione uma pasta na Biblioteca e encontre sua próxima aventura.",
            HeroActionLabel = populated ? "Continuar assistindo" : "Abrir biblioteca",
            HeroEyebrow = populated ? "CONTINUE SUA JORNADA" : "SUA PRÓXIMA HISTÓRIA",
            HeroQuote = "Cada nova história começa com o primeiro passo.",
            CanPlayHero = populated, HasHeroAnime = populated, HeroProgressPercent = populated ? 63d : 0d,
            HeroProgressLabel = populated ? "63% assistido" : "Pronto para começar",
            LibraryAnimeCount = populated ? "24" : "0", LibraryEpisodeCount = populated ? "312" : "0", LibraryCompletedCount = populated ? "96" : "0",
            SessionMessage = populated ? "Duas histórias prontas para continuar." : "Escolha uma história na biblioteca e aproveite.",
            ContinueCards = cards.Take(1).ToArray(), RecentCards = cards, FavoriteCards = cards.Take(1).ToArray(), TopRatedCards = cards,
            CalendarItems = populated ? new[] { new DashboardCalendarItem("LOCAL", cards[0].Title, "Episódio 09") } : Array.Empty<DashboardCalendarItem>()
        };
        window.Loaded += (_, _) =>
        {
            foreach (var border in Descendants(window).OfType<Border>().Where(b => Equals(b.Tag, "Artwork")))
            {
                border.SizeChanged += (_, _) => RoundedPanelClip.Update(border);
                RoundedPanelClip.Update(border);
            }
            foreach (var name in new[] { "ContinueEmptyText", "RecentEmptyText", "FavoritesEmptyText", "TopRatedEmptyText" })
                ((UIElement)window.FindName(name)).Visibility = populated ? Visibility.Collapsed : Visibility.Visible;
            for (var i=0;i<4;i++) ((Button)window.FindName($"HeroDot{i}")).Visibility = populated && i<2 ? Visibility.Visible : Visibility.Collapsed;
            ApplyPreviewAppearance(window, AniTSystemSettings.Default);
        };
        ((FrameworkElement)window.FindName("MainContentHost")).SizeChanged += (_,_) => DashboardLayout.Update(window);
        return window;
    }

    public static void ApplyPreviewAppearance(Window window, AniTSystemSettings settings)
    {
        var options = new AniT.Presentation.AppearanceOptions((AniT.Presentation.VisualTheme)settings.ThemeMode, settings.AccentColor, settings.InterfaceScalePercent, settings.TextScalePercent, settings.CornerRadius, settings.ReduceMotion);
        ((AniT.Presentation.CinematicTheme)window.Tag).Apply(options);
        window.Resources["Home.HeroTitleSize"] = 36d * settings.TextScalePercent / 100;
        ((CinematicSidebar)window.FindName("ShellSidebar")).ApplyAppearance(options);
        ((CinematicHeader)window.FindName("ShellHeader")).ApplyAppearance(options);
        window.UpdateLayout();
        DashboardLayout.Update(window);
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

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AniT.App;
using AniT.Infrastructure;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(a => a is "--preview-fluent" or "--check-fluent" or "--benchmark-fluent" or "--check-design-system" or "--check-shell" or "--preview-shell" or "--check-home-cinematic" or "--preview-home-light" or "--preview-home-populated")) return FluentPilotRunner.Run(args);
        // Load the same theme dictionaries, image optimization and appearance hooks as the app.
        // Never run App.OnStartup or open the user's profiles/database during layout checks.
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var theme in new[] { "Colors", "Typography", "Effects", "Animations", "Buttons", "Cards", "Inputs", "Images" })
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri($"pack://application:,,,/AniT;component/Themes/{theme}.xaml") });
        AppearanceManager.Initialize(application);
        if (args.Contains("--preview-home"))
        {
            AppearanceManager.Apply(AniTSystemSettings.Default);
            application.Run(HomeVisualChecks.Preview());
            return 0;
        }
        if (args.Contains("--home-only"))
        {
            try { HomeVisualChecks.Run(); return 0; }
            finally { application.Shutdown(); }
        }
        if (args.Contains("--preview"))
        {
            AppearanceManager.Apply(AniTSystemSettings.Default);
            var preview = NewWindow();
            preview.Title = "AniT · Prévia de layout";
            application.Run(preview);
            return 0;
        }

        var configurations = 0;
        try
        {
            foreach (var size in new[] { (1280d, 720d), (1920d, 1080d), (3440d, 1440d), (580d, 420d) })
            foreach (var displayScale in new[] { 1d, 1.25, 1.5 })
            foreach (var interfaceScale in new[] { 100, 125 })
            foreach (var textScale in new[] { 100, 140 })
            foreach (var theme in new[] { AppearanceThemeMode.Dark, AppearanceThemeMode.Light, AppearanceThemeMode.Automatic })
            {
                var settings = AniTSystemSettings.Default with
                {
                    ThemeMode = theme, InterfaceScalePercent = interfaceScale,
                    TextScalePercent = textScale, ArtworkStretchMode = ArtworkStretchMode.Manual
                };
                AppearanceManager.Apply(settings);
                var window = NewWindow();
                window.ShowActivated = false;
                window.ShowInTaskbar = false;
                window.Opacity = 0;
                window.Width = Math.Max(window.MinWidth, size.Item1 / displayScale);
                window.Height = Math.Max(window.MinHeight, size.Item2 / displayScale);
                window.Show();
                Pump();
                for (var step = 0; step < 4; step++)
                {
                    CheckLayout(window);
                    if (step < 2) Find<Button>(window, "NextButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    else if (step == 2) Find<Button>(window, "SkipFolderButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Pump();
                }
                window.Close();
                configurations++;
            }
            CheckOptimizedBrandImage();
            HomeVisualChecks.Run();
            Console.WriteLine($"PASS: {configurations} layout configurations, four steps each; optimized brand image keeps its layout.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL after {configurations} configurations: {exception}");
            return 1;
        }
        finally { application.Shutdown(); }
    }

    private static OnboardingWindow NewWindow() => new(
        new FirstRunSetupStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "anit-layout-check-unused")),
        new ProfileSettings("Meu perfil", "", DateTimeOffset.Now), null);

    private static void CheckLayout(OnboardingWindow window)
    {
        window.UpdateLayout();
        var root = Find<Grid>(window, "RootGrid");
        var scroller = Find<ScrollViewer>(window, "StepScroller");
        Require(scroller.ViewportWidth > 180, "Content viewport is too narrow.");
        Require(scroller.ExtentWidth <= scroller.ViewportWidth + 1, "Content overflows horizontally.");
        var heading = Find<TextBlock>(window, "Heading");
        Require(heading.TranslatePoint(new Point(), scroller).Y >= 7, $"Heading needs room above the glyphs: y={heading.TranslatePoint(new Point(), scroller).Y}, step={Find<TextBlock>(window, "StepText").Text}, offset={scroller.VerticalOffset}.");
        var next = Find<Button>(window, "NextButton");
        var bounds = next.TransformToAncestor(root).TransformBounds(new Rect(next.RenderSize));
        Require(bounds.Left >= 0 && bounds.Right <= root.ActualWidth + 1 && bounds.Bottom <= root.ActualHeight + 1,
            "Navigation is clipped.");
        Require(Find<Image>(window, "BrandLogo").Stretch == Stretch.Uniform, "Brand logo was changed to a cropped fill.");
        Require(Find<Border>(window, "StoryPanel").IsVisible == (root.ActualWidth >= 900 && root.ActualHeight >= 580),
            "Sidebar does not follow available space after scaling.");
        scroller.ScrollToBottom();
        window.UpdateLayout();
        Require(scroller.VerticalOffset + scroller.ViewportHeight >= scroller.ExtentHeight - 1,
            "The end of the content cannot be reached.");
        scroller.ScrollToTop();
    }

    private static void CheckOptimizedBrandImage()
    {
        var image = new Image
        {
            Source = new TransformedBitmap(new BitmapImage(new Uri("pack://application:,,,/AniT;component/Assets/logo-letter-white.png")), new ScaleTransform(0.3, 0.3)),
            Width = 128, Height = 64, Stretch = Stretch.Uniform
        };
        var window = new Window { Content = image, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        Pump();
        AppearanceManager.ApplyToWindow(window);
        Require(image.Stretch == Stretch.Uniform && image.RenderTransform.Value.IsIdentity,
            "Optimized application artwork lost its identity and inherited cover cropping.");
        window.Close();
    }

    private static T Find<T>(FrameworkElement window, string name) where T : FrameworkElement =>
        (T)(window.FindName(name) ?? throw new InvalidOperationException($"Missing element: {name}"));
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}

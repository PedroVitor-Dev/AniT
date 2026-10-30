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
        if (args.Length >= 2 && args[0] is "--probe-instance" or "--probe-restart") return StartupChecks.Probe(args);
        // Load the same theme dictionaries, image optimization and appearance hooks as the app.
        // Never run App.OnStartup or open the user's profiles/database during layout checks.
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var theme in new[] { "Colors", "Typography", "Effects", "Animations", "Buttons", "Cards", "Inputs", "Images" })
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri($"pack://application:,,,/AniT;component/Themes/{theme}.xaml") });
        AppearanceManager.Initialize(application);
        if (args.Contains("--check-playback-evidence"))
        {
            try { PlaybackEvidenceChecks.Run(); return 0; }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally { application.Shutdown(); }
        }
        if (args.Contains("--check-player-extras")) return OnlinePlayerExtrasChecks.Run(application);
        if (args.Contains("--check-player-tracks")) return OnlinePlayerTrackChecks.Run(application);
        if (args.Contains("--check-player-host")) return OnlinePlayerHostChecks.Run(application);
        if (args.Contains("--check-player-design"))
        {
            try { OnlinePlayerDesignChecks.Run(); return 0; }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally { application.Shutdown(); }
        }
        if (args.Contains("--check-online-import")) return OnlineImportFlowChecks.Run(application);
        if (args.Contains("--check-online-player"))
        {
            var fixture = args.Contains("--mkv") ? Environment.GetEnvironmentVariable("ANIT_TEST_MKV") : null;
            if (args.Contains("--mkv") && (string.IsNullOrWhiteSpace(fixture) || !System.IO.File.Exists(fixture)))
            {
                Console.Error.WriteLine("--mkv exige ANIT_TEST_MKV apontando para um MKV de teste autorizado.");
                return 1;
            }
            return OnlinePlayerChecks.Run(application, fixture, args.Contains("--resume"));
        }
        if (args.Contains("--check-online"))
        {
            try { OnlineAnimeVisualChecks.Run(); return 0; }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally { application.Shutdown(); }
        }
        if (args.Contains("--check-theater-floating"))
        {
            try { TheaterFloatingChecks.Run(); return 0; }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally { application.Shutdown(); }
        }
        if (args.Contains("--check-startup"))
        {
            try { StartupChecks.Run(); return 0; }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally { application.Shutdown(); }
        }
        if(args.Contains("--preview-theater-viewer"))return TheaterPlayerChecks.Preview(application);
        if(args.Contains("--check-theater-player"))return TheaterPlayerChecks.Run(application);
        if(args.Contains("--check-theater-only"))
        {
            try {TheaterVisualChecks.Run();return 0;}
            catch(Exception error){Console.Error.WriteLine(error);return 1;}
            finally{application.Shutdown();}
        }
        if(args.Contains("--preview-theater"))
        {
            AppearanceManager.Apply(AniTSystemSettings.Default with{ThemeMode=args.Contains("--light")?AppearanceThemeMode.Light:AppearanceThemeMode.Dark});
            var theater=TheaterVisualChecks.Preview(args.Contains("--host"));
            theater.Title="AniT · Prévia Movie Theater";theater.Closed+=(_,_)=>application.Shutdown();
            application.Run(theater);return 0;
        }
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
            AppearanceManager.Apply(AniTSystemSettings.Default with { ThemeMode = args.Contains("--light") ? AppearanceThemeMode.Light : AppearanceThemeMode.Dark });
            var preview = NewWindow();
            preview.Title = args.Contains("--light") ? "AniT · Onboarding Fluent claro" : "AniT · Onboarding Fluent escuro";
            application.Run(preview);
            return 0;
        }

        var dictionaryCount = application.Resources.MergedDictionaries.Count;
        var legacyButton = application.TryFindResource(typeof(Button));
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
            CheckFluentFlow();
            if (!args.Contains("--check-onboarding-only")) TheaterVisualChecks.Run();
            Require(application.Resources.MergedDictionaries.Count == dictionaryCount, "Onboarding must not install global dictionaries.");
            Require(ReferenceEquals(legacyButton, application.TryFindResource(typeof(Button))), "Legacy button style must stay unchanged.");
            Require(application.TryFindResource("OnboardingButton") is null, "Fluent styles must remain scoped to onboarding.");
            CheckOptimizedBrandImage();
            if (!args.Contains("--check-onboarding-only")) HomeVisualChecks.Run();
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
        var headingSize = Find<TextBlock>(window, "Heading").FontSize;
        AppearanceManager.ApplyToWindow(window);
        Require(Find<TextBlock>(window, "Heading").FontSize == headingSize, "Repeated appearance must not compound text scaling.");
        Require(Find<Button>(window, "NextButton") is Wpf.Ui.Controls.Button, "Navigation uses Fluent controls.");
        Require(Find<TextBox>(window, "NameBox") is Wpf.Ui.Controls.TextBox, "Profile input uses Fluent control.");
        Require(Find<Button>(window, "NextButton").MinHeight >= 44, "Navigation hit target.");
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

    private static void CheckFluentFlow()
    {
        var window = NewWindow();
        window.ShowActivated = false; window.ShowInTaskbar = false; window.Opacity = 0;
        window.Show(); Pump();
        void Click(string name) { Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
        Click("NextButton");
        Find<TextBox>(window, "NameBox").Text = "Perfil de teste";
        Click("NextButton");
        Require(!Find<Button>(window, "NextButton").IsEnabled, "Folder step requires a choice or explicit skip.");
        Click("SkipFolderButton");
        Require(Find<TextBlock>(window, "SummaryName").Text == "Perfil de teste", "Review keeps the chosen profile.");
        Click("BackButton"); Click("BackButton");
        Require(Find<TextBox>(window, "NameBox").Text == "Perfil de teste", "Back navigation preserves profile input.");
        window.Close();
        Console.WriteLine("PASS: Fluent controls, appearance idempotence, scoped resources and profile/skip/back flow without saving.");
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

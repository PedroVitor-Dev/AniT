using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AniT.App;

public partial class DashboardWindow : Window, INotifyPropertyChanged
{
    private static readonly Brush ActiveHeroDotBrush = new SolidColorBrush(Color.FromRgb(168, 231, 255));
    private static readonly Brush InactiveHeroDotBrush = new SolidColorBrush(Color.FromRgb(49, 90, 137));
    private readonly DispatcherTimer heroRotationTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly List<DashboardHeroSlide> heroSlides = [];
    private Guid? heroAnimeId;
    private Guid? heroEpisodeId;
    private int heroSlideIndex;
    private LibraryWindow? libraryWindow;
    private ExploreWindow? exploreWindow;

    public ObservableCollection<DashboardCard> ContinueCards { get; } = [];
    public ObservableCollection<DashboardCard> RecentCards { get; } = [];
    public ObservableCollection<DashboardCard> FavoriteCards { get; } = [];
    public ObservableCollection<DashboardCard> TopRatedCards { get; } = [];
    public ObservableCollection<DashboardCalendarItem> CalendarItems { get; } = [];

    public string HeroTitle { get; private set; } = "Sua próxima história";
    public string HeroSubtitle { get; private set; } = "Sua estante está pronta";
    public string HeroCoverPath { get; private set; } = "Assets/normal-sf.png";
    public string HeroQuote { get; private set; } = AnimeQuoteCatalog.GetFor(Guid.Empty);
    public double HeroProgressPercent { get; private set; }
    public string HeroProgressLabel { get; private set; } = "Pronto para começar";
    public string LibraryAnimeCount { get; private set; } = "0";
    public string LibraryEpisodeCount { get; private set; } = "0";
    public string LibraryCompletedCount { get; private set; } = "0";
    public string SessionMessage { get; private set; } = "Grandes histórias te esperam aqui.";
    public bool CanPlayHero { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public DashboardWindow()
    {
        InitializeComponent();
        ResponsiveWindow.FitToWorkArea(this, 1380, 860);
        heroRotationTimer.Tick += HeroRotationTimer_Tick;
        Closed += (_, _) => heroRotationTimer.Stop();
        DataContext = this;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateResponsiveLayout(ActualWidth, ActualHeight);
        await RefreshAsync();
    }
    private async void Window_Activated(object? sender, EventArgs e) => await RefreshAsync();

    public async Task RefreshAsync()
    {
        await using var context = App.OpenFreshDatabase();
        var anime = await context.Anime
            .Include(item => item.Seasons)
            .ThenInclude(season => season.Episodes)
            .ThenInclude(episode => episode.PlaybackProgress)
            .AsNoTracking()
            .ToListAsync();

        ContinueCards.Clear();
        RecentCards.Clear();
        FavoriteCards.Clear();
        TopRatedCards.Clear();
        CalendarItems.Clear();

        var watching = anime
            .SelectMany(item => item.Seasons.SelectMany(season => season.Episodes.Select(episode => new { Anime = item, Episode = episode })))
            .Where(item => item.Episode.Status == global::AniT.Core.WatchStatus.Watching)
            .OrderByDescending(item => item.Episode.PlaybackProgress?.LastPlayedAt)
            .ToList();

        foreach (var item in watching.Take(4))
        {
            ContinueCards.Add(CreateContinueCard(item.Anime, item.Episode));
        }

        var recent = anime.OrderByDescending(item => item.CreatedAt).ToList();
        foreach (var item in recent.Take(5)) RecentCards.Add(CreateAnimeCard(item));
        foreach (var item in recent.Where(item => item.IsFavorite).Take(4)) FavoriteCards.Add(CreateAnimeCard(item));

        var rated = anime
            .Select(item => new
            {
                Anime = item,
                LocalRating = item.Seasons.SelectMany(season => season.Episodes).Where(episode => episode.Rating is > 0).Select(episode => episode.Rating!.Value).DefaultIfEmpty().Average(),
                HasLocalRating = item.Seasons.SelectMany(season => season.Episodes).Any(episode => episode.Rating is > 0)
            })
            .Where(item => item.HasLocalRating || item.Anime.CriticScore is not null)
            .OrderByDescending(item => item.HasLocalRating ? item.LocalRating : item.Anime.CriticScore!.Value / 20d)
            .Take(4);
        foreach (var item in rated)
        {
            var score = item.HasLocalRating ? item.LocalRating : item.Anime.CriticScore!.Value / 20d;
            TopRatedCards.Add(CreateAnimeCard(item.Anime, $"★ {score:0.0}"));
        }

        var nextLocalEpisodes = anime
            .Select(item => new
            {
                Anime = item,
                Episode = item.Seasons.SelectMany(season => season.Episodes)
                    .Where(episode => episode.Status != global::AniT.Core.WatchStatus.Completed)
                    .OrderBy(episode => episode.Number)
                    .FirstOrDefault()
            })
            .Where(item => item.Episode is not null)
            .OrderBy(item => item.Anime.Title)
            .Take(5);
        foreach (var item in nextLocalEpisodes)
        {
            CalendarItems.Add(new DashboardCalendarItem("LOCAL", item.Anime.Title, $"Ep. {item.Episode!.Number}"));
        }

        var allEpisodes = anime.SelectMany(item => item.Seasons).SelectMany(season => season.Episodes).ToList();
        LibraryAnimeCount = anime.Count.ToString();
        LibraryEpisodeCount = allEpisodes.Count.ToString();
        LibraryCompletedCount = allEpisodes.Count(episode => episode.Status == global::AniT.Core.WatchStatus.Completed).ToString();
        SessionMessage = ContinueCards.Count switch
        {
            0 => "Escolha uma história na biblioteca e aproveite.",
            1 => "Uma história está pronta para você continuar.",
            _ => $"Você tem {ContinueCards.Count} histórias prontas para continuar."
        };

        BuildHeroSlides(anime);

        ContinueEmptyText.Visibility = ContinueCards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FavoritesEmptyText.Visibility = FavoriteCards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TopRatedEmptyText.Visibility = TopRatedCards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DataContext = null;
        DataContext = this;
    }

    private void BuildHeroSlides(IReadOnlyCollection<global::AniT.Core.Anime> anime)
    {
        heroRotationTimer.Stop();
        heroSlides.Clear();

        var latestWatched = anime
            .Select(item => new
            {
                Anime = item,
                Episode = item.Seasons
                    .SelectMany(season => season.Episodes)
                    .Where(episode => episode.PlaybackProgress is not null)
                    .OrderByDescending(episode => episode.PlaybackProgress!.LastPlayedAt)
                    .FirstOrDefault()
            })
            .Where(item => item.Episode is not null)
            .OrderByDescending(item => item.Episode!.PlaybackProgress!.LastPlayedAt)
            .Take(4);

        foreach (var item in latestWatched)
        {
            var episode = item.Episode!;
            var progress = episode.PlaybackProgress;
            var progressPercent = episode.Status == global::AniT.Core.WatchStatus.Completed
                ? 100
                : progress is { DurationSeconds: > 0 }
                    ? Math.Clamp(progress.PositionSeconds / progress.DurationSeconds * 100, 0, 100)
                    : 0;
            var action = episode.Status switch
            {
                global::AniT.Core.WatchStatus.Watching => "Continue agora",
                global::AniT.Core.WatchStatus.Completed => "Visto recentemente",
                _ => "Pronto para assistir"
            };
            heroSlides.Add(new DashboardHeroSlide(
                item.Anime.Id,
                episode.Id,
                item.Anime.Title,
                $"Episódio {episode.Number:00}  •  {action}",
                IsUsableCover(item.Anime.CoverPath) ? item.Anime.CoverPath! : "Assets/normal-sf.png",
                progressPercent,
                AnimeQuoteCatalog.GetFor(item.Anime.Id),
                true));
        }

        if (heroSlides.Count == 0)
        {
            heroSlides.Add(new DashboardHeroSlide(
                null,
                null,
                "Sua próxima história",
                "Assista a um episódio para começar",
                "Assets/normal-sf.png",
                0,
                AnimeQuoteCatalog.GetFor(Guid.Empty),
                false));
        }

        ShowHeroSlide(0);
        if (heroSlides.Count > 1) heroRotationTimer.Start();
    }

    private void ShowHeroSlide(int index)
    {
        if (heroSlides.Count == 0) return;
        heroSlideIndex = (index % heroSlides.Count + heroSlides.Count) % heroSlides.Count;
        var slide = heroSlides[heroSlideIndex];
        heroAnimeId = slide.AnimeId;
        heroEpisodeId = slide.EpisodeId;
        HeroTitle = slide.Title;
        HeroSubtitle = slide.Subtitle;
        HeroCoverPath = slide.CoverPath;
        HeroQuote = slide.Quote;
        HeroProgressPercent = slide.ProgressPercent;
        HeroProgressLabel = slide.CanPlay ? $"{slide.ProgressPercent:0}% assistido" : "Pronto para começar";
        CanPlayHero = slide.CanPlay;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HeroTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HeroSubtitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HeroCoverPath)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HeroQuote)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HeroProgressPercent)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HeroProgressLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanPlayHero)));
        UpdateHeroDots();
    }

    private void UpdateHeroDots()
    {
        Ellipse[] dots = [HeroDot0, HeroDot1, HeroDot2, HeroDot3];
        for (var index = 0; index < dots.Length; index++)
        {
            dots[index].Visibility = index < heroSlides.Count ? Visibility.Visible : Visibility.Collapsed;
            dots[index].Fill = index == heroSlideIndex ? ActiveHeroDotBrush : InactiveHeroDotBrush;
            dots[index].Width = index == heroSlideIndex ? 18 : 7;
        }
    }

    private void HeroRotationTimer_Tick(object? sender, EventArgs e) => ShowHeroSlide(heroSlideIndex + 1);

    private void HeroDot_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || !int.TryParse(element.Tag?.ToString(), out var index) || index >= heroSlides.Count) return;
        ShowHeroSlide(index);
        heroRotationTimer.Stop();
        if (heroSlides.Count > 1) heroRotationTimer.Start();
    }

    private static DashboardCard CreateContinueCard(global::AniT.Core.Anime anime, global::AniT.Core.Episode episode)
    {
        var progress = episode.PlaybackProgress;
        var percent = progress is { DurationSeconds: > 0 }
            ? Math.Clamp(progress.PositionSeconds / progress.DurationSeconds * 100, 0, 100)
            : 0;
        return new DashboardCard(anime.Id, episode.Id, anime.Title, $"Episódio {episode.Number:00}", anime.EnglishTitle ?? "Anime local", IsUsableCover(anime.CoverPath) ? anime.CoverPath! : "Assets/normal-sf.png", percent, $"{percent:0}%", string.Empty);
    }

    private static DashboardCard CreateAnimeCard(global::AniT.Core.Anime anime, string scoreLabel = "")
    {
        var episodeCount = anime.Seasons.Sum(season => season.Episodes.Count);
        return new DashboardCard(anime.Id, null, anime.Title, string.Empty, anime.EnglishTitle ?? $"{episodeCount} episódio{(episodeCount == 1 ? string.Empty : "s")}", IsUsableCover(anime.CoverPath) ? anime.CoverPath! : "Assets/normal-sf.png", 0, string.Empty, scoreLabel);
    }

    private static bool IsUsableCover(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    private void Library_Click(object sender, RoutedEventArgs e)
    {
        if (libraryWindow is { IsLoaded: true })
        {
            if (libraryWindow.WindowState == WindowState.Minimized) libraryWindow.WindowState = WindowState.Normal;
            libraryWindow.Activate();
            return;
        }

        libraryWindow = new LibraryWindow { Owner = this };
        libraryWindow.Closed += (_, _) => libraryWindow = null;
        libraryWindow.Show();
    }

    private void Explore_Click(object sender, RoutedEventArgs e)
    {
        if (exploreWindow is { IsLoaded: true })
        {
            if (exploreWindow.WindowState == WindowState.Minimized) exploreWindow.WindowState = WindowState.Maximized;
            exploreWindow.Activate();
            return;
        }

        exploreWindow = new ExploreWindow { Owner = this };
        exploreWindow.Closed += (_, _) => exploreWindow = null;
        exploreWindow.Show();
    }

    private async void ConfigureShelf_Click(object sender, RoutedEventArgs e)
    {
        if (new SetupShelfWindow { Owner = this }.ShowDialog() is true) await RefreshAsync();
    }

    private async void HeroContinue_Click(object sender, RoutedEventArgs e) => await PlayEpisodeAsync(heroEpisodeId);
    private async void ContinueTop_Click(object sender, RoutedEventArgs e) => await PlayEpisodeAsync(heroEpisodeId);

    private async void ContinueCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid episodeId }) await PlayEpisodeAsync(episodeId);
    }

    private void HeroDetails_Click(object sender, RoutedEventArgs e)
    {
        if (heroAnimeId is Guid animeId) new AnimeDetailsWindow(animeId) { Owner = this }.ShowDialog();
    }

    private void AnimeCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid animeId }) new AnimeDetailsWindow(animeId) { Owner = this }.ShowDialog();
    }

    private async Task PlayEpisodeAsync(Guid? episodeId)
    {
        if (episodeId is not Guid id) return;
        try
        {
            await App.PlayEpisodeAsync(id);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Não foi possível continuar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Library_Click(sender, e);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchHint is not null) SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RoundedPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not Border border || border.ActualWidth <= 0 || border.ActualHeight <= 0) return;
        var radius = double.TryParse(border.Tag?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedRadius)
            ? parsedRadius
            : 18;
        border.Clip = new RectangleGeometry(new Rect(0, 0, border.ActualWidth, border.ActualHeight), radius, radius);
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout(e.NewSize.Width, e.NewSize.Height);
    }

    private void UpdateResponsiveLayout(double width, double height)
    {
        if (SidebarColumn is null || RightRailColumn is null || MainContentHost is null) return;

        if (width < 1220)
        {
            SidebarColumn.Width = new GridLength(184);
            RightRailColumn.Width = new GridLength(270);
            MainContentHost.Margin = new Thickness(18, 16, 18, 34);
            SearchContainer.MaxWidth = 350;
            CollectionsTopButton.Visibility = Visibility.Collapsed;
            MyListTopButton.Visibility = Visibility.Collapsed;
            HeroQuotePanel.Visibility = Visibility.Collapsed;
            HeroArtwork.Width = 230;
            HeroTitleText.FontSize = 30;
            HeroBanner.Height = height < 780 ? 286 : 310;
        }
        else if (width < 1600)
        {
            SidebarColumn.Width = new GridLength(220);
            RightRailColumn.Width = new GridLength(310);
            MainContentHost.Margin = new Thickness(24, 20, 24, 40);
            SearchContainer.MaxWidth = 500;
            CollectionsTopButton.Visibility = Visibility.Collapsed;
            MyListTopButton.Visibility = Visibility.Visible;
            HeroQuotePanel.Visibility = Visibility.Visible;
            HeroArtwork.Width = 280;
            HeroTitleText.FontSize = 37;
            HeroBanner.Height = height < 820 ? 308 : 336;
        }
        else if (width < 2300)
        {
            SidebarColumn.Width = new GridLength(232);
            RightRailColumn.Width = new GridLength(340);
            MainContentHost.Margin = new Thickness(30, 22, 30, 44);
            SearchContainer.MaxWidth = 580;
            CollectionsTopButton.Visibility = Visibility.Visible;
            MyListTopButton.Visibility = Visibility.Visible;
            HeroQuotePanel.Visibility = Visibility.Visible;
            HeroArtwork.Width = 310;
            HeroTitleText.FontSize = 41;
            HeroBanner.Height = 352;
        }
        else
        {
            SidebarColumn.Width = new GridLength(250);
            RightRailColumn.Width = new GridLength(380);
            MainContentHost.Margin = new Thickness(42, 28, 42, 52);
            SearchContainer.MaxWidth = 660;
            CollectionsTopButton.Visibility = Visibility.Visible;
            MyListTopButton.Visibility = Visibility.Visible;
            HeroQuotePanel.Visibility = Visibility.Visible;
            HeroArtwork.Width = 330;
            HeroTitleText.FontSize = 44;
            HeroBanner.Height = 380;
        }
    }
}

public sealed record DashboardCard(
    Guid AnimeId,
    Guid? EpisodeId,
    string Title,
    string Subtitle,
    string SecondaryText,
    string? CoverPath,
    double ProgressPercent,
    string ProgressLabel,
    string ScoreLabel);

public sealed record DashboardCalendarItem(string DayLabel, string Title, string EpisodeLabel);

public sealed record DashboardHeroSlide(
    Guid? AnimeId,
    Guid? EpisodeId,
    string Title,
    string Subtitle,
    string CoverPath,
    double ProgressPercent,
    string Quote,
    bool CanPlay);

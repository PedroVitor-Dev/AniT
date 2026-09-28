using Microsoft.EntityFrameworkCore;
using AniT.Infrastructure;
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
    private AniTSystemSettings homeSettings = AniTSystemSettings.Default;

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
        GlobalSearchController.Attach(this, SearchBox, SearchHint, SearchContainer);
        ResponsiveWindow.FitToWorkArea(this, 1380, 860);
        homeSettings = AniTSystemSettingsStore.Load();
        heroRotationTimer.Interval = TimeSpan.FromSeconds(homeSettings.HomeBannerIntervalSeconds);
        heroRotationTimer.Tick += HeroRotationTimer_Tick;
        Closed += (_, _) => heroRotationTimer.Stop();
        DataContext = this;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateResponsiveLayout(ActualWidth, ActualHeight);
        await RefreshAsync();
        var settings = global::AniT.Infrastructure.AniTSystemSettingsStore.Load();
        if (settings.ScanLibraryOnStartup)
        {
            await App.RefreshConfiguredLibraryOnStartupAsync();
            await RefreshAsync();
        }
        await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render);
        if (settings.RefreshMetadataOnStartup)
        {
            await App.RefreshMissingAnimeMetadataOnStartupAsync();
            await RefreshAsync();
        }
    }
    private async void Window_Activated(object? sender, EventArgs e) => await RefreshAsync();

    public async Task RefreshAsync()
    {
        homeSettings = AniTSystemSettingsStore.Load();
        heroRotationTimer.Interval = TimeSpan.FromSeconds(homeSettings.HomeBannerIntervalSeconds);
        ApplyHomeSectionLayout();
        await using var context = App.OpenFreshDatabase();
        var anime = await context.Anime
            .Where(global::AniT.Core.LibraryCatalogPresence.AnimeFilter)
            .Include(item => item.Seasons)
            .ThenInclude(season => season.Episodes)
            .ThenInclude(episode => episode.PlaybackProgress)
            .Include(item => item.Seasons)
            .ThenInclude(season => season.Episodes)
            .ThenInclude(episode => episode.MediaFiles)
            .Include(item => item.Aliases)
            .AsNoTracking()
            .ToListAsync();

        ContinueCards.Clear();
        RecentCards.Clear();
        FavoriteCards.Clear();
        TopRatedCards.Clear();
        CalendarItems.Clear();

        var watching = anime
            .SelectMany(item => item.Seasons.SelectMany(season => season.Episodes.Select(episode => new { Anime = item, Episode = episode })))
            .Where(item => global::AniT.Core.LibraryCatalogPresence.IsPresent(item.Episode)
                           && (item.Episode.Status == global::AniT.Core.WatchStatus.Watching
                               || (!homeSettings.HideCompletedFromContinue && item.Episode.Status == global::AniT.Core.WatchStatus.Completed)))
            .OrderByDescending(item => item.Episode.PlaybackProgress?.LastPlayedAt)
            .ToList();

        foreach (var item in watching.Take(homeSettings.HomeContinueItems))
        {
            ContinueCards.Add(CreateContinueCard(item.Anime, item.Episode));
        }

        var recent = anime.OrderByDescending(global::AniT.Core.LibraryFreshness.GetLatestImportAt).ToList();
        foreach (var item in recent.Take(homeSettings.HomeRecentItems)) RecentCards.Add(CreateAnimeCard(item));
        foreach (var item in recent.Where(item => item.IsFavorite).Take(homeSettings.HomeFavoriteItems)) FavoriteCards.Add(CreateAnimeCard(item));

        var rated = anime
            .Select(item => new
            {
                Anime = item,
                LocalRating = item.Seasons.SelectMany(season => season.Episodes).Where(episode => episode.Rating is > 0).Select(episode => episode.Rating!.Value).DefaultIfEmpty().Average(),
                HasLocalRating = item.Seasons.SelectMany(season => season.Episodes).Any(episode => episode.Rating is > 0)
            })
            .Where(item => item.HasLocalRating || item.Anime.CriticScore is not null)
            .OrderByDescending(item => item.HasLocalRating ? item.LocalRating : item.Anime.CriticScore!.Value / 20d)
            .Take(homeSettings.HomeTopRatedItems);
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
                    .Where(episode => global::AniT.Core.LibraryCatalogPresence.IsPresent(episode)
                                      && episode.Status != global::AniT.Core.WatchStatus.Completed)
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

        var allEpisodes = anime.SelectMany(item => item.Seasons).SelectMany(season => season.Episodes)
            .Where(global::AniT.Core.LibraryCatalogPresence.IsPresent)
            .ToList();
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
                    .Where(episode => global::AniT.Core.LibraryCatalogPresence.IsPresent(episode)
                                      && episode.PlaybackProgress is not null)
                    .OrderByDescending(episode => episode.PlaybackProgress!.LastPlayedAt)
                    .FirstOrDefault()
            })
            .Where(item => item.Episode is not null &&
                           (!homeSettings.HideCompletedFromContinue || item.Episode.Status != global::AniT.Core.WatchStatus.Completed))
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
                OrganizationPreferences.PreferredTitle(item.Anime, homeSettings.AnimeTitlePreference),
                $"{OrganizationPreferences.EpisodeCode(episode.Season?.Number ?? 1, episode.Number, homeSettings.EpisodeNumberDisplayFormat)}  •  {action}",
                IsUsableCover(item.Anime.CoverPath) ? item.Anime.CoverPath! : App.UpdatingArtworkPath,
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
        if (homeSettings.HomeBannerAutoRotate && heroSlides.Count > 1) heroRotationTimer.Start();
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
        if (homeSettings.HomeBannerAutoRotate && heroSlides.Count > 1) heroRotationTimer.Start();
    }

    private void ApplyHomeSectionLayout()
    {
        var sections = new Dictionary<string, FrameworkElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["continue"] = ContinueSection,
            ["recent"] = RecentSection,
            ["highlights"] = HighlightsSection
        };
        var hidden = (homeSettings.HiddenHomeSections ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, section) in sections) section.Visibility = hidden.Contains(id) ? Visibility.Collapsed : Visibility.Visible;

        var order = (homeSettings.HomeSectionOrder ?? AniTSystemSettings.DefaultHomeSectionOrder)
            .Where(sections.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var id in AniTSystemSettings.DefaultHomeSectionOrder.Where(id => !order.Contains(id, StringComparer.OrdinalIgnoreCase))) order.Add(id);

        var recentIndex = order.FindIndex(id => id.Equals("recent", StringComparison.OrdinalIgnoreCase));
        var highlightsIndex = order.FindIndex(id => id.Equals("highlights", StringComparison.OrdinalIgnoreCase));
        if (recentIndex >= 0 && highlightsIndex >= 0)
        {
            var favoritesFirst = homeSettings.HomeContentPriority == HomeContentPriority.FavoritesFirst;
            if ((favoritesFirst && highlightsIndex > recentIndex) || (!favoritesFirst && recentIndex > highlightsIndex))
            {
                (order[recentIndex], order[highlightsIndex]) = (order[highlightsIndex], order[recentIndex]);
            }
        }

        foreach (var section in sections.Values) HomeSectionsHost.Children.Remove(section);
        foreach (var id in order) HomeSectionsHost.Children.Add(sections[id]);
    }

    private static DashboardCard CreateContinueCard(global::AniT.Core.Anime anime, global::AniT.Core.Episode episode)
    {
        var settings = AniTSystemSettingsStore.Load();
        var preferredTitle = OrganizationPreferences.PreferredTitle(anime, settings.AnimeTitlePreference);
        var progress = episode.PlaybackProgress;
        var percent = progress is { DurationSeconds: > 0 }
            ? Math.Clamp(progress.PositionSeconds / progress.DurationSeconds * 100, 0, 100)
            : 0;
        return new DashboardCard(anime.Id, episode.Id, preferredTitle, OrganizationPreferences.EpisodeCode(episode.Season?.Number ?? 1, episode.Number, settings.EpisodeNumberDisplayFormat), anime.EnglishTitle ?? "Anime local", IsUsableCover(anime.CoverPath) ? anime.CoverPath! : App.UpdatingArtworkPath, percent, $"{percent:0}%", string.Empty, global::AniT.Core.LibraryFreshness.IsNew(anime, DateTimeOffset.UtcNow));
    }

    private static DashboardCard CreateAnimeCard(global::AniT.Core.Anime anime, string scoreLabel = "")
    {
        var settings = AniTSystemSettingsStore.Load();
        var preferredTitle = OrganizationPreferences.PreferredTitle(anime, settings.AnimeTitlePreference);
        var episodeCount = anime.Seasons.SelectMany(season => season.Episodes)
            .Count(global::AniT.Core.LibraryCatalogPresence.IsPresent);
        return new DashboardCard(anime.Id, null, preferredTitle, string.Empty, anime.EnglishTitle ?? $"{episodeCount} episódio{(episodeCount == 1 ? string.Empty : "s")}", IsUsableCover(anime.CoverPath) ? anime.CoverPath! : App.UpdatingArtworkPath, 0, string.Empty, scoreLabel, global::AniT.Core.LibraryFreshness.IsNew(anime, DateTimeOffset.UtcNow));
    }

    private static bool IsUsableCover(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    private void Library_Click(object sender, RoutedEventArgs e) => AppNavigation.OpenLibrary(this);
    private void Explore_Click(object sender, RoutedEventArgs e) => AppNavigation.Explore(this);
    private void Calendar_Click(object sender, RoutedEventArgs e) => AppNavigation.Calendar(this);
    private void History_Click(object sender, RoutedEventArgs e) => AppNavigation.History(this);
    private void Profile_Click(object sender, RoutedEventArgs e) => AppNavigation.Profile(this);
    private void Achievements_Click(object sender, RoutedEventArgs e) => AppNavigation.Achievements(this);

    private async void HeroContinue_Click(object sender, RoutedEventArgs e) => await ContinueAsync(heroEpisodeId, heroAnimeId);
    private async void ContinueTop_Click(object sender, RoutedEventArgs e) => await ContinueAsync(heroEpisodeId, heroAnimeId);

    private async void ContinueCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DashboardCard card }) await ContinueAsync(card.EpisodeId, card.AnimeId);
    }

    private void HeroDetails_Click(object sender, RoutedEventArgs e)
    {
        if (heroAnimeId is Guid animeId) AppNavigation.OpenAnimeDetails(this, animeId);
    }

    private void AnimeCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid animeId }) AppNavigation.OpenAnimeDetails(this, animeId);
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

    private async Task ContinueAsync(Guid? episodeId, Guid? animeId)
    {
        if (homeSettings.HomeContinueBehavior == HomeContinueBehavior.OpenAnimeDetails)
        {
            if (animeId is Guid id) AppNavigation.OpenAnimeDetails(this, id);
            return;
        }
        await PlayEpisodeAsync(episodeId);
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
            MyListTopButton.Visibility = Visibility.Collapsed;
            HeroQuotePanel.Visibility = Visibility.Collapsed;
            HeroArtwork.Width = 230;
            HeroTitleText.FontSize = 30;
            HeroProgressPanel.Width = 330;
            HeroBanner.Height = height < 780 ? 286 : 310;
        }
        else if (width < 1600)
        {
            SidebarColumn.Width = new GridLength(220);
            RightRailColumn.Width = new GridLength(310);
            MainContentHost.Margin = new Thickness(24, 20, 24, 40);
            SearchContainer.MaxWidth = 500;
            MyListTopButton.Visibility = Visibility.Visible;
            HeroQuotePanel.Visibility = Visibility.Visible;
            HeroArtwork.Width = 280;
            HeroTitleText.FontSize = 37;
            HeroProgressPanel.Width = 420;
            HeroBanner.Height = height < 820 ? 308 : 336;
        }
        else if (width < 2300)
        {
            SidebarColumn.Width = new GridLength(232);
            RightRailColumn.Width = new GridLength(340);
            MainContentHost.Margin = new Thickness(30, 22, 30, 44);
            SearchContainer.MaxWidth = 580;
            MyListTopButton.Visibility = Visibility.Visible;
            HeroQuotePanel.Visibility = Visibility.Visible;
            HeroArtwork.Width = 310;
            HeroTitleText.FontSize = 41;
            HeroProgressPanel.Width = 480;
            HeroBanner.Height = 352;
        }
        else
        {
            SidebarColumn.Width = new GridLength(250);
            RightRailColumn.Width = new GridLength(380);
            MainContentHost.Margin = new Thickness(42, 28, 42, 52);
            SearchContainer.MaxWidth = 660;
            MyListTopButton.Visibility = Visibility.Visible;
            HeroQuotePanel.Visibility = Visibility.Visible;
            HeroArtwork.Width = 330;
            HeroTitleText.FontSize = 44;
            HeroProgressPanel.Width = 520;
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
    string ScoreLabel,
    bool IsNew)
{
    public Visibility NewBadgeVisibility => IsNew ? Visibility.Visible : Visibility.Collapsed;
}

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

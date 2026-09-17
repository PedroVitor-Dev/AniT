using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AniT.App;

public partial class ExploreWindow : Window
{
    private Guid? nextEpisodeId;
    private readonly List<ExploreAnimeCard> sourceCards = [];

    public ObservableCollection<ExploreGenreCard> Genres { get; } =
    [
        new("Ação", "⚔", "#FF6580", "Assets/Explore/2.png"),
        new("Romance", "♥", "#FF6FAA", "Assets/Explore/7.png"),
        new("Fantasia", "✦", "#73E6FF", "Assets/Explore/1.png"),
        new("Drama", "◈", "#D595FF", "Assets/Explore/4.png"),
        new("Slice of Life", "♨", "#FFD06A", "Assets/Explore/5.png"),
        new("Mistério", "⌕", "#9BE9FF", "Assets/Explore/6.png"),
        new("Comédia", "☻", "#FFD45D", "Assets/Explore/3.png"),
        new("Isekai", "⚑", "#77E1D2", "Assets/Explore/8.png")
    ];

    public ObservableCollection<ExploreAnimeCard> TrendingCards { get; } = [];
    public ObservableCollection<ExploreAnimeCard> RecommendedCards { get; } = [];
    public ObservableCollection<ExploreAnimeCard> ReleaseCards { get; } = [];
    public ExploreWindow()
    {
        InitializeComponent();
        ResponsiveWindow.FitToWorkArea(this, 1380, 860);
        DataContext = this;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateResponsiveLayout(ActualWidth);
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await using var context = App.OpenFreshDatabase();
        var anime = await context.Anime
            .Include(item => item.Seasons)
            .ThenInclude(season => season.Episodes)
            .ThenInclude(episode => episode.PlaybackProgress)
            .AsNoTracking()
            .ToListAsync();

        sourceCards.Clear();
        foreach (var item in anime)
        {
            var episodes = item.Seasons.SelectMany(season => season.Episodes).ToList();
            var localRatings = episodes.Where(episode => episode.Rating is > 0).Select(episode => episode.Rating!.Value).ToList();
            var score = localRatings.Count > 0
                ? localRatings.Average()
                : item.CriticScore is > 0
                    ? item.CriticScore.Value / 20d
                    : 0;
            var nextEpisode = episodes
                .Where(episode => episode.Status != global::AniT.Core.WatchStatus.Completed)
                .OrderBy(episode => episode.Number)
                .FirstOrDefault();
            var lastActivity = episodes
                .Select(episode => episode.PlaybackProgress?.LastPlayedAt)
                .Where(value => value is not null)
                .DefaultIfEmpty(item.CreatedAt)
                .Max() ?? item.CreatedAt;

            sourceCards.Add(new ExploreAnimeCard(
                item.Id,
                item.Title,
                item.EnglishTitle ?? $"{episodes.Count} episódio{(episodes.Count == 1 ? string.Empty : "s")}",
                IsUsableCover(item.CoverPath) ? item.CoverPath! : "Assets/Explore/banner.png",
                score > 0 ? $"★ {score:0.0}" : "Novo",
                item.IsFavorite,
                item.CreatedAt,
                lastActivity,
                nextEpisode?.Id));
        }

        nextEpisodeId = sourceCards
            .Where(card => card.NextEpisodeId is not null)
            .OrderByDescending(card => card.LastActivity)
            .Select(card => card.NextEpisodeId)
            .FirstOrDefault();

        ApplyCurrentFilters();
    }

    private void ApplyCurrentFilters()
    {
        var query = SearchBox?.Text?.Trim() ?? string.Empty;
        IEnumerable<ExploreAnimeCard> filtered = sourceCards;
        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(card =>
                card.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || card.SecondaryText.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        }

        filtered = OrderFilter?.SelectedIndex switch
        {
            1 => filtered.OrderByDescending(card => card.CreatedAt),
            2 => filtered.OrderByDescending(card => ParseScore(card.ScoreLabel)),
            3 => filtered.OrderBy(card => card.Title),
            _ => filtered.OrderByDescending(card => card.LastActivity)
        };

        var cards = filtered.ToList();
        Replace(TrendingCards, cards.Take(5));
        Replace(RecommendedCards, cards.OrderByDescending(card => card.IsFavorite).ThenByDescending(card => ParseScore(card.ScoreLabel)).Take(5));
        Replace(ReleaseCards, cards.OrderByDescending(card => card.CreatedAt).Take(3));

        TrendingEmpty.Visibility = TrendingCards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ReleasesEmpty.Visibility = ReleaseCards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }

    private static double ParseScore(string value)
        => double.TryParse(value.Replace("★", string.Empty).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var score)
            ? score
            : 0;

    private static bool IsUsableCover(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchHint is not null) SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (IsLoaded) ApplyCurrentFilters();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ApplyCurrentFilters();
    }

    private void ApplyFilters_Click(object sender, RoutedEventArgs e) => ApplyCurrentFilters();

    private void Genre_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string genre }) return;
        GenreFilter.SelectedIndex = Math.Max(0, Genres.ToList().FindIndex(item => item.Name == genre) + 1);
        ApplyCurrentFilters();
    }

    private void BrowseNow_Click(object sender, RoutedEventArgs e) => TrendingSection.BringIntoView();

    private async void ContinueTop_Click(object sender, RoutedEventArgs e)
    {
        if (nextEpisodeId is not Guid episodeId) return;
        try
        {
            await App.PlayEpisodeAsync(episodeId);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Não foi possível continuar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AnimeCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid animeId } && animeId != Guid.Empty)
            new AnimeDetailsWindow(animeId) { Owner = this }.ShowDialog();
    }

    private void Library_Click(object sender, RoutedEventArgs e) => AppNavigation.OpenLibrary(this);

    private async void ConfigureShelf_Click(object sender, RoutedEventArgs e)
    {
        if (new SetupShelfWindow { Owner = this }.ShowDialog() is true) await LoadAsync();
    }

    private void Home_Click(object sender, RoutedEventArgs e) => AppNavigation.Home(this);
    private void Calendar_Click(object sender, RoutedEventArgs e) => AppNavigation.Calendar(this);
    private void History_Click(object sender, RoutedEventArgs e) => AppNavigation.History(this);
    private void Profile_Click(object sender, RoutedEventArgs e) => AppNavigation.Profile(this);

    private void RoundedPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not Border border || border.ActualWidth <= 0 || border.ActualHeight <= 0) return;
        var radius = double.TryParse(border.Tag?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedRadius)
            ? parsedRadius
            : 16;
        border.Clip = new RectangleGeometry(new Rect(0, 0, border.ActualWidth, border.ActualHeight), radius, radius);
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsiveLayout(e.NewSize.Width);

    private void UpdateResponsiveLayout(double width)
    {
        if (SidebarColumn is null || RightRailColumn is null || ExploreContentHost is null) return;

        if (width < 1180)
        {
            SidebarColumn.Width = new GridLength(184);
            RightRailColumn.Width = new GridLength(0);
            RightRail.Visibility = Visibility.Collapsed;
            ExploreContentHost.Margin = new Thickness(18, 14, 18, 34);
            SearchContainer.MaxWidth = 350;
            CollectionsTopButton.Visibility = Visibility.Collapsed;
            LibraryTopButton.Visibility = Visibility.Collapsed;
            DiscoveryBanner.Height = 248;
        }
        else if (width < 1600)
        {
            SidebarColumn.Width = new GridLength(220);
            RightRailColumn.Width = new GridLength(300);
            RightRail.Visibility = Visibility.Visible;
            ExploreContentHost.Margin = new Thickness(24, 16, 24, 42);
            SearchContainer.MaxWidth = 500;
            CollectionsTopButton.Visibility = Visibility.Collapsed;
            LibraryTopButton.Visibility = Visibility.Visible;
            DiscoveryBanner.Height = 276;
        }
        else if (width < 2300)
        {
            SidebarColumn.Width = new GridLength(232);
            RightRailColumn.Width = new GridLength(330);
            RightRail.Visibility = Visibility.Visible;
            ExploreContentHost.Margin = new Thickness(30, 18, 30, 46);
            SearchContainer.MaxWidth = 580;
            CollectionsTopButton.Visibility = Visibility.Visible;
            LibraryTopButton.Visibility = Visibility.Visible;
            DiscoveryBanner.Height = 292;
        }
        else
        {
            SidebarColumn.Width = new GridLength(250);
            RightRailColumn.Width = new GridLength(360);
            RightRail.Visibility = Visibility.Visible;
            ExploreContentHost.Margin = new Thickness(42, 22, 42, 54);
            SearchContainer.MaxWidth = 660;
            CollectionsTopButton.Visibility = Visibility.Visible;
            LibraryTopButton.Visibility = Visibility.Visible;
            DiscoveryBanner.Height = 310;
        }
    }
}

public sealed record ExploreGenreCard(string Name, string Icon, string Accent, string ImagePath);

public sealed record ExploreAnimeCard(
    Guid AnimeId,
    string Title,
    string SecondaryText,
    string CoverPath,
    string ScoreLabel,
    bool IsFavorite,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivity,
    Guid? NextEpisodeId);

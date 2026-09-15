using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AniT.App;

public partial class DashboardWindow : Window
{
    private Guid? heroAnimeId;
    private Guid? heroEpisodeId;

    public ObservableCollection<DashboardCard> ContinueCards { get; } = [];
    public ObservableCollection<DashboardCard> RecentCards { get; } = [];
    public ObservableCollection<DashboardCard> FavoriteCards { get; } = [];
    public ObservableCollection<DashboardCard> TopRatedCards { get; } = [];
    public ObservableCollection<DashboardCalendarItem> CalendarItems { get; } = [];

    public string HeroTitle { get; private set; } = "Sua próxima história";
    public string HeroSubtitle { get; private set; } = "Sua estante está pronta";
    public string? HeroCoverPath { get; private set; }
    public bool CanPlayHero { get; private set; }

    public DashboardWindow()
    {
        InitializeComponent();
        ResponsiveWindow.FitToWorkArea(this, 1380, 860);
        DataContext = this;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync();
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

        var hero = watching.FirstOrDefault();
        if (hero is not null)
        {
            SetHero(hero.Anime, hero.Episode, true);
        }
        else
        {
            var heroAnime = recent.FirstOrDefault();
            var nextEpisode = heroAnime?.Seasons.SelectMany(season => season.Episodes)
                .Where(episode => episode.Status != global::AniT.Core.WatchStatus.Completed)
                .OrderBy(episode => episode.Number)
                .FirstOrDefault();
            SetHero(heroAnime, nextEpisode, nextEpisode is not null);
        }

        ContinueEmptyText.Visibility = ContinueCards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FavoritesEmptyText.Visibility = FavoriteCards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TopRatedEmptyText.Visibility = TopRatedCards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DataContext = null;
        DataContext = this;
    }

    private void SetHero(global::AniT.Core.Anime? anime, global::AniT.Core.Episode? episode, bool canPlay)
    {
        heroAnimeId = anime?.Id;
        heroEpisodeId = episode?.Id;
        HeroTitle = anime?.Title ?? "Sua próxima história";
        HeroSubtitle = episode is null
            ? "Adicione um anime para começar"
            : $"Episódio {episode.Number:00}  •  {(episode.Status == global::AniT.Core.WatchStatus.Watching ? "Continue agora" : "Pronto para assistir")}";
        HeroCoverPath = IsUsableCover(anime?.CoverPath) ? anime!.CoverPath : null;
        CanPlayHero = canPlay;
    }

    private static DashboardCard CreateContinueCard(global::AniT.Core.Anime anime, global::AniT.Core.Episode episode)
    {
        var progress = episode.PlaybackProgress;
        var percent = progress is { DurationSeconds: > 0 }
            ? Math.Clamp(progress.PositionSeconds / progress.DurationSeconds * 100, 0, 100)
            : 0;
        return new DashboardCard(anime.Id, episode.Id, anime.Title, $"Episódio {episode.Number:00}", anime.EnglishTitle ?? "Anime local", IsUsableCover(anime.CoverPath) ? anime.CoverPath : null, percent, $"{percent:0}%", string.Empty);
    }

    private static DashboardCard CreateAnimeCard(global::AniT.Core.Anime anime, string scoreLabel = "")
    {
        var episodeCount = anime.Seasons.Sum(season => season.Episodes.Count);
        return new DashboardCard(anime.Id, null, anime.Title, string.Empty, anime.EnglishTitle ?? $"{episodeCount} episódio{(episodeCount == 1 ? string.Empty : "s")}", IsUsableCover(anime.CoverPath) ? anime.CoverPath : null, 0, string.Empty, scoreLabel);
    }

    private static bool IsUsableCover(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    private void Library_Click(object sender, RoutedEventArgs e) => new LibraryWindow { Owner = this }.ShowDialog();

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

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (SidebarColumn is null || CalendarColumn is null || SessionCardColumn is null) return;

        if (e.NewSize.Width < 1160)
        {
            SidebarColumn.Width = new GridLength(178);
            CalendarColumn.Width = new GridLength(270);
            SessionCardColumn.Width = new GridLength(215);
            SearchContainer.MaxWidth = 330;
            CollectionsTopButton.Visibility = Visibility.Collapsed;
            MyListTopButton.Visibility = Visibility.Collapsed;
            HeroQuote.Visibility = Visibility.Collapsed;
            HeroTitleText.FontSize = 29;
        }
        else if (e.NewSize.Width < 1450)
        {
            SidebarColumn.Width = new GridLength(220);
            CalendarColumn.Width = new GridLength(350);
            SessionCardColumn.Width = new GridLength(235);
            SearchContainer.MaxWidth = 430;
            CollectionsTopButton.Visibility = Visibility.Collapsed;
            MyListTopButton.Visibility = Visibility.Visible;
            HeroQuote.Visibility = Visibility.Visible;
            HeroTitleText.FontSize = 34;
        }
        else
        {
            SidebarColumn.Width = new GridLength(240);
            CalendarColumn.Width = new GridLength(420);
            SessionCardColumn.Width = new GridLength(260);
            SearchContainer.MaxWidth = 520;
            CollectionsTopButton.Visibility = Visibility.Visible;
            MyListTopButton.Visibility = Visibility.Visible;
            HeroQuote.Visibility = Visibility.Visible;
            HeroTitleText.FontSize = 38;
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

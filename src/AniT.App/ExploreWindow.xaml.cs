using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace AniT.App;

public partial class ExploreWindow : Window, INotifyPropertyChanged
{
    private Guid? nextEpisodeId;
    private readonly List<ExploreAnimeCard> sourceCards = [];
    private string? selectedRouletteMood;
    private ExploreAnimeCard? rouletteCard;
    private int rouletteSpinVersion;
    private string? selectedGenreCanonical;

    private static readonly IReadOnlyDictionary<string, string[]> MoodGenres =
        new Dictionary<string, string[]>(StringComparer.CurrentCultureIgnoreCase)
        {
            ["Fofo"] = ["Romance", "Comedy", "Slice of Life"],
            ["Emocionante"] = ["Action", "Adventure", "Sports", "Thriller"],
            ["Relaxante"] = ["Slice of Life", "Music"],
            ["Triste"] = ["Drama", "Psychological"],
            ["Épico"] = ["Fantasy", "Adventure", "Action", "Isekai", "Sci-Fi", "Mahou Shoujo"],
            ["Engraçado"] = ["Comedy", "Slice of Life"]
        };

    public ObservableCollection<ExploreGenreCard> Genres { get; } =
    [
        new("Ação", Icon("M4,4 L20,20 M4,4 L8,5 5,8 Z M16,19 L19,16 21,19 19,21 Z M20,4 L4,20 M20,4 L16,5 19,8 Z M8,19 L5,16 3,19 5,21 Z"), "#FF6580", "Assets/Explore/2.png"),
        new("Romance", Icon("M12,21 C10,19 3,14.8 3,8.8 C3,5.6 5.4,3.5 8.4,3.5 C10.1,3.5 11.4,4.4 12,5.8 C12.6,4.4 13.9,3.5 15.6,3.5 C18.6,3.5 21,5.6 21,8.8 C21,14.8 14,19 12,21 Z"), "#FF6FAA", "Assets/Explore/7.png"),
        new("Fantasia", Icon("M12,2.5 L14,8.5 20,10.5 14,12.5 12,18.5 10,12.5 4,10.5 10,8.5 Z M19,15.5 L20,18.5 22.5,19.5 20,20.5 19,23 18,20.5 15.5,19.5 18,18.5 Z"), "#73E6FF", "Assets/Explore/1.png"),
        new("Drama", Icon("M3.5,4.5 C7,3.5 10,3.5 13,4.5 L13,11.5 C12,15 9.5,17 7.5,17 C5.5,15.5 4,12.5 3.5,4.5 Z M7,8 L8.5,8 M10.5,8 L12,8 M7.2,12.5 C8.2,11.5 10.3,11.5 11.3,12.5 M12,7 C15,6 18,6 20.5,7 L20,14 C19,18 16.5,20.5 14.5,20.5 C12.8,19.6 11.5,18 10.8,16 M14,11 L15.5,11 M17.5,10.5 L19,10.5 M14,15.5 C15,17 18,17 19,15"), "#D595FF", "Assets/Explore/4.png"),
        new("Slice of Life", Icon("M4,8 L17,8 17,14 C17,18 14,20.5 10.5,20.5 C7,20.5 4,18 4,14 Z M17,10 L19,10 C22,10 22,15 19,15 L17,15 M8,3 C6,5 10,5.5 8,8 M13,3 C11,5 15,5.5 13,8"), "#FFD06A", "Assets/Explore/5.png"),
        new("Mistério", Icon("M10,3 A7,7 0 0 1 10,17 A7,7 0 0 1 10,3 M15,15 L21,21"), "#9BE9FF", "Assets/Explore/6.png"),
        new("Comédia", Icon("M12,3 A9,9 0 0 1 12,21 A9,9 0 0 1 12,3 M8,10 L8.1,10 M16,10 L16.1,10 M8,14 C9.2,17.2 14.8,17.2 16,14"), "#FFD45D", "Assets/Explore/3.png"),
        new("Isekai", Icon("M5,3 L18,3 18,21 5,21 Z M9,7 L15,6 15,18 9,17 Z M12.5,12 L13,12 M18,21 L21,21"), "#77E1D2", "Assets/Explore/8.png"),
        new("Ecchi", Icon("M9,20 C7.3,18.5 3.5,15.5 3.5,11.8 C3.5,9.4 5.2,8 7.2,8 C8.3,8 9.3,8.6 10,9.5 C10.7,8.6 11.7,8 12.8,8 C14.8,8 16.5,9.4 16.5,11.8 C16.5,15.5 10.7,18.8 9,20 Z M18,3 L18.8,5.2 21,6 18.8,6.8 18,9 17.2,6.8 15,6 17.2,5.2 Z"), "#FF82C7", "Assets/Explore/11.png"),
        new("Horror", Icon("M5,10 C5,5.5 8,3 12,3 C16,3 19,5.5 19,10 C19,13 17.5,15.5 15.5,16.5 L15.5,20 13.2,18.8 12,21 10.8,18.8 8.5,20 8.5,16.5 C6.5,15.5 5,13 5,10 Z M8.5,8 A2,2 0 0 1 8.5,12 A2,2 0 0 1 8.5,8 M15.5,8 A2,2 0 0 1 15.5,12 A2,2 0 0 1 15.5,8 M12,12.5 L10.8,15 13.2,15 Z"), "#C18BFF", "Assets/Explore/12.png"),
        new("Aventura", Icon("M12,3 A9,9 0 0 1 12,21 A9,9 0 0 1 12,3 M15.8,8.2 L13.5,13.5 8.2,15.8 10.5,10.5 Z M12,11.2 L12.1,11.2"), "#F4B35F", "Assets/Explore/10.png"),
        new("Esportes", Icon("M12,3 A9,9 0 0 1 12,21 A9,9 0 0 1 12,3 M9,8.5 L12,6.5 15,8.5 14,12.5 10,12.5 Z M4,10 L10,12.5 12,20 M20,10 L14,12.5 12,20 M7,5 L9,8.5 M17,5 L15,8.5"), "#69D9FF", "Assets/Explore/9.png")
    ];

    private static new Geometry Icon(string pathData)
    {
        var geometry = Geometry.Parse(pathData);
        geometry.Freeze();
        return geometry;
    }

    public ObservableCollection<ExploreAnimeCard> TrendingCards { get; } = [];
    public ObservableCollection<ExploreAnimeCard> RecommendedCards { get; } = [];
    public ObservableCollection<ExploreAnimeCard> ReleaseCards { get; } = [];
    public ObservableCollection<GenreAnimeShowcaseCard> GenreAnimeCards { get; } = [];
    public int GenreCategoryColumns { get; private set; } = 4;
    public double GenreCategoryImageHeight { get; private set; } = 226;
    public double GenreResultCardWidth { get; private set; } = 268;
    public double GenreResultCoverHeight { get; private set; } = 252;
    public event PropertyChangedEventHandler? PropertyChanged;
    public ExploreWindow()
    {
        InitializeComponent();
        var appearance = global::AniT.Infrastructure.AniTSystemSettingsStore.Load();
        RouletteParticlesLayer.Opacity = appearance.ReduceMotion ? 0 : appearance.AnimationIntensity / 100d;
        RouletteParticlesLayer.Visibility = appearance.ReduceMotion || appearance.AnimationIntensity == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        GlobalSearchController.Attach(this, SearchBox, SearchHint, SearchContainer);
        ResponsiveWindow.FitToWorkArea(this, 1380, 860);
        App.LibraryArtworkUpdated += App_LibraryArtworkUpdated;
        Closed += (_, _) => App.LibraryArtworkUpdated -= App_LibraryArtworkUpdated;
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
            .Where(global::AniT.Core.LibraryCatalogPresence.AnimeFilter)
            .Include(item => item.Seasons)
            .ThenInclude(season => season.Episodes)
            .ThenInclude(episode => episode.PlaybackProgress)
            .Include(item => item.Seasons)
            .ThenInclude(season => season.Episodes)
            .ThenInclude(episode => episode.MediaFiles)
            .AsNoTracking()
            .ToListAsync();

        sourceCards.Clear();
        var now = DateTimeOffset.UtcNow;
        foreach (var item in anime)
        {
            var episodes = item.Seasons.SelectMany(season => season.Episodes)
                .Where(global::AniT.Core.LibraryCatalogPresence.IsPresent)
                .ToList();
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
            var libraryStatus = episodes.Count > 0 && episodes.All(episode => episode.Status == global::AniT.Core.WatchStatus.Completed)
                ? ExploreLibraryStatus.Completed
                : episodes.Any(episode => episode.Status == global::AniT.Core.WatchStatus.Watching || episode.PlaybackProgress is { PositionSeconds: > 0 })
                    ? ExploreLibraryStatus.Watching
                    : ExploreLibraryStatus.NotStarted;

            sourceCards.Add(new ExploreAnimeCard(
                item.Id,
                item.Title,
                item.EnglishTitle ?? $"{episodes.Count} episódio{(episodes.Count == 1 ? string.Empty : "s")}",
                IsUsableCover(item.CoverPath) ? item.CoverPath! : App.UpdatingArtworkPath,
                score > 0 ? $"★ {score:0.0}" : "Sem nota",
                item.IsFavorite,
                item.CreatedAt,
                global::AniT.Core.LibraryFreshness.GetLatestImportAt(item),
                global::AniT.Core.LibraryFreshness.IsNew(item, now),
                lastActivity,
                nextEpisode?.Id,
                item.Genres ?? string.Empty,
                libraryStatus));
        }

        nextEpisodeId = sourceCards
            .Where(card => card.NextEpisodeId is not null)
            .OrderByDescending(card => card.LastActivity)
            .Select(card => card.NextEpisodeId)
            .FirstOrDefault();

        if (selectedGenreCanonical is not null) RefreshGenreResults(selectedGenreCanonical);
        ApplyCurrentFilters();
    }

    private void ApplyCurrentFilters()
    {
        var query = SearchBox?.Text?.Trim() ?? string.Empty;
        IEnumerable<ExploreAnimeCard> filtered = sourceCards;
        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(card =>
                global::AniT.Core.AnimeSearch.Matches(
                    query,
                    card.Title,
                    card.SecondaryText,
                    global::AniT.Core.AnimeGenreCatalog.ToSearchText(card.Genres)));
        }

        filtered = filtered.OrderByDescending(card => card.LastActivity);

        var cards = filtered.ToList();
        Replace(TrendingCards, cards.Take(5));
        Replace(RecommendedCards, cards.OrderByDescending(card => card.IsFavorite).ThenByDescending(card => ParseScore(card.ScoreLabel)).Take(5));
        Replace(ReleaseCards, cards.OrderByDescending(card => card.LatestImportAt).Take(3));

        TrendingEmpty.Visibility = TrendingCards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecommendedEmpty.Visibility = RecommendedCards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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

    private async void App_LibraryArtworkUpdated(object? sender, EventArgs e)
    {
        if (!IsLoaded || !IsVisible) return;
        await LoadAsync();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchHint is not null) SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (IsLoaded) ApplyCurrentFilters();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ApplyCurrentFilters();
    }

    private void RouletteClose_Click(object sender, RoutedEventArgs e)
    {
        rouletteSpinVersion++;
        RouletteOverlay.Visibility = Visibility.Collapsed;
    }

    private async void RouletteAgain_Click(object sender, RoutedEventArgs e)
        => await SpinRouletteAsync(selectedRouletteMood);

    private void RouletteDetails_Click(object sender, RoutedEventArgs e)
    {
        if (rouletteCard is not { } card) return;
        RouletteOverlay.Visibility = Visibility.Collapsed;
        AppNavigation.OpenAnimeDetails(this, card.AnimeId);
    }

    private async void RouletteWatch_Click(object sender, RoutedEventArgs e)
    {
        if (rouletteCard?.NextEpisodeId is not Guid episodeId) return;
        try
        {
            RouletteOverlay.Visibility = Visibility.Collapsed;
            await App.PlayEpisodeAsync(episodeId);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Não foi possível reproduzir", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Mood_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string mood } selectedButton) return;

        selectedButton.IsChecked = true;
        foreach (var button in MoodTagPanel.Children.OfType<ToggleButton>())
        {
            if (!ReferenceEquals(button, selectedButton)) button.IsChecked = false;
        }

        selectedRouletteMood = mood;
        MoodHint.Text = $"Girando uma sugestão com clima {mood.ToLowerInvariant()}...";
        await SpinRouletteAsync(mood);
    }

    private async Task SpinRouletteAsync(string? mood)
    {
        var spinVersion = ++rouletteSpinVersion;
        RouletteOverlay.Visibility = Visibility.Visible;
        RouletteDialog.Opacity = 0;
        RouletteDialogScale.ScaleX = 0.92;
        RouletteDialogScale.ScaleY = 0.92;
        RouletteDialog.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        RouletteDialogScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        RouletteDialogScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

        RouletteAgainButton.IsEnabled = false;
        RouletteDetailsButton.IsEnabled = false;
        RouletteWatchButton.IsEnabled = false;

        if (sourceCards.Count == 0)
        {
            rouletteCard = null;
            RouletteResult.Visibility = Visibility.Collapsed;
            RouletteEmpty.Visibility = Visibility.Visible;
            RouletteStatusText.Text = "A roleta precisa de títulos na sua estante.";
            RouletteAgainButton.Visibility = Visibility.Collapsed;
            RouletteDetailsButton.Visibility = Visibility.Collapsed;
            RouletteWatchButton.Visibility = Visibility.Collapsed;
            return;
        }

        RouletteResult.Visibility = Visibility.Visible;
        RouletteEmpty.Visibility = Visibility.Collapsed;
        RouletteAgainButton.Visibility = Visibility.Visible;
        RouletteDetailsButton.Visibility = Visibility.Visible;
        RouletteStatusText.Text = "Girando entre as histórias da sua estante...";
        RouletteMoodBadge.Text = mood ?? "Clima surpresa";
        RouletteExplanation.Text = string.Empty;

        var exactCandidates = string.IsNullOrWhiteSpace(mood) || !MoodGenres.TryGetValue(mood, out var desiredGenres)
            ? sourceCards.ToList()
            : sourceCards.Where(card => global::AniT.Core.AnimeGenreCatalog.Parse(card.Genres)
                .Any(genre => desiredGenres.Contains(genre, StringComparer.OrdinalIgnoreCase)))
                .ToList();
        var exactMoodMatch = exactCandidates.Count > 0;
        var candidates = exactMoodMatch ? exactCandidates : sourceCards.ToList();

        for (var step = 0; step < 7; step++)
        {
            if (spinVersion != rouletteSpinVersion) return;
            RouletteResult.DataContext = candidates[Random.Shared.Next(candidates.Count)];
            await Task.Delay(55 + step * 18);
        }

        if (spinVersion != rouletteSpinVersion) return;
        var finalCandidates = candidates.Count > 1 && rouletteCard is not null
            ? candidates.Where(card => card.AnimeId != rouletteCard.AnimeId).ToList()
            : candidates;
        rouletteCard = finalCandidates[Random.Shared.Next(finalCandidates.Count)];
        RouletteResult.DataContext = rouletteCard;
        RouletteResult.Opacity = 0.45;
        RouletteResult.BeginAnimation(OpacityProperty, new DoubleAnimation(0.45, 1, TimeSpan.FromMilliseconds(240)));

        RouletteMoodBadge.Text = exactMoodMatch ? mood ?? "Clima surpresa" : "Surpresa do acervo";
        RouletteStatusText.Text = "Sugestão pronta para você";
        RouletteExplanation.Text = exactMoodMatch
            ? $"Escolhido entre os títulos do seu acervo que combinam com o clima {mood?.ToLowerInvariant()}."
            : $"Não havia um título classificado como {mood?.ToLowerInvariant()}; por isso a roleta ampliou o sorteio para toda a sua estante.";
        MoodHint.Text = $"A roleta encontrou “{rouletteCard.Title}”. Você pode girar novamente.";

        RouletteAgainButton.IsEnabled = true;
        RouletteDetailsButton.IsEnabled = true;
        RouletteWatchButton.Visibility = rouletteCard.NextEpisodeId is null ? Visibility.Collapsed : Visibility.Visible;
        RouletteWatchButton.IsEnabled = rouletteCard.NextEpisodeId is not null;
    }

    private void Genre_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string genre }) return;
        if (Genres.FirstOrDefault(item => item.Name == genre)?.IsSelected == true)
        {
            ClearGenreShowcase();
            return;
        }

        SelectGenreShowcase(genre);
    }

    private void GenreCategoriesToggle_Click(object sender, RoutedEventArgs e)
    {
        SetGenreCategoriesExpanded(GenreCardsHost.Visibility != Visibility.Visible);
    }

    private void SetGenreCategoriesExpanded(bool expanded)
    {
        if (expanded)
        {
            GenreCardsHost.Visibility = Visibility.Visible;
            GenreCardsHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            GenreCategoriesToggle.Content = "Recolher categorias  ⌃";
            return;
        }

        GenreCardsHost.Visibility = Visibility.Collapsed;
        GenreCategoriesToggle.Content = "Mostrar categorias  ⌄";
    }

    private void ClearGenreShowcase()
    {
        selectedGenreCanonical = null;
        foreach (var item in Genres) item.IsSelected = false;
        GenreAnimeCards.Clear();
        GenreResultsPanel.Visibility = Visibility.Collapsed;
        SelectedGenrePill.Visibility = Visibility.Collapsed;
    }

    private void SelectGenreShowcase(string displayName)
    {
        var definition = global::AniT.Core.AnimeGenreCatalog.All.FirstOrDefault(item =>
            string.Equals(item.DisplayName, displayName, StringComparison.CurrentCultureIgnoreCase)
            || string.Equals(item.CanonicalName, displayName, StringComparison.OrdinalIgnoreCase));
        selectedGenreCanonical = definition?.CanonicalName ?? displayName;
        var selectedDisplayName = definition?.DisplayName ?? displayName;

        foreach (var item in Genres)
        {
            var itemDefinition = global::AniT.Core.AnimeGenreCatalog.All.FirstOrDefault(candidate =>
                string.Equals(candidate.DisplayName, item.Name, StringComparison.CurrentCultureIgnoreCase));
            item.IsSelected = string.Equals(itemDefinition?.CanonicalName ?? item.Name, selectedGenreCanonical, StringComparison.OrdinalIgnoreCase);
        }

        SelectedGenreText.Text = $"✦  {selectedDisplayName}";
        SelectedGenrePill.Visibility = Visibility.Visible;
        GenreResultsTitle.Text = $"Sua estante em {selectedDisplayName}";
        RefreshGenreResults(selectedGenreCanonical);
        GenreResultsPanel.Visibility = Visibility.Visible;
        GenreResultsPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(260)));
    }

    private void RefreshGenreResults(string selectedCanonicalGenre)
    {
        var matches = sourceCards
            .Where(card => global::AniT.Core.AnimeGenreCatalog.Parse(card.Genres)
                .Contains(selectedCanonicalGenre, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(card => card.IsFavorite)
            .ThenByDescending(card => ParseScore(card.ScoreLabel))
            .ThenBy(card => card.Title)
            .ToArray();

        GenreAnimeCards.Clear();
        foreach (var card in matches)
        {
            var tags = global::AniT.Core.AnimeGenreCatalog.Parse(card.Genres)
                .Select(genre => new ExploreGenreTag(
                    global::AniT.Core.AnimeGenreCatalog.DisplayName(genre),
                    string.Equals(genre, selectedCanonicalGenre, StringComparison.OrdinalIgnoreCase),
                    string.Equals(genre, selectedCanonicalGenre, StringComparison.OrdinalIgnoreCase) ? "#F1FDFF" : "#B9D3EA"))
                .ToArray();
            GenreAnimeCards.Add(new GenreAnimeShowcaseCard(
                card.AnimeId,
                card.Title,
                card.SecondaryText,
                card.CoverPath,
                card.ScoreLabel,
                tags));
        }

        GenreResultsCount.Text = $"{GenreAnimeCards.Count} título{(GenreAnimeCards.Count == 1 ? string.Empty : "s")}";
        GenreResultsEmpty.Visibility = GenreAnimeCards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SelectGenre(string genre)
    {
        SearchBox.Clear();
        if (IsLoaded)
        {
            SelectGenreShowcase(genre);
        }
        GenreSection.BringIntoView();
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
            AppNavigation.OpenAnimeDetails(this, animeId);
    }

    private void Library_Click(object sender, RoutedEventArgs e) => AppNavigation.OpenLibrary(this);

    private void Home_Click(object sender, RoutedEventArgs e) => AppNavigation.Home(this);
    private void Calendar_Click(object sender, RoutedEventArgs e) => AppNavigation.Calendar(this);
    private void History_Click(object sender, RoutedEventArgs e) => AppNavigation.History(this);
    private void Profile_Click(object sender, RoutedEventArgs e) => AppNavigation.Profile(this);
    private void Achievements_Click(object sender, RoutedEventArgs e) => AppNavigation.Achievements(this);

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
            LibraryTopButton.Visibility = Visibility.Collapsed;
            DiscoveryBanner.Height = 248;
            DiscoveryMascot.Width = 310;
            DiscoveryMascot.Height = 290;
            DiscoveryMascot.Margin = new Thickness(0);
            DiscoveryMascotTranslate.X = 4;
            DiscoveryMascotGlow.Width = 225;
            DiscoveryMascotGlow.Margin = new Thickness(0, 0, 14, -16);
            SetGenreCardSizes(3, 188, 230, 220, 13);
        }
        else if (width < 1600)
        {
            SidebarColumn.Width = new GridLength(220);
            RightRailColumn.Width = new GridLength(300);
            RightRail.Visibility = Visibility.Visible;
            ExploreContentHost.Margin = new Thickness(24, 16, 24, 42);
            SearchContainer.MaxWidth = 500;
            LibraryTopButton.Visibility = Visibility.Visible;
            DiscoveryBanner.Height = 276;
            DiscoveryMascot.Width = 400;
            DiscoveryMascot.Height = 350;
            DiscoveryMascot.Margin = new Thickness(0);
            DiscoveryMascotTranslate.X = 8;
            DiscoveryMascotGlow.Width = 300;
            DiscoveryMascotGlow.Margin = new Thickness(0, 0, 22, -18);
            SetGenreCardSizes(width < 1450 ? 3 : 4, 204, 244, 230, 16);
        }
        else if (width < 2300)
        {
            SidebarColumn.Width = new GridLength(232);
            RightRailColumn.Width = new GridLength(330);
            RightRail.Visibility = Visibility.Visible;
            ExploreContentHost.Margin = new Thickness(30, 18, 30, 46);
            SearchContainer.MaxWidth = 580;
            LibraryTopButton.Visibility = Visibility.Visible;
            DiscoveryBanner.Height = 292;
            DiscoveryMascot.Width = 440;
            DiscoveryMascot.Height = 380;
            DiscoveryMascot.Margin = new Thickness(0);
            DiscoveryMascotTranslate.X = 10;
            DiscoveryMascotGlow.Width = 335;
            DiscoveryMascotGlow.Margin = new Thickness(0, 0, 24, -20);
            SetGenreCardSizes(width < 1850 ? 4 : 6, 226, 268, 252, 20);
        }
        else
        {
            SidebarColumn.Width = new GridLength(250);
            RightRailColumn.Width = new GridLength(360);
            RightRail.Visibility = Visibility.Visible;
            ExploreContentHost.Margin = new Thickness(42, 22, 42, 54);
            SearchContainer.MaxWidth = 660;
            LibraryTopButton.Visibility = Visibility.Visible;
            DiscoveryBanner.Height = 310;
            DiscoveryMascot.Width = 470;
            DiscoveryMascot.Height = 410;
            DiscoveryMascot.Margin = new Thickness(0);
            DiscoveryMascotTranslate.X = 12;
            DiscoveryMascotGlow.Width = 360;
            DiscoveryMascotGlow.Margin = new Thickness(0, 0, 27, -22);
            SetGenreCardSizes(6, 250, 292, 276, 22);
        }
    }

    private void SetGenreCardSizes(
        int categoryColumns,
        double categoryImageHeight,
        double resultWidth,
        double resultCoverHeight,
        double panelPadding)
    {
        var appearance = global::AniT.Infrastructure.AniTSystemSettingsStore.Load();
        if (appearance.CardsPerRow > 0) categoryColumns = appearance.CardsPerRow;
        var density = appearance.CardSize switch
        {
            global::AniT.Infrastructure.AppearanceCardSize.Compact => 0.88,
            global::AniT.Infrastructure.AppearanceCardSize.Large => 1.12,
            _ => 1d
        };
        categoryImageHeight *= density;
        resultWidth *= density;
        resultCoverHeight *= density;
        GenreCategoryColumns = categoryColumns;
        GenreCategoryImageHeight = categoryImageHeight;
        GenreResultCardWidth = resultWidth;
        GenreResultCoverHeight = resultCoverHeight;
        GenreCardsHost.Padding = new Thickness(panelPadding);
        GenreResultsPanel.Padding = new Thickness(panelPadding);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GenreCategoryColumns)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GenreCategoryImageHeight)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GenreResultCardWidth)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GenreResultCoverHeight)));
    }
}

public sealed class ExploreGenreCard : INotifyPropertyChanged
{
    private const int OptimizedImageWidth = 720;
    private static readonly ConcurrentDictionary<string, ImageSource> ImageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly DropShadowEffect normalGlow;
    private readonly DropShadowEffect selectedGlow;
    private bool isSelected;

    public ExploreGenreCard(string name, Geometry iconData, string accent, string imagePath)
    {
        Name = name;
        IconData = iconData;
        AccentColor = (Color)ColorConverter.ConvertFromString(accent);
        var accentBrush = new SolidColorBrush(AccentColor);
        accentBrush.Freeze();
        AccentBrush = accentBrush;
        normalGlow = CreateGlow(AccentColor, 21, 0.44);
        selectedGlow = CreateGlow(AccentColor, 38, 0.92);
        ImageSource = ImageCache.GetOrAdd(imagePath, LoadOptimizedImage);
    }

    public string Name { get; }
    public Geometry IconData { get; }
    public Color AccentColor { get; }
    public Brush AccentBrush { get; }
    public Effect GlowEffect => isSelected ? selectedGlow : normalGlow;
    public ImageSource ImageSource { get; }
    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value) return;
            isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GlowEffect)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private static DropShadowEffect CreateGlow(Color color, double blurRadius, double opacity)
    {
        var effect = new DropShadowEffect
        {
            Color = color,
            BlurRadius = blurRadius,
            ShadowDepth = 0,
            Opacity = opacity,
            RenderingBias = RenderingBias.Performance
        };
        effect.Freeze();
        return effect;
    }

    private static ImageSource LoadOptimizedImage(string imagePath)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = OptimizedImageWidth;
        image.UriSource = new Uri($"pack://application:,,,/{imagePath.Replace('\\', '/')}", UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }
}

public sealed record GenreAnimeShowcaseCard(
    Guid AnimeId,
    string Title,
    string SecondaryText,
    string CoverPath,
    string ScoreLabel,
    IReadOnlyList<ExploreGenreTag> Tags);

public sealed record ExploreGenreTag(string Name, bool IsSelected, string Foreground);

public sealed record ExploreAnimeCard(
    Guid AnimeId,
    string Title,
    string SecondaryText,
    string CoverPath,
    string ScoreLabel,
    bool IsFavorite,
    DateTimeOffset CreatedAt,
    DateTimeOffset LatestImportAt,
    bool IsNew,
    DateTimeOffset LastActivity,
    Guid? NextEpisodeId,
    string Genres,
    ExploreLibraryStatus LibraryStatus)
{
    public Visibility NewBadgeVisibility => IsNew ? Visibility.Visible : Visibility.Collapsed;
}

public enum ExploreLibraryStatus
{
    NotStarted,
    Watching,
    Completed
}

using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
        new("Ação", Geometry.Parse("M5,3 L19,17 M3,5 L7,3 7,7 M17,17 L21,21 M19,3 L5,17 M21,5 L17,3 17,7 M7,17 L3,21"), "#FF6580", "Assets/Explore/2.png"),
        new("Romance", Geometry.Parse("M12,21 C10.2,19.2 4,15 4,9.5 C4,6.5 6.2,4.5 9,4.5 C10.6,4.5 11.6,5.4 12,6.2 C12.4,5.4 13.4,4.5 15,4.5 C17.8,4.5 20,6.5 20,9.5 C20,15 13.8,19.2 12,21 Z"), "#FF6FAA", "Assets/Explore/7.png"),
        new("Fantasia", Geometry.Parse("M12,3 L14.7,8.5 21,9.4 16.5,13.8 17.6,20 12,17 6.4,20 7.5,13.8 3,9.4 9.3,8.5 Z"), "#73E6FF", "Assets/Explore/1.png"),
        new("Drama", Geometry.Parse("M12,3 L21,12 12,21 3,12 Z M12,7 L17,12 12,17 7,12 Z"), "#D595FF", "Assets/Explore/4.png"),
        new("Slice of Life", Geometry.Parse("M12,2 C13.2,6.2 18.5,8.1 18.5,13.4 C18.5,18 15.7,21.5 12,21.5 C8.1,21.5 5,18.4 5,14.1 C5,10.5 7.4,8 9.2,5.1 C9.3,8.1 10.6,9.6 12,10.6 C13.2,8.1 12.8,4.8 12,2 Z M12,12 C14.2,14.1 14.1,16.7 12,18.7 C9.9,17.5 9.2,14.8 12,12 Z"), "#FFD06A", "Assets/Explore/5.png"),
        new("Mistério", Geometry.Parse("M10.5,4 A6.5,6.5 0 1 1 10.49,17 M15.2,15.2 L21,21"), "#9BE9FF", "Assets/Explore/6.png"),
        new("Comédia", Geometry.Parse("M12,3 A9,9 0 1 1 11.99,3 M8,10 L8.1,10 M16,10 L16.1,10 M8,14 C9,17 15,17 16,14"), "#FFD45D", "Assets/Explore/3.png"),
        new("Isekai", Geometry.Parse("M5,22 L5,3 M6,4 L19,4 16,9 19,14 6,14 Z"), "#77E1D2", "Assets/Explore/8.png")
    ];

    public ObservableCollection<ExploreAnimeCard> TrendingCards { get; } = [];
    public ObservableCollection<ExploreAnimeCard> RecommendedCards { get; } = [];
    public ObservableCollection<ExploreAnimeCard> ReleaseCards { get; } = [];
    public ObservableCollection<GenreAnimeShowcaseCard> GenreAnimeCards { get; } = [];
    public double GenreCategoryCardWidth { get; private set; } = 246;
    public double GenreCategoryImageHeight { get; private set; } = 226;
    public double GenreResultCardWidth { get; private set; } = 268;
    public double GenreResultCoverHeight { get; private set; } = 252;
    public event PropertyChangedEventHandler? PropertyChanged;
    public ExploreWindow()
    {
        InitializeComponent();
        GlobalSearchController.Attach(this, SearchBox, SearchHint, SearchContainer);
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
            var libraryStatus = episodes.Count > 0 && episodes.All(episode => episode.Status == global::AniT.Core.WatchStatus.Completed)
                ? ExploreLibraryStatus.Completed
                : episodes.Any(episode => episode.Status == global::AniT.Core.WatchStatus.Watching || episode.PlaybackProgress is { PositionSeconds: > 0 })
                    ? ExploreLibraryStatus.Watching
                    : ExploreLibraryStatus.NotStarted;

            sourceCards.Add(new ExploreAnimeCard(
                item.Id,
                item.Title,
                item.EnglishTitle ?? $"{episodes.Count} episódio{(episodes.Count == 1 ? string.Empty : "s")}",
                IsUsableCover(item.CoverPath) ? item.CoverPath! : "Assets/Explore/banner.png",
                score > 0 ? $"★ {score:0.0}" : "Novo",
                item.IsFavorite,
                item.CreatedAt,
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

        if (GenreFilter?.SelectedItem is ComboBoxItem { Content: string selectedGenre }
            && GenreFilter.SelectedIndex > 0)
        {
            var canonicalGenre = global::AniT.Core.AnimeGenreCatalog.All
                .FirstOrDefault(item => string.Equals(item.DisplayName, selectedGenre, StringComparison.CurrentCultureIgnoreCase))
                ?.CanonicalName ?? selectedGenre;
            filtered = filtered.Where(card => global::AniT.Core.AnimeGenreCatalog.Parse(card.Genres)
                .Contains(canonicalGenre, StringComparer.OrdinalIgnoreCase));
        }

        if (YearFilter?.SelectedItem is ComboBoxItem { Content: string selectedYear }
            && YearFilter.SelectedIndex > 0
            && int.TryParse(selectedYear, out var year))
        {
            filtered = filtered.Where(card => card.CreatedAt.Year == year);
        }

        if (StatusFilter?.SelectedIndex > 0)
        {
            var status = StatusFilter.SelectedIndex == 1
                ? ExploreLibraryStatus.Watching
                : ExploreLibraryStatus.Completed;
            filtered = filtered.Where(card => card.LibraryStatus == status);
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

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchHint is not null) SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (IsLoaded) ApplyCurrentFilters();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ApplyCurrentFilters();
    }

    private void QuickFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (ReferenceEquals(sender, GenreFilter)) SyncGenreShowcaseFromFilter();
        ApplyCurrentFilters();
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
        var targetIndex = Math.Max(0, Genres.ToList().FindIndex(item => item.Name == genre) + 1);
        if (Genres.FirstOrDefault(item => item.Name == genre)?.IsSelected == true)
        {
            GenreFilter.SelectedIndex = 0;
            SyncGenreShowcaseFromFilter();
            ApplyCurrentFilters();
            return;
        }

        GenreFilter.SelectedIndex = targetIndex;
        SelectGenreShowcase(genre);
        ApplyCurrentFilters();
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

    private void SyncGenreShowcaseFromFilter()
    {
        if (GenreFilter.SelectedIndex <= 0 || GenreFilter.SelectedItem is not ComboBoxItem { Content: string displayName })
        {
            selectedGenreCanonical = null;
            foreach (var item in Genres) item.IsSelected = false;
            GenreAnimeCards.Clear();
            GenreResultsPanel.Visibility = Visibility.Collapsed;
            SelectedGenrePill.Visibility = Visibility.Collapsed;
            return;
        }

        SelectGenreShowcase(displayName);
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
        var option = GenreFilter.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Content?.ToString(), genre, StringComparison.CurrentCultureIgnoreCase));
        if (option is not null) GenreFilter.SelectedItem = option;
        SearchBox.Clear();
        if (IsLoaded)
        {
            SelectGenreShowcase(option?.Content?.ToString() ?? genre);
            ApplyCurrentFilters();
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

    private async void ConfigureShelf_Click(object sender, RoutedEventArgs e)
    {
        if (new SetupShelfWindow { Owner = this }.ShowDialog() is true) await LoadAsync();
    }

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
            SetGenreCardSizes(210, 188, 230, 220, 13);
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
            SetGenreCardSizes(226, 204, 244, 230, 16);
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
            SetGenreCardSizes(246, 226, 268, 252, 20);
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
            SetGenreCardSizes(276, 250, 292, 276, 22);
        }
    }

    private void SetGenreCardSizes(
        double categoryWidth,
        double categoryImageHeight,
        double resultWidth,
        double resultCoverHeight,
        double panelPadding)
    {
        GenreCategoryCardWidth = categoryWidth;
        GenreCategoryImageHeight = categoryImageHeight;
        GenreResultCardWidth = resultWidth;
        GenreResultCoverHeight = resultCoverHeight;
        GenreCardsHost.Padding = new Thickness(panelPadding);
        GenreResultsPanel.Padding = new Thickness(panelPadding);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GenreCategoryCardWidth)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GenreCategoryImageHeight)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GenreResultCardWidth)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GenreResultCoverHeight)));
    }
}

public sealed class ExploreGenreCard : INotifyPropertyChanged
{
    private bool isSelected;

    public ExploreGenreCard(string name, Geometry iconData, string accent, string imagePath)
    {
        Name = name;
        IconData = iconData;
        Accent = accent;
        ImagePath = imagePath;
    }

    public string Name { get; }
    public Geometry IconData { get; }
    public string Accent { get; }
    public string ImagePath { get; }
    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value) return;
            isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
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
    DateTimeOffset LastActivity,
    Guid? NextEpisodeId,
    string Genres,
    ExploreLibraryStatus LibraryStatus);

public enum ExploreLibraryStatus
{
    NotStarted,
    Watching,
    Completed
}

using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AniT.App;

public partial class AnimeDetailsWindow : Window
{
    private readonly Guid animeId;
    private Guid? selectedReviewEpisodeId;
    private bool isLoadingReview;
    private double selectedRating;
    private LibraryWindow? libraryWindow;
    private ExploreWindow? exploreWindow;
    public ObservableCollection<EpisodeItem> Episodes { get; } = [];
    public string AnimeTitle { get; private set; } = string.Empty;
    public string EnglishTitleDisplay { get; private set; } = string.Empty;
    public string Initial { get; private set; } = "?";
    public string Summary { get; private set; } = string.Empty;
    public string EpisodeCountLabel { get; private set; } = string.Empty;
    public string PlayNextLabel { get; private set; } = "▶  Assistir próximo episódio";
    public bool CanPlayNext { get; private set; } = true;
    public string? CoverPath { get; private set; }
    public string Synopsis { get; private set; } = "A sinopse ainda não está disponível.";
    public string CriticScoreLabel { get; private set; } = "—";
    public string CriticFiveLabel { get; private set; } = "—";
    public string WatchStateLabel { get; private set; } = "Na sua biblioteca";
    public string LocalAverageLabel { get; private set; } = "—";
    public string RatedEpisodeCountLabel { get; private set; } = "Nenhum episódio avaliado";
    public string LocalReviewSummary { get; private set; } = "Você ainda não escreveu comentários sobre esta obra.";

    public AnimeDetailsWindow(Guid animeId)
    {
        this.animeId = animeId;
        InitializeComponent();
        ResponsiveWindow.FitToWorkArea(this, 1140, 800);
        DataContext = this;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateResponsiveLayout(ActualWidth);
        await LoadAsync();
    }
    private async void Window_Activated(object? sender, EventArgs e) => await LoadAsync();

    private async Task LoadAsync()
    {
        var preferredReviewEpisodeId = selectedReviewEpisodeId;
        await using var context = App.OpenFreshDatabase();
        var anime = await context.Anime
            .Include(item => item.Seasons)
            .ThenInclude(season => season.Episodes)
            .ThenInclude(episode => episode.PlaybackProgress)
            .Include(item => item.Seasons)
            .ThenInclude(season => season.Episodes)
            .ThenInclude(episode => episode.MediaFiles)
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == animeId);
        if (anime is null) return;

        AnimeTitle = anime.Title;
        EnglishTitleDisplay = FormatEnglishTitle(anime.EnglishTitle);
        Initial = anime.Title[..1].ToUpperInvariant();
        CoverPath = File.Exists(anime.CoverPath) ? anime.CoverPath : null;
        Synopsis = string.IsNullOrWhiteSpace(anime.Synopsis)
            ? "A sinopse ainda não está disponível. Use Atualizar Títulos na Biblioteca para tentar novamente."
            : anime.Synopsis;
        CriticScoreLabel = anime.CriticScore is { } criticScore ? $"{criticScore:0}/100" : "—";
        CriticFiveLabel = anime.CriticScore is { } publicScore ? $"{publicScore / 20d:0.0}" : "—";
        var allEpisodes = anime.Seasons.SelectMany(season => season.Episodes).OrderBy(episode => episode.Season!.Number).ThenBy(episode => episode.Number).ToList();
        var watchSummary = global::AniT.Core.AnimeWatchSummary.Create(allEpisodes);
        var watched = watchSummary.CompletedEpisodes;
        var watching = watchSummary.InProgressEpisodes;
        Summary = watchSummary.IsCompleted
            ? $"Concluído · {watched} de {allEpisodes.Count} episódios assistidos"
            : watching > 0
                ? $"{watching} episódio{(watching == 1 ? string.Empty : "s")} em andamento"
                : watched == 0
                    ? "Ainda não iniciado"
                    : $"{watched} de {allEpisodes.Count} episódios assistidos";
        EpisodeCountLabel = $"{allEpisodes.Count} episódios";
        CanPlayNext = !watchSummary.IsCompleted;
        WatchStateLabel = watchSummary.IsCompleted
            ? "Concluído"
            : watching > 0
                ? "Em andamento"
                : "Na sua biblioteca";
        PlayNextLabel = watchSummary.IsCompleted
            ? "✓  Anime concluído"
            : watching > 0
                ? "▶  Continuar assistindo"
                : "▶  Assistir próximo episódio";
        Episodes.Clear();
        foreach (var episode in allEpisodes)
        {
            var progress = episode.PlaybackProgress;
            var percent = progress is { DurationSeconds: > 0 } ? progress.PositionSeconds / progress.DurationSeconds * 100 : 0;
            Episodes.Add(new EpisodeItem(
                episode.Id,
                episode.Number.ToString("00"),
                episode.Title ?? $"Episódio {episode.Number}",
                episode.Status,
                percent,
                episode.Rating,
                episode.ReviewNotes,
                episode.MediaFiles.Count,
                episode.MediaFiles.Count(file => file.Availability == global::AniT.Core.MediaFileAvailability.Available)));
        }
        var normalizedRatings = allEpisodes
            .Where(episode => episode.Rating is > 0)
            .Select(episode => NormalizeSavedRating(episode.Rating))
            .ToList();
        LocalAverageLabel = normalizedRatings.Count == 0 ? "—" : normalizedRatings.Average().ToString("0.0");
        RatedEpisodeCountLabel = normalizedRatings.Count == 0
            ? "Nenhum episódio avaliado"
            : $"{normalizedRatings.Count} episódio{(normalizedRatings.Count == 1 ? string.Empty : "s")} avaliado{(normalizedRatings.Count == 1 ? string.Empty : "s")}";
        LocalReviewSummary = allEpisodes
            .Where(episode => !string.IsNullOrWhiteSpace(episode.ReviewNotes))
            .OrderByDescending(episode => episode.WatchedAt)
            .Select(episode => episode.ReviewNotes!)
            .FirstOrDefault()
            ?? "Você ainda não escreveu comentários sobre esta obra.";
        DataContext = null;
        DataContext = this;

        var reviewEpisode = Episodes.FirstOrDefault(item => item.Id == preferredReviewEpisodeId)
            ?? Episodes.LastOrDefault(item => item.Status is global::AniT.Core.WatchStatus.Watching or global::AniT.Core.WatchStatus.Completed)
            ?? Episodes.FirstOrDefault();
        if (reviewEpisode is not null)
        {
            EpisodeReviewSelector.SelectedItem = reviewEpisode;
            LoadEpisodeReview(reviewEpisode);
        }

        if (CoverPath is null
            || string.IsNullOrWhiteSpace(anime.EnglishTitle)
            || string.IsNullOrWhiteSpace(anime.Synopsis)
            || anime.CriticScore is null)
        {
            try
            {
                var metadata = await App.EnsureAnimeMetadataAsync(
                    anime.Id,
                    anime.Title,
                    anime.EnglishTitle,
                    anime.CoverPath,
                    savedSynopsis: anime.Synopsis,
                    savedCriticScore: anime.CriticScore);
                CoverPath = metadata.CoverPath;
                EnglishTitleDisplay = FormatEnglishTitle(metadata.EnglishTitle);
                Synopsis = string.IsNullOrWhiteSpace(metadata.Synopsis)
                    ? Synopsis
                    : metadata.Synopsis;
                CriticScoreLabel = metadata.CriticScore is { } score ? $"{score:0}/100" : "—";
                CriticFiveLabel = metadata.CriticScore is { } updatedScore ? $"{updatedScore / 20d:0.0}" : "—";
                DataContext = null;
                DataContext = this;
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"AniT could not load the anime cover: {exception}");
            }
        }
    }

    private static string FormatEnglishTitle(string? title) => string.IsNullOrWhiteSpace(title) ? string.Empty : $"({title})";

    private async void RatingCard_Checked(object sender, RoutedEventArgs e)
    {
        if (RatingValueText is null || ReviewStatusText is null) return;
        if (sender is not RadioButton ratingCard
            || !double.TryParse(ratingCard.Tag?.ToString(), out var rating)) return;
        selectedRating = rating;
        RatingValueText.Text = selectedRating.ToString("0");
        if (!isLoadingReview) await SaveSelectedRatingAsync();
    }

    private async void ClearRating_Click(object sender, RoutedEventArgs e)
    {
        ApplyRatingSelection(0);
        if (!isLoadingReview) await SaveSelectedRatingAsync();
    }

    private void EpisodeReviewSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EpisodeReviewSelector.SelectedItem is EpisodeItem episode) LoadEpisodeReview(episode);
    }

    private void ReviewEpisode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid episodeId }) return;
        var episode = Episodes.FirstOrDefault(item => item.Id == episodeId);
        if (episode is null) return;

        EpisodeReviewSelector.SelectedItem = episode;
        LoadEpisodeReview(episode);
        AnimeTabControl.SelectedItem = ReviewTab;
    }

    private void LoadEpisodeReview(EpisodeItem episode)
    {
        isLoadingReview = true;
        try
        {
            selectedReviewEpisodeId = episode.Id;
            ApplyRatingSelection(NormalizeSavedRating(episode.Rating));
            ReviewTextBox.Text = episode.ReviewNotes ?? string.Empty;
            ReviewEpisodeTitleText.Text = $"Episódio {episode.Number} · {episode.Title}";
            ReviewEpisodeNumberText.Text = $"Episódio {episode.Number}";
            ReviewEpisodeProgress.Value = Math.Clamp(episode.ProgressPercent, 0, 100);
            ReviewEpisodeProgressText.Text = episode.Status == global::AniT.Core.WatchStatus.Completed
                ? "Concluído"
                : episode.ProgressPercent > 0
                    ? $"{episode.ProgressPercent:0}% assistido"
                    : "Não iniciado";
            ReviewStatusText.Text = string.Empty;
        }
        finally
        {
            isLoadingReview = false;
        }
    }

    private void ApplyRatingSelection(double rating)
    {
        selectedRating = rating;
        RatingOne.IsChecked = rating == 1;
        RatingTwo.IsChecked = rating == 2;
        RatingThree.IsChecked = rating == 3;
        RatingFour.IsChecked = rating == 4;
        RatingFive.IsChecked = rating == 5;
        RatingValueText.Text = rating <= 0 ? "—" : rating.ToString("0");
    }

    private static double NormalizeSavedRating(double? rating)
    {
        if (rating is null or <= 0) return 0;
        var fivePointRating = rating > 5 ? rating.Value / 2 : rating.Value;
        return Math.Clamp(Math.Round(fivePointRating, MidpointRounding.AwayFromZero), 1, 5);
    }

    private async Task SaveSelectedRatingAsync()
    {
        if (selectedReviewEpisodeId is not Guid episodeId)
        {
            ReviewStatusText.Text = "Escolha um episódio para avaliar.";
            return;
        }

        await using var context = App.OpenFreshDatabase();
        var episode = await context.Episodes.FirstOrDefaultAsync(item => item.Id == episodeId);
        if (episode is null) return;

        episode.Rating = selectedRating <= 0 ? null : selectedRating;
        await context.SaveChangesAsync();

        var item = Episodes.FirstOrDefault(candidate => candidate.Id == episodeId);
        if (item is not null) item.Rating = episode.Rating;
        ReviewStatusText.Text = episode.Rating is null ? "Nota removida" : "✓ Nota salva automaticamente";
    }

    private async void SaveComment_Click(object sender, RoutedEventArgs e)
    {
        if (selectedReviewEpisodeId is not Guid episodeId)
        {
            ReviewStatusText.Text = "Escolha um episódio para comentar.";
            return;
        }

        await using var context = App.OpenFreshDatabase();
        var episode = await context.Episodes.FirstOrDefaultAsync(item => item.Id == episodeId);
        if (episode is null) return;

        episode.ReviewNotes = string.IsNullOrWhiteSpace(ReviewTextBox.Text) ? null : ReviewTextBox.Text.Trim();
        await context.SaveChangesAsync();

        var item = Episodes.FirstOrDefault(candidate => candidate.Id == episodeId);
        if (item is not null) item.ReviewNotes = episode.ReviewNotes;
        ReviewStatusText.Text = episode.ReviewNotes is null ? "Comentário removido" : "✓ Comentário salvo localmente";
    }

    private async void ToggleWatched_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid episodeId }) return;
        await using var context = App.OpenFreshDatabase();
        var episode = await context.Episodes.FindAsync(episodeId);
        if (episode is null) return;
        if (episode.Status == global::AniT.Core.WatchStatus.Completed)
        {
            episode.Status = global::AniT.Core.WatchStatus.NotStarted;
            episode.WatchedAt = null;
        }
        else
        {
            episode.Status = global::AniT.Core.WatchStatus.Completed;
            episode.WatchedAt = DateTimeOffset.UtcNow;
        }
        await context.SaveChangesAsync();
        await LoadAsync();
    }

    private void EpisodeFiles_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid episodeId }) return;
        new MediaVersionsWindow(episodeId) { Owner = this }.ShowDialog();
    }

    private async void EpisodeRow_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindVisualAncestor<Button>(source) is not null) return;
        if (sender is not FrameworkElement { DataContext: EpisodeItem episode }) return;
        try
        {
            await App.PlayEpisodeAsync(episode.Id);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Não foi possível iniciar o player", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static T? FindVisualAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private async void PlayNext_Click(object sender, RoutedEventArgs e)
    {
        await using var context = App.OpenFreshDatabase();
        var nextEpisode = await context.Episodes
            .Where(episode => episode.Season!.AnimeId == animeId && episode.Status != global::AniT.Core.WatchStatus.Completed)
            .OrderBy(episode => episode.Season!.Number)
            .ThenBy(episode => episode.Number)
            .AsNoTracking()
            .FirstOrDefaultAsync();
        if (nextEpisode is null)
        {
            MessageBox.Show("Você já concluiu todos os episódios desta biblioteca.", "AniT", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            await App.PlayEpisodeAsync(nextEpisode.Id);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Não foi possível iniciar o player", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

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
            exploreWindow.Activate();
            return;
        }

        exploreWindow = new ExploreWindow { Owner = this };
        exploreWindow.Closed += (_, _) => exploreWindow = null;
        exploreWindow.Show();
    }

    private void OpenReviewTab_Click(object sender, RoutedEventArgs e) => AnimeTabControl.SelectedItem = ReviewTab;

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchHint is not null) SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) Library_Click(sender, e);
    }

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
        if (SidebarColumn is null || DetailsContentHost is null || RightRailColumn is null) return;
        if (width < 1180)
        {
            SidebarColumn.Width = new GridLength(184);
            RightRailColumn.Width = new GridLength(0);
            DetailsRightRail.Visibility = Visibility.Collapsed;
            DetailsContentHost.Margin = new Thickness(18, 14, 18, 36);
            SearchContainer.MaxWidth = 350;
            LibraryTopButton.Visibility = Visibility.Collapsed;
            CollectionsTopButton.Visibility = Visibility.Collapsed;
            HeroBanner.Height = 286;
            HeroTitleText.FontSize = 34;
            HeroArtwork.Width = 300;
        }
        else if (width < 1600)
        {
            SidebarColumn.Width = new GridLength(220);
            RightRailColumn.Width = new GridLength(320);
            DetailsRightRail.Visibility = Visibility.Visible;
            DetailsContentHost.Margin = new Thickness(24, 16, 24, 44);
            SearchContainer.MaxWidth = 500;
            LibraryTopButton.Visibility = Visibility.Visible;
            CollectionsTopButton.Visibility = Visibility.Collapsed;
            HeroBanner.Height = 322;
            HeroTitleText.FontSize = 42;
            HeroArtwork.Width = 410;
        }
        else if (width < 2300)
        {
            SidebarColumn.Width = new GridLength(232);
            RightRailColumn.Width = new GridLength(350);
            DetailsRightRail.Visibility = Visibility.Visible;
            DetailsContentHost.Margin = new Thickness(30, 20, 30, 50);
            SearchContainer.MaxWidth = 580;
            LibraryTopButton.Visibility = Visibility.Visible;
            CollectionsTopButton.Visibility = Visibility.Visible;
            HeroBanner.Height = 350;
            HeroTitleText.FontSize = 46;
            HeroArtwork.Width = 460;
        }
        else
        {
            SidebarColumn.Width = new GridLength(250);
            RightRailColumn.Width = new GridLength(390);
            DetailsRightRail.Visibility = Visibility.Visible;
            DetailsContentHost.Margin = new Thickness(42, 24, 42, 58);
            SearchContainer.MaxWidth = 660;
            LibraryTopButton.Visibility = Visibility.Visible;
            CollectionsTopButton.Visibility = Visibility.Visible;
            HeroBanner.Height = 380;
            HeroTitleText.FontSize = 50;
            HeroArtwork.Width = 520;
        }
    }
}

public sealed class EpisodeItem(
    Guid id,
    string number,
    string title,
    global::AniT.Core.WatchStatus status,
    double progressPercent,
    double? rating,
    string? reviewNotes,
    int versionCount,
    int availableVersionCount)
{
    public Guid Id { get; } = id;
    public string Number { get; } = number;
    public string Title { get; } = title;
    public global::AniT.Core.WatchStatus Status { get; } = status;
    public double ProgressPercent { get; } = progressPercent;
    public double? Rating { get; set; } = rating;
    public string? ReviewNotes { get; set; } = reviewNotes;
    public int VersionCount { get; } = versionCount;
    public int AvailableVersionCount { get; } = availableVersionCount;
    public string VersionSummary => VersionCount == 0
        ? "Sem arquivo disponível"
        : VersionCount == 1 ? (AvailableVersionCount == 1 ? "1 versão local" : "1 versão indisponível")
        : $"{AvailableVersionCount} de {VersionCount} versões disponíveis";
    public string ReviewDisplay => $"Episódio {Number} · {Title}";

    public string StatusLabel => Status switch
    {
        global::AniT.Core.WatchStatus.Completed => "✓ Assistido",
        global::AniT.Core.WatchStatus.Watching => "▶ Continuar",
        _ => "▶ Assistir"
    };

    public string StatusColor => Status switch
    {
        global::AniT.Core.WatchStatus.Completed => "#6CDEB2",
        global::AniT.Core.WatchStatus.Watching => "#53B6FF",
        _ => "#91ABD0"
    };

    public string ProgressLabel => Status == global::AniT.Core.WatchStatus.Completed ? "Concluído" : ProgressPercent > 0 ? $"{ProgressPercent:0}% assistido" : "Não iniciado";
    public string WatchedActionLabel => Status == global::AniT.Core.WatchStatus.Completed ? "Remover assistido" : "Marcar visto";
    public string RatingLabel
    {
        get
        {
            if (Rating is not > 0) return "Sem nota";
            var normalized = Rating > 5 ? Rating.Value / 2 : Rating.Value;
            return $"★ {normalized:0.0}";
        }
    }
}

using Microsoft.EntityFrameworkCore;
using AniT.Infrastructure;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AniT.App;

public partial class AnimeDetailsWindow : Window
{
    private readonly Guid animeId;
    private readonly Guid? requestedEpisodeId;
    private bool requestedEpisodeFocused;
    private bool isLoadingPage;
    private bool loadFailureShown;
    private CancellationTokenSource? artworkGalleryCancellation;
    private int projectorArtworkIndex;
    public ObservableCollection<EpisodeItem> Episodes { get; } = [];
    public ObservableCollection<AnimeArtworkItem> ArtworkGallery { get; } = [];
    public string AnimeTitle { get; private set; } = string.Empty;
    public string EnglishTitleDisplay { get; private set; } = string.Empty;
    public string Initial { get; private set; } = "?";
    public string Summary { get; private set; } = string.Empty;
    public string EpisodeCountLabel { get; private set; } = string.Empty;
    public string PlayNextLabel { get; private set; } = "Assistir próximo episódio";
    public bool CanPlayNext { get; private set; } = true;
    public string? CoverPath { get; private set; }
    public string? SynopsisArtworkPath { get; private set; }
    public string Synopsis { get; private set; } = "A sinopse ainda não está disponível.";
    public string CriticScoreLabel { get; private set; } = "—";
    public string CriticFiveLabel { get; private set; } = "—";
    public string WatchStateLabel { get; private set; } = "Na sua biblioteca";
    public bool IsFavorite { get; private set; }
    public string FavoriteLabel => IsFavorite ? "Favoritado" : "Favoritar";
    public string FavoriteFill => IsFavorite ? "#FF67A9" : "Transparent";
    public string FavoriteStroke => IsFavorite ? "#FFD0E4" : "#D2F0FF";
    public string FavoriteToolTip => IsFavorite ? "Remover este anime dos favoritos" : "Adicionar este anime aos favoritos";
    public string LocalAverageLabel { get; private set; } = "—";
    public string RatedEpisodeCountLabel { get; private set; } = "Nenhum episódio avaliado";
    public string LocalReviewSummary { get; private set; } = "Você ainda não escreveu comentários sobre esta obra.";

    public AnimeDetailsWindow(Guid animeId, Guid? requestedEpisodeId = null)
    {
        this.animeId = animeId;
        this.requestedEpisodeId = requestedEpisodeId;
        InitializeComponent();
        GlobalSearchController.Attach(this, SearchBox, SearchHint, SearchContainer);
        ResponsiveWindow.FitToWorkArea(this, 1140, 800);
        DataContext = this;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateResponsiveLayout(ActualWidth);
        await LoadSafelyAsync();
    }
    private async void Window_Activated(object? sender, EventArgs e) => await LoadSafelyAsync();

    private async Task LoadSafelyAsync()
    {
        if (isLoadingPage) return;

        isLoadingPage = true;
        try
        {
            await LoadAsync();
            loadFailureShown = false;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Falha ao carregar a pagina do anime: {exception}");
            if (!loadFailureShown)
            {
                loadFailureShown = true;
                MessageBox.Show(
                    "Nao foi possivel carregar a pagina deste anime. Tente novamente.",
                    "AniT",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            isLoadingPage = false;
        }
    }

    private async Task LoadAsync()
    {
        await using var context = App.OpenFreshDatabase();
        var anime = await context.Anime
            .Include(item => item.Seasons)
            .ThenInclude(season => season.Episodes)
            .ThenInclude(episode => episode.PlaybackProgress)
            .Include(item => item.Seasons)
            .ThenInclude(season => season.Episodes)
            .ThenInclude(episode => episode.MediaFiles)
            .Include(item => item.Aliases)
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == animeId);
        if (anime is null) return;

        var organizationSettings = global::AniT.Infrastructure.AniTSystemSettingsStore.Load();
        var preferredTitle = global::AniT.Infrastructure.OrganizationPreferences.PreferredTitle(anime, organizationSettings.AnimeTitlePreference);
        AnimeTitle = preferredTitle;
        IsFavorite = anime.IsFavorite;
        EnglishTitleDisplay = FormatEnglishTitle(anime.EnglishTitle);
        Initial = preferredTitle[..1].ToUpperInvariant();
        var hasUsableCover = !string.IsNullOrWhiteSpace(anime.CoverPath) && File.Exists(anime.CoverPath);
        CoverPath = hasUsableCover ? anime.CoverPath : App.UpdatingArtworkPath;
        SynopsisArtworkPath = App.GetLockedArtworkPath(anime.Id) ?? App.GetCachedAnimeBannerPath(anime.Id) ?? CoverPath;
        ReplaceArtworkGallery([SynopsisArtworkPath, CoverPath]);
        Synopsis = global::AniT.Infrastructure.AnimeCoverProvider.IsLikelyPortugueseSynopsis(anime.Synopsis)
            ? anime.Synopsis!
            : "Buscando a sinopse em português…";
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
            ? "Anime concluído"
            : watching > 0
                ? "Continuar assistindo"
                : "Assistir próximo episódio";
        Episodes.Clear();
        foreach (var episode in allEpisodes)
        {
            var progress = episode.PlaybackProgress;
            var percent = progress is { DurationSeconds: > 0 } ? progress.PositionSeconds / progress.DurationSeconds * 100 : 0;
            Episodes.Add(new EpisodeItem(
                episode.Id,
                global::AniT.Infrastructure.OrganizationPreferences.EpisodeCode(episode.Season?.Number ?? 1, episode.Number, organizationSettings.EpisodeNumberDisplayFormat),
                global::AniT.Core.EpisodeDisplayName.Format(preferredTitle, episode.Season?.Number ?? 1, episode.Number, organizationSettings.EpisodeNumberDisplayFormat.ToString()),
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
        FocusRequestedEpisode();

        if (!hasUsableCover
            || string.IsNullOrWhiteSpace(anime.EnglishTitle)
            || !global::AniT.Infrastructure.AnimeCoverProvider.IsLikelyPortugueseSynopsis(anime.Synopsis)
            || anime.CriticScore is null
            || string.IsNullOrWhiteSpace(anime.Genres)
            || App.ShouldRefreshAnimeBanner(anime.Id))
        {
            try
            {
                var metadata = await App.EnsureAnimeMetadataAsync(
                    anime.Id,
                    anime.Title,
                    anime.EnglishTitle,
                    anime.CoverPath,
                    savedSynopsis: anime.Synopsis,
                    savedCriticScore: anime.CriticScore,
                    savedGenres: anime.Genres);
                CoverPath = metadata.CoverPath ?? App.UpdatingArtworkPath;
                SynopsisArtworkPath = App.GetLockedArtworkPath(anime.Id) ?? metadata.BannerPath ?? CoverPath;
                ReplaceArtworkGallery([SynopsisArtworkPath, CoverPath]);
                EnglishTitleDisplay = FormatEnglishTitle(metadata.EnglishTitle);
                Synopsis = global::AniT.Infrastructure.AnimeCoverProvider.IsLikelyPortugueseSynopsis(metadata.Synopsis)
                    ? metadata.Synopsis!
                    : "A sinopse em português ainda não está disponível. Tente atualizar os títulos da biblioteca novamente.";
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

        StartArtworkGalleryLoad(anime.Title);
    }

    private static string FormatEnglishTitle(string? title) => string.IsNullOrWhiteSpace(title) ? string.Empty : $"({title})";

    private void StartArtworkGalleryLoad(string title)
    {
        artworkGalleryCancellation?.Cancel();
        artworkGalleryCancellation = new CancellationTokenSource();
        _ = LoadArtworkGalleryAsync(title, artworkGalleryCancellation.Token);
    }

    private async Task LoadArtworkGalleryAsync(string title, CancellationToken cancellationToken)
    {
        try
        {
            var paths = await App.EnsureAnimeArtworkGalleryAsync(
                animeId,
                title,
                CoverPath,
                SynopsisArtworkPath,
                cancellationToken);
            if (cancellationToken.IsCancellationRequested || !IsLoaded) return;
            ReplaceArtworkGallery(paths);
        }
        catch (OperationCanceledException)
        {
            // Closing or reloading the page cancels optional background artwork discovery.
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"AniT could not load the artwork gallery: {exception}");
        }
    }

    private void ReplaceArtworkGallery(IEnumerable<string?> paths)
    {
        var unique = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();
        ArtworkGallery.Clear();
        for (var index = 0; index < unique.Length; index++)
            ArtworkGallery.Add(new AnimeArtworkItem(index, unique[index], $"Arte {index + 1}"));

        if (ArtworkCountText is not null)
        {
            ArtworkCountText.Text = unique.Length switch
            {
                0 => "Arte indisponível",
                1 => "1 arte · clique para ampliar",
                _ => $"{unique.Length} artes · clique para ampliar"
            };
        }
    }

    private void OpenArtworkProjector_Click(object sender, RoutedEventArgs e)
    {
        if (ArtworkGallery.Count == 0) return;
        ShowArtwork(0);
        ArtworkProjectorOverlay.Visibility = Visibility.Visible;
        Keyboard.Focus(ArtworkProjectorOverlay);
    }

    private void ShowArtwork(int index)
    {
        if (ArtworkGallery.Count == 0) return;
        projectorArtworkIndex = (index % ArtworkGallery.Count + ArtworkGallery.Count) % ArtworkGallery.Count;
        var selectedPath = ArtworkGallery[projectorArtworkIndex].Path;
        ProjectorImage.Source = LoadImageSource(selectedPath);
        ApplyProjectorFraming();
        ProjectorCounterText.Text = $"{projectorArtworkIndex + 1} de {ArtworkGallery.Count}";
        var isLocked = string.Equals(App.GetLockedArtworkPath(animeId), selectedPath, StringComparison.OrdinalIgnoreCase);
        ArtworkLockLabel.Text = isLocked ? "Desbloquear esta arte" : "Bloquear esta arte";
    }

    private void ApplyProjectorFraming()
    {
        var settings = AniTSystemSettingsStore.Load();
        ProjectorImage.Stretch = settings.ArtworkStretchMode == ArtworkStretchMode.Uniform ? Stretch.Uniform : Stretch.UniformToFill;
        ProjectorImage.RenderTransform = Transform.Identity;
        ProjectorImage.RenderTransformOrigin = new Point(0.5, 0.5);
        if (settings.ArtworkStretchMode != ArtworkStretchMode.Manual) return;
        ProjectorImage.RenderTransformOrigin = new Point(settings.ArtworkFocusXPercent / 100d, settings.ArtworkFocusYPercent / 100d);
        ProjectorImage.RenderTransform = new ScaleTransform(1.08, 1.08);
    }

    private void ToggleArtworkLock_Click(object sender, RoutedEventArgs e)
    {
        if (ArtworkGallery.Count == 0) return;
        var selectedPath = ArtworkGallery[projectorArtworkIndex].Path;
        var isLocked = string.Equals(App.GetLockedArtworkPath(animeId), selectedPath, StringComparison.OrdinalIgnoreCase);
        App.SetArtworkLock(animeId, isLocked ? null : selectedPath);
        SynopsisArtworkPath = isLocked ? App.GetCachedAnimeBannerPath(animeId) ?? CoverPath : selectedPath;
        ArtworkLockLabel.Text = isLocked ? "Bloquear esta arte" : "Desbloquear esta arte";
        DataContext = null;
        DataContext = this;
    }

    private static ImageSource? LoadImageSource(string path)
    {
        try
        {
            var uri = Path.IsPathRooted(path)
                ? new Uri(path, UriKind.Absolute)
                : new Uri($"pack://application:,,,/{path.Replace('\\', '/')}", UriKind.Absolute);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = uri;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private void PreviousArtwork_Click(object sender, RoutedEventArgs e) => ShowArtwork(projectorArtworkIndex - 1);
    private void NextArtwork_Click(object sender, RoutedEventArgs e) => ShowArtwork(projectorArtworkIndex + 1);
    private void ArtworkThumbnail_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int index }) ShowArtwork(index);
    }
    private void CloseArtworkProjector_Click(object sender, RoutedEventArgs e) => CloseArtworkProjector();
    private void ArtworkProjectorBackdrop_Click(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, ArtworkProjectorOverlay)) CloseArtworkProjector();
    }
    private void CloseArtworkProjector() => ArtworkProjectorOverlay.Visibility = Visibility.Collapsed;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ArtworkProjectorOverlay.Visibility != Visibility.Visible) return;
        if (e.Key == Key.Escape) CloseArtworkProjector();
        else if (e.Key == Key.Left) ShowArtwork(projectorArtworkIndex - 1);
        else if (e.Key == Key.Right) ShowArtwork(projectorArtworkIndex + 1);
        else return;
        e.Handled = true;
    }

    private void Window_Closed(object? sender, EventArgs e) => artworkGalleryCancellation?.Cancel();

    private void FocusRequestedEpisode()
    {
        if (requestedEpisodeFocused || requestedEpisodeId is not Guid episodeId) return;
        requestedEpisodeFocused = true;
        AnimeTabControl.SelectedItem = EpisodesTab;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            EpisodeList.UpdateLayout();
            var episode = Episodes.FirstOrDefault(item => item.Id == episodeId);
            if (episode is not null && EpisodeList.ItemContainerGenerator.ContainerFromItem(episode) is FrameworkElement container)
                container.BringIntoView();
        });
    }

    private static double NormalizeSavedRating(double? rating)
    {
        if (rating is null or <= 0) return 0;
        var fivePointRating = rating > 5 ? rating.Value / 2 : rating.Value;
        return Math.Clamp(Math.Round(fivePointRating, MidpointRounding.AwayFromZero), 1, 5);
    }

    private async void EpisodeRating_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Primitives.ToggleButton { DataContext: EpisodeItem episodeItem } ratingButton
            || !double.TryParse(ratingButton.Tag?.ToString(), out var rating)) return;

        await using var context = App.OpenFreshDatabase();
        var episode = await context.Episodes.FirstOrDefaultAsync(item => item.Id == episodeItem.Id);
        if (episode is null) return;

        var requestedRating = Math.Clamp(rating, 1, 5);
        var previousRating = NormalizeSavedRating(episode.Rating);
        episode.Rating = previousRating == requestedRating
            ? null
            : requestedRating;
        await context.SaveChangesAsync();
        await App.Achievements.RecordAsync(new global::AniT.Core.Achievements.AchievementEvent(
            global::AniT.Core.Achievements.AchievementEventType.RatingChanged,
            episode.Id,
            (long)(episode.Rating ?? 0),
            (long)previousRating));
        await LoadSafelyAsync();
    }

    private async void ToggleWatched_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid episodeId }) return;
        await using var context = App.OpenFreshDatabase();
        var episode = await context.Episodes.FindAsync(episodeId);
        if (episode is null) return;
        var markedCompleted = episode.Status != global::AniT.Core.WatchStatus.Completed;
        if (!markedCompleted)
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
        if (markedCompleted)
        {
            await App.Achievements.RecordAsync(new global::AniT.Core.Achievements.AchievementEvent(
                global::AniT.Core.Achievements.AchievementEventType.EpisodeCompleted,
                episode.Id));
        }
        else
        {
            await App.Achievements.RecalculateAsync();
        }
        await LoadSafelyAsync();
    }

    private async void EpisodePlay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: EpisodeItem episode }) return;
        e.Handled = true;
        try
        {
            await App.PlayEpisodeAsync(episode.Id);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Não foi possível iniciar o player", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void EpisodeNote_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: EpisodeItem episode }) return;
        e.Handled = true;
        var editor = new EpisodeCommentWindow(animeId, episode.Id) { Owner = this };
        if (editor.ShowDialog() == true) await LoadSafelyAsync();
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

    private async void Favorite_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button is not null) button.IsEnabled = false;
        try
        {
            var updated = await App.ToggleAnimeFavoriteAsync(animeId);
            if (updated is not bool isFavorite) return;
            IsFavorite = isFavorite;
            DataContext = null;
            DataContext = this;
        }
        finally
        {
            if (button is not null) button.IsEnabled = true;
        }
    }

    private async void OrganizeAnime_Click(object sender, RoutedEventArgs e)
    {
        var window = new AnimeOrganizationWindow(animeId) { Owner = this };
        if (window.ShowDialog() is true) await LoadSafelyAsync();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => AppNavigation.Back(this);

    private void Library_Click(object sender, RoutedEventArgs e) => AppNavigation.OpenLibrary(this);
    private void Explore_Click(object sender, RoutedEventArgs e) => AppNavigation.Explore(this);
    private void Calendar_Click(object sender, RoutedEventArgs e) => AppNavigation.Calendar(this);
    private void History_Click(object sender, RoutedEventArgs e) => AppNavigation.History(this);
    private void Profile_Click(object sender, RoutedEventArgs e) => AppNavigation.Profile(this);

    private void OpenEpisodeRatings_Click(object sender, RoutedEventArgs e) => AnimeTabControl.SelectedItem = EpisodesTab;

    private async void EditComment_Click(object sender, RoutedEventArgs e)
    {
        var editor = new EpisodeCommentWindow(animeId) { Owner = this };
        if (editor.ShowDialog() == true) await LoadSafelyAsync();
    }

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
        var episodeTemplate = (DataTemplate)FindResource(width >= 1800 ? "EpisodeCardLargeTemplate" : "EpisodeCardTemplate");
        SynopsisEpisodeList.ItemTemplate = episodeTemplate;
        EpisodeList.ItemTemplate = episodeTemplate;
        if (width < 1180)
        {
            SidebarColumn.Width = new GridLength(184);
            RightRailColumn.Width = new GridLength(0);
            DetailsRightRail.Visibility = Visibility.Collapsed;
            DetailsContentHost.Margin = new Thickness(18, 14, 18, 36);
            SearchContainer.MaxWidth = 350;
            LibraryTopButton.Visibility = Visibility.Collapsed;
            HeroBanner.Height = 286;
            HeroTitleText.FontSize = 34;
            HeroCopy.MaxWidth = 560;
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
            HeroBanner.Height = 322;
            HeroTitleText.FontSize = 42;
            HeroCopy.MaxWidth = 720;
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
            HeroBanner.Height = 350;
            HeroTitleText.FontSize = 46;
            HeroCopy.MaxWidth = 860;
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
            HeroBanner.Height = 380;
            HeroTitleText.FontSize = 50;
            HeroCopy.MaxWidth = 1100;
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
        global::AniT.Core.WatchStatus.Completed => "Assistido",
        global::AniT.Core.WatchStatus.Watching => "Continuar",
        _ => "Assistir"
    };

    public string StatusColor => Status switch
    {
        global::AniT.Core.WatchStatus.Completed => "#6CDEB2",
        global::AniT.Core.WatchStatus.Watching => "#53B6FF",
        _ => "#91ABD0"
    };

    public string ProgressLabel => Status == global::AniT.Core.WatchStatus.Completed ? "Concluído" : ProgressPercent > 0 ? $"{ProgressPercent:0}% assistido" : "Não iniciado";
    public string WatchedActionLabel => Status == global::AniT.Core.WatchStatus.Completed ? "Remover assistido" : "Marcar visto";
    public bool HasEpisodeNote => !string.IsNullOrWhiteSpace(ReviewNotes);
    public string EpisodeNoteActionLabel => HasEpisodeNote ? "Editar nota" : "Nota do episódio";
    public string EpisodeNoteToolTip => HasEpisodeNote ? ReviewNotes! : "Adicionar uma nota opcional somente para este episódio";
    public double NormalizedRating => Rating is > 5 ? Rating.Value / 2 : Rating ?? 0;
    public bool IsRatingOne => Math.Round(NormalizedRating, MidpointRounding.AwayFromZero) == 1;
    public bool IsRatingTwo => Math.Round(NormalizedRating, MidpointRounding.AwayFromZero) == 2;
    public bool IsRatingThree => Math.Round(NormalizedRating, MidpointRounding.AwayFromZero) == 3;
    public bool IsRatingFour => Math.Round(NormalizedRating, MidpointRounding.AwayFromZero) == 4;
    public bool IsRatingFive => Math.Round(NormalizedRating, MidpointRounding.AwayFromZero) == 5;
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

public sealed record AnimeArtworkItem(int Index, string Path, string Label);

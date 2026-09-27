using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace AniT.App;

public partial class LibraryWindow : Window, INotifyPropertyChanged
{
    public ObservableCollection<AnimeLibraryItem> Anime { get; } = [];
    public ObservableCollection<LibraryRootItem> LibraryRoots { get; } = [];
    public ObservableCollection<ReviewQueueItem> ReviewItems { get; } = [];
    public ObservableCollection<FileStateItem> DuplicateItems { get; } = [];
    public ObservableCollection<FileStateItem> UnavailableItems { get; } = [];
    public ObservableCollection<LibraryFilterOption> GenreFilterOptions { get; } = [new("", "Todos os gêneros")];
    public ObservableCollection<LibraryFilterOption> YearFilterOptions { get; } = [new("", "Todos os anos")];
    public ObservableCollection<LibraryFilterOption> StatusFilterOptions { get; } =
    [
        new("", "Todos os status"),
        new("watching", "Em andamento"),
        new("completed", "Concluídos"),
        new("not-started", "Ainda não iniciados")
    ];
    public ObservableCollection<LibraryFilterOption> StudioFilterOptions { get; } = [new("", "Todos os estúdios")];
    public ObservableCollection<LibraryFilterOption> TagFilterOptions { get; } = [new("", "Todas as tags")];
    public ObservableCollection<LibraryFilterOption> CollectionFilterOptions { get; } = [new("", "Todas as coleções")];
    public ObservableCollection<LibraryFilterOption> OrderFilterOptions { get; } =
    [
        new("recent", "Adicionados recentemente"),
        new("title", "Título · A–Z"),
        new("year-desc", "Ano · mais novos"),
        new("year-asc", "Ano · mais antigos"),
        new("score", "Melhor avaliados"),
        new("progress", "Mais avançados"),
        new("favorites", "Favoritos primeiro")
    ];
    public int ReviewCount => ReviewItems.Count;
    public double LibraryCardWidth { get; private set; } = 220;
    public event PropertyChangedEventHandler? PropertyChanged;
    private readonly List<AnimeLibraryItem> allAnime = [];
    private CancellationTokenSource? coverLoadingCancellation;
    private CancellationTokenSource? libraryRefreshCancellation;
    private bool isRefreshing;
    private bool isPopulatingFilters;
    private bool areFiltersExpanded = true;
    private readonly SemaphoreSlim loadGate = new(1, 1);
    private string libraryEmptyTitle = "Nenhuma pasta adicionada ainda";
    private string libraryEmptyDetail = "Adicione uma pasta. O AniT encontra os vídeos sem exigir que você renomeie ou reorganize nada.";

    public LibraryWindow()
    {
        InitializeComponent();
        GlobalSearchController.Attach(this, LibrarySearchBox, SearchPlaceholder, SearchBanner);
        ResponsiveWindow.FitToWorkArea(this, 1180, 760);
        DataContext = this;
        var organizationSettings = global::AniT.Infrastructure.AniTSystemSettingsStore.Load();
        OrderFilter.SelectedValue = global::AniT.Infrastructure.OrganizationPreferences.LibrarySortKey(organizationSettings.LibraryDefaultSort);
        UpdateCardLayout(Width);
    }

    public void ShowReviewQueue()
    {
        if (LibraryTabs is not null) LibraryTabs.SelectedIndex = 2;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await LoadSafelyAsync();
    private async void Window_Activated(object? sender, EventArgs e)
    {
        if (!isRefreshing) await LoadSafelyAsync();
    }

    private async Task LoadSafelyAsync()
    {
        if (!await loadGate.WaitAsync(0)) return;
        try
        {
            await LoadAsync();
        }
        catch (OperationCanceledException)
        {
            // A previous metadata load is routinely cancelled when the window regains focus.
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"AniT Library failed to load: {exception}");
            try
            {
                var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AniT", "Data");
                Directory.CreateDirectory(logDirectory);
                await File.AppendAllTextAsync(Path.Combine(logDirectory, "library.log"), $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
            }
            catch
            {
                // A diagnostic write must not hide the recoverable UI state.
            }
            EmptyState.Visibility = Visibility.Visible;
            EmptyTitleText.Text = "Não foi possível abrir a Biblioteca";
            EmptyDetailText.Text = "Seus dados continuam seguros. Feche e abra novamente; os detalhes técnicos foram salvos em library.log.";
        }
        finally
        {
            loadGate.Release();
        }
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var shouldHideSearch = ActualWidth < 920;
        SearchBanner.Visibility = shouldHideSearch ? Visibility.Collapsed : Visibility.Visible;
        if (shouldHideSearch && LibrarySearchBox is not null && LibrarySearchBox.Text.Length > 0)
            LibrarySearchBox.Clear();
        UpdateCardLayout(e.NewSize.Width);
    }

    private void UpdateCardLayout(double windowWidth)
    {
        var settings = global::AniT.Infrastructure.AniTSystemSettingsStore.Load();
        var preferred = settings.CardSize switch
        {
            global::AniT.Infrastructure.AppearanceCardSize.Compact => 184d,
            global::AniT.Infrastructure.AppearanceCardSize.Large => 260d,
            _ => 220d
        };
        if (settings.CardsPerRow > 0)
        {
            var usable = Math.Max(500, windowWidth - 105);
            preferred = Math.Clamp((usable - 19 * (settings.CardsPerRow - 1)) / settings.CardsPerRow, 160, 310);
        }
        if (Math.Abs(LibraryCardWidth - preferred) < 0.5) return;
        LibraryCardWidth = preferred;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LibraryCardWidth)));
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        coverLoadingCancellation?.Cancel();
        libraryRefreshCancellation?.Cancel();
        foreach (var item in allAnime) item.PropertyChanged -= AnimeItem_PropertyChanged;
    }

    private async Task LoadAsync()
    {
        coverLoadingCancellation?.Cancel();
        coverLoadingCancellation = new CancellationTokenSource();
        await using var context = App.OpenFreshDatabase();
        var anime = await context.Anime
            .AsNoTracking()
            .Include(item => item.Seasons)
            .ThenInclude(season => season.Episodes)
            .ThenInclude(episode => episode.PlaybackProgress)
            .Include(item => item.Seasons)
            .ThenInclude(season => season.Episodes)
            .ThenInclude(episode => episode.MediaFiles)
            .Include(item => item.Aliases)
            .OrderBy(item => item.Title)
            .ToListAsync();

        var organizationSettings = global::AniT.Infrastructure.AniTSystemSettingsStore.Load();
        var animeNames = anime.ToDictionary(item => item.Id, item => global::AniT.Infrastructure.OrganizationPreferences.PreferredTitle(item, organizationSettings.AnimeTitlePreference));
        var roots = await context.LibraryRoots.AsNoTracking().Where(root => root.IsEnabled).OrderBy(root => root.DisplayName).ToListAsync();
        var reviewItems = await context.LibraryReviewItems.AsNoTracking()
            .Where(item => item.Status == global::AniT.Core.LibraryReviewStatus.Pending)
            .OrderByDescending(item => item.Confidence)
            .ThenBy(item => item.FileName)
            .ToListAsync();
        var physicalFiles = await context.MediaFiles.AsNoTracking()
            .Include(file => file.LibraryRoot)
            .Include(file => file.Episode)!.ThenInclude(episode => episode!.Season)!.ThenInclude(season => season!.Anime)
            .ToListAsync();

        foreach (var existingItem in allAnime) existingItem.PropertyChanged -= AnimeItem_PropertyChanged;
        allAnime.Clear();
        Anime.Clear();
        var missingMetadata = new List<(AnimeLibraryItem Item, string? SavedCoverPath, string? SavedSynopsis, double? SavedCriticScore, string? SavedGenres, int? SavedReleaseYear, string? SavedStudio)>();
        var now = DateTimeOffset.UtcNow;
        foreach (var item in anime)
        {
            var preferredTitle = global::AniT.Infrastructure.OrganizationPreferences.PreferredTitle(item, organizationSettings.AnimeTitlePreference);
            var hasUsableCover = !string.IsNullOrWhiteSpace(item.CoverPath) && File.Exists(item.CoverPath);
            var episodes = item.Seasons.SelectMany(season => season.Episodes).ToList();
            var watchSummary = global::AniT.Core.AnimeWatchSummary.Create(episodes);
            var watched = watchSummary.CompletedEpisodes;
            var current = episodes
                .Where(global::AniT.Core.AnimeWatchSummary.IsInProgress)
                .OrderByDescending(episode => episode.PlaybackProgress?.LastPlayedAt)
                .FirstOrDefault();
            var partialPercent = current?.PlaybackProgress is { DurationSeconds: > 0 } progress
                ? Math.Clamp(progress.PositionSeconds / progress.DurationSeconds * 100d, 0, 100)
                : 0;
            var totalPercent = episodes.Count == 0 ? 0 : (watched * 100d + partialPercent) / episodes.Count;
            var catalogStatus = watchSummary.IsCompleted
                ? LibraryCatalogStatus.Completed
                : current is not null || watched > 0
                    ? LibraryCatalogStatus.Watching
                    : LibraryCatalogStatus.NotStarted;
            var viewItem = new AnimeLibraryItem(
                item.Id,
                preferredTitle,
                item.EnglishTitle,
                string.IsNullOrWhiteSpace(preferredTitle) ? "?" : preferredTitle[..1].ToUpperInvariant(),
                $"{episodes.Count} episódio(s)",
                watchSummary.IsCompleted
                    ? "Concluído"
                    : current is not null
                        ? $"Em andamento · {(global::AniT.Infrastructure.OrganizationPreferences.EpisodeCode(current.Season?.Number ?? 1, current.Number, organizationSettings.EpisodeNumberDisplayFormat))}"
                        : watched == 0
                            ? "Ainda não iniciado"
                            : $"{watched} assistido(s)",
                totalPercent,
                hasUsableCover ? item.CoverPath : App.UpdatingArtworkPath,
                global::AniT.Core.LibraryFreshness.IsNew(item, now),
                item.IsFavorite,
                item.Genres,
                item.ReleaseYear,
                item.Studio,
                item.CustomTags,
                item.UserCollections,
                item.CriticScore,
                catalogStatus,
                item.CreatedAt,
                global::AniT.Core.LibraryFreshness.GetLatestImportAt(item));
            viewItem.PropertyChanged += AnimeItem_PropertyChanged;
            allAnime.Add(viewItem);
            if (!hasUsableCover
                || string.IsNullOrWhiteSpace(viewItem.EnglishTitle)
                || viewItem.ReleaseYear is null
                || string.IsNullOrWhiteSpace(viewItem.Studio))
            {
                missingMetadata.Add((viewItem, item.CoverPath, item.Synopsis, item.CriticScore, item.Genres, item.ReleaseYear, item.Studio));
            }
        }

        libraryEmptyTitle = roots.Count == 0
            ? "Configure a pasta da sua estante"
            : reviewItems.Count > 0 ? "Há arquivos aguardando revisão" : "Nenhum título identificado";
        libraryEmptyDetail = roots.Count == 0
            ? "Escolha um único diretório principal. O AniT encontra os vídeos sem exigir que você renomeie ou reorganize nada."
            : reviewItems.Count > 0
                ? "Abra a aba Revisão para confirmar os arquivos ambíguos com segurança."
                : "Atualize a Biblioteca depois de adicionar vídeos às pastas monitoradas.";
        RebuildDynamicFilterOptions();
        ApplySearchFilter();
        LibraryRoots.Clear();
        foreach (var root in roots)
        {
            var fileCount = physicalFiles.Count(file => file.LibraryRootId == root.Id);
            LibraryRoots.Add(new LibraryRootItem(
                root.Id,
                root.DisplayName,
                root.Path,
                root.IncludeSubfolders,
                root.IsEnabled,
                root.LastUnavailableAt is not null
                    ? "Pasta indisponível · seus dados foram preservados"
                    : root.LastScanAt is null ? "Ainda não escaneada" : $"{fileCount} arquivo(s) · verificada {root.LastScanAt:dd/MM HH:mm}"));
        }

        ReviewItems.Clear();
        foreach (var item in reviewItems)
        {
            var proposed = item.SuggestedAnimeId is Guid animeId && animeNames.TryGetValue(animeId, out var name)
                ? name
                : item.CandidateTitle ?? "Anime não identificado";
            ReviewItems.Add(new ReviewQueueItem(
                item.Id,
                item.FileName,
                proposed,
                item.SuggestedSeasonNumber,
                item.SuggestedEpisodeNumber,
                item.Confidence,
                item.Reasons));
        }

        DuplicateItems.Clear();
        foreach (var file in physicalFiles.Where(file => file.DuplicateOfMediaFileId is not null))
            DuplicateItems.Add(ToFileState(file, "Duplicata física"));

        UnavailableItems.Clear();
        foreach (var file in physicalFiles.Where(file => file.Availability != global::AniT.Core.MediaFileAvailability.Available))
            UnavailableItems.Add(ToFileState(file, file.Availability == global::AniT.Core.MediaFileAvailability.RootUnavailable ? "Disco ou pasta desconectada" : "Arquivo não encontrado no último scan"));

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReviewCount)));
        _ = LoadMissingMetadataAsync(missingMetadata, coverLoadingCancellation.Token);
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        SearchPlaceholder.Visibility = LibrarySearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = LibrarySearchBox.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        ApplySearchFilter();
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        LibrarySearchBox.Clear();
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F || Keyboard.Modifiers != ModifierKeys.Control || SearchBanner.Visibility != Visibility.Visible) return;
        LibrarySearchBox.Focus();
        LibrarySearchBox.SelectAll();
        e.Handled = true;
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        LibrarySearchBox.Clear();
        LibrarySearchBox.Focus();
    }

    private void AnimeItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AnimeLibraryItem.IsFavorite)
            || (e.PropertyName == nameof(AnimeLibraryItem.EnglishTitle) && LibrarySearchBox.Text.Length > 0))
        {
            ApplySearchFilter();
        }
    }

    private void ApplySearchFilter()
    {
        var query = LibrarySearchBox?.Text?.Trim() ?? string.Empty;
        var selectedGenre = GenreFilter?.SelectedValue as string ?? string.Empty;
        var selectedYear = YearFilter?.SelectedValue as string ?? string.Empty;
        var selectedStatus = StatusFilter?.SelectedValue as string ?? string.Empty;
        var selectedStudio = StudioFilter?.SelectedValue as string ?? string.Empty;
        var selectedTag = TagFilter?.SelectedValue as string ?? string.Empty;
        var selectedCollection = CollectionFilter?.SelectedValue as string ?? string.Empty;
        var selectedOrder = OrderFilter?.SelectedValue as string ?? "recent";

        IEnumerable<AnimeLibraryItem> filtered = allAnime;
        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(item => global::AniT.Core.AnimeSearch.Matches(
                query,
                item.Title,
                item.EnglishTitle,
                global::AniT.Core.AnimeGenreCatalog.ToSearchText(item.Genres),
                item.Studio,
                item.ReleaseYear?.ToString(CultureInfo.InvariantCulture),
                string.Join(' ', global::AniT.Core.AnimeOrganizationLabels.Parse(item.CustomTags)),
                string.Join(' ', global::AniT.Core.AnimeOrganizationLabels.Parse(item.UserCollections))));
        }

        if (!string.IsNullOrWhiteSpace(selectedGenre))
        {
            filtered = filtered.Where(item => global::AniT.Core.AnimeGenreCatalog.Parse(item.Genres)
                .Contains(selectedGenre, StringComparer.OrdinalIgnoreCase));
        }

        if (selectedYear == "unknown") filtered = filtered.Where(item => item.ReleaseYear is null);
        else if (int.TryParse(selectedYear, NumberStyles.None, CultureInfo.InvariantCulture, out var releaseYear))
            filtered = filtered.Where(item => item.ReleaseYear == releaseYear);

        filtered = selectedStatus switch
        {
            "watching" => filtered.Where(item => item.CatalogStatus == LibraryCatalogStatus.Watching),
            "completed" => filtered.Where(item => item.CatalogStatus == LibraryCatalogStatus.Completed),
            "not-started" => filtered.Where(item => item.CatalogStatus == LibraryCatalogStatus.NotStarted),
            _ => filtered
        };

        if (selectedStudio == "unknown") filtered = filtered.Where(item => string.IsNullOrWhiteSpace(item.Studio));
        else if (!string.IsNullOrWhiteSpace(selectedStudio))
            filtered = filtered.Where(item => string.Equals(item.Studio, selectedStudio, StringComparison.CurrentCultureIgnoreCase));

        if (!string.IsNullOrWhiteSpace(selectedTag))
            filtered = filtered.Where(item => global::AniT.Core.AnimeOrganizationLabels.Contains(item.CustomTags, selectedTag));
        if (!string.IsNullOrWhiteSpace(selectedCollection))
            filtered = filtered.Where(item => global::AniT.Core.AnimeOrganizationLabels.Contains(item.UserCollections, selectedCollection));

        var settings = global::AniT.Infrastructure.AniTSystemSettingsStore.Load();
        IOrderedEnumerable<AnimeLibraryItem> ordered = filtered
            .OrderByDescending(item => settings.FavoritesFirstRule && item.IsFavorite)
            .ThenByDescending(item => settings.InProgressFirstRule && item.CatalogStatus == LibraryCatalogStatus.Watching);
        filtered = selectedOrder switch
        {
            "title" => ordered.ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase),
            "year-desc" => ordered.ThenByDescending(item => item.ReleaseYear ?? int.MinValue).ThenBy(item => item.Title),
            "year-asc" => ordered.ThenBy(item => item.ReleaseYear ?? int.MaxValue).ThenBy(item => item.Title),
            "score" => ordered.ThenByDescending(item => item.CriticScore ?? double.MinValue).ThenBy(item => item.Title),
            "progress" => ordered.ThenByDescending(item => item.ProgressPercent).ThenBy(item => item.Title),
            "favorites" => ordered.ThenByDescending(item => item.IsFavorite).ThenBy(item => item.Title),
            _ => ordered.ThenByDescending(item => item.LatestImportAt).ThenBy(item => item.Title)
        };

        Anime.Clear();
        foreach (var item in filtered)
            Anime.Add(item);

        var activeFilterCount = new[] { selectedGenre, selectedYear, selectedStatus, selectedStudio, selectedTag, selectedCollection }.Count(value => !string.IsNullOrWhiteSpace(value));
        FilterResultText.Text = Anime.Count == allAnime.Count
            ? $"{Anime.Count} título{(Anime.Count == 1 ? string.Empty : "s")}"
            : $"{Anime.Count} de {allAnime.Count} títulos";
        ClearFiltersButton.Content = activeFilterCount == 0 ? "Limpar filtros" : $"Limpar filtros ({activeFilterCount})";
        ClearFiltersButton.Visibility = activeFilterCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateEmptyState(query, activeFilterCount > 0);
    }

    private void UpdateEmptyState(string query, bool hasActiveFilters)
    {
        if (allAnime.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            EmptyTitleText.Text = libraryEmptyTitle;
            EmptyDetailText.Text = libraryEmptyDetail;
            EmptyActionButton.Visibility = Visibility.Visible;
            return;
        }

        if (Anime.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            EmptyTitleText.Text = "Nenhum título encontrado";
            EmptyDetailText.Text = hasActiveFilters
                ? "Nenhum anime corresponde a esta combinação. Ajuste ou limpe os filtros para ampliar os resultados."
                : $"Não encontramos resultados para “{query.Trim()}”. Tente outro título ou limpe a pesquisa.";
            EmptyActionButton.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;
        EmptyActionButton.Visibility = Visibility.Visible;
    }

    private void LibraryFilter_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded || isPopulatingFilters) return;
        ApplySearchFilter();
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        isPopulatingFilters = true;
        GenreFilter.SelectedIndex = 0;
        YearFilter.SelectedIndex = 0;
        StatusFilter.SelectedIndex = 0;
        StudioFilter.SelectedIndex = 0;
        TagFilter.SelectedIndex = 0;
        CollectionFilter.SelectedIndex = 0;
        isPopulatingFilters = false;
        ApplySearchFilter();
    }

    private void FiltersToggle_Click(object sender, RoutedEventArgs e)
    {
        areFiltersExpanded = !areFiltersExpanded;
        FilterFieldsPanel.Visibility = areFiltersExpanded ? Visibility.Visible : Visibility.Collapsed;
        FiltersToggleText.Text = areFiltersExpanded ? "Recolher filtros" : "Mostrar filtros";
        FiltersToggleButton.ToolTip = FiltersToggleText.Text;
        System.Windows.Automation.AutomationProperties.SetName(FiltersToggleButton, FiltersToggleText.Text);
        FiltersToggleIcon.RenderTransform = new System.Windows.Media.RotateTransform(areFiltersExpanded ? 180 : 0);
    }

    private void RebuildDynamicFilterOptions()
    {
        var selectedGenre = GenreFilter?.SelectedValue as string ?? string.Empty;
        var selectedYear = YearFilter?.SelectedValue as string ?? string.Empty;
        var selectedStudio = StudioFilter?.SelectedValue as string ?? string.Empty;
        var selectedTag = TagFilter?.SelectedValue as string ?? string.Empty;
        var selectedCollection = CollectionFilter?.SelectedValue as string ?? string.Empty;
        isPopulatingFilters = true;
        try
        {
            ReplaceFilterOptions(
                GenreFilterOptions,
                new[] { new LibraryFilterOption("", "Todos os gêneros") }.Concat(
                    allAnime.SelectMany(item => global::AniT.Core.AnimeGenreCatalog.Parse(item.Genres))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Select(genre => new LibraryFilterOption(genre, global::AniT.Core.AnimeGenreCatalog.DisplayName(genre)))
                        .OrderBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase)));

            var years = allAnime.Where(item => item.ReleaseYear is not null)
                .Select(item => item.ReleaseYear!.Value)
                .Distinct()
                .OrderByDescending(year => year)
                .Select(year => new LibraryFilterOption(year.ToString(CultureInfo.InvariantCulture), year.ToString(CultureInfo.InvariantCulture)));
            ReplaceFilterOptions(YearFilterOptions, new[] { new LibraryFilterOption("", "Todos os anos") }
                .Concat(years)
                .Concat(allAnime.Any(item => item.ReleaseYear is null) ? [new("unknown", "Ano não informado")] : []));

            var studios = allAnime.Where(item => !string.IsNullOrWhiteSpace(item.Studio))
                .Select(item => item.Studio!.Trim())
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(studio => studio, StringComparer.CurrentCultureIgnoreCase)
                .Select(studio => new LibraryFilterOption(studio, studio));
            ReplaceFilterOptions(StudioFilterOptions, new[] { new LibraryFilterOption("", "Todos os estúdios") }
                .Concat(studios)
                .Concat(allAnime.Any(item => string.IsNullOrWhiteSpace(item.Studio)) ? [new("unknown", "Estúdio não informado")] : []));

            ReplaceFilterOptions(TagFilterOptions, new[] { new LibraryFilterOption("", "Todas as tags") }.Concat(
                allAnime.SelectMany(item => global::AniT.Core.AnimeOrganizationLabels.Parse(item.CustomTags))
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase)
                    .Select(item => new LibraryFilterOption(item, item))));
            ReplaceFilterOptions(CollectionFilterOptions, new[] { new LibraryFilterOption("", "Todas as coleções") }.Concat(
                allAnime.SelectMany(item => global::AniT.Core.AnimeOrganizationLabels.Parse(item.UserCollections))
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase)
                    .Select(item => new LibraryFilterOption(item, item))));

            RestoreFilterSelection(GenreFilter!, selectedGenre);
            RestoreFilterSelection(YearFilter!, selectedYear);
            RestoreFilterSelection(StudioFilter!, selectedStudio);
            RestoreFilterSelection(TagFilter!, selectedTag);
            RestoreFilterSelection(CollectionFilter!, selectedCollection);
        }
        finally
        {
            isPopulatingFilters = false;
        }
    }

    private static void ReplaceFilterOptions(ObservableCollection<LibraryFilterOption> target, IEnumerable<LibraryFilterOption> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }

    private static void RestoreFilterSelection(System.Windows.Controls.ComboBox filter, string value)
    {
        filter.SelectedValue = filter.Items.OfType<LibraryFilterOption>().Any(item => item.Value == value) ? value : string.Empty;
        if (filter.SelectedIndex < 0) filter.SelectedIndex = 0;
    }

    private static FileStateItem ToFileState(global::AniT.Core.MediaFile file, string status)
    {
        var title = file.Episode?.Season?.Anime?.Title ?? "Episódio";
        var episode = file.Episode is null ? string.Empty : $" · S{file.Episode.Season?.Number ?? 1:00}E{file.Episode.Number:00}";
        return new FileStateItem($"{title}{episode}", file.LibraryRoot is null ? file.RelativePath : Path.Combine(file.LibraryRoot.Path, file.RelativePath), status);
    }

    private async Task LoadMissingMetadataAsync(
        IEnumerable<(AnimeLibraryItem Item, string? SavedCoverPath, string? SavedSynopsis, double? SavedCriticScore, string? SavedGenres, int? SavedReleaseYear, string? SavedStudio)> missingMetadata,
        CancellationToken cancellationToken)
    {
        var catalogMetadataChanged = false;
        foreach (var entry in missingMetadata)
        {
            try
            {
                var metadata = await App.EnsureAnimeMetadataAsync(
                    entry.Item.Id,
                    entry.Item.Title,
                    entry.Item.EnglishTitle,
                    entry.SavedCoverPath,
                    cancellationToken,
                    entry.SavedSynopsis,
                    entry.SavedCriticScore,
                    savedGenres: entry.SavedGenres,
                    savedReleaseYear: entry.SavedReleaseYear,
                    savedStudio: entry.SavedStudio,
                    refreshCatalogDetails: entry.SavedReleaseYear is null || string.IsNullOrWhiteSpace(entry.SavedStudio));
                entry.Item.EnglishTitle = metadata.EnglishTitle;
                entry.Item.CoverPath = metadata.CoverPath ?? App.UpdatingArtworkPath;
                entry.Item.Genres = metadata.Genres;
                entry.Item.ReleaseYear = metadata.ReleaseYear;
                entry.Item.Studio = metadata.Studio;
                entry.Item.CriticScore = metadata.CriticScore;
                catalogMetadataChanged = true;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"AniT could not load the anime cover: {exception}");
            }
        }

        if (catalogMetadataChanged && !cancellationToken.IsCancellationRequested)
        {
            RebuildDynamicFilterOptions();
            ApplySearchFilter();
        }
    }

    private async void RefreshLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (isRefreshing) return;
        isRefreshing = true;
        RefreshLibraryButton.IsEnabled = false;
        coverLoadingCancellation?.Cancel();
        libraryRefreshCancellation = new CancellationTokenSource();
        var cancellationToken = libraryRefreshCancellation.Token;
        ShowRefreshOverlay("Preparando sua estante", "Lendo as pastas configuradas…", 0);

        try
        {
            var scanSummary = await ScanLibraryRootsAsync(cancellationToken);
            var metadataUpdated = await RefreshMetadataAsync(cancellationToken);
            await LoadAsync();
            ShowRefreshOverlay(
                "Estante atualizada!",
                $"{scanSummary.FilesFound} arquivo(s) · {scanSummary.Matched} identificado(s) · {scanSummary.Review} revisão · {scanSummary.Duplicates} duplicata(s) · {metadataUpdated} título(s)",
                100);
            CancelRefreshButton.IsEnabled = false;
            await Task.Delay(850, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            ShowRefreshOverlay("Atualização cancelada", "Nada que já estava salvo foi perdido.", RefreshProgress.Value);
            CancelRefreshButton.IsEnabled = false;
            await Task.Delay(650);
        }
        catch (Exception exception)
        {
            ShowRefreshOverlay("Não foi possível concluir", exception.Message, RefreshProgress.Value);
            CancelRefreshButton.IsEnabled = false;
            await Task.Delay(1800);
        }
        finally
        {
            RefreshOverlay.Visibility = Visibility.Collapsed;
            RefreshLibraryButton.IsEnabled = true;
            isRefreshing = false;
            libraryRefreshCancellation?.Dispose();
            libraryRefreshCancellation = null;
        }
    }

    private async Task<LibraryRefreshSummary> ScanLibraryRootsAsync(CancellationToken cancellationToken)
    {
        await using var context = App.OpenFreshDatabase();
        var roots = await context.LibraryRoots.AsNoTracking().Where(root => root.IsEnabled).OrderBy(root => root.DisplayName).ToListAsync(cancellationToken);
        if (roots.Count == 0) throw new InvalidOperationException("Nenhuma pasta está configurada na estante.");

        var summary = new LibraryRefreshSummary();
        for (var rootIndex = 0; rootIndex < roots.Count; rootIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = roots[rootIndex];
            var currentRootIndex = rootIndex;
            ShowRefreshOverlay("Verificando arquivos", root.DisplayName, rootIndex * 45d / roots.Count);
            var progress = new Progress<global::AniT.Infrastructure.LibraryScanProgress>(scan =>
            {
                var rootFraction = scan.TotalFiles == 0 ? 1 : (double)scan.FilesProcessed / scan.TotalFiles;
                var percent = (currentRootIndex + rootFraction) * 45d / roots.Count;
                ShowRefreshOverlay(
                    "Verificando arquivos",
                    $"{root.DisplayName} · {scan.FilesProcessed} de {scan.TotalFiles}",
                    percent);
            });
            var result = await new global::AniT.Infrastructure.LibraryScanner(
                    context,
                    settings: global::AniT.Infrastructure.AniTSystemSettingsStore.Load())
                .ScanAsync(root, progress, cancellationToken);
            summary.FilesFound += result.FilesFound;
            summary.Matched += result.FilesMatched;
            summary.Review += result.NeedsReview;
            summary.Duplicates += result.PhysicalDuplicates;
            summary.UnavailableRoots += result.RootUnavailable ? 1 : 0;
        }

        await App.Achievements.RecordAsync(new global::AniT.Core.Achievements.AchievementEvent(
            global::AniT.Core.Achievements.AchievementEventType.LibraryChanged), cancellationToken);
        return summary;
    }

    private async Task<int> RefreshMetadataAsync(CancellationToken cancellationToken)
    {
        List<AnimeMetadataItem> anime;
        await using (var context = App.OpenFreshDatabase())
        {
            anime = await context.Anime
                .AsNoTracking()
                .OrderBy(item => item.Title)
                .Select(item => new AnimeMetadataItem(
                    item.Id,
                    item.Title,
                    item.EnglishTitle,
                    item.CoverPath,
                    item.Synopsis,
                    item.CriticScore,
                    item.Genres,
                    item.ReleaseYear,
                    item.Studio))
                .ToListAsync(cancellationToken);
        }

        if (anime.Count == 0)
        {
            ShowRefreshOverlay("Atualizando títulos e capas", "Nenhum anime encontrado nas pastas.", 100);
            return 0;
        }

        var updated = 0;
        for (var index = 0; index < anime.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = anime[index];
            var startPercent = 45d + index * 55d / anime.Count;
            ShowRefreshOverlay(
                "Atualizando títulos e capas",
                $"{index + 1} de {anime.Count} · {item.JapaneseTitle}",
                startPercent);
            try
            {
                await App.EnsureAnimeMetadataAsync(
                    item.Id,
                    item.JapaneseTitle,
                    item.EnglishTitle,
                    item.CoverPath,
                    cancellationToken,
                    item.Synopsis,
                    item.CriticScore,
                    forceRefresh: true,
                    savedGenres: item.Genres,
                    savedReleaseYear: item.ReleaseYear,
                    savedStudio: item.Studio);
                updated++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Metadata is an optional enhancement. A local library scan remains successful offline.
                System.Diagnostics.Debug.WriteLine($"AniT metadata refresh skipped for {item.Id}: {exception.Message}");
            }
        }

        return updated;
    }

    private void CancelRefresh_Click(object sender, RoutedEventArgs e)
    {
        CancelRefreshButton.IsEnabled = false;
        RefreshDetailText.Text = "Cancelando com segurança…";
        libraryRefreshCancellation?.Cancel();
    }

    private void ShowRefreshOverlay(string title, string detail, double percent)
    {
        RefreshOverlay.Visibility = Visibility.Visible;
        RefreshTitleText.Text = title;
        RefreshDetailText.Text = detail;
        RefreshProgress.Value = Math.Clamp(percent, 0, 100);
        RefreshPercentText.Text = $"{RefreshProgress.Value:0}%";
        CancelRefreshButton.IsEnabled = true;
    }

    private void AnimeCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: AnimeLibraryItem anime }) return;
        AppNavigation.OpenAnimeDetails(this, anime.Id);
    }

    private async void FavoriteAnime_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.DataContext is not AnimeLibraryItem anime) return;
        e.Handled = true;
        button.IsEnabled = false;
        try
        {
            var updated = await App.ToggleAnimeFavoriteAsync(anime.Id);
            if (updated is bool isFavorite) anime.IsFavorite = isFavorite;
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Settings(this, SettingsSection.Shelf);

    private async void RootSubfolders_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox { Tag: Guid id, IsChecked: bool value }) return;
        await UpdateRootAsync(id, root => root.IncludeSubfolders = value);
    }

    private async void RootEnabled_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox { Tag: Guid id, IsChecked: bool value }) return;
        await UpdateRootAsync(id, root => root.IsEnabled = value);
    }

    private static async Task UpdateRootAsync(Guid id, Action<global::AniT.Core.LibraryRoot> update)
    {
        await using var context = App.OpenFreshDatabase();
        var root = await context.LibraryRoots.FindAsync(id);
        if (root is null) return;
        update(root);
        await context.SaveChangesAsync();
    }

    private async void ReviewItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: Guid id }) return;
        var window = new ReviewMatchWindow(id) { Owner = this };
        if (window.ShowDialog() is true) await LoadAsync();
    }

    private async void OpenOrganizer_Click(object sender, RoutedEventArgs e)
    {
        var window = new OrganizationPreviewWindow { Owner = this };
        if (window.ShowDialog() is true) await LoadAsync();
    }
}

internal sealed record AnimeMetadataItem(
    Guid Id,
    string JapaneseTitle,
    string? EnglishTitle,
    string? CoverPath,
    string? Synopsis,
    double? CriticScore,
    string? Genres,
    int? ReleaseYear,
    string? Studio);

public sealed record LibraryFilterOption(string Value, string Label);

public enum LibraryCatalogStatus
{
    NotStarted,
    Watching,
    Completed
}

public sealed class AnimeLibraryItem(
    Guid id,
    string title,
    string? englishTitle,
    string initial,
    string episodeSummary,
    string status,
    double progressPercent,
    string? coverPath,
    bool isNew,
    bool isFavorite,
    string? genres,
    int? releaseYear,
    string? studio,
    string? customTags,
    string? userCollections,
    double? criticScore,
    LibraryCatalogStatus catalogStatus,
    DateTimeOffset createdAt,
    DateTimeOffset latestImportAt) : INotifyPropertyChanged
{
    private string? coverPath = coverPath;
    private string? englishTitle = englishTitle;
    private bool isFavorite = isFavorite;
    private string? genres = genres;
    private int? releaseYear = releaseYear;
    private string? studio = studio;
    private double? criticScore = criticScore;

    public Guid Id { get; } = id;
    public string Title { get; } = title;
    public string? EnglishTitle
    {
        get => englishTitle;
        set
        {
            if (string.Equals(englishTitle, value, StringComparison.Ordinal)) return;
            englishTitle = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EnglishTitle)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EnglishTitleDisplay)));
        }
    }

    public string EnglishTitleDisplay => string.IsNullOrWhiteSpace(EnglishTitle) ? string.Empty : $"({EnglishTitle})";
    public string Initial { get; } = initial;
    public string EpisodeSummary { get; } = episodeSummary;
    public string Status { get; } = status;
    public double ProgressPercent { get; } = progressPercent;
    public LibraryCatalogStatus CatalogStatus { get; } = catalogStatus;
    public DateTimeOffset CreatedAt { get; } = createdAt;
    public DateTimeOffset LatestImportAt { get; } = latestImportAt;
    public string? Genres
    {
        get => genres;
        set
        {
            if (string.Equals(genres, value, StringComparison.Ordinal)) return;
            genres = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Genres)));
        }
    }
    public int? ReleaseYear
    {
        get => releaseYear;
        set
        {
            if (releaseYear == value) return;
            releaseYear = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReleaseYear)));
        }
    }
    public string? Studio
    {
        get => studio;
        set
        {
            if (string.Equals(studio, value, StringComparison.Ordinal)) return;
            studio = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Studio)));
        }
    }
    public string? CustomTags { get; } = customTags;
    public string? UserCollections { get; } = userCollections;
    public double? CriticScore
    {
        get => criticScore;
        set
        {
            if (criticScore == value) return;
            criticScore = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CriticScore)));
        }
    }
    public bool IsNew { get; } = isNew;
    public Visibility NewBadgeVisibility => IsNew ? Visibility.Visible : Visibility.Collapsed;
    public bool IsFavorite
    {
        get => isFavorite;
        set
        {
            if (isFavorite == value) return;
            isFavorite = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFavorite)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FavoriteFill)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FavoriteStroke)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FavoriteToolTip)));
        }
    }
    public string FavoriteFill => IsFavorite ? "#FF67A9" : "Transparent";
    public string FavoriteStroke => IsFavorite ? "#FFD0E4" : "#C8ECFF";
    public string FavoriteToolTip => IsFavorite ? "Remover dos favoritos" : "Adicionar aos favoritos";

    public string? CoverPath
    {
        get => coverPath;
        set
        {
            if (string.Equals(coverPath, value, StringComparison.OrdinalIgnoreCase)) return;
            coverPath = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CoverPath)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record LibraryRootItem(Guid Id, string DisplayName, string Path, bool IncludeSubfolders, bool IsEnabled, string StatusText);

public sealed record ReviewQueueItem(
    Guid Id,
    string FileName,
    string ProposedAnime,
    int? Season,
    double? Episode,
    double Confidence,
    string Reasons)
{
    public string SuggestedText => $"{ProposedAnime} · Temporada {Season ?? 1:00} · Episódio {(Episode?.ToString("0.##") ?? "?")}";
    public double ConfidencePercent => Confidence * 100;
    public string ConfidenceLabel => $"{ConfidencePercent:0}%";
}

public sealed record FileStateItem(string Title, string Path, string Status);

internal sealed class LibraryRefreshSummary
{
    public int FilesFound { get; set; }
    public int Matched { get; set; }
    public int Review { get; set; }
    public int Duplicates { get; set; }
    public int UnavailableRoots { get; set; }
}

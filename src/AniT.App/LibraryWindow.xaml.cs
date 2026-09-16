using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;

namespace AniT.App;

public partial class LibraryWindow : Window, INotifyPropertyChanged
{
    public ObservableCollection<AnimeLibraryItem> Anime { get; } = [];
    public ObservableCollection<LibraryRootItem> LibraryRoots { get; } = [];
    public ObservableCollection<ReviewQueueItem> ReviewItems { get; } = [];
    public ObservableCollection<FileStateItem> DuplicateItems { get; } = [];
    public ObservableCollection<FileStateItem> UnavailableItems { get; } = [];
    public int ReviewCount => ReviewItems.Count;
    public event PropertyChangedEventHandler? PropertyChanged;
    private CancellationTokenSource? coverLoadingCancellation;
    private CancellationTokenSource? libraryRefreshCancellation;
    private bool isRefreshing;

    public LibraryWindow()
    {
        InitializeComponent();
        ResponsiveWindow.FitToWorkArea(this, 1180, 760);
        DataContext = this;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await LoadAsync();
    private async void Window_Activated(object? sender, EventArgs e)
    {
        if (!isRefreshing) await LoadAsync();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        SearchBanner.Visibility = ActualWidth < 920 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        coverLoadingCancellation?.Cancel();
        libraryRefreshCancellation?.Cancel();
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
            .OrderBy(item => item.Title)
            .ToListAsync();

        var animeNames = anime.ToDictionary(item => item.Id, item => item.Title);
        var roots = await context.LibraryRoots.AsNoTracking().OrderBy(root => root.DisplayName).ToListAsync();
        var reviewItems = await context.LibraryReviewItems.AsNoTracking()
            .Where(item => item.Status == global::AniT.Core.LibraryReviewStatus.Pending)
            .OrderByDescending(item => item.Confidence)
            .ThenBy(item => item.FileName)
            .ToListAsync();
        var physicalFiles = await context.MediaFiles.AsNoTracking()
            .Include(file => file.LibraryRoot)
            .Include(file => file.Episode)!.ThenInclude(episode => episode!.Season)!.ThenInclude(season => season!.Anime)
            .ToListAsync();

        Anime.Clear();
        var missingMetadata = new List<(AnimeLibraryItem Item, string? SavedCoverPath)>();
        foreach (var item in anime)
        {
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
            var viewItem = new AnimeLibraryItem(
                item.Id,
                item.Title,
                item.EnglishTitle,
                string.IsNullOrWhiteSpace(item.Title) ? "?" : item.Title[..1].ToUpperInvariant(),
                $"{episodes.Count} episódio(s)",
                watchSummary.IsCompleted
                    ? "Concluído"
                    : current is not null
                        ? $"Em andamento · Episódio {current.Number:00}"
                        : watched == 0
                            ? "Ainda não iniciado"
                            : $"{watched} assistido(s)",
                totalPercent,
                File.Exists(item.CoverPath) ? item.CoverPath : null);
            Anime.Add(viewItem);
            if (viewItem.CoverPath is null || string.IsNullOrWhiteSpace(viewItem.EnglishTitle))
            {
                missingMetadata.Add((viewItem, item.CoverPath));
            }
        }

        EmptyState.Visibility = Anime.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitleText.Text = roots.Count == 0
            ? "Nenhuma pasta adicionada ainda"
            : reviewItems.Count > 0 ? "Há arquivos aguardando revisão" : "Nenhum título identificado";
        EmptyDetailText.Text = roots.Count == 0
            ? "Adicione uma pasta. O AniT encontra os vídeos sem exigir que você renomeie ou reorganize nada."
            : reviewItems.Count > 0
                ? "Abra a aba Revisão para confirmar os arquivos ambíguos com segurança."
                : "Atualize a Biblioteca depois de adicionar vídeos às pastas monitoradas.";
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

    private static FileStateItem ToFileState(global::AniT.Core.MediaFile file, string status)
    {
        var title = file.Episode?.Season?.Anime?.Title ?? "Episódio";
        var episode = file.Episode is null ? string.Empty : $" · S{file.Episode.Season?.Number ?? 1:00}E{file.Episode.Number:00}";
        return new FileStateItem($"{title}{episode}", file.LibraryRoot is null ? file.RelativePath : Path.Combine(file.LibraryRoot.Path, file.RelativePath), status);
    }

    private static async Task LoadMissingMetadataAsync(
        IEnumerable<(AnimeLibraryItem Item, string? SavedCoverPath)> missingMetadata,
        CancellationToken cancellationToken)
    {
        foreach (var entry in missingMetadata)
        {
            try
            {
                var metadata = await App.EnsureAnimeMetadataAsync(
                    entry.Item.Id,
                    entry.Item.Title,
                    entry.Item.EnglishTitle,
                    entry.SavedCoverPath,
                    cancellationToken);
                entry.Item.EnglishTitle = metadata.EnglishTitle;
                entry.Item.CoverPath = metadata.CoverPath;
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
        var roots = await context.LibraryRoots.AsNoTracking().OrderBy(root => root.DisplayName).ToListAsync(cancellationToken);
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
            var result = await new global::AniT.Infrastructure.LibraryScanner(context)
                .ScanAsync(root, progress, cancellationToken);
            summary.FilesFound += result.FilesFound;
            summary.Matched += result.FilesMatched;
            summary.Review += result.NeedsReview;
            summary.Duplicates += result.PhysicalDuplicates;
            summary.UnavailableRoots += result.RootUnavailable ? 1 : 0;
        }

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
                    item.CriticScore))
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
                    forceRefresh: true);
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
        var details = new AnimeDetailsWindow(anime.Id) { Owner = this };
        details.ShowDialog();
    }

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Adicionar uma pasta à Biblioteca" };
        if (dialog.ShowDialog() is not true) return;
        await using var context = App.OpenFreshDatabase();
        var path = Path.GetFullPath(dialog.FolderName);
        if (await context.LibraryRoots.AnyAsync(root => root.Path.ToLower() == path.ToLower()))
        {
            MessageBox.Show("Essa pasta já faz parte da Biblioteca.", "AniT", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var root = new global::AniT.Core.LibraryRoot
        {
            Path = path,
            DisplayName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            IncludeSubfolders = true
        };
        context.LibraryRoots.Add(root);
        await context.SaveChangesAsync();
        isRefreshing = true;
        ShowRefreshOverlay("Escaneando nova pasta", root.DisplayName, 0);
        try
        {
            var progress = new Progress<global::AniT.Infrastructure.LibraryScanProgress>(scan =>
                ShowRefreshOverlay("Escaneando nova pasta", $"{scan.FilesProcessed} de {scan.TotalFiles} · {scan.NeedsReview} para revisão", scan.TotalFiles == 0 ? 100 : scan.FilesProcessed * 100d / scan.TotalFiles));
            await new global::AniT.Infrastructure.LibraryScanner(context).ScanAsync(root, progress);
            await LoadAsync();
        }
        finally
        {
            RefreshOverlay.Visibility = Visibility.Collapsed;
            isRefreshing = false;
        }
    }

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
    double? CriticScore);

public sealed class AnimeLibraryItem(
    Guid id,
    string title,
    string? englishTitle,
    string initial,
    string episodeSummary,
    string status,
    double progressPercent,
    string? coverPath) : INotifyPropertyChanged
{
    private string? coverPath = coverPath;
    private string? englishTitle = englishTitle;

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

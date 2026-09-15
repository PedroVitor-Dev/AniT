using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;

namespace AniT.App;

public partial class LibraryWindow : Window
{
    public ObservableCollection<AnimeLibraryItem> Anime { get; } = [];
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

        Anime.Clear();
        var missingMetadata = new List<(AnimeLibraryItem Item, string? SavedCoverPath)>();
        foreach (var item in anime)
        {
            var episodes = item.Seasons.SelectMany(season => season.Episodes).ToList();
            var watched = episodes.Count(episode => episode.Status == global::AniT.Core.WatchStatus.Completed);
            var current = episodes
                .Where(episode => episode.Status == global::AniT.Core.WatchStatus.Watching || episode.PlaybackProgress is { PositionSeconds: > 0 })
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
                current is not null ? $"Em andamento · Episódio {current.Number:00}" : watched == 0 ? "Ainda não iniciado" : $"{watched} assistido(s)",
                totalPercent,
                File.Exists(item.CoverPath) ? item.CoverPath : null);
            Anime.Add(viewItem);
            if (viewItem.CoverPath is null || string.IsNullOrWhiteSpace(viewItem.EnglishTitle))
            {
                missingMetadata.Add((viewItem, item.CoverPath));
            }
        }

        EmptyState.Visibility = Anime.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _ = LoadMissingMetadataAsync(missingMetadata, coverLoadingCancellation.Token);
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
            var episodesAdded = await ScanLibraryRootsAsync(cancellationToken);
            var metadataUpdated = await RefreshMetadataAsync(cancellationToken);
            await LoadAsync();
            ShowRefreshOverlay(
                "Estante atualizada!",
                $"{episodesAdded} episódio(s) novo(s) · {metadataUpdated} anime(s) verificado(s)",
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

    private async Task<int> ScanLibraryRootsAsync(CancellationToken cancellationToken)
    {
        await using var context = App.OpenFreshDatabase();
        var roots = await context.LibraryRoots.AsNoTracking().OrderBy(root => root.DisplayName).ToListAsync(cancellationToken);
        if (roots.Count == 0) throw new InvalidOperationException("Nenhuma pasta está configurada na estante.");

        var episodesAdded = 0;
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
            episodesAdded += result.EpisodesAdded;
        }

        return episodesAdded;
    }

    private async Task<int> RefreshMetadataAsync(CancellationToken cancellationToken)
    {
        List<AnimeMetadataItem> anime;
        await using (var context = App.OpenFreshDatabase())
        {
            anime = await context.Anime
                .AsNoTracking()
                .OrderBy(item => item.Title)
                .Select(item => new AnimeMetadataItem(item.Id, item.Title, item.EnglishTitle, item.CoverPath))
                .ToListAsync(cancellationToken);
        }

        if (anime.Count == 0)
        {
            ShowRefreshOverlay("Atualizando títulos e capas", "Nenhum anime encontrado nas pastas.", 100);
            return 0;
        }

        for (var index = 0; index < anime.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = anime[index];
            var startPercent = 45d + index * 55d / anime.Count;
            ShowRefreshOverlay(
                "Atualizando títulos e capas",
                $"{index + 1} de {anime.Count} · {item.JapaneseTitle}",
                startPercent);
            await App.EnsureAnimeMetadataAsync(
                item.Id,
                item.JapaneseTitle,
                item.EnglishTitle,
                item.CoverPath,
                cancellationToken);
        }

        return anime.Count;
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
}

internal sealed record AnimeMetadataItem(Guid Id, string JapaneseTitle, string? EnglishTitle, string? CoverPath);

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

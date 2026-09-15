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

    public LibraryWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await LoadAsync();
    private async void Window_Activated(object? sender, EventArgs e) => await LoadAsync();

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
                _ = LoadMetadataAsync(viewItem, item.CoverPath, coverLoadingCancellation.Token);
            }
        }

        EmptyState.Visibility = Anime.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static async Task LoadMetadataAsync(AnimeLibraryItem item, string? savedCoverPath, CancellationToken cancellationToken)
    {
        try
        {
            var metadata = await App.EnsureAnimeMetadataAsync(
                item.Id,
                item.Title,
                item.EnglishTitle,
                savedCoverPath,
                cancellationToken);
            item.EnglishTitle = metadata.EnglishTitle;
            item.CoverPath = metadata.CoverPath;
        }
        catch (OperationCanceledException)
        {
            // A fresh library load superseded this request.
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"AniT could not load the anime cover: {exception}");
        }
    }

    private void AnimeCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: AnimeLibraryItem anime }) return;
        var details = new AnimeDetailsWindow(anime.Id) { Owner = this };
        details.ShowDialog();
    }
}

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

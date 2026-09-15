using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.Windows;

namespace AniT.App;

public partial class LibraryWindow : Window
{
    public ObservableCollection<AnimeLibraryItem> Anime { get; } = [];

    public LibraryWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await LoadAsync();
    private async void Window_Activated(object? sender, EventArgs e) => await LoadAsync();

    private async Task LoadAsync()
    {
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
            Anime.Add(new AnimeLibraryItem(
                item.Id,
                item.Title,
                string.IsNullOrWhiteSpace(item.Title) ? "?" : item.Title[..1].ToUpperInvariant(),
                $"{episodes.Count} episódio(s)",
                current is not null ? $"Em andamento · Episódio {current.Number:00}" : watched == 0 ? "Ainda não iniciado" : $"{watched} assistido(s)",
                totalPercent));
        }

        EmptyState.Visibility = Anime.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AnimeCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: AnimeLibraryItem anime }) return;
        var details = new AnimeDetailsWindow(anime.Id) { Owner = this };
        details.ShowDialog();
    }
}

public sealed record AnimeLibraryItem(Guid Id, string Title, string Initial, string EpisodeSummary, string Status, double ProgressPercent);

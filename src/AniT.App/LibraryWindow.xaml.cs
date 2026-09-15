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

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var anime = await App.Database.Anime
            .AsNoTracking()
            .Include(item => item.Seasons)
            .ThenInclude(season => season.Episodes)
            .OrderBy(item => item.Title)
            .ToListAsync();

        foreach (var item in anime)
        {
            var episodes = item.Seasons.SelectMany(season => season.Episodes).ToList();
            var watched = episodes.Count(episode => episode.Status == global::AniT.Core.WatchStatus.Completed);
            Anime.Add(new AnimeLibraryItem(
                item.Id,
                item.Title,
                string.IsNullOrWhiteSpace(item.Title) ? "?" : item.Title[..1].ToUpperInvariant(),
                $"{episodes.Count} episódio(s)",
                watched == 0 ? "Ainda não iniciado" : $"{watched} assistido(s)",
                episodes.Count == 0 ? 0 : watched * 100d / episodes.Count));
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

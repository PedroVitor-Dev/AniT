using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace AniT.App;

public partial class EpisodeCommentWindow : Window
{
    private readonly Guid animeId;
    public ObservableCollection<CommentEpisodeItem> Episodes { get; } = [];

    public EpisodeCommentWindow(Guid animeId)
    {
        this.animeId = animeId;
        InitializeComponent();
        DataContext = this;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await using var context = App.OpenFreshDatabase();
        var episodes = await context.Episodes
            .Where(episode => episode.Season!.AnimeId == animeId)
            .OrderBy(episode => episode.Season!.Number)
            .ThenBy(episode => episode.Number)
            .AsNoTracking()
            .Select(episode => new { episode.Id, episode.Number, episode.Title, episode.ReviewNotes })
            .ToListAsync();

        foreach (var episode in episodes)
        {
            Episodes.Add(new CommentEpisodeItem(
                episode.Id,
                $"Episódio {episode.Number:00} · {episode.Title ?? $"Episódio {episode.Number}"}",
                episode.ReviewNotes));
        }
        EpisodeSelector.SelectedItem = Episodes.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Comment))
            ?? Episodes.FirstOrDefault();
    }

    private void EpisodeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EpisodeSelector.SelectedItem is not CommentEpisodeItem episode) return;
        CommentTextBox.Text = episode.Comment ?? string.Empty;
        StatusText.Text = string.Empty;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (EpisodeSelector.SelectedItem is not CommentEpisodeItem selectedEpisode) return;

        await using var context = App.OpenFreshDatabase();
        var episode = await context.Episodes.FindAsync(selectedEpisode.Id);
        if (episode is null) return;

        episode.ReviewNotes = string.IsNullOrWhiteSpace(CommentTextBox.Text) ? null : CommentTextBox.Text.Trim();
        await context.SaveChangesAsync();
        if (!string.IsNullOrWhiteSpace(episode.ReviewNotes))
        {
            await App.Achievements.RecordAsync(new global::AniT.Core.Achievements.AchievementEvent(
                global::AniT.Core.Achievements.AchievementEventType.ReviewCreated,
                episode.Id));
        }
        StatusText.Text = episode.ReviewNotes is null ? "Comentário removido." : "✓ Comentário salvo localmente.";
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

public sealed record CommentEpisodeItem(Guid Id, string Display, string? Comment);

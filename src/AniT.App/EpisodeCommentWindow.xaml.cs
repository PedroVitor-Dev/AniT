using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace AniT.App;

public partial class EpisodeCommentWindow : Window
{
    private readonly Guid animeId;
    private readonly Guid? requestedEpisodeId;
    public ObservableCollection<CommentEpisodeItem> Episodes { get; } = [];

    public EpisodeCommentWindow(Guid animeId, Guid? requestedEpisodeId = null)
    {
        this.animeId = animeId;
        this.requestedEpisodeId = requestedEpisodeId;
        InitializeComponent();
        DataContext = this;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await using var context = App.OpenFreshDatabase();
        var episodes = await context.Episodes
            .Where(episode => episode.Season!.AnimeId == animeId)
            .Include(episode => episode.Season)!.ThenInclude(season => season!.Anime)!.ThenInclude(anime => anime!.Aliases)
            .OrderBy(episode => episode.Season!.Number)
            .ThenBy(episode => episode.Number)
            .AsNoTracking()
            .ToListAsync();

        var settings = global::AniT.Infrastructure.AniTSystemSettingsStore.Load();
        foreach (var episode in episodes)
        {
            var anime = episode.Season!.Anime!;
            var preferredTitle = global::AniT.Infrastructure.OrganizationPreferences.PreferredTitle(anime, settings.AnimeTitlePreference);
            Episodes.Add(new CommentEpisodeItem(
                episode.Id,
                global::AniT.Core.EpisodeDisplayName.Format(preferredTitle, episode.Season.Number, episode.Number, settings.EpisodeNumberDisplayFormat.ToString()),
                episode.ReviewNotes));
        }
        EpisodeSelector.SelectedItem = Episodes.FirstOrDefault(item => item.Id == requestedEpisodeId)
            ?? Episodes.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Comment))
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
        StatusText.Text = episode.ReviewNotes is null ? "Nota removida." : "✓ Nota do episódio salva localmente.";
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

public sealed record CommentEpisodeItem(Guid Id, string Display, string? Comment);

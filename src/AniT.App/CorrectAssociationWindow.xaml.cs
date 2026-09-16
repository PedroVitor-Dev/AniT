using Microsoft.EntityFrameworkCore;
using System.Windows;

namespace AniT.App;

public partial class CorrectAssociationWindow : Window
{
    private readonly Guid mediaFileId;
    public CorrectAssociationWindow(Guid mediaFileId) { this.mediaFileId = mediaFileId; InitializeComponent(); }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await using var context = App.OpenFreshDatabase();
        var file = await context.MediaFiles.AsNoTracking().Include(item => item.Episode)!.ThenInclude(episode => episode!.Season).SingleAsync(item => item.Id == mediaFileId);
        var currentEpisode = file.Episode ?? throw new InvalidOperationException("Este arquivo não está associado a um episódio.");
        var currentSeason = currentEpisode.Season ?? throw new InvalidOperationException("A temporada deste episódio não foi encontrada.");
        var anime = await context.Anime.AsNoTracking().OrderBy(item => item.Title).Select(item => new AnimeChoice(item.Id, item.Title)).ToListAsync();
        AnimeSelector.ItemsSource = anime;
        AnimeSelector.SelectedItem = anime.FirstOrDefault(item => item.Id == currentSeason.AnimeId);
        SeasonText.Text = currentSeason.Number.ToString();
        EpisodeText.Text = currentEpisode.Number.ToString();
        FileText.Text = file.FileName;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (AnimeSelector.SelectedItem is not AnimeChoice anime || !int.TryParse(SeasonText.Text, out var seasonNumber) || seasonNumber < 0 || !int.TryParse(EpisodeText.Text, out var episodeNumber) || episodeNumber <= 0)
        { StatusText.Text = "Selecione o anime e informe números válidos."; return; }
        await using var context = App.OpenFreshDatabase();
        var season = await context.Seasons.SingleOrDefaultAsync(item => item.AnimeId == anime.Id && item.Number == seasonNumber);
        if (season is null) { season = new global::AniT.Core.Season { AnimeId = anime.Id, Number = seasonNumber }; context.Seasons.Add(season); }
        var episode = await context.Episodes.SingleOrDefaultAsync(item => item.SeasonId == season.Id && item.Number == episodeNumber);
        if (episode is null) { episode = new global::AniT.Core.Episode { Season = season, Number = episodeNumber }; context.Episodes.Add(episode); await context.SaveChangesAsync(); }
        await new global::AniT.Infrastructure.LibraryReviewService(context).CorrectAssociationAsync(mediaFileId, episode.Id);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

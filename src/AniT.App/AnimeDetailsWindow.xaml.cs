using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace AniT.App;

public partial class AnimeDetailsWindow : Window
{
    private readonly Guid animeId;
    public ObservableCollection<EpisodeItem> Episodes { get; } = [];
    public string AnimeTitle { get; private set; } = string.Empty;
    public string Initial { get; private set; } = "?";
    public string Summary { get; private set; } = string.Empty;
    public string EpisodeCountLabel { get; private set; } = string.Empty;
    public string PlayNextLabel { get; private set; } = "▶  Assistir próximo episódio";
    public string? CoverPath { get; private set; }

    public AnimeDetailsWindow(Guid animeId)
    {
        this.animeId = animeId;
        InitializeComponent();
        DataContext = this;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await LoadAsync();
    private async void Window_Activated(object? sender, EventArgs e) => await LoadAsync();

    private async Task LoadAsync()
    {
        await using var context = App.OpenFreshDatabase();
        var anime = await context.Anime
            .Include(item => item.Seasons)
            .ThenInclude(season => season.Episodes)
            .ThenInclude(episode => episode.PlaybackProgress)
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == animeId);
        if (anime is null) return;

        AnimeTitle = anime.Title;
        Initial = anime.Title[..1].ToUpperInvariant();
        CoverPath = File.Exists(anime.CoverPath) ? anime.CoverPath : null;
        var allEpisodes = anime.Seasons.SelectMany(season => season.Episodes).OrderBy(episode => episode.Season!.Number).ThenBy(episode => episode.Number).ToList();
        var watched = allEpisodes.Count(episode => episode.Status == global::AniT.Core.WatchStatus.Completed);
        var watching = allEpisodes.Count(episode => episode.Status == global::AniT.Core.WatchStatus.Watching || episode.PlaybackProgress is { PositionSeconds: > 0 });
        Summary = watching > 0
            ? $"{watching} episódio{(watching == 1 ? string.Empty : "s")} em andamento"
            : watched == 0 ? "Ainda não iniciado" : $"{watched} de {allEpisodes.Count} episódios assistidos";
        EpisodeCountLabel = $"{allEpisodes.Count} episódios";
        PlayNextLabel = allEpisodes.Any(episode => episode.Status == global::AniT.Core.WatchStatus.Watching)
            ? "▶  Continuar assistindo"
            : "▶  Assistir próximo episódio";
        Episodes.Clear();
        foreach (var episode in allEpisodes)
        {
            var progress = episode.PlaybackProgress;
            var percent = progress is { DurationSeconds: > 0 } ? progress.PositionSeconds / progress.DurationSeconds * 100 : 0;
            Episodes.Add(new EpisodeItem(episode.Id, episode.Number.ToString("00"), episode.Title ?? $"Episódio {episode.Number}", episode.Status, percent));
        }
        DataContext = null;
        DataContext = this;

        if (CoverPath is null)
        {
            CoverPath = await App.EnsureAnimeCoverAsync(anime.Id, anime.Title, anime.CoverPath);
            DataContext = null;
            DataContext = this;
        }
    }

    private async void ToggleWatched_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid episodeId }) return;
        await using var context = App.OpenFreshDatabase();
        var episode = await context.Episodes.FindAsync(episodeId);
        if (episode is null) return;
        if (episode.Status == global::AniT.Core.WatchStatus.Completed)
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
        await LoadAsync();
    }

    private async void EpisodeRow_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button) return;
        if (sender is not FrameworkElement { DataContext: EpisodeItem episode }) return;
        try
        {
            await App.PlayEpisodeAsync(episode.Id);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Não foi possível iniciar o player", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
}

public sealed record EpisodeItem(Guid Id, string Number, string Title, global::AniT.Core.WatchStatus Status, double ProgressPercent)
{
    public string StatusLabel => Status switch
    {
        global::AniT.Core.WatchStatus.Completed => "✓ Assistido",
        global::AniT.Core.WatchStatus.Watching => "▶ Continuar",
        _ => "▶ Assistir"
    };

    public string StatusColor => Status switch
    {
        global::AniT.Core.WatchStatus.Completed => "#6CDEB2",
        global::AniT.Core.WatchStatus.Watching => "#53B6FF",
        _ => "#91ABD0"
    };

    public string ProgressLabel => Status == global::AniT.Core.WatchStatus.Completed ? "Concluído" : ProgressPercent > 0 ? $"{ProgressPercent:0}% assistido" : "Não iniciado";
    public string WatchedActionLabel => Status == global::AniT.Core.WatchStatus.Completed ? "Remover assistido" : "Marcar visto";
}

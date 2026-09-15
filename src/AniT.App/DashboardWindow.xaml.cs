using Microsoft.EntityFrameworkCore;
using System.Windows;

namespace AniT.App;

public partial class DashboardWindow : Window
{
    private Guid? continueEpisodeId;
    private readonly System.Windows.Controls.Button continueButton;

    public DashboardWindow()
    {
        InitializeComponent();
        continueButton = new System.Windows.Controls.Button
        {
            Content = "▶  Continuar episódio",
            Style = (Style)Application.Current.FindResource("BrandButton"),
            MinWidth = 210,
            Height = 40,
            Padding = new Thickness(18, 8, 18, 8),
            Margin = new Thickness(0, 15, 0, 0),
            Visibility = Visibility.Collapsed
        };
        continueButton.Click += Continue_Click;
        ((System.Windows.Controls.StackPanel)ContinueText.Parent).Children.Insert(2, continueButton);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void Window_Activated(object? sender, EventArgs e) => await RefreshAsync();

    public async Task RefreshAsync()
    {
        await using var context = App.OpenFreshDatabase();
        var animeCount = await context.Anime.CountAsync();
        var episodeCount = await context.Episodes.CountAsync();
        var completedCount = await context.Episodes.CountAsync(episode => episode.Status == global::AniT.Core.WatchStatus.Completed);
        var recent = (await context.Anime
                .Select(anime => new { anime.Title, anime.CreatedAt })
                .ToListAsync())
            .OrderByDescending(anime => anime.CreatedAt)
            .Take(4)
            .Select(anime => anime.Title)
            .ToList();
        AnimeCountText.Text = animeCount.ToString();
        EpisodeCountText.Text = episodeCount.ToString();
        CompletedCountText.Text = completedCount.ToString();
        WelcomeText.Text = animeCount == 0 ? "Sua estante está pronta para começar." : "Sua estante está atualizada.";
        RecentText.Text = recent.Count == 0 ? "Nenhum anime reconhecido ainda. Você pode adicionar outra pasta em Configurar estante." : string.Join("  ·  ", recent);
        var watchingEpisodes = await context.Episodes
            .Where(episode => episode.Status == global::AniT.Core.WatchStatus.Watching)
            .Include(episode => episode.PlaybackProgress)
            .AsNoTracking()
            .ToListAsync();
        var currentEpisode = watchingEpisodes.OrderByDescending(episode => episode.PlaybackProgress?.LastPlayedAt).FirstOrDefault();
        continueEpisodeId = currentEpisode?.Id;
        continueButton.Visibility = currentEpisode is null ? Visibility.Collapsed : Visibility.Visible;
        ContinueText.Text = currentEpisode is null
            ? "Quando você começar um episódio, ele aparecerá aqui para continuar de onde parou."
            : $"Episódio {currentEpisode.Number:00} pronto para retomar de onde você parou.";
    }

    private void Library_Click(object sender, RoutedEventArgs e) => new LibraryWindow { Owner = this }.ShowDialog();

    private async void ConfigureShelf_Click(object sender, RoutedEventArgs e)
    {
        if (new SetupShelfWindow { Owner = this }.ShowDialog() is true) await RefreshAsync();
    }

    private async void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (continueEpisodeId is not { } episodeId) return;
        try
        {
            await App.PlayEpisodeAsync(episodeId);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Não foi possível continuar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

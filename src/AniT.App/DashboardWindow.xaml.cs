using Microsoft.EntityFrameworkCore;
using System.Windows;

namespace AniT.App;

public partial class DashboardWindow : Window
{
    public DashboardWindow() => InitializeComponent();

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync();

    public async Task RefreshAsync()
    {
        var animeCount = await App.Database.Anime.CountAsync();
        var episodeCount = await App.Database.Episodes.CountAsync();
        var completedCount = await App.Database.Episodes.CountAsync(episode => episode.Status == global::AniT.Core.WatchStatus.Completed);
        var recent = await App.Database.Anime.OrderByDescending(anime => anime.CreatedAt).Take(4).Select(anime => anime.Title).ToListAsync();
        AnimeCountText.Text = animeCount.ToString();
        EpisodeCountText.Text = episodeCount.ToString();
        CompletedCountText.Text = completedCount.ToString();
        WelcomeText.Text = animeCount == 0 ? "Sua estante está pronta para começar." : "Sua estante está atualizada.";
        RecentText.Text = recent.Count == 0 ? "Nenhum anime reconhecido ainda. Você pode adicionar outra pasta em Configurar estante." : string.Join("  ·  ", recent);
        ContinueText.Text = await App.Database.Episodes.AnyAsync(episode => episode.Status == global::AniT.Core.WatchStatus.Watching)
            ? "Abra a Biblioteca e continue seu episódio em andamento."
            : "Quando você começar um episódio, ele aparecerá aqui para continuar de onde parou.";
    }

    private void Library_Click(object sender, RoutedEventArgs e) => new LibraryWindow { Owner = this }.ShowDialog();

    private async void ConfigureShelf_Click(object sender, RoutedEventArgs e)
    {
        if (new SetupShelfWindow { Owner = this }.ShowDialog() is true) await RefreshAsync();
    }
}

using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Windows;

namespace AniT.App;

public partial class ReviewMatchWindow : Window
{
    private readonly Guid reviewItemId;

    public ReviewMatchWindow(Guid reviewItemId)
    {
        this.reviewItemId = reviewItemId;
        InitializeComponent();
        ResponsiveWindow.FitToWorkArea(this, 660, 570);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await using var context = App.OpenFreshDatabase();
        var review = await context.LibraryReviewItems.AsNoTracking().SingleOrDefaultAsync(item => item.Id == reviewItemId);
        if (review is null) { Close(); return; }
        var anime = await context.Anime.AsNoTracking().OrderBy(item => item.Title).Select(item => new AnimeChoice(item.Id, item.Title)).ToListAsync();
        AnimeSelector.ItemsSource = anime;
        AnimeSelector.SelectedItem = anime.FirstOrDefault(item => item.Id == review.SuggestedAnimeId) ?? anime.FirstOrDefault();
        FileNameText.Text = review.FileName;
        NewAnimeTitleText.Text = anime.Count == 0 ? review.CandidateTitle ?? string.Empty : string.Empty;
        SeasonText.Text = (review.SuggestedSeasonNumber ?? 1).ToString(CultureInfo.InvariantCulture);
        EpisodeText.Text = review.SuggestedEpisodeNumber is { } number && Math.Abs(number % 1) < 0.0001
            ? Convert.ToInt32(number).ToString(CultureInfo.InvariantCulture)
            : "1";
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(SeasonText.Text, out var season) || season < 0
            || !int.TryParse(EpisodeText.Text, out var episode) || episode <= 0)
        {
            StatusText.Text = "Informe temporada e episódio com números válidos.";
            return;
        }

        await using var context = App.OpenFreshDatabase();
        Guid animeId;
        if (!string.IsNullOrWhiteSpace(NewAnimeTitleText.Text))
        {
            var title = NewAnimeTitleText.Text.Trim();
            var anime = new global::AniT.Core.Anime { Title = title };
            anime.Aliases.Add(new global::AniT.Core.AnimeAlias
            {
                AnimeId = anime.Id,
                Alias = title,
                NormalizedAlias = global::AniT.Core.AnimeTitleNormalizer.Normalize(title),
                Source = global::AniT.Core.AnimeAliasSource.Manual
            });
            context.Anime.Add(anime);
            await context.SaveChangesAsync();
            animeId = anime.Id;
        }
        else if (AnimeSelector.SelectedItem is AnimeChoice selected)
        {
            animeId = selected.Id;
        }
        else
        {
            StatusText.Text = "Escolha um anime ou informe um novo título.";
            return;
        }

        try
        {
            await new global::AniT.Infrastructure.LibraryReviewService(context).ResolveAsync(
                reviewItemId, animeId, season, episode, LearnAliasCheck.IsChecked == true);
            DialogResult = true;
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

public sealed record AnimeChoice(Guid Id, string Title);

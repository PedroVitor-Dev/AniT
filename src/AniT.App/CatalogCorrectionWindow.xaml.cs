using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace AniT.App;

public partial class CatalogCorrectionWindow : Window
{
    public ObservableCollection<CorrectionAnimeChoice> AnimeChoices { get; } = [];
    public ObservableCollection<CorrectionEpisodeChoice> SplitEpisodes { get; } = [];

    public CatalogCorrectionWindow()
    {
        InitializeComponent();
        ResponsiveWindow.FitToWorkArea(this, 860, 670);
        DataContext = this;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await ReloadAnimeAsync();

    private async Task ReloadAnimeAsync()
    {
        await using var context = App.OpenFreshDatabase();
        var choices = await context.Anime.AsNoTracking().OrderBy(anime => anime.Title)
            .Select(anime => new CorrectionAnimeChoice(anime.Id, anime.Title)).ToListAsync();
        AnimeChoices.Clear();
        foreach (var choice in choices) AnimeChoices.Add(choice);
        MergeSourceBox.ItemsSource = AnimeChoices;
        MergeTargetBox.ItemsSource = AnimeChoices;
        SplitSourceBox.ItemsSource = AnimeChoices;
        MergeSourceBox.SelectedIndex = choices.Count > 0 ? 0 : -1;
        MergeTargetBox.SelectedIndex = choices.Count > 1 ? 1 : -1;
        SplitSourceBox.SelectedIndex = choices.Count > 0 ? 0 : -1;
    }

    private async void Merge_Click(object sender, RoutedEventArgs e)
    {
        if (MergeSourceBox.SelectedItem is not CorrectionAnimeChoice source || MergeTargetBox.SelectedItem is not CorrectionAnimeChoice target)
        { StatusText.Text = "Escolha a origem e o destino."; return; }
        if (source.Id == target.Id) { StatusText.Text = "Origem e destino precisam ser diferentes."; return; }
        if (MessageBox.Show($"Mesclar “{source.Title}” dentro de “{target.Title}”?\n\nO título de origem será removido após a transferência.", "Confirmar mesclagem", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            await using var context = App.OpenFreshDatabase();
            await new global::AniT.Infrastructure.CatalogCorrectionService(context).MergeAnimeAsync(source.Id, target.Id);
            StatusText.Text = "✓ Títulos mesclados e histórico preservado.";
            await ReloadAnimeAsync();
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private async void SplitSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SplitEpisodes.Clear();
        if (SplitSourceBox.SelectedItem is not CorrectionAnimeChoice source) return;
        await using var context = App.OpenFreshDatabase();
        var settings = global::AniT.Infrastructure.AniTSystemSettingsStore.Load();
        var episodes = await context.Episodes.AsNoTracking().Where(episode => episode.Season!.AnimeId == source.Id)
            .Select(episode => new { episode.Id, episode.Number, Season = episode.Season!.Number }).OrderBy(item => item.Season).ThenBy(item => item.Number).ToListAsync();
        foreach (var episode in episodes)
            SplitEpisodes.Add(new CorrectionEpisodeChoice(episode.Id, $"{(global::AniT.Infrastructure.OrganizationPreferences.EpisodeCode(episode.Season, episode.Number, settings.EpisodeNumberDisplayFormat))} · {source.Title}"));
    }

    private async void Split_Click(object sender, RoutedEventArgs e)
    {
        if (SplitSourceBox.SelectedItem is not CorrectionAnimeChoice source) { StatusText.Text = "Escolha o título original."; return; }
        var selected = SplitEpisodes.Where(episode => episode.IsSelected).Select(episode => episode.Id).ToArray();
        if (selected.Length == 0) { StatusText.Text = "Selecione ao menos um episódio."; return; }
        if (MessageBox.Show($"Criar “{NewTitleBox.Text.Trim()}” com {selected.Length} episódio(s) retirados de “{source.Title}”?", "Confirmar separação", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            await using var context = App.OpenFreshDatabase();
            await new global::AniT.Infrastructure.CatalogCorrectionService(context).SplitEpisodesAsync(source.Id, selected, NewTitleBox.Text);
            StatusText.Text = "✓ Novo título criado; arquivos e progresso foram mantidos.";
            NewTitleBox.Clear();
            await ReloadAnimeAsync();
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed record CorrectionAnimeChoice(Guid Id, string Title);

public sealed class CorrectionEpisodeChoice(Guid id, string label) : INotifyPropertyChanged
{
    private bool isSelected;
    public Guid Id { get; } = id;
    public string Label { get; } = label;
    public bool IsSelected { get => isSelected; set { if (isSelected == value) return; isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

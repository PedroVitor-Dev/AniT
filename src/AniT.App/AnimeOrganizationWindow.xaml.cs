using AniT.Core;
using AniT.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.Windows;
using System.Windows.Controls;

namespace AniT.App;

public partial class AnimeOrganizationWindow : Window
{
    private readonly Guid animeId;
    private readonly List<CheckBox> tagBoxes = [];
    private readonly List<CheckBox> collectionBoxes = [];

    public AnimeOrganizationWindow(Guid animeId)
    {
        this.animeId = animeId;
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await using var context = App.OpenFreshDatabase();
        var anime = await context.Anime.AsNoTracking().SingleOrDefaultAsync(item => item.Id == animeId);
        if (anime is null)
        {
            MessageBox.Show("Este anime não está mais disponível na Biblioteca.", "AniT", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
            return;
        }

        var settings = AniTSystemSettingsStore.Load();
        Populate(TagsPanel, tagBoxes, settings.CustomTags ?? [], AnimeOrganizationLabels.Parse(anime.CustomTags));
        Populate(CollectionsPanel, collectionBoxes, settings.UserCollections ?? [], AnimeOrganizationLabels.Parse(anime.UserCollections));
        NoTagsText.Visibility = tagBoxes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoCollectionsText.Visibility = collectionBoxes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void Populate(Panel panel, ICollection<CheckBox> destination, IEnumerable<string> options, IReadOnlyCollection<string> selected)
    {
        panel.Children.Clear();
        destination.Clear();
        foreach (var option in options.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.CurrentCultureIgnoreCase))
        {
            var box = new CheckBox
            {
                Content = option.Trim(),
                Tag = option.Trim(),
                IsChecked = selected.Contains(option.Trim(), StringComparer.CurrentCultureIgnoreCase),
                Margin = new Thickness(0, 0, 22, 12),
                MinWidth = 135
            };
            destination.Add(box);
            panel.Children.Add(box);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            IsEnabled = false;
            await using var context = App.OpenFreshDatabase();
            var anime = await context.Anime.SingleOrDefaultAsync(item => item.Id == animeId);
            if (anime is null) return;
            anime.CustomTags = AnimeOrganizationLabels.Serialize(Selected(tagBoxes));
            anime.UserCollections = AnimeOrganizationLabels.Serialize(Selected(collectionBoxes));
            await context.SaveChangesAsync();
            DialogResult = true;
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Não foi possível salvar a organização: {exception.Message}", "AniT", MessageBoxButton.OK, MessageBoxImage.Warning);
            IsEnabled = true;
        }
    }

    private static IEnumerable<string> Selected(IEnumerable<CheckBox> boxes) =>
        boxes.Where(box => box.IsChecked == true).Select(box => box.Tag?.ToString()).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>();
}

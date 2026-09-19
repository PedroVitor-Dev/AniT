using AniT.Infrastructure;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace AniT.App;

public partial class SystemSettingsWindow : Window
{
    public ObservableCollection<SystemImageSourceItem> ImageSources { get; } = [];

    public SystemSettingsWindow()
    {
        InitializeComponent();
        ResponsiveWindow.FitToWorkArea(this, 1380, 860);
        var settings = AniTSystemSettingsStore.Load();
        BannerIntervalSlider.Value = settings.HomeBannerIntervalSeconds;
        foreach (var source in settings.ImageSources) ImageSources.Add(SystemImageSourceItem.From(source));
        DataContext = this;
    }

    private void BannerIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BannerIntervalText is not null) BannerIntervalText.Text = $"{Math.Round(e.NewValue):0} segundos";
        SaveStatusText?.ClearValue(TextBlock.TextProperty);
    }

    private void AddSource_Click(object sender, RoutedEventArgs e)
    {
        var source = new CustomImageSourceSettings(
            Guid.NewGuid(),
            SourceNameBox.Text.Trim(),
            SourceUrlBox.Text.Trim(),
            UseForBannersBox.IsChecked == true,
            UseForCoversBox.IsChecked == true);
        if (!AniTSystemSettingsStore.TryValidateSource(source, out var error))
        {
            ValidationText.Text = error;
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        ImageSources.Add(SystemImageSourceItem.From(source));
        SourceNameBox.Clear();
        SourceUrlBox.Clear();
        UseForBannersBox.IsChecked = true;
        UseForCoversBox.IsChecked = false;
        ValidationText.Visibility = Visibility.Collapsed;
        SaveStatusText.Text = "Fonte adicionada. Salve para aplicar.";
    }

    private void RemoveSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid id })
        {
            var source = ImageSources.FirstOrDefault(item => item.Id == id);
            if (source is not null) ImageSources.Remove(source);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var sources = ImageSources.Select(item => item.ToSettings()).ToArray();
        foreach (var source in sources)
        {
            if (AniTSystemSettingsStore.TryValidateSource(source, out var error)) continue;
            ValidationText.Text = $"{source.Name}: {error}";
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        AniTSystemSettingsStore.Save(new AniTSystemSettings((int)Math.Round(BannerIntervalSlider.Value), sources));
        ValidationText.Visibility = Visibility.Collapsed;
        SaveStatusText.Text = "✓ Configurações salvas e prontas para o próximo uso.";
    }

    private void Home_Click(object sender, RoutedEventArgs e) => AppNavigation.Home(this);
    private void Explore_Click(object sender, RoutedEventArgs e) => AppNavigation.Explore(this);
    private void Library_Click(object sender, RoutedEventArgs e) => AppNavigation.OpenLibrary(this);
    private void Calendar_Click(object sender, RoutedEventArgs e) => AppNavigation.Calendar(this);
    private void History_Click(object sender, RoutedEventArgs e) => AppNavigation.History(this);
    private void Achievements_Click(object sender, RoutedEventArgs e) => AppNavigation.Achievements(this);
    private void Profile_Click(object sender, RoutedEventArgs e) => AppNavigation.Profile(this);
    private void ConfigureShelf_Click(object sender, RoutedEventArgs e) => new SetupShelfWindow { Owner = this }.ShowDialog();
}

public sealed class SystemImageSourceItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string SearchUrlTemplate { get; init; } = string.Empty;
    public bool UseForProfileBanners { get; set; }
    public bool UseForAnimeCovers { get; set; }
    public bool IsEnabled { get; set; }

    public static SystemImageSourceItem From(CustomImageSourceSettings settings) => new()
    {
        Id = settings.Id,
        Name = settings.Name,
        SearchUrlTemplate = settings.SearchUrlTemplate,
        UseForProfileBanners = settings.UseForProfileBanners,
        UseForAnimeCovers = settings.UseForAnimeCovers,
        IsEnabled = settings.IsEnabled
    };

    public CustomImageSourceSettings ToSettings() => new(
        Id,
        Name,
        SearchUrlTemplate,
        UseForProfileBanners,
        UseForAnimeCovers,
        IsEnabled);
}

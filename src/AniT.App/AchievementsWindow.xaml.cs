using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AniT.Core.Achievements;

namespace AniT.App;

public partial class AchievementsWindow : Window, INotifyPropertyChanged
{
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");
    private IReadOnlyList<AchievementProgress> all = [];

    public ObservableCollection<AchievementCardView> VisibleAchievements { get; } = [];
    public string UnlockedLabel { get; private set; } = "0 / 100 desbloqueadas";
    public string AniPointsLabel { get; private set; } = "0 AniPoints";
    public double OverallPercent { get; private set; }
    public string ChainSummary { get; private set; } = "Carregando suas jornadas…";
    public event PropertyChangedEventHandler? PropertyChanged;

    public AchievementsWindow()
    {
        InitializeComponent();
        DataContext = this;
        CategoryFilter.ItemsSource = new[] { new FilterOption<AchievementCategory?>("Todas as categorias", null) }
            .Concat(Enum.GetValues<AchievementCategory>().Select(item => new FilterOption<AchievementCategory?>(AchievementLabels.Category(item), item)));
        RarityFilter.ItemsSource = new[] { new FilterOption<AchievementRarity?>("Todas as raridades", null) }
            .Concat(Enum.GetValues<AchievementRarity>().Select(item => new FilterOption<AchievementRarity?>(AchievementLabels.Rarity(item), item)));
        StatusFilter.SelectedIndex = CategoryFilter.SelectedIndex = RarityFilter.SelectedIndex = SortFilter.SelectedIndex = 0;
#if DEBUG
        var debugButton = new Button { Content = "🧪  Debug", Style = (Style)FindResource("HomeSecondaryButton"), Margin = new Thickness(0, 0, 10, 0) };
        debugButton.Click += (_, _) => new AchievementDebugWindow { Owner = this }.ShowDialog();
        HeaderActions.Children.Insert(0, debugButton);
#endif
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await LoadAsync();

    private async Task LoadAsync()
    {
        all = await App.Achievements.GetProgressAsync();
        var unlocked = all.Count(item => item.IsUnlocked);
        var points = all.Where(item => item.IsUnlocked).Sum(item => item.Points);
        UnlockedLabel = $"{unlocked} / {AchievementCatalog.All.Count} desbloqueadas";
        AniPointsLabel = $"{points.ToString("N0", Portuguese)} AniPoints";
        OverallPercent = unlocked * 100d / AchievementCatalog.All.Count;
        ChainSummary = BuildChainSummary();
        Raise(nameof(UnlockedLabel), nameof(AniPointsLabel), nameof(OverallPercent), nameof(ChainSummary));
        ApplyFilters();
    }

    private string BuildChainSummary()
    {
        var summaries = new List<string>();
        foreach (var chain in AchievementCatalog.Chains.Where(item => item.Code is "STREAK" or "LIBRARY" or "RATINGS" or "WATCH_TIME" or "BACKUPS"))
        {
            var next = chain.Milestones.FirstOrDefault(definition => !all.Single(item => item.Definition.Id == definition.Id).IsUnlocked);
            summaries.Add(next is null ? $"{chain.Name}: concluída ✓" : $"{chain.Name}: próxima — {next.Name}");
        }
        return string.Join("   •   ", summaries);
    }

    private void ApplyFilters()
    {
        if (StatusFilter is null || CategoryFilter is null || RarityFilter is null || SortFilter is null) return;
        IEnumerable<AchievementProgress> query = all;
        query = StatusFilter.SelectedIndex switch
        {
            1 => query.Where(item => item.IsUnlocked),
            2 => query.Where(item => !item.IsUnlocked),
            3 => query.Where(item => !item.IsUnlocked && item.CurrentValue > 0),
            4 => query.Where(item => item.Definition.IsSecret),
            _ => query
        };
        if (CategoryFilter.SelectedItem is FilterOption<AchievementCategory?> { Value: { } category })
            query = query.Where(item => item.Definition.Category == category);
        if (RarityFilter.SelectedItem is FilterOption<AchievementRarity?> { Value: { } rarity })
            query = query.Where(item => item.Definition.Rarity == rarity);
        query = SortFilter.SelectedIndex switch
        {
            1 => query.OrderByDescending(item => item.Definition.Rarity).ThenBy(item => item.Definition.Id),
            2 => query.OrderByDescending(item => item.Percent).ThenBy(item => item.Definition.Id),
            3 => query.OrderByDescending(item => item.UnlockedAt ?? DateTimeOffset.MinValue).ThenBy(item => item.Definition.Id),
            _ => query.OrderBy(item => item.Definition.Id)
        };

        VisibleAchievements.Clear();
        foreach (var item in query) VisibleAchievements.Add(AchievementCardView.Create(item));
        EmptyState.Visibility = VisibleAchievements.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilters();

    private async void Recalculate_Click(object sender, RoutedEventArgs e)
    {
        await App.Achievements.RecalculateAsync();
        await LoadAsync();
    }

    private void Achievement_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int id }) return;
        var progress = all.Single(item => item.Definition.Id == id);
        new AchievementDetailWindow(progress) { Owner = this }.ShowDialog();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => new AchievementSettingsWindow { Owner = this }.ShowDialog();
    private void Home_Click(object sender, RoutedEventArgs e) => AppNavigation.Home(this);
    private void Raise(params string[] names)
    {
        foreach (var name in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed record FilterOption<T>(string Label, T Value);

public sealed record AchievementCardView(
    int Id,
    string IdLabel,
    string DisplayName,
    string DisplayDescription,
    string IconPath,
    Visibility IconVisibility,
    Visibility LockedVisibility,
    string RarityLabel,
    string ProgressLabel,
    string PercentLabel,
    double ProgressPercent,
    string StatusLabel,
    string ChainLabel,
    Visibility ChainVisibility,
    Brush AccentBrush,
    Brush RarityBackground,
    Brush CardBackground,
    Color GlowColor)
{
    public static AchievementCardView Create(AchievementProgress progress)
    {
        var definition = progress.Definition;
        var hidden = definition.IsHiddenUntilUnlocked && !progress.IsUnlocked;
        var accent = Accent(definition.Rarity);
        return new(
            definition.Id,
            $"#{definition.Id:000}",
            hidden ? "???" : definition.Name,
            hidden ? "Conquista secreta" : definition.Description,
            hidden ? string.Empty : definition.IconPath,
            hidden ? Visibility.Collapsed : Visibility.Visible,
            hidden ? Visibility.Visible : Visibility.Collapsed,
            hidden ? "SECRETA" : AchievementLabels.Rarity(definition.Rarity).ToUpper(Portuguese),
            FormatProgress(progress),
            $"{progress.Percent:0}%",
            progress.Percent,
            progress.IsUnlocked ? $"✓ DESBLOQUEADA  {progress.UnlockedAt?.LocalDateTime:dd/MM/yyyy}" : progress.CurrentValue > 0 ? "EM PROGRESSO" : "BLOQUEADA",
            definition.ChainCode is null ? string.Empty : $"CADEIA  •  {definition.ChainCode.Replace('_', ' ')}",
            definition.ChainCode is null ? Visibility.Collapsed : Visibility.Visible,
            new SolidColorBrush(accent),
            new SolidColorBrush(Color.FromArgb(60, accent.R, accent.G, accent.B)),
            new SolidColorBrush(Color.FromArgb(240, 7, 25, 50)),
            accent);
    }

    public static string FormatProgress(AchievementProgress progress)
    {
        if (progress.Definition.ProgressType == AchievementProgressType.DurationSeconds)
            return $"{progress.DisplayValue / 3600d:0.#} / {progress.Definition.TargetValue / 3600d:0.#} h";
        var unit = string.IsNullOrWhiteSpace(progress.Definition.Unit) ? string.Empty : $" {progress.Definition.Unit}";
        return $"{progress.DisplayValue:N0} / {progress.Definition.TargetValue:N0}{unit}";
    }

    public static Color Accent(AchievementRarity rarity) => rarity switch
    {
        AchievementRarity.Uncommon => Color.FromRgb(83, 222, 166),
        AchievementRarity.Rare => Color.FromRgb(73, 198, 255),
        AchievementRarity.Epic => Color.FromRgb(180, 124, 255),
        AchievementRarity.Secret => Color.FromRgb(130, 111, 201),
        AchievementRarity.Legendary => Color.FromRgb(255, 205, 79),
        AchievementRarity.SupremeLegendary => Color.FromRgb(255, 224, 92),
        _ => Color.FromRgb(147, 180, 210)
    };

    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");
}

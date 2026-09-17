using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AniT.App;

public partial class HistoryWindow : Window, INotifyPropertyChanged
{
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");
    private readonly List<HistoryActivityRecord> activities = [];
    private LibraryWindow? libraryWindow;
    private ExploreWindow? exploreWindow;
    private CalendarWindow? calendarWindow;
    private HistoryPeriod period = HistoryPeriod.Today;
    private bool isLoading;

    public ObservableCollection<HistoryDayGroup> HistoryGroups { get; } = [];
    public ObservableCollection<string> AnimeFilters { get; } = ["Todos os animes"];
    public string TotalEpisodesLabel { get; private set; } = "0";
    public string WeekEpisodesLabel { get; private set; } = "Nenhum esta semana";
    public string TotalHoursLabel { get; private set; } = "0m";
    public string WeekHoursLabel { get; private set; } = "Nenhum esta semana";
    public string StreakLabel { get; private set; } = "0 dias";
    public string LatestSessionLabel { get; private set; } = "Nenhuma";
    public string LatestSessionDetail { get; private set; } = "Comece uma nova história";
    public string PeriodLabel { get; private set; } = "Hoje";
    public string PeriodEpisodesLabel { get; private set; } = "0";
    public string PeriodHoursLabel { get; private set; } = "0m";
    public string ActiveDaysLabel { get; private set; } = "0";
    public string UniqueAnimeLabel { get; private set; } = "0";
    public string HighlightCoverPath { get; private set; } = "Assets/History/2.png";
    public string HighlightTitle { get; private set; } = "Nenhum destaque ainda";
    public string HighlightSubtitle { get; private set; } = "Seu anime mais visto aparecerá aqui";
    public double HighlightPercent { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public HistoryWindow()
    {
        InitializeComponent();
        ResponsiveWindow.FitToWorkArea(this, 1380, 860);
        DataContext = this;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateResponsiveLayout(ActualWidth);
        UpdatePeriodButtons();
        await LoadAsync();
    }

    private async void Window_Activated(object? sender, EventArgs e)
    {
        await Task.Delay(250);
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (isLoading) return;
        isLoading = true;
        try
        {
            await using var context = App.OpenFreshDatabase();
            var episodes = await context.Episodes
                .Include(item => item.Season)!
                .ThenInclude(season => season!.Anime)
                .Include(item => item.PlaybackProgress)
                .AsNoTracking()
                .ToListAsync();

            activities.Clear();
            foreach (var episode in episodes)
            {
                var activityAt = episode.WatchedAt ?? episode.PlaybackProgress?.LastPlayedAt;
                if (activityAt is null || episode.Season?.Anime is not { } anime) continue;

                var duration = Math.Max(0, episode.PlaybackProgress?.DurationSeconds ?? 0);
                var position = episode.Status == global::AniT.Core.WatchStatus.Completed && duration > 0
                    ? duration
                    : Math.Max(0, episode.PlaybackProgress?.PositionSeconds ?? 0);
                var percent = episode.Status == global::AniT.Core.WatchStatus.Completed
                    ? 100
                    : duration > 0 ? Math.Clamp(position / duration * 100, 0, 100) : 0;
                var watchedSeconds = position > 0 ? position : episode.Status == global::AniT.Core.WatchStatus.Completed ? 24 * 60 : 0;

                activities.Add(new HistoryActivityRecord(
                    activityAt.Value.LocalDateTime,
                    anime.Id,
                    episode.Id,
                    anime.Title,
                    episode.Number,
                    episode.Title ?? $"Episódio {episode.Number:00}",
                    IsUsableCover(anime.CoverPath) ? anime.CoverPath! : "Assets/History/2.png",
                    percent,
                    watchedSeconds,
                    duration,
                    episode.Rating,
                    episode.ReviewNotes,
                    episode.Status));
            }

            var selectedAnime = AnimeFilter.SelectedItem as string;
            AnimeFilters.Clear();
            AnimeFilters.Add("Todos os animes");
            foreach (var title in activities.Select(item => item.AnimeTitle).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(title => title, StringComparer.CurrentCultureIgnoreCase))
                AnimeFilters.Add(title);
            AnimeFilter.SelectedItem = AnimeFilters.Contains(selectedAnime ?? string.Empty) ? selectedAnime : AnimeFilters[0];

            BuildOverview();
            ApplyFilters();
        }
        finally
        {
            isLoading = false;
        }
    }

    private void BuildOverview()
    {
        TotalEpisodesLabel = activities.Count.ToString();
        TotalHoursLabel = FormatDuration(activities.Sum(item => item.WatchedSeconds));

        var weekStart = DateTime.Today.AddDays(-6);
        var weekItems = activities.Where(item => item.ActivityAt.Date >= weekStart).ToList();
        WeekEpisodesLabel = weekItems.Count == 0 ? "Nenhum esta semana" : $"+{weekItems.Count} esta semana";
        WeekHoursLabel = weekItems.Count == 0 ? "Nenhum esta semana" : $"+{FormatDuration(weekItems.Sum(item => item.WatchedSeconds))} esta semana";

        var watchedDates = activities.Select(item => item.ActivityAt.Date).Distinct().OrderByDescending(date => date).ToList();
        var streak = 0;
        if (watchedDates.Count > 0 && watchedDates[0] >= DateTime.Today.AddDays(-1))
        {
            var cursor = watchedDates[0];
            foreach (var date in watchedDates)
            {
                if (date != cursor) break;
                streak++;
                cursor = cursor.AddDays(-1);
            }
        }
        StreakLabel = $"{streak} dia{(streak == 1 ? string.Empty : "s")}";

        var latest = activities.OrderByDescending(item => item.ActivityAt).FirstOrDefault();
        if (latest is not null)
        {
            LatestSessionLabel = latest.ActivityAt.Date == DateTime.Today
                ? $"Hoje, {latest.ActivityAt:HH:mm}"
                : $"{latest.ActivityAt:dd/MM}, {latest.ActivityAt:HH:mm}";
            var latestCount = activities.Count(item => item.ActivityAt.Date == latest.ActivityAt.Date);
            LatestSessionDetail = $"{latestCount} episódio{(latestCount == 1 ? string.Empty : "s")} registrado{(latestCount == 1 ? string.Empty : "s")}";
        }
        else
        {
            LatestSessionLabel = "Nenhuma";
            LatestSessionDetail = "Comece uma nova história";
        }

        Raise(nameof(TotalEpisodesLabel), nameof(TotalHoursLabel), nameof(WeekEpisodesLabel), nameof(WeekHoursLabel), nameof(StreakLabel), nameof(LatestSessionLabel), nameof(LatestSessionDetail));
    }

    private void ApplyFilters()
    {
        if (HistorySearchBox is null || AnimeFilter is null || OrderFilter is null) return;
        IEnumerable<HistoryActivityRecord> filtered = activities;
        var today = DateTime.Today;
        filtered = period switch
        {
            HistoryPeriod.Today => filtered.Where(item => item.ActivityAt.Date == today),
            HistoryPeriod.Week => filtered.Where(item => item.ActivityAt.Date >= today.AddDays(-6)),
            HistoryPeriod.Month => filtered.Where(item => item.ActivityAt.Year == today.Year && item.ActivityAt.Month == today.Month),
            _ => filtered
        };

        var query = HistorySearchBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(query))
            filtered = filtered.Where(item => item.AnimeTitle.Contains(query, StringComparison.CurrentCultureIgnoreCase) || item.EpisodeTitle.Contains(query, StringComparison.CurrentCultureIgnoreCase) || (item.ReviewNotes?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false));

        if (AnimeFilter.SelectedItem is string anime && !string.Equals(anime, "Todos os animes", StringComparison.Ordinal))
            filtered = filtered.Where(item => string.Equals(item.AnimeTitle, anime, StringComparison.CurrentCultureIgnoreCase));

        var items = filtered.ToList();
        items = OrderFilter.SelectedIndex switch
        {
            1 => items.OrderBy(item => item.ActivityAt).ToList(),
            2 => items.OrderByDescending(item => NormalizeRating(item.Rating)).ThenByDescending(item => item.ActivityAt).ToList(),
            3 => items.OrderByDescending(item => item.ProgressPercent).ThenByDescending(item => item.ActivityAt).ToList(),
            _ => items.OrderByDescending(item => item.ActivityAt).ToList()
        };

        HistoryGroups.Clear();
        var grouped = items.GroupBy(item => item.ActivityAt.Date);
        if (OrderFilter.SelectedIndex == 1) grouped = grouped.OrderBy(group => group.Key);
        else grouped = grouped.OrderByDescending(group => group.Key);
        foreach (var group in grouped)
        {
            var rows = group.Select(CreateActivityItem).ToList();
            HistoryGroups.Add(new HistoryDayGroup(
                DayTitle(group.Key),
                $"{rows.Count} episódio{(rows.Count == 1 ? string.Empty : "s")} · {FormatDuration(group.Sum(item => item.WatchedSeconds))}",
                group.Key == today ? "#57DCFF" : group.Key == today.AddDays(-1) ? "#F4B445" : "#4A9FE5",
                rows));
        }

        BuildPeriodSummary(items);
        HistoryEmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildPeriodSummary(IReadOnlyList<HistoryActivityRecord> items)
    {
        PeriodLabel = period switch { HistoryPeriod.Today => "Hoje", HistoryPeriod.Week => "Esta semana", HistoryPeriod.Month => "Este mês", _ => "Todo o período" };
        PeriodEpisodesLabel = items.Count.ToString();
        PeriodHoursLabel = FormatDuration(items.Sum(item => item.WatchedSeconds));
        ActiveDaysLabel = items.Select(item => item.ActivityAt.Date).Distinct().Count().ToString();
        UniqueAnimeLabel = items.Select(item => item.AnimeId).Distinct().Count().ToString();

        var highlight = items.GroupBy(item => item.AnimeId)
            .Select(group => new { Items = group.ToList(), Count = group.Count(), Latest = group.Max(item => item.ActivityAt) })
            .OrderByDescending(item => item.Count)
            .ThenByDescending(item => item.Latest)
            .FirstOrDefault();
        if (highlight is not null)
        {
            var latest = highlight.Items.OrderByDescending(item => item.ActivityAt).First();
            HighlightCoverPath = latest.CoverPath;
            HighlightTitle = latest.AnimeTitle;
            HighlightSubtitle = $"{highlight.Count} episódio{(highlight.Count == 1 ? string.Empty : "s")} neste período";
            HighlightPercent = items.Count == 0 ? 0 : highlight.Count * 100d / items.Count;
        }
        else
        {
            HighlightCoverPath = "Assets/History/2.png";
            HighlightTitle = "Nenhum destaque ainda";
            HighlightSubtitle = "Seu anime mais visto aparecerá aqui";
            HighlightPercent = 0;
        }

        Raise(nameof(PeriodLabel), nameof(PeriodEpisodesLabel), nameof(PeriodHoursLabel), nameof(ActiveDaysLabel), nameof(UniqueAnimeLabel), nameof(HighlightCoverPath), nameof(HighlightTitle), nameof(HighlightSubtitle), nameof(HighlightPercent));
    }

    private static HistoryActivityItem CreateActivityItem(HistoryActivityRecord item)
    {
        var rating = NormalizeRating(item.Rating);
        var sessionLength = Math.Clamp(item.WatchedSeconds, 0, 6 * 60 * 60);
        var startedAt = item.ActivityAt.AddSeconds(-sessionLength);
        return new HistoryActivityItem(
            item.AnimeId,
            item.EpisodeId,
            item.AnimeTitle,
            $"Episódio {item.EpisodeNumber:00}",
            item.EpisodeTitle,
            item.CoverPath,
            $"{startedAt:HH:mm} – {item.ActivityAt:HH:mm}",
            FormatDuration(item.WatchedSeconds),
            item.ProgressPercent,
            RatingIcon(rating),
            RatingLabel(rating),
            RatingBackground(rating),
            RatingBorder(rating),
            string.IsNullOrWhiteSpace(item.ReviewNotes) ? "Adicionar comentário…" : item.ReviewNotes!);
    }

    private void Period_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !Enum.TryParse(tag, out HistoryPeriod selected)) return;
        period = selected;
        UpdatePeriodButtons();
        ApplyFilters();
    }

    private void UpdatePeriodButtons()
    {
        if (TodayButton is null) return;
        foreach (var (button, value) in new[] { (TodayButton, HistoryPeriod.Today), (WeekButton, HistoryPeriod.Week), (MonthButton, HistoryPeriod.Month), (AllButton, HistoryPeriod.All) })
        {
            var active = value == period;
            button.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#1A5F9E" : "#091F3E"));
            button.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#67DFFF" : "#2B6A9F"));
            button.Foreground = active ? Brushes.White : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BFD8F1"));
        }
    }

    private void HistorySearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (HistorySearchHint is not null) HistorySearchHint.Visibility = string.IsNullOrEmpty(HistorySearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (!isLoading) ApplyFilters();
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e) { if (!isLoading) ApplyFilters(); }

    private void GlobalSearchBox_TextChanged(object sender, TextChangedEventArgs e) => GlobalSearchHint.Visibility = string.IsNullOrEmpty(GlobalSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    private void GlobalSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        HistorySearchBox.Text = GlobalSearchBox.Text;
        HistorySearchBox.Focus();
    }

    private void HistoryRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindVisualAncestor<ButtonBase>(source) is not null) return;
        if (sender is Border { Tag: Guid animeId }) new AnimeDetailsWindow(animeId) { Owner = this }.ShowDialog();
    }

    private async void EditComment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid animeId }) return;
        if (new EpisodeCommentWindow(animeId) { Owner = this }.ShowDialog() == true) await LoadAsync();
    }

    private async void Continue_Click(object sender, RoutedEventArgs e)
    {
        var episodeId = activities.OrderByDescending(item => item.ActivityAt).FirstOrDefault()?.EpisodeId;
        if (episodeId is not Guid id) return;
        try { await App.PlayEpisodeAsync(id); }
        catch (Exception exception) { MessageBox.Show(exception.Message, "Não foi possível continuar", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void Library_Click(object sender, RoutedEventArgs e)
    {
        if (libraryWindow is { IsLoaded: true }) { libraryWindow.Activate(); return; }
        libraryWindow = new LibraryWindow { Owner = this };
        libraryWindow.Closed += (_, _) => libraryWindow = null;
        libraryWindow.Show();
    }

    private void Explore_Click(object sender, RoutedEventArgs e)
    {
        if (exploreWindow is { IsLoaded: true }) { exploreWindow.Activate(); return; }
        exploreWindow = new ExploreWindow { Owner = this };
        exploreWindow.Closed += (_, _) => exploreWindow = null;
        exploreWindow.Show();
    }

    private void Calendar_Click(object sender, RoutedEventArgs e)
    {
        if (calendarWindow is { IsLoaded: true }) { calendarWindow.Activate(); return; }
        calendarWindow = new CalendarWindow { Owner = this };
        calendarWindow.Closed += (_, _) => calendarWindow = null;
        calendarWindow.Show();
    }

    private void Home_Click(object sender, RoutedEventArgs e) => Close();

    private void RoundedPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not Border border || border.ActualWidth <= 0 || border.ActualHeight <= 0) return;
        var radius = double.TryParse(border.Tag?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedRadius) ? parsedRadius : 16;
        border.Clip = new RectangleGeometry(new Rect(0, 0, border.ActualWidth, border.ActualHeight), radius, radius);
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsiveLayout(e.NewSize.Width);

    private void UpdateResponsiveLayout(double width)
    {
        if (SidebarColumn is null || RightRailColumn is null || HistoryContentHost is null) return;
        if (width < 1320)
        {
            SidebarColumn.Width = new GridLength(184); RightRailColumn.Width = new GridLength(0); RightRail.Visibility = Visibility.Collapsed;
            HistoryContentHost.Margin = new Thickness(18, 0, 18, 36); SearchContainer.MaxWidth = 350; LibraryTopButton.Visibility = Visibility.Collapsed; CollectionsTopButton.Visibility = Visibility.Collapsed;
        }
        else if (width < 1700)
        {
            SidebarColumn.Width = new GridLength(220); RightRailColumn.Width = new GridLength(280); RightRail.Visibility = Visibility.Visible;
            HistoryContentHost.Margin = new Thickness(24, 0, 24, 42); SearchContainer.MaxWidth = 500; LibraryTopButton.Visibility = Visibility.Visible; CollectionsTopButton.Visibility = Visibility.Collapsed;
        }
        else if (width < 2300)
        {
            SidebarColumn.Width = new GridLength(232); RightRailColumn.Width = new GridLength(320); RightRail.Visibility = Visibility.Visible;
            HistoryContentHost.Margin = new Thickness(30, 0, 30, 46); SearchContainer.MaxWidth = 580; LibraryTopButton.Visibility = Visibility.Visible; CollectionsTopButton.Visibility = Visibility.Visible;
        }
        else
        {
            SidebarColumn.Width = new GridLength(250); RightRailColumn.Width = new GridLength(360); RightRail.Visibility = Visibility.Visible;
            HistoryContentHost.Margin = new Thickness(42, 0, 42, 52); SearchContainer.MaxWidth = 660; LibraryTopButton.Visibility = Visibility.Visible; CollectionsTopButton.Visibility = Visibility.Visible;
        }
    }

    private void Raise(params string[] properties)
    {
        foreach (var property in properties) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    private static T? FindVisualAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private static string DayTitle(DateTime date)
    {
        var prefix = date == DateTime.Today ? "Hoje" : date == DateTime.Today.AddDays(-1) ? "Ontem" : Portuguese.TextInfo.ToTitleCase(date.ToString("dddd", Portuguese));
        return $"{prefix} · {date.Day} de {date.ToString("MMMM", Portuguese)} de {date.Year}";
    }

    private static bool IsUsableCover(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    private static double NormalizeRating(double? rating) => rating is > 5 ? rating.Value / 2 : rating ?? 0;
    private static string RatingIcon(double rating) => rating switch { <= 0 => "☆", <= 2 => "☁", <= 3 => "●", <= 4 => "♥", _ => "★" };
    private static string RatingLabel(double rating) => rating switch { <= 0 => "Sem nota", <= 1 => "Não curti", <= 2 => "Fraquinho", <= 3 => "Legal", <= 4 => "Muito bom!", _ => "Incrível" };
    private static string RatingBackground(double rating) => rating switch { <= 0 => "#172C4C", <= 2 => "#233C68", <= 3 => "#173D69", <= 4 => "#4B245C", _ => "#4D431D" };
    private static string RatingBorder(double rating) => rating switch { <= 0 => "#496786", <= 2 => "#5C8BC8", <= 3 => "#35A8E8", <= 4 => "#D45AC8", _ => "#F1C64B" };
    private static string FormatDuration(double seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return duration.TotalHours >= 1 ? $"{(int)duration.TotalHours}h {duration.Minutes:00}m" : $"{Math.Max(1, duration.Minutes)}m";
    }
}

internal enum HistoryPeriod { Today, Week, Month, All }
internal sealed record HistoryActivityRecord(DateTime ActivityAt, Guid AnimeId, Guid EpisodeId, string AnimeTitle, int EpisodeNumber, string EpisodeTitle, string CoverPath, double ProgressPercent, double WatchedSeconds, double DurationSeconds, double? Rating, string? ReviewNotes, global::AniT.Core.WatchStatus Status);
public sealed record HistoryActivityItem(Guid AnimeId, Guid EpisodeId, string AnimeTitle, string EpisodeLabel, string EpisodeTitle, string CoverPath, string SessionTimeLabel, string DurationLabel, double ProgressPercent, string RatingIcon, string RatingLabel, string RatingBackground, string RatingBorder, string ReviewText);
public sealed record HistoryDayGroup(string Title, string Summary, string Accent, IReadOnlyList<HistoryActivityItem> Activities);

using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AniT.App;

public partial class CalendarWindow : Window, INotifyPropertyChanged
{
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");
    private readonly List<CalendarActivityRecord> activities = [];
    private readonly Dictionary<string, string> notes = CalendarNoteStore.Load();
    private LibraryWindow? libraryWindow;
    private ExploreWindow? exploreWindow;
    private HistoryWindow? historyWindow;
    private ProfileWindow? profileWindow;
    private DateTime selectedDate = DateTime.Today;
    private DateTime displayedMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private bool isLoading;

    public ObservableCollection<CalendarDayItem> CalendarDays { get; } = [];
    public ObservableCollection<CalendarActivityItem> SelectedActivities { get; } = [];
    public string MonthTitle { get; private set; } = string.Empty;
    public string SelectedDayTitle { get; private set; } = string.Empty;
    public string SelectedDaySubtitle { get; private set; } = string.Empty;
    public string WatchedDaysLabel { get; private set; } = "0";
    public string WatchedEpisodesLabel { get; private set; } = "0";
    public string WatchedHoursLabel { get; private set; } = "0h";
    public string BestDayLabel { get; private set; } = "—";
    public string StreakLabel { get; private set; } = "0 dias";
    public event PropertyChangedEventHandler? PropertyChanged;

    public CalendarWindow()
    {
        InitializeComponent();
        ResponsiveWindow.FitToWorkArea(this, 1380, 860);
        DataContext = this;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateResponsiveLayout(ActualWidth);
        await LoadAsync(selectLatestActivity: true);
    }

    private async void Window_Activated(object? sender, EventArgs e)
    {
        // The external player persists its last position asynchronously when it closes.
        // A short delay prevents this screen from re-reading the previous progress value.
        await Task.Delay(350);
        await LoadAsync(selectLatestActivity: false);
    }

    private async Task LoadAsync(bool selectLatestActivity)
    {
        if (isLoading) return;
        isLoading = true;
        try
        {
            await using var context = App.OpenFreshDatabase();
            var episodes = await context.Episodes
                .Include(episode => episode.Season)!
                .ThenInclude(season => season!.Anime)
                .Include(episode => episode.PlaybackProgress)
                .AsNoTracking()
                .ToListAsync();

            activities.Clear();
            foreach (var episode in episodes)
            {
                var activityAt = episode.WatchedAt ?? episode.PlaybackProgress?.LastPlayedAt;
                if (activityAt is null || episode.Season?.Anime is not { } anime) continue;

                var duration = episode.PlaybackProgress?.DurationSeconds ?? 0;
                var position = episode.Status == global::AniT.Core.WatchStatus.Completed && duration > 0
                    ? duration
                    : episode.PlaybackProgress?.PositionSeconds ?? 0;
                var percent = episode.Status == global::AniT.Core.WatchStatus.Completed
                    ? 100
                    : duration > 0
                        ? Math.Clamp(position / duration * 100, 0, 100)
                        : 0;

                activities.Add(new CalendarActivityRecord(
                    activityAt.Value.LocalDateTime,
                    anime.Id,
                    episode.Id,
                    anime.Title,
                    episode.Number,
                    episode.Title ?? $"Episódio {episode.Number:00}",
                    IsUsableCover(anime.CoverPath) ? anime.CoverPath! : "Assets/Calendar/banner.png",
                    percent,
                    position,
                    duration,
                    episode.Rating));
            }

            if (selectLatestActivity && activities.Count > 0)
            {
                selectedDate = activities.Max(item => item.ActivityAt).Date;
                displayedMonth = new DateTime(selectedDate.Year, selectedDate.Month, 1);
            }

            RebuildView();
        }
        finally
        {
            isLoading = false;
        }
    }

    private void RebuildView()
    {
        BuildCalendarDays();
        BuildSelectedDay();
        BuildSummary();
        MonthTitle = Portuguese.TextInfo.ToTitleCase(displayedMonth.ToString("MMMM yyyy", Portuguese));
        RaiseAll();
    }

    private void BuildCalendarDays()
    {
        CalendarDays.Clear();
        var firstVisibleDate = displayedMonth.AddDays(-(int)displayedMonth.DayOfWeek);
        for (var offset = 0; offset < 42; offset++)
        {
            var date = firstVisibleDate.AddDays(offset);
            var dayActivities = activities.Where(item => item.ActivityAt.Date == date.Date).OrderBy(item => item.ActivityAt).ToList();
            var markers = dayActivities
                .Take(3)
                .Select(item => new CalendarDayMarker(item.CoverPath, $"Ep. {item.EpisodeNumber:00}"))
                .ToList();
            var isSelected = date.Date == selectedDate.Date;
            var belongsToMonth = date.Month == displayedMonth.Month && date.Year == displayedMonth.Year;
            CalendarDays.Add(new CalendarDayItem(
                date,
                date.Day.ToString(),
                markers,
                dayActivities.Count > 3 ? $"+{dayActivities.Count - 3}" : string.Empty,
                belongsToMonth ? 1 : 0.36,
                isSelected,
                isSelected ? "#183F6C" : dayActivities.Count > 0 ? "#102F55" : "#0A2444",
                dayActivities.Count > 0 ? "#277CB4" : "#1C527C"));
        }
    }

    private void BuildSelectedDay()
    {
        SelectedActivities.Clear();
        var selected = activities
            .Where(item => item.ActivityAt.Date == selectedDate.Date)
            .OrderBy(item => item.ActivityAt)
            .ToList();
        foreach (var item in selected)
        {
            var rating = NormalizeRating(item.Rating);
            SelectedActivities.Add(new CalendarActivityItem(
                item.AnimeId,
                item.EpisodeId,
                item.AnimeTitle,
                $"Episódio {item.EpisodeNumber:00} · {item.EpisodeTitle}",
                item.CoverPath,
                item.ProgressPercent,
                item.ProgressPercent >= 99.5 ? "100% assistido" : $"{item.ProgressPercent:0}% assistido",
                FormatPlaybackTime(item.PositionSeconds, item.DurationSeconds),
                MoodAsset(rating),
                MoodLabel(rating)));
        }

        SelectedDayTitle = $"{selectedDate.Day} de {selectedDate.ToString("MMMM", Portuguese)} · {Portuguese.TextInfo.ToTitleCase(selectedDate.ToString("dddd", Portuguese))}";
        SelectedDaySubtitle = selected.Count == 0
            ? "Nenhum anime assistido neste dia"
            : $"{selected.Count} anime{(selected.Count == 1 ? string.Empty : "s")} assistido{(selected.Count == 1 ? string.Empty : "s")} neste dia";
        EmptyDayText.Visibility = selected.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DayNoteTextBox.Text = notes.GetValueOrDefault(NoteKey(selectedDate), string.Empty);
        NoteStatusText.Text = string.Empty;
    }

    private void BuildSummary()
    {
        var monthActivities = activities
            .Where(item => item.ActivityAt.Year == displayedMonth.Year && item.ActivityAt.Month == displayedMonth.Month)
            .ToList();
        WatchedDaysLabel = monthActivities.Select(item => item.ActivityAt.Date).Distinct().Count().ToString();
        WatchedEpisodesLabel = monthActivities.Count.ToString();
        var watchedSeconds = monthActivities.Sum(item => item.PositionSeconds > 0 ? item.PositionSeconds : 24 * 60);
        var duration = TimeSpan.FromSeconds(watchedSeconds);
        WatchedHoursLabel = duration.TotalHours >= 1 ? $"{(int)duration.TotalHours}h {duration.Minutes:00}m" : $"{duration.Minutes}m";
        BestDayLabel = monthActivities.Count == 0
            ? "—"
            : Portuguese.TextInfo.ToTitleCase(monthActivities.GroupBy(item => item.ActivityAt.DayOfWeek).OrderByDescending(group => group.Count()).First().Key.ToString() switch
            {
                "Sunday" => "domingo", "Monday" => "segunda-feira", "Tuesday" => "terça-feira", "Wednesday" => "quarta-feira",
                "Thursday" => "quinta-feira", "Friday" => "sexta-feira", "Saturday" => "sábado", var value => value
            });

        var dates = activities.Select(item => item.ActivityAt.Date).Distinct().OrderByDescending(date => date).ToList();
        var streak = 0;
        if (dates.Count > 0)
        {
            var cursor = dates[0];
            foreach (var date in dates)
            {
                if (date != cursor) break;
                streak++;
                cursor = cursor.AddDays(-1);
            }
        }
        StreakLabel = $"{streak} dia{(streak == 1 ? string.Empty : "s")}";
    }

    private void RaiseAll()
    {
        foreach (var property in new[] { nameof(MonthTitle), nameof(SelectedDayTitle), nameof(SelectedDaySubtitle), nameof(WatchedDaysLabel), nameof(WatchedEpisodesLabel), nameof(WatchedHoursLabel), nameof(BestDayLabel), nameof(StreakLabel) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    private void Day_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DateTime date }) return;
        selectedDate = date.Date;
        if (selectedDate.Month != displayedMonth.Month || selectedDate.Year != displayedMonth.Year)
            displayedMonth = new DateTime(selectedDate.Year, selectedDate.Month, 1);
        RebuildView();
    }

    private void PreviousMonth_Click(object sender, RoutedEventArgs e) => ChangeMonth(-1);
    private void NextMonth_Click(object sender, RoutedEventArgs e) => ChangeMonth(1);
    private void PreviousDay_Click(object sender, RoutedEventArgs e) => ChangeDay(-1);
    private void NextDay_Click(object sender, RoutedEventArgs e) => ChangeDay(1);

    private void ChangeMonth(int offset)
    {
        displayedMonth = displayedMonth.AddMonths(offset);
        selectedDate = new DateTime(displayedMonth.Year, displayedMonth.Month, 1);
        RebuildView();
    }

    private void ChangeDay(int offset)
    {
        selectedDate = selectedDate.AddDays(offset);
        displayedMonth = new DateTime(selectedDate.Year, selectedDate.Month, 1);
        RebuildView();
    }

    private void Today_Click(object sender, RoutedEventArgs e)
    {
        selectedDate = DateTime.Today;
        displayedMonth = new DateTime(selectedDate.Year, selectedDate.Month, 1);
        RebuildView();
    }

    private void SaveNote_Click(object sender, RoutedEventArgs e)
    {
        var key = NoteKey(selectedDate);
        var value = DayNoteTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(value)) notes.Remove(key); else notes[key] = value;
        CalendarNoteStore.Save(notes);
        NoteStatusText.Text = string.IsNullOrWhiteSpace(value) ? "Nota removida." : "✓ Nota salva localmente.";
    }

    private void ActivityDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid id }) new AnimeDetailsWindow(id) { Owner = this }.ShowDialog();
    }

    private async void Continue_Click(object sender, RoutedEventArgs e)
    {
        var episodeId = SelectedActivities.FirstOrDefault()?.EpisodeId ?? activities.OrderByDescending(item => item.ActivityAt).FirstOrDefault()?.EpisodeId;
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

    private void History_Click(object sender, RoutedEventArgs e)
    {
        if (historyWindow is { IsLoaded: true }) { historyWindow.Activate(); return; }
        historyWindow = new HistoryWindow { Owner = this };
        historyWindow.Closed += (_, _) => historyWindow = null;
        historyWindow.Show();
    }

    private void Profile_Click(object sender, RoutedEventArgs e)
    {
        if (profileWindow is { IsLoaded: true }) { profileWindow.Activate(); return; }
        profileWindow = new ProfileWindow { Owner = this };
        profileWindow.Closed += (_, _) => profileWindow = null;
        profileWindow.Show();
    }

    private void Home_Click(object sender, RoutedEventArgs e) => Close();
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    private void SearchBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Library_Click(sender, e); }

    private void RoundedPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not Border border || border.ActualWidth <= 0 || border.ActualHeight <= 0) return;
        var radius = double.TryParse(border.Tag?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedRadius) ? parsedRadius : 16;
        border.Clip = new RectangleGeometry(new Rect(0, 0, border.ActualWidth, border.ActualHeight), radius, radius);
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsiveLayout(e.NewSize.Width);

    private void UpdateResponsiveLayout(double width)
    {
        if (SidebarColumn is null || RightRailColumn is null || CalendarContentHost is null) return;
        if (width < 1180)
        {
            SidebarColumn.Width = new GridLength(184); RightRailColumn.Width = new GridLength(0); RightRail.Visibility = Visibility.Collapsed;
            CalendarContentHost.Margin = new Thickness(18, 0, 18, 36); SearchContainer.MaxWidth = 350; LibraryTopButton.Visibility = Visibility.Collapsed; CollectionsTopButton.Visibility = Visibility.Collapsed;
        }
        else if (width < 1600)
        {
            SidebarColumn.Width = new GridLength(220); RightRailColumn.Width = new GridLength(270); RightRail.Visibility = Visibility.Visible;
            CalendarContentHost.Margin = new Thickness(24, 0, 24, 42); SearchContainer.MaxWidth = 500; LibraryTopButton.Visibility = Visibility.Visible; CollectionsTopButton.Visibility = Visibility.Collapsed;
        }
        else if (width < 2300)
        {
            SidebarColumn.Width = new GridLength(232); RightRailColumn.Width = new GridLength(310); RightRail.Visibility = Visibility.Visible;
            CalendarContentHost.Margin = new Thickness(30, 0, 30, 46); SearchContainer.MaxWidth = 580; LibraryTopButton.Visibility = Visibility.Visible; CollectionsTopButton.Visibility = Visibility.Visible;
        }
        else
        {
            SidebarColumn.Width = new GridLength(250); RightRailColumn.Width = new GridLength(360); RightRail.Visibility = Visibility.Visible;
            CalendarContentHost.Margin = new Thickness(42, 0, 42, 52); SearchContainer.MaxWidth = 660; LibraryTopButton.Visibility = Visibility.Visible; CollectionsTopButton.Visibility = Visibility.Visible;
        }
    }

    private static bool IsUsableCover(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    private static string NoteKey(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static double NormalizeRating(double? rating) => rating is > 5 ? rating.Value / 2 : rating ?? 0;
    private static string MoodAsset(double rating) => rating switch { <= 0 => "Assets/Calendar/3.png", <= 1 => "Assets/Calendar/5.png", <= 2 => "Assets/Calendar/1.png", <= 3 => "Assets/Calendar/2.png", <= 4 => "Assets/Calendar/4.png", _ => "Assets/Calendar/6.png" };
    private static string MoodLabel(double rating) => rating switch { <= 0 => "Sem nota", <= 1 => "Não curti", <= 2 => "Fraquinho", <= 3 => "Legal", <= 4 => "Muito bom", _ => "Excelente" };
    private static string FormatPlaybackTime(double position, double duration) => duration > 0 ? $"{FormatTime(position)} / {FormatTime(duration)}" : FormatTime(position);
    private static string FormatTime(double seconds) => $"{(int)TimeSpan.FromSeconds(Math.Max(0, seconds)).TotalMinutes:00}:{TimeSpan.FromSeconds(Math.Max(0, seconds)).Seconds:00}";
}

internal sealed record CalendarActivityRecord(DateTime ActivityAt, Guid AnimeId, Guid EpisodeId, string AnimeTitle, int EpisodeNumber, string EpisodeTitle, string CoverPath, double ProgressPercent, double PositionSeconds, double DurationSeconds, double? Rating);
public sealed record CalendarDayMarker(string CoverPath, string EpisodeLabel);
public sealed record CalendarDayItem(DateTime Date, string DayNumber, IReadOnlyList<CalendarDayMarker> Markers, string OverflowLabel, double Opacity, bool IsSelected, string Background, string BorderBrush);
public sealed record CalendarActivityItem(Guid AnimeId, Guid EpisodeId, string AnimeTitle, string EpisodeTitle, string CoverPath, double ProgressPercent, string ProgressLabel, string TimeLabel, string MoodAsset, string MoodLabel);

internal static class CalendarNoteStore
{
    private static readonly string NotesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AniT", "Data", "calendar-notes.json");

    public static Dictionary<string, string> Load()
    {
        try { return File.Exists(NotesPath) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(NotesPath)) ?? [] : []; }
        catch { return []; }
    }

    public static void Save(Dictionary<string, string> notes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(NotesPath)!);
        File.WriteAllText(NotesPath, JsonSerializer.Serialize(notes, new JsonSerializerOptions { WriteIndented = true }));
    }
}

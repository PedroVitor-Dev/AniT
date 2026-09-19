using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AniT.App;

public partial class ProfileWindow : Window, INotifyPropertyChanged
{
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");
    private List<ProfileActivity> activities = [];
    private Guid? latestEpisodeId;
    private ProfileSettings settings = ProfileSettingsStore.Load();
    private string automaticAvatarPath = "Assets/Profile/1.png";
    private bool isLoading;

    public ObservableCollection<ProfileHighlight> Highlights { get; } = [];
    public ObservableCollection<ProfileAnimeCard> Favorites { get; } = [];
    public ObservableCollection<ProfileRankItem> TopAnime { get; } = [];
    public ObservableCollection<ProfileAchievement> Achievements { get; } = [];
    public ObservableCollection<ProfileActivityItem> RecentActivities { get; } = [];
    public ObservableCollection<ProfileCalendarDay> CalendarDays { get; } = [];

    public string DisplayName { get; private set; } = "Pet-S";
    public string Bio { get; private set; } = string.Empty;
    public string AvatarPath { get; private set; } = "Assets/Profile/1.png";
    public double AvatarSize { get; private set; } = 142;
    public string BannerPath { get; private set; } = "Assets/History/2.png";
    public string LevelLabel { get; private set; } = "Nv. 1";
    public string MemberSinceLabel { get; private set; } = string.Empty;
    public string DaysWatchedLabel { get; private set; } = "0";
    public string EpisodesWatchedLabel { get; private set; } = "0";
    public string HoursWatchedLabel { get; private set; } = "0m";
    public string CompletedAnimeLabel { get; private set; } = "0";
    public string AverageRatingLabel { get; private set; } = "—";
    public string RatingsCountLabel { get; private set; } = "Nenhuma avaliação";
    public string CurrentMonthLabel { get; private set; } = string.Empty;
    public string FavoriteCountLabel { get; private set; } = "♡  0 favoritos";
    public string CompletedCountLabel { get; private set; } = "✓  0 concluídos";
    public string WatchingCountLabel { get; private set; } = "▶  0 em andamento";
    public string AchievementsUnlockedLabel { get; private set; } = "0 de 0 desbloqueadas";
    public string AchievementPointsLabel { get; private set; } = "0 pontos";
    public double AchievementProgressPercent { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public ProfileWindow()
    {
        InitializeComponent();
        GlobalSearchController.Attach(this, SearchBox, SearchHint, SearchContainer);
        ResponsiveWindow.FitToWorkArea(this, 1380, 860);
        DataContext = this;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateResponsiveLayout(ActualWidth);
        await LoadAsync();
    }

    private async void Window_Activated(object? sender, EventArgs e)
    {
        if (!IsLoaded) return;
        await Task.Delay(180);
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (isLoading) return;
        isLoading = true;
        try
        {
            await using var context = App.OpenFreshDatabase();
            var anime = await context.Anime
                .Include(item => item.Seasons).ThenInclude(season => season.Episodes).ThenInclude(episode => episode.PlaybackProgress)
                .AsNoTracking().ToListAsync();

            settings = ProfileSettingsStore.Load();
            DisplayName = settings.DisplayName;
            Bio = settings.Bio;
            AvatarSize = settings.AvatarSize;
            BannerPath = IsUsableCover(settings.BannerPath) ? settings.BannerPath! : "Assets/History/2.png";

            activities = anime.SelectMany(item => item.Seasons.SelectMany(season => season.Episodes.Select(episode => new { Anime = item, Episode = episode })))
                .Select(item => new ProfileActivity(item.Anime.Id, item.Episode.Id, item.Anime.Title, item.Episode.Number,
                    IsUsableCover(item.Anime.CoverPath) ? item.Anime.CoverPath! : "Assets/Profile/1.png",
                    item.Episode.WatchedAt ?? item.Episode.PlaybackProgress?.LastPlayedAt,
                    item.Episode.Status, item.Episode.Rating,
                    WatchedSeconds(item.Episode.Status, item.Episode.PlaybackProgress?.PositionSeconds ?? 0, item.Episode.PlaybackProgress?.DurationSeconds ?? 0)))
                .Where(item => item.ActivityAt is not null).OrderByDescending(item => item.ActivityAt).ToList();

            var allEpisodes = anime.SelectMany(item => item.Seasons).SelectMany(season => season.Episodes).ToList();
            var watchedDates = activities.Select(item => item.ActivityAt!.Value.LocalDateTime.Date).Distinct().OrderByDescending(date => date).ToList();
            var ratings = allEpisodes.Where(item => item.Rating is > 0).Select(item => NormalizeRating(item.Rating)).ToList();
            var completedAnime = anime.Count(item => item.Seasons.SelectMany(season => season.Episodes).Any() && item.Seasons.SelectMany(season => season.Episodes).All(episode => episode.Status == global::AniT.Core.WatchStatus.Completed));
            var watchingAnime = anime.Count(item => item.Seasons.SelectMany(season => season.Episodes).Any(episode => episode.Status == global::AniT.Core.WatchStatus.Watching));
            var latest = activities.FirstOrDefault();
            latestEpisodeId = latest?.EpisodeId;
            automaticAvatarPath = latest?.CoverPath ?? anime.Select(item => item.CoverPath).FirstOrDefault(IsUsableCover) ?? "Assets/Profile/1.png";
            AvatarPath = IsUsableCover(settings.AvatarPath) ? settings.AvatarPath! : automaticAvatarPath;
            DaysWatchedLabel = watchedDates.Count.ToString("N0", Portuguese);
            EpisodesWatchedLabel = activities.Count.ToString("N0", Portuguese);
            HoursWatchedLabel = FormatDuration(activities.Sum(item => item.WatchedSeconds));
            CompletedAnimeLabel = completedAnime.ToString("N0", Portuguese);
            AverageRatingLabel = ratings.Count == 0 ? "—" : ratings.Average().ToString("0.0", Portuguese);
            RatingsCountLabel = ratings.Count == 0 ? "Nenhuma avaliação" : $"Baseada em {ratings.Count} avaliação{(ratings.Count == 1 ? string.Empty : "ões")}";
            LevelLabel = $"Nv. {Math.Max(1, activities.Count / 5 + 1)}";
            var memberSince = new[] { settings.MemberSince }.Concat(anime.Select(item => item.CreatedAt)).Min();
            MemberSinceLabel = $"Desde {memberSince.LocalDateTime.ToString("MMM. 'de' yyyy", Portuguese)}  •  Cada história deixa uma nova lembrança.";
            FavoriteCountLabel = $"♡  {anime.Count(item => item.IsFavorite)} favoritos";
            CompletedCountLabel = $"✓  {completedAnime} concluídos";
            WatchingCountLabel = $"▶  {watchingAnime} em andamento";

            BuildHighlights(anime, watchedDates, ratings);
            BuildFavorites(anime);
            BuildRanking(anime);
            await BuildAchievementsAsync();
            BuildRecentActivity();
            BuildCalendar(watchedDates);
            FavoritesEmpty.Visibility = Favorites.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RankingEmpty.Visibility = TopAnime.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RaiseAll();
        }
        finally { isLoading = false; }
    }

    private void BuildHighlights(IReadOnlyCollection<global::AniT.Core.Anime> anime, IReadOnlyList<DateTime> watchedDates, IReadOnlyList<double> ratings)
    {
        Highlights.Clear();
        var streak = CalculateStreak(watchedDates);
        var favorite = anime.Where(item => item.IsFavorite).OrderByDescending(item => item.CreatedAt).FirstOrDefault();
        var latest = activities.FirstOrDefault();
        var top = anime.Select(item => new { Anime = item, Ratings = item.Seasons.SelectMany(season => season.Episodes).Where(episode => episode.Rating is > 0).Select(episode => NormalizeRating(episode.Rating)).ToList() }).Where(item => item.Ratings.Count > 0).OrderByDescending(item => item.Ratings.Average()).FirstOrDefault();
        Highlights.Add(new("♨", "Sequência atual", $"{streak} dia{(streak == 1 ? string.Empty : "s")}", streak > 0 ? "Continue construindo memórias." : "Assista hoje para começar.", "#FFB958", "#563D27"));
        Highlights.Add(new("♡", "Favorito recente", favorite?.Title ?? "Ainda não escolhido", favorite is null ? "Marque um anime como favorito." : "Um lugar especial na sua estante.", "#FF8BD1", "#512C55"));
        Highlights.Add(new("▶", "Último anime visto", latest?.AnimeTitle ?? "Nenhuma sessão", latest is null ? "Sua jornada começa na biblioteca." : $"Episódio {latest.EpisodeNumber:00}", "#77DFFF", "#194C78"));
        Highlights.Add(new("★", "Melhor nota", top is null ? "—" : top.Ratings.Average().ToString("0.0", Portuguese), top?.Anime.Title ?? "Avalie seus episódios.", "#FFE066", "#58482B"));
        Highlights.Add(new("▣", "Coleção local", $"{anime.Count} anime{(anime.Count == 1 ? string.Empty : "s")}", "Sua estante, do seu jeito.", "#C5A4FF", "#3E335E"));
    }

    private void BuildFavorites(IEnumerable<global::AniT.Core.Anime> anime)
    {
        Favorites.Clear();
        foreach (var item in anime.Where(item => item.IsFavorite).OrderByDescending(item => item.CreatedAt).Take(5))
            Favorites.Add(new(item.Id, item.Title, item.EnglishTitle ?? $"{item.Seasons.Sum(season => season.Episodes.Count)} episódios", IsUsableCover(item.CoverPath) ? item.CoverPath! : "Assets/Profile/1.png"));
    }

    private void BuildRanking(IEnumerable<global::AniT.Core.Anime> anime)
    {
        TopAnime.Clear();
        var ranked = anime.Select(item => new { Anime = item, Ratings = item.Seasons.SelectMany(season => season.Episodes).Where(episode => episode.Rating is > 0).Select(episode => NormalizeRating(episode.Rating)).ToList() }).Where(item => item.Ratings.Count > 0).OrderByDescending(item => item.Ratings.Average()).ThenBy(item => item.Anime.Title).Take(5).ToList();
        for (var index = 0; index < ranked.Count; index++)
        {
            var item = ranked[index];
            TopAnime.Add(new(item.Anime.Id, index + 1, item.Anime.Title, $"{item.Ratings.Count} episódio{(item.Ratings.Count == 1 ? string.Empty : "s")} avaliado{(item.Ratings.Count == 1 ? string.Empty : "s")}", IsUsableCover(item.Anime.CoverPath) ? item.Anime.CoverPath! : "Assets/Profile/1.png", $"★ {item.Ratings.Average().ToString("0.0", Portuguese)}"));
        }
    }

    private async Task BuildAchievementsAsync()
    {
        var progress = await App.Achievements.GetProgressAsync();
        Achievements.Clear();
        foreach (var item in progress
                     .OrderByDescending(item => item.IsUnlocked)
                     .ThenByDescending(item => item.UnlockedAt ?? DateTimeOffset.MinValue)
                     .ThenByDescending(item => item.Percent)
                     .Take(14))
        {
            var hidden = item.Definition.IsHiddenUntilUnlocked && !item.IsUnlocked;
            var color = AchievementCardView.Accent(item.Definition.Rarity);
            var accent = color.ToString();
            Achievements.Add(new(
                hidden ? "???" : item.Definition.Name,
                hidden ? "Conquista secreta" : item.Definition.Description,
                hidden ? string.Empty : item.Definition.IconPath,
                hidden ? "SECRETA" : global::AniT.Core.Achievements.AchievementLabels.Rarity(item.Definition.Rarity).ToUpper(Portuguese),
                AchievementCardView.FormatProgress(item),
                item.Percent,
                item.IsUnlocked ? "✓ DESBLOQUEADA" : item.CurrentValue > 0 ? "EM PROGRESSO" : "BLOQUEADA",
                $"+{item.Points} AniPoints",
                item.Points,
                item.IsUnlocked,
                item.IsUnlocked ? 1 : 0.8,
                hidden ? 0 : item.IsUnlocked ? 1 : 0.42,
                accent,
                $"#{color.R:X2}{color.G:X2}{color.B:X2}33",
                item.IsUnlocked ? "#E80A2142" : "#E009172C",
                item.IsUnlocked ? accent : "#344C68",
                accent,
                item.IsUnlocked ? Visibility.Collapsed : Visibility.Visible));
        }

        var unlocked = progress.Count(item => item.IsUnlocked);
        var points = progress.Where(item => item.IsUnlocked).Sum(item => item.Points);
        AchievementsUnlockedLabel = $"{unlocked} de {progress.Count} desbloqueadas";
        AchievementPointsLabel = $"✦ {points.ToString("N0", Portuguese)} AniPoints";
        AchievementProgressPercent = progress.Count == 0 ? 0 : unlocked * 100d / progress.Count;
    }

    private void BuildRecentActivity()
    {
        RecentActivities.Clear();
        foreach (var item in activities.Take(4)) RecentActivities.Add(new(item.AnimeId, item.AnimeTitle, $"Episódio {item.EpisodeNumber:00}", item.CoverPath, RelativeTime(item.ActivityAt!.Value.LocalDateTime)));
    }

    private void BuildCalendar(IReadOnlyCollection<DateTime> activityDates)
    {
        CalendarDays.Clear();
        var today = DateTime.Today;
        CurrentMonthLabel = today.ToString("MMMM yyyy", Portuguese);
        var first = new DateTime(today.Year, today.Month, 1);
        for (var index = 0; index < (int)first.DayOfWeek; index++) CalendarDays.Add(new("", "Transparent", "Transparent", "Transparent"));
        for (var day = 1; day <= DateTime.DaysInMonth(today.Year, today.Month); day++)
        {
            var date = new DateTime(today.Year, today.Month, day);
            var active = activityDates.Contains(date);
            var isToday = date == today;
            CalendarDays.Add(new(day.ToString(), isToday ? "#2679CA" : active ? "#164D7C" : "Transparent", isToday ? "#70E4FF" : active ? "#3E9ED7" : "#1D4264", active || isToday ? "White" : "#9AB6D2"));
        }
    }

    private async void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (latestEpisodeId is not Guid id) return;
        try { await App.PlayEpisodeAsync(id); }
        catch (Exception exception) { MessageBox.Show(exception.Message, "Não foi possível continuar", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ProfileEditWindow(settings, AvatarPath) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SavedSettings is not { } saved) return;

        var avatarChanged = !string.Equals(settings.AvatarPath, saved.AvatarPath, StringComparison.OrdinalIgnoreCase)
                            || Math.Abs(settings.AvatarSize - saved.AvatarSize) > 0.1;
        try
        {
            saved = saved with { AvatarPath = ProfileSettingsStore.PersistAvatar(saved.AvatarPath) };
            ProfileSettingsStore.Save(saved);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Não foi possível salvar a foto do perfil.\n\n{exception.Message}", "AniT", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        settings = saved;
        DisplayName = saved.DisplayName;
        Bio = saved.Bio;
        AvatarSize = saved.AvatarSize;
        AvatarPath = IsUsableCover(saved.AvatarPath) ? saved.AvatarPath! : automaticAvatarPath;
        BannerPath = IsUsableCover(saved.BannerPath) ? saved.BannerPath! : "Assets/History/2.png";
        Raise(nameof(DisplayName), nameof(Bio), nameof(AvatarPath), nameof(AvatarSize), nameof(BannerPath));
        await App.Achievements.RecordAsync(new global::AniT.Core.Achievements.AchievementEvent(
            global::AniT.Core.Achievements.AchievementEventType.ProfileUpdated));
        if (avatarChanged)
        {
            await App.Achievements.RecordAsync(new global::AniT.Core.Achievements.AchievementEvent(
                global::AniT.Core.Achievements.AchievementEventType.AvatarChanged));
        }
    }

    private async void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "Exportar backup do AniT", Filter = "Backup do AniT (*.zip)|*.zip", FileName = $"AniT-backup-{DateTime.Now:yyyy-MM-dd}.zip", AddExtension = true, DefaultExt = ".zip" };
        if (dialog.ShowDialog(this) != true) return;
        var source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AniT");
        try
        {
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException("A pasta de dados do AniT ainda não existe.");
            if (File.Exists(dialog.FileName)) File.Delete(dialog.FileName);
            ZipFile.CreateFromDirectory(source, dialog.FileName, CompressionLevel.Optimal, false);
            await App.Achievements.RecordAsync(new global::AniT.Core.Achievements.AchievementEvent(
                global::AniT.Core.Achievements.AchievementEventType.BackupCreated));
            MessageBox.Show("Backup exportado com sucesso.", "AniT", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) { MessageBox.Show($"Não foi possível exportar o backup.\n\n{exception.Message}", "AniT", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void AnimeCard_Click(object sender, RoutedEventArgs e) { if (sender is Button { Tag: Guid id }) AppNavigation.OpenAnimeDetails(this, id); }
    private void Home_Click(object sender, RoutedEventArgs e) => AppNavigation.Home(this);
    private void Library_Click(object sender, RoutedEventArgs e) => AppNavigation.OpenLibrary(this);
    private void Explore_Click(object sender, RoutedEventArgs e) => AppNavigation.Explore(this);
    private void Calendar_Click(object sender, RoutedEventArgs e) => AppNavigation.Calendar(this);
    private void History_Click(object sender, RoutedEventArgs e) => AppNavigation.History(this);
    private void Achievements_Click(object sender, RoutedEventArgs e) => AppNavigation.Achievements(this);
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { if (SearchHint is not null) SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed; }
    private void SearchBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Library_Click(sender, e); }
    private void RoundedPanel_SizeChanged(object sender, SizeChangedEventArgs e) { if (sender is not Border border || border.ActualWidth <= 0 || border.ActualHeight <= 0) return; var radius = double.TryParse(border.Tag?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 16; border.Clip = new RectangleGeometry(new Rect(0, 0, border.ActualWidth, border.ActualHeight), radius, radius); }
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsiveLayout(e.NewSize.Width);

    private void UpdateResponsiveLayout(double width)
    {
        if (SidebarColumn is null || RightRailColumn is null || ProfileContentHost is null) return;
        if (width < 1320) { SidebarColumn.Width = new GridLength(184); RightRailColumn.Width = new GridLength(0); RightRail.Visibility = Visibility.Collapsed; ProfileContentHost.Margin = new Thickness(18, 14, 18, 36); SearchContainer.MaxWidth = 350; LibraryTopButton.Visibility = Visibility.Collapsed; HeroPanel.Height = 400; }
        else if (width < 1700) { SidebarColumn.Width = new GridLength(220); RightRailColumn.Width = new GridLength(300); RightRail.Visibility = Visibility.Visible; ProfileContentHost.Margin = new Thickness(24, 16, 24, 42); SearchContainer.MaxWidth = 500; LibraryTopButton.Visibility = Visibility.Visible; HeroPanel.Height = 380; }
        else { SidebarColumn.Width = new GridLength(232); RightRailColumn.Width = new GridLength(340); RightRail.Visibility = Visibility.Visible; ProfileContentHost.Margin = new Thickness(30, 18, 30, 48); SearchContainer.MaxWidth = 580; LibraryTopButton.Visibility = Visibility.Visible; HeroPanel.Height = 370; }
    }

    private static bool IsUsableCover(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    private static double NormalizeRating(double? rating) => rating is null ? 0 : rating > 5 ? rating.Value / 2d : rating.Value;
    private static double WatchedSeconds(global::AniT.Core.WatchStatus status, double position, double duration) => status == global::AniT.Core.WatchStatus.Completed ? (duration > 0 ? duration : 24 * 60) : Math.Max(0, position);
    private static int CalculateStreak(IReadOnlyList<DateTime> dates) { if (dates.Count == 0 || dates[0] < DateTime.Today.AddDays(-1)) return 0; var streak = 0; var cursor = dates[0]; foreach (var date in dates) { if (date != cursor) break; streak++; cursor = cursor.AddDays(-1); } return streak; }
    private static string FormatDuration(double seconds) { var span = TimeSpan.FromSeconds(Math.Max(0, seconds)); return span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes:00}m" : $"{Math.Max(0, span.Minutes)}m"; }
    private static string RelativeTime(DateTime value) { var delta = DateTime.Now - value; if (value.Date == DateTime.Today) return delta.TotalHours < 1 ? "agora" : $"há {(int)delta.TotalHours}h"; if (value.Date == DateTime.Today.AddDays(-1)) return "ontem"; return value.ToString("dd/MM"); }
    private void Raise(params string[] names) { foreach (var name in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
    private void RaiseAll() => Raise(nameof(DisplayName), nameof(Bio), nameof(AvatarPath), nameof(AvatarSize), nameof(BannerPath), nameof(LevelLabel), nameof(MemberSinceLabel), nameof(DaysWatchedLabel), nameof(EpisodesWatchedLabel), nameof(HoursWatchedLabel), nameof(CompletedAnimeLabel), nameof(AverageRatingLabel), nameof(RatingsCountLabel), nameof(CurrentMonthLabel), nameof(FavoriteCountLabel), nameof(CompletedCountLabel), nameof(WatchingCountLabel), nameof(AchievementsUnlockedLabel), nameof(AchievementPointsLabel), nameof(AchievementProgressPercent));
}

public sealed record ProfileSettings(
    string DisplayName,
    string Bio,
    DateTimeOffset MemberSince,
    string? AvatarPath = null,
    double AvatarSize = 142,
    string? BannerPath = null);

internal static class ProfileSettingsStore
{
    private static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AniT", "Data", "profile.json");
    private static readonly string AvatarDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AniT", "Data", "Profile");

    public static ProfileSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<ProfileSettings>(File.ReadAllText(FilePath)) ?? Default();
                var avatarSize = loaded.AvatarSize is >= 104 and <= 190 ? loaded.AvatarSize : 142;
                return loaded with { AvatarSize = avatarSize };
            }
        }
        catch { }
        return Default();
    }

    public static string? PersistAvatar(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return null;
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("A imagem escolhida não está mais disponível.", sourcePath);

        Directory.CreateDirectory(AvatarDirectory);
        var sourceFullPath = Path.GetFullPath(sourcePath);
        var avatarDirectoryFullPath = Path.GetFullPath(AvatarDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (sourceFullPath.StartsWith(avatarDirectoryFullPath, StringComparison.OrdinalIgnoreCase)) return sourceFullPath;

        var extension = Path.GetExtension(sourceFullPath).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp"))
            throw new InvalidDataException("Escolha uma imagem PNG, JPG, JPEG ou BMP.");
        var destination = Path.Combine(AvatarDirectory, $"avatar-{Guid.NewGuid():N}{extension}");
        File.Copy(sourceFullPath, destination, overwrite: false);
        return destination;
    }

    public static void Save(ProfileSettings settings) { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true })); }
    private static ProfileSettings Default() => new("Pet-S", "Animes tornam os dias comuns em momentos especiais. ♡", DateTimeOffset.Now);
}

public sealed record ProfileHighlight(string Icon, string Label, string Value, string Detail, string Accent, string AccentBackground);
public sealed record ProfileAnimeCard(Guid AnimeId, string Title, string Subtitle, string CoverPath);
public sealed record ProfileRankItem(Guid AnimeId, int Rank, string Title, string Subtitle, string CoverPath, string ScoreLabel);
public sealed record ProfileAchievement(
    string Title,
    string Description,
    string BadgePath,
    string Rarity,
    string ProgressLabel,
    double ProgressPercent,
    string StatusLabel,
    string PointsLabel,
    int Points,
    bool IsUnlocked,
    double Opacity,
    double BadgeOpacity,
    string Accent,
    string RarityBackground,
    string CardBackground,
    string Border,
    string GlowColor,
    Visibility LockedVisibility)
{
    public string Glow => GlowColor;
}
public sealed record ProfileActivityItem(Guid AnimeId, string Title, string Detail, string CoverPath, string WhenLabel);
public sealed record ProfileCalendarDay(string Day, string Background, string Border, string Foreground);
internal sealed record ProfileActivity(Guid AnimeId, Guid EpisodeId, string AnimeTitle, int EpisodeNumber, string CoverPath, DateTimeOffset? ActivityAt, global::AniT.Core.WatchStatus Status, double? Rating, double WatchedSeconds);

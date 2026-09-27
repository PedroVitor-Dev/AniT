using AniT.Infrastructure;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace AniT.App;

public enum SettingsSection
{
    General = 0,
    Playback = 1,
    Appearance = 2,
    Shelf = 3,
    Experience = 4,
    Images = 5,
    Backup = 6,
    Organization = 7,
    Notifications = 8,
    ProfilePrivacy = 9,
    Advanced = 10
}

public partial class SystemSettingsWindow : Window
{
    public ObservableCollection<SystemImageSourceItem> ImageSources { get; } = [];
    public ObservableCollection<PageMascotSettingItem> PageMascots { get; } = [];
    public ObservableCollection<HomeSectionSettingItem> HomeSections { get; } = [];
    public ObservableCollection<ProfileSettings> LocalProfiles { get; } = [];
    private Point homeSectionDragStart;
    private HomeSectionSettingItem? draggedHomeSection;
    private ProfileSettings profileDraft = ProfileSettingsStore.Load();

    public SystemSettingsWindow(SettingsSection initialSection = SettingsSection.General)
    {
        InitializeComponent();
        ResponsiveWindow.FitToWorkArea(this, 1380, 860);
        var settings = AniTSystemSettingsStore.Load();
        BannerIntervalSlider.Value = settings.HomeBannerIntervalSeconds;
        BannerAutoRotateBox.IsChecked = settings.HomeBannerAutoRotate;
        ContinueItemsBox.SelectedValue = settings.HomeContinueItems.ToString();
        RecentItemsBox.SelectedValue = settings.HomeRecentItems.ToString();
        FavoriteItemsBox.SelectedValue = settings.HomeFavoriteItems.ToString();
        TopRatedItemsBox.SelectedValue = settings.HomeTopRatedItems.ToString();
        ContinueBehaviorBox.SelectedValue = settings.HomeContinueBehavior.ToString();
        HideCompletedFromContinueBox.IsChecked = settings.HideCompletedFromContinue;
        HomePriorityBox.SelectedValue = settings.HomeContentPriority.ToString();
        var hiddenHomeSections = (settings.HiddenHomeSections ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var sectionId in settings.HomeSectionOrder ?? AniTSystemSettings.DefaultHomeSectionOrder)
        {
            HomeSections.Add(HomeSectionSettingItem.Create(sectionId, !hiddenHomeSections.Contains(sectionId)));
        }
        HomePriorityBox.SelectionChanged += HomePriorityBox_SelectionChanged;
        ApplyHomePriorityToSectionList();
        StartupScanBox.IsChecked = settings.ScanLibraryOnStartup;
        StartupMetadataBox.IsChecked = settings.RefreshMetadataOnStartup;
        EnableTransitionsBox.IsChecked = settings.EnableNavigationLoading;
        LoadingDurationSlider.Value = settings.NavigationLoadingMilliseconds;
        BackgroundScanSlider.Value = settings.LibraryBackgroundScanMinutes;
        VideoExtensionsBox.Text = string.Join(", ", settings.VideoExtensions ?? AniTSystemSettings.DefaultVideoExtensions);
        RecognizeSeasonEpisodeCodesBox.IsChecked = settings.RecognizeSeasonEpisodeCodes;
        RecognizeEpisodePrefixesBox.IsChecked = settings.RecognizeEpisodePrefixes;
        RecognizeDashNumbersBox.IsChecked = settings.RecognizeDashNumbers;
        RecognizeBracketNumbersBox.IsChecked = settings.RecognizeBracketNumbers;
        RecognizeTrailingNumbersBox.IsChecked = settings.RecognizeTrailingNumbers;
        UseFolderSeasonBox.IsChecked = settings.UseFolderSeason;
        RecognizeSpecialsBox.IsChecked = settings.RecognizeSpecials;
        PlaybackPlayerModeBox.SelectedValue = settings.PlaybackPlayerMode.ToString();
        ResumePlaybackBox.IsChecked = settings.ResumePlayback;
        WatchedThresholdSlider.Value = settings.WatchedThresholdPercent;
        AutoPlayNextEpisodeBox.IsChecked = settings.AutoPlayNextEpisode;
        SkipOpeningBox.IsChecked = settings.SkipOpening;
        SkipEndingBox.IsChecked = settings.SkipEnding;
        PreferredAudioLanguageBox.SelectedValue = settings.PreferredAudioLanguage;
        PreferredSubtitleLanguageBox.SelectedValue = settings.PreferredSubtitleLanguage;
        PlaybackSpeedBox.SelectedValue = settings.DefaultPlaybackSpeed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        StartPlaybackFullscreenBox.IsChecked = settings.StartPlaybackFullscreen;
        ProgressSaveIntervalSlider.Value = settings.ProgressSaveIntervalSeconds;
        EpisodeNumberFormatBox.SelectedValue = settings.EpisodeNumberDisplayFormat.ToString();
        AnimeTitlePreferenceBox.SelectedValue = settings.AnimeTitlePreference.ToString();
        LibraryDefaultSortBox.SelectedValue = settings.LibraryDefaultSort.ToString();
        CustomTagsBox.Text = string.Join(Environment.NewLine, settings.CustomTags ?? []);
        UserCollectionsBox.Text = string.Join(Environment.NewLine, settings.UserCollections ?? []);
        FavoritesFirstRuleBox.IsChecked = settings.FavoritesFirstRule;
        InProgressFirstRuleBox.IsChecked = settings.InProgressFirstRule;
        NotifyNewEpisodesBox.IsChecked = settings.NotifyNewEpisodes;
        NotifyMetadataUpdatesBox.IsChecked = settings.NotifyMetadataUpdates;
        NotifyAchievementsBox.IsChecked = settings.NotifyAchievements;
        WeeklyWatchSummaryBox.IsChecked = settings.WeeklyWatchSummary;
        QuietHoursEnabledBox.IsChecked = settings.QuietHoursEnabled;
        var hourChoices = Enumerable.Range(0, 24).Select(hour => new NotificationHourChoice(hour, $"{hour:00}:00")).ToArray();
        QuietHoursStartBox.ItemsSource = hourChoices;
        QuietHoursEndBox.ItemsSource = hourChoices;
        QuietHoursStartBox.SelectedValue = settings.QuietHoursStartHour;
        QuietHoursEndBox.SelectedValue = settings.QuietHoursEndHour;
        NotificationDeliveryModeBox.SelectedValue = settings.NotificationDeliveryMode.ToString();
        AutomaticBackupEnabledBox.IsChecked = settings.AutomaticBackupEnabled;
        AutomaticBackupFrequencyBox.SelectedValue = settings.AutomaticBackupFrequency.ToString();
        AutomaticBackupRetentionSlider.Value = settings.AutomaticBackupRetention;
        AutomaticBackupDirectoryBox.Text = settings.AutomaticBackupDirectory ?? AniTSystemSettings.Default.AutomaticBackupDirectory ?? string.Empty;
        PersonalDataExportFormatBox.SelectedValue = settings.PersonalDataExportFormat.ToString();
        PrepareFutureSyncBox.IsChecked = settings.PrepareFutureSync;
        OnlineConnectionLimitSlider.Value = settings.OnlineConnectionLimit;
        OfflineModeBox.IsChecked = settings.OfflineMode;
        OnlineTimeoutSlider.Value = settings.OnlineSourceTimeoutSeconds;
        DiagnosticLoggingBox.IsChecked = settings.DiagnosticLoggingEnabled;
        DeveloperModeBox.IsChecked = settings.DeveloperMode;
        LoadShortcutControls(settings.KeyboardShortcuts ?? AniTSystemSettings.DefaultKeyboardShortcuts);
        RefreshDeveloperInfo();
        ResetSettingsSectionBox.SelectedValue = SettingsSection.General.ToString();
        ReloadLocalProfiles(profileDraft.ProfileId);
        LoadProfileDraft(profileDraft);
        ArtworkDirectoryBox.Text = settings.ArtworkDirectory ?? AniTSystemSettings.Default.ArtworkDirectory ?? string.Empty;
        CacheDirectoryBox.Text = settings.ImageCacheDirectory ?? AniTSystemSettings.Default.ImageCacheDirectory ?? string.Empty;
        DuplicateHandlingBox.SelectedValue = settings.DuplicateHandling.ToString();
        IgnoredFoldersBox.Text = string.Join(Environment.NewLine, settings.IgnoredFolders ?? []);
        IgnoredFilesBox.Text = string.Join(Environment.NewLine, settings.IgnoredFiles ?? []);
        IgnoredWordsBox.Text = string.Join(Environment.NewLine, settings.IgnoredWords ?? []);
        ThemeModeBox.SelectedValue = settings.ThemeMode.ToString();
        AccentColorBox.Text = settings.AccentColor;
        GlowIntensitySlider.Value = settings.GlowIntensity;
        CardSizeBox.SelectedValue = settings.CardSize.ToString();
        CardsPerRowSlider.Value = settings.CardsPerRow;
        CornerRadiusSlider.Value = settings.CornerRadius;
        AnimationIntensitySlider.Value = settings.AnimationIntensity;
        ReduceMotionBox.IsChecked = settings.ReduceMotion;
        InterfaceScaleSlider.Value = settings.InterfaceScalePercent;
        TextScaleSlider.Value = settings.TextScalePercent;
        ArtworkSourcePriorityBox.SelectedValue = settings.ArtworkSourcePriority.ToString();
        ImageMaxDimensionBox.SelectedValue = settings.ImageMaxDimension.ToString();
        ImageCacheLimitSlider.Value = settings.ImageCacheLimitMb;
        OnlySfwArtworkBox.IsChecked = settings.OnlySfwArtwork;
        PreferLocalArtworkBox.IsChecked = settings.PreferLocalArtwork;
        ArtworkRefreshBox.SelectedValue = settings.ArtworkRefreshDays.ToString();
        ArtworkStretchBox.SelectedValue = settings.ArtworkStretchMode.ToString();
        ArtworkFocusXSlider.Value = settings.ArtworkFocusXPercent;
        ArtworkFocusYSlider.Value = settings.ArtworkFocusYPercent;
        UpdateImageCacheStatus(settings);
        UpdateLockedArtworkCount(settings);
        foreach (var (page, label) in PageMascotSettingItem.Pages)
        {
            var selected = settings.PageMascots is not null && settings.PageMascots.TryGetValue(page, out var mascot) ? mascot : "default";
            PageMascots.Add(new PageMascotSettingItem(page, label, selected));
        }
        foreach (var source in settings.ImageSources) ImageSources.Add(SystemImageSourceItem.From(source));
        DataContext = this;
        SelectSection(initialSection);
    }

    public void SelectSection(SettingsSection section)
    {
        if (SettingsTabs is null) return;
        var index = (int)section;
        SettingsTabs.SelectedIndex = index;
        if (GeneralCategory is null) return;
        GeneralCategory.IsChecked = section == SettingsSection.General;
        AppearanceCategory.IsChecked = section == SettingsSection.Appearance;
        ShelfCategory.IsChecked = section == SettingsSection.Shelf;
        PlaybackCategory.IsChecked = section == SettingsSection.Playback;
        OrganizationCategory.IsChecked = section == SettingsSection.Organization;
        NotificationsCategory.IsChecked = section == SettingsSection.Notifications;
        ProfilePrivacyCategory.IsChecked = section == SettingsSection.ProfilePrivacy;
        ExperienceCategory.IsChecked = section == SettingsSection.Experience;
        ImagesCategory.IsChecked = section == SettingsSection.Images;
        BackupCategory.IsChecked = section == SettingsSection.Backup;
        AdvancedCategory.IsChecked = section == SettingsSection.Advanced;
        if (section == SettingsSection.Shelf) ShelfSettings?.Reload();
    }

    private void Category_Checked(object sender, RoutedEventArgs e)
    {
        if (SettingsTabs is null) return;
        SettingsTabs.SelectedIndex = sender == GeneralCategory ? 0
            : sender == PlaybackCategory ? 1
            : sender == AppearanceCategory ? 2
            : sender == ShelfCategory ? 3
            : sender == ExperienceCategory ? 4
            : sender == ImagesCategory ? 5
            : sender == BackupCategory ? 6
            : sender == OrganizationCategory ? 7
            : sender == NotificationsCategory ? 8
            : sender == ProfilePrivacyCategory ? 9
            : 10;
    }

    private void BannerIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BannerIntervalText is not null) BannerIntervalText.Text = $"{Math.Round(e.NewValue):0} segundos";
        SaveStatusText?.ClearValue(TextBlock.TextProperty);
    }

    private void WatchedThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (WatchedThresholdText is not null) WatchedThresholdText.Text = $"{Math.Round(e.NewValue):0}%";
        SaveStatusText?.ClearValue(TextBlock.TextProperty);
    }

    private void ProgressSaveIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ProgressSaveIntervalText is not null) ProgressSaveIntervalText.Text = $"{Math.Round(e.NewValue):0} s";
        SaveStatusText?.ClearValue(TextBlock.TextProperty);
    }

    private void HomeSectionsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        homeSectionDragStart = e.GetPosition(HomeSectionsList);
        draggedHomeSection = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as HomeSectionSettingItem;
    }

    private void HomeSectionsList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || draggedHomeSection is null) return;
        var position = e.GetPosition(HomeSectionsList);
        if (Math.Abs(position.X - homeSectionDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - homeSectionDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop(HomeSectionsList, draggedHomeSection, DragDropEffects.Move);
    }

    private void HomeSectionsList_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(HomeSectionSettingItem)) || e.Data.GetData(typeof(HomeSectionSettingItem)) is not HomeSectionSettingItem source) return;
        var target = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as HomeSectionSettingItem;
        if (target is null || ReferenceEquals(source, target)) return;
        var oldIndex = HomeSections.IndexOf(source);
        var newIndex = HomeSections.IndexOf(target);
        if (oldIndex >= 0 && newIndex >= 0) HomeSections.Move(oldIndex, newIndex);
        var recentIndex = HomeSections.ToList().FindIndex(item => item.Id == "recent");
        var highlightsIndex = HomeSections.ToList().FindIndex(item => item.Id == "highlights");
        if (recentIndex >= 0 && highlightsIndex >= 0)
        {
            HomePriorityBox.SelectedValue = highlightsIndex < recentIndex ? HomeContentPriority.FavoritesFirst.ToString() : HomeContentPriority.RecentFirst.ToString();
        }
        draggedHomeSection = null;
        SaveStatusText.Text = "Ordem alterada. Salve para aplicar na página inicial.";
    }

    private void HomePriorityBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyHomePriorityToSectionList();

    private void ApplyHomePriorityToSectionList()
    {
        var recent = HomeSections.FirstOrDefault(item => item.Id == "recent");
        var highlights = HomeSections.FirstOrDefault(item => item.Id == "highlights");
        if (recent is null || highlights is null) return;
        var recentIndex = HomeSections.IndexOf(recent);
        var highlightsIndex = HomeSections.IndexOf(highlights);
        var favoritesFirst = HomePriorityBox.SelectedValue?.ToString() == HomeContentPriority.FavoritesFirst.ToString();
        if ((favoritesFirst && highlightsIndex > recentIndex) || (!favoritesFirst && recentIndex > highlightsIndex))
        {
            HomeSections.Move(favoritesFirst ? highlightsIndex : recentIndex, favoritesFirst ? recentIndex : highlightsIndex);
        }
    }

    private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T match) return match;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    private void LoadingDurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LoadingDurationText is not null) LoadingDurationText.Text = $"{Math.Round(e.NewValue):0} ms";
        SaveStatusText?.ClearValue(TextBlock.TextProperty);
    }

    private void BackgroundScanSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BackgroundScanText is null) return;
        var minutes = (int)Math.Round(e.NewValue);
        BackgroundScanText.Text = minutes == 0 ? "desativada" : minutes == 1 ? "1 minuto" : $"{minutes} minutos";
        SaveStatusText?.ClearValue(TextBlock.TextProperty);
    }

    private void GlowIntensitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (GlowIntensityText is not null) GlowIntensityText.Text = $"{Math.Round(e.NewValue):0}%";
    }

    private void CardsPerRowSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CardsPerRowText is null) return;
        var value = (int)Math.Round(e.NewValue);
        CardsPerRowText.Text = value == 0 ? "Automático" : $"{value} por linha";
    }

    private void CornerRadiusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CornerRadiusText is not null) CornerRadiusText.Text = $"{Math.Round(e.NewValue):0} px";
    }

    private void AnimationIntensitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AnimationIntensityText is not null) AnimationIntensityText.Text = $"{Math.Round(e.NewValue):0}%";
    }

    private void InterfaceScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (InterfaceScaleText is not null) InterfaceScaleText.Text = $"{Math.Round(e.NewValue):0}%";
    }

    private void TextScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TextScaleText is not null) TextScaleText.Text = $"{Math.Round(e.NewValue):0}%";
    }

    private void ImageCacheLimitSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ImageCacheLimitText is null) return;
        var megabytes = (int)Math.Round(e.NewValue);
        ImageCacheLimitText.Text = megabytes >= 1024 ? $"{megabytes / 1024d:0.#} GB" : $"{megabytes} MB";
    }

    private void ArtworkFocusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ArtworkFocusXText is not null) ArtworkFocusXText.Text = $"{Math.Round(ArtworkFocusXSlider.Value):0}%";
        if (ArtworkFocusYText is not null) ArtworkFocusYText.Text = $"{Math.Round(ArtworkFocusYSlider.Value):0}%";
    }

    private async void ClearImageCache_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Limpar as miniaturas e fan arts armazenadas? Capas principais e artes bloqueadas serão preservadas.", "Limpar cache de imagens", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        ImageCacheStatusText.Text = "Limpando cache…";
        await App.ClearArtworkCacheAsync(rebuild: false);
        UpdateImageCacheStatus(AniTSystemSettingsStore.Load(), "✓ Cache temporário limpo.");
    }

    private async void RebuildImageCache_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Reconstruir o cache remove miniaturas, fan arts e marcadores de falha para que sejam baixados novamente quando cada anime for aberto. Artes bloqueadas serão preservadas. Continuar?", "Reconstruir cache", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        ImageCacheStatusText.Text = "Preparando reconstrução…";
        await App.ClearArtworkCacheAsync(rebuild: true);
        UpdateImageCacheStatus(AniTSystemSettingsStore.Load(), "✓ Cache preparado. As artes serão reconstruídas sob demanda.");
    }

    private void UnlockAllArtwork_Click(object sender, RoutedEventArgs e)
    {
        var settings = AniTSystemSettingsStore.Load();
        if (settings.LockedArtwork is null || settings.LockedArtwork.Count == 0) return;
        if (MessageBox.Show("Permitir que todas as artes bloqueadas voltem a receber atualizações automáticas?", "Desbloquear artes", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        AniTSystemSettingsStore.Save(settings with { LockedArtwork = new Dictionary<string, string>() });
        UpdateLockedArtworkCount(AniTSystemSettingsStore.Load());
        SaveStatusText.Text = "✓ Todas as artes foram desbloqueadas.";
    }

    private void UpdateImageCacheStatus(AniTSystemSettings settings, string? prefix = null)
    {
        if (ImageCacheStatusText is null) return;
        ArtworkCacheManager.EnsureManagedCache(settings);
        var bytes = ArtworkCacheManager.GetSizeBytes(settings);
        var size = bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.00} GB" : $"{bytes / (1024d * 1024):0.0} MB";
        ImageCacheStatusText.Text = string.IsNullOrWhiteSpace(prefix) ? $"Em uso: {size}" : $"{prefix} Em uso: {size}";
    }

    private void UpdateLockedArtworkCount(AniTSystemSettings settings)
    {
        if (LockedArtworkCountText is null) return;
        var count = settings.LockedArtwork?.Count ?? 0;
        LockedArtworkCountText.Text = count == 0 ? "Nenhuma arte bloqueada" : $"{count} arte{(count == 1 ? string.Empty : "s")} protegida{(count == 1 ? string.Empty : "s")}";
    }

    private void AccentColorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (AccentPreview is null) return;
        try
        {
            AccentPreview.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(AccentColorBox.Text));
        }
        catch { AccentPreview.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(66, 200, 255)); }
    }

    private void AccentPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string color }) AccentColorBox.Text = color;
    }

    private void AddSource_Click(object sender, RoutedEventArgs e)
    {
        var source = new CustomImageSourceSettings(
            Guid.NewGuid(),
            SourceNameBox.Text.Trim(),
            SourceUrlBox.Text.Trim(),
            UseForBannersBox.IsChecked == true,
            UseForCoversBox.IsChecked == true,
            IsEnabled: true,
            IsSfw: SourceIsSfwBox.IsChecked == true);
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
        SourceIsSfwBox.IsChecked = true;
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
        if (!UpdateProfileDraftFromControls()) return;
        if (!TryReadShortcuts(out var keyboardShortcuts))
        {
            SelectSection(SettingsSection.Advanced);
            return;
        }
        var sources = ImageSources.Select(item => item.ToSettings()).ToArray();
        foreach (var source in sources)
        {
            if (AniTSystemSettingsStore.TryValidateSource(source, out var error)) continue;
            ValidationText.Text = $"{source.Name}: {error}";
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        var extensions = SplitValues(VideoExtensionsBox.Text);
        if (extensions.Count == 0)
        {
            LibrarySettingsStatusText.Text = "Informe pelo menos uma extensão de vídeo aceita.";
            SelectSection(SettingsSection.Shelf);
            return;
        }

        var artworkDirectory = NormalizeDirectoryInput(ArtworkDirectoryBox.Text, AniTSystemSettings.Default.ArtworkDirectory!);
        var cacheDirectory = NormalizeDirectoryInput(CacheDirectoryBox.Text, AniTSystemSettings.Default.ImageCacheDirectory!);
        var duplicateHandling = Enum.TryParse<LibraryDuplicateHandling>(DuplicateHandlingBox.SelectedValue?.ToString(), out var parsedDuplicateHandling)
            ? parsedDuplicateHandling
            : LibraryDuplicateHandling.KeepAndMark;
        var existingSettings = AniTSystemSettingsStore.Load();
        var settings = new AniTSystemSettings(
            (int)Math.Round(BannerIntervalSlider.Value),
            sources,
            StartupScanBox.IsChecked == true,
            StartupMetadataBox.IsChecked == true,
            EnableTransitionsBox.IsChecked == true,
            (int)Math.Round(LoadingDurationSlider.Value),
            (int)Math.Round(BackgroundScanSlider.Value),
            extensions,
            RecognizeSeasonEpisodeCodesBox.IsChecked == true,
            RecognizeEpisodePrefixesBox.IsChecked == true,
            RecognizeDashNumbersBox.IsChecked == true,
            RecognizeBracketNumbersBox.IsChecked == true,
            RecognizeTrailingNumbersBox.IsChecked == true,
            UseFolderSeasonBox.IsChecked == true,
            RecognizeSpecialsBox.IsChecked == true,
            artworkDirectory,
            cacheDirectory,
            duplicateHandling,
            SplitValues(IgnoredFoldersBox.Text),
            SplitValues(IgnoredFilesBox.Text),
            SplitValues(IgnoredWordsBox.Text),
            Enum.TryParse<AppearanceThemeMode>(ThemeModeBox.SelectedValue?.ToString(), out var themeMode) ? themeMode : AppearanceThemeMode.Dark,
            AccentColorBox.Text,
            (int)Math.Round(GlowIntensitySlider.Value),
            Enum.TryParse<AppearanceCardSize>(CardSizeBox.SelectedValue?.ToString(), out var cardSize) ? cardSize : AppearanceCardSize.Comfortable,
            (int)Math.Round(CardsPerRowSlider.Value),
            (int)Math.Round(CornerRadiusSlider.Value),
            (int)Math.Round(AnimationIntensitySlider.Value),
            ReduceMotionBox.IsChecked == true,
            (int)Math.Round(InterfaceScaleSlider.Value),
            (int)Math.Round(TextScaleSlider.Value),
            PageMascots.ToDictionary(item => item.Page, item => item.SelectedMascot, StringComparer.OrdinalIgnoreCase),
            BannerAutoRotateBox.IsChecked == true,
            HomeSections.Select(item => item.Id).ToArray(),
            HomeSections.Where(item => !item.IsVisible).Select(item => item.Id).ToArray(),
            SelectedCount(ContinueItemsBox, 4),
            SelectedCount(RecentItemsBox, 5),
            SelectedCount(FavoriteItemsBox, 4),
            SelectedCount(TopRatedItemsBox, 4),
            Enum.TryParse<HomeContinueBehavior>(ContinueBehaviorBox.SelectedValue?.ToString(), out var continueBehavior) ? continueBehavior : HomeContinueBehavior.ResumePlayback,
            HideCompletedFromContinueBox.IsChecked == true,
            Enum.TryParse<HomeContentPriority>(HomePriorityBox.SelectedValue?.ToString(), out var homePriority) ? homePriority : HomeContentPriority.RecentFirst,
            Enum.TryParse<ArtworkSourcePriority>(ArtworkSourcePriorityBox.SelectedValue?.ToString(), out var sourcePriority) ? sourcePriority : ArtworkSourcePriority.AniListFirst,
            SelectedCount(ImageMaxDimensionBox, 2560),
            (int)Math.Round(ImageCacheLimitSlider.Value),
            OnlySfwArtworkBox.IsChecked == true,
            PreferLocalArtworkBox.IsChecked == true,
            SelectedCount(ArtworkRefreshBox, 14),
            Enum.TryParse<ArtworkStretchMode>(ArtworkStretchBox.SelectedValue?.ToString(), out var stretchMode) ? stretchMode : ArtworkStretchMode.UniformToFill,
            (int)Math.Round(ArtworkFocusXSlider.Value),
            (int)Math.Round(ArtworkFocusYSlider.Value),
            existingSettings.LockedArtwork,
            Enum.TryParse<PlaybackPlayerMode>(PlaybackPlayerModeBox.SelectedValue?.ToString(), out var playerMode) ? playerMode : PlaybackPlayerMode.IntegratedMpcHc,
            ResumePlaybackBox.IsChecked == true,
            (int)Math.Round(WatchedThresholdSlider.Value),
            AutoPlayNextEpisodeBox.IsChecked == true,
            SkipOpeningBox.IsChecked == true,
            SkipEndingBox.IsChecked == true,
            PreferredAudioLanguageBox.SelectedValue?.ToString() ?? "ja",
            PreferredSubtitleLanguageBox.SelectedValue?.ToString() ?? "pt-BR",
            double.TryParse(PlaybackSpeedBox.SelectedValue?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var playbackSpeed) ? playbackSpeed : 1d,
            StartPlaybackFullscreenBox.IsChecked == true,
            (int)Math.Round(ProgressSaveIntervalSlider.Value),
            Enum.TryParse<EpisodeNumberDisplayFormat>(EpisodeNumberFormatBox.SelectedValue?.ToString(), out var episodeFormat) ? episodeFormat : EpisodeNumberDisplayFormat.NumberOnly,
            Enum.TryParse<AnimeTitlePreference>(AnimeTitlePreferenceBox.SelectedValue?.ToString(), out var titlePreference) ? titlePreference : AnimeTitlePreference.Romaji,
            Enum.TryParse<LibraryDefaultSort>(LibraryDefaultSortBox.SelectedValue?.ToString(), out var librarySort) ? librarySort : LibraryDefaultSort.RecentlyAdded,
            SplitValues(CustomTagsBox.Text),
            SplitValues(UserCollectionsBox.Text),
            FavoritesFirstRuleBox.IsChecked == true,
            InProgressFirstRuleBox.IsChecked == true,
            NotifyNewEpisodesBox.IsChecked == true,
            NotifyMetadataUpdatesBox.IsChecked == true,
            NotifyAchievementsBox.IsChecked == true,
            WeeklyWatchSummaryBox.IsChecked == true,
            QuietHoursEnabledBox.IsChecked == true,
            QuietHoursStartBox.SelectedValue is int quietStart ? quietStart : 22,
            QuietHoursEndBox.SelectedValue is int quietEnd ? quietEnd : 8,
            Enum.TryParse<NotificationDeliveryMode>(NotificationDeliveryModeBox.SelectedValue?.ToString(), out var deliveryMode) ? deliveryMode : NotificationDeliveryMode.InAppOnly,
            AutomaticBackupEnabledBox.IsChecked == true,
            Enum.TryParse<AutomaticBackupFrequency>(AutomaticBackupFrequencyBox.SelectedValue?.ToString(), out var backupFrequency) ? backupFrequency : AutomaticBackupFrequency.Daily,
            (int)Math.Round(AutomaticBackupRetentionSlider.Value),
            NormalizeDirectoryInput(AutomaticBackupDirectoryBox.Text, AniTSystemSettings.Default.AutomaticBackupDirectory!),
            Enum.TryParse<PersonalDataExportFormat>(PersonalDataExportFormatBox.SelectedValue?.ToString(), out var exportFormat) ? exportFormat : PersonalDataExportFormat.Json,
            PrepareFutureSyncBox.IsChecked == true,
            (int)Math.Round(OnlineConnectionLimitSlider.Value),
            OfflineModeBox.IsChecked == true,
            (int)Math.Round(OnlineTimeoutSlider.Value),
            DiagnosticLoggingBox.IsChecked == true,
            keyboardShortcuts,
            DeveloperModeBox.IsChecked == true);
        AniTSystemSettingsStore.Save(settings);
        ProfileSettingsStore.Save(profileDraft);
        var persistedSettings = AniTSystemSettingsStore.Load();
        Directory.CreateDirectory(artworkDirectory);
        Directory.CreateDirectory(cacheDirectory);
        App.ApplyLibrarySettings(persistedSettings);
        ThemeModeBox.SelectedValue = persistedSettings.ThemeMode.ToString();
        ValidationText.Visibility = Visibility.Collapsed;
        SaveStatusText.Text = $"✓ Configurações salvas. Tema {ThemeLabel(persistedSettings.ThemeMode)} será mantido no próximo uso.";
        LibrarySettingsStatusText.Text = "✓ Regras da biblioteca aplicadas. A próxima leitura já usará estas preferências.";
        UpdateImageCacheStatus(persistedSettings);
    }

    private void OpenRenamePreview_Click(object sender, RoutedEventArgs e)
    {
        var window = new OrganizationPreviewWindow { Owner = this };
        window.ShowDialog();
    }

    private void OpenCatalogCorrection_Click(object sender, RoutedEventArgs e)
    {
        var window = new CatalogCorrectionWindow { Owner = this };
        window.ShowDialog();
    }

    private async void TestNotification_Click(object sender, RoutedEventArgs e)
    {
        var mode = Enum.TryParse<NotificationDeliveryMode>(NotificationDeliveryModeBox.SelectedValue?.ToString(), out var selected)
            ? selected
            : NotificationDeliveryMode.InAppOnly;
        await AniTNotificationService.ShowTestAsync(mode);
        SaveStatusText.Text = "✓ Notificação de teste enviada com o canal selecionado.";
    }

    private static string ThemeLabel(AppearanceThemeMode mode) => mode switch
    {
        AppearanceThemeMode.Light => "claro",
        AppearanceThemeMode.Automatic => "automático",
        _ => "escuro"
    };

    private void ChooseArtworkDirectory_Click(object sender, RoutedEventArgs e) =>
        ChooseDirectory(ArtworkDirectoryBox, "Escolha onde o AniT deve guardar capas e artes");

    private void ChooseCacheDirectory_Click(object sender, RoutedEventArgs e) =>
        ChooseDirectory(CacheDirectoryBox, "Escolha a pasta de cache do AniT");

    private static void ChooseDirectory(TextBox target, string title)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = title,
            InitialDirectory = Directory.Exists(target.Text) ? target.Text : string.Empty
        };
        if (dialog.ShowDialog() == true) target.Text = dialog.FolderName;
    }

    private async void RebuildCatalog_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                "O AniT vai apagar somente o índice dos arquivos e reexaminar todas as pastas ativas. Seu histórico, progresso, notas, favoritos e conquistas serão mantidos. Continuar?",
                "Reconstruir catálogo",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        RebuildCatalogButton.IsEnabled = false;
        LibrarySettingsStatusText.Text = "Reconstruindo o catálogo e reassociando os episódios…";
        try
        {
            await using var context = App.OpenFreshDatabase();
            await context.LibraryReviewItems.ExecuteDeleteAsync();
            await context.MediaFiles.ExecuteDeleteAsync();
            var rootIds = await context.LibraryRoots.AsNoTracking().Where(root => root.IsEnabled).Select(root => root.Id).ToArrayAsync();
            var files = 0;
            var episodes = 0;
            var reviews = 0;
            foreach (var rootId in rootIds)
            {
                await using var scanContext = App.OpenFreshDatabase();
                var root = await scanContext.LibraryRoots.FirstOrDefaultAsync(item => item.Id == rootId);
                if (root is null || !Directory.Exists(root.Path)) continue;
                var result = await new LibraryScanner(scanContext, settings: AniTSystemSettingsStore.Load())
                    .ScanAsync(root, preferExistingCatalog: true);
                files += result.FilesFound;
                episodes += result.EpisodesAdded;
                reviews += result.NeedsReview;
            }

            App.Database.ChangeTracker.Clear();
            LibrarySettingsStatusText.Text = reviews > 0
                ? $"✓ Catálogo reconstruído: {files} arquivo(s), {episodes} episódio(s) novo(s) e {reviews} item(ns) para revisar."
                : $"✓ Catálogo reconstruído: {files} arquivo(s) reassociados e {episodes} episódio(s) novo(s).";
            ShelfSettings.Reload();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"AniT could not rebuild the catalog: {exception}");
            LibrarySettingsStatusText.Text = "Não foi possível reconstruir o catálogo. Nenhum histórico pessoal foi removido.";
        }
        finally
        {
            RebuildCatalogButton.IsEnabled = true;
        }
    }

    private static IReadOnlyList<string> SplitValues(string? value) => (value ?? string.Empty)
        .Split([',', ';', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static int SelectedCount(ComboBox comboBox, int fallback) =>
        int.TryParse(comboBox.SelectedValue?.ToString(), out var value) ? value : fallback;

    private static string NormalizeDirectoryInput(string? value, string fallback)
    {
        try { return Path.GetFullPath(string.IsNullOrWhiteSpace(value) ? fallback : value.Trim()); }
        catch { return Path.GetFullPath(fallback); }
    }

    private void ReloadLocalProfiles(Guid selectedId)
    {
        LocalProfiles.Clear();
        foreach (var profile in ProfileSettingsStore.GetProfiles()) LocalProfiles.Add(profile);
        LocalProfileSelectorBox.ItemsSource = LocalProfiles;
        LocalProfileSelectorBox.SelectedValue = LocalProfiles.Any(profile => profile.ProfileId == selectedId)
            ? selectedId
            : ProfileSettingsStore.ActiveProfileId;
    }

    private void LoadProfileDraft(ProfileSettings profile)
    {
        profileDraft = profile;
        ProfileDisplayNameBox.Text = profile.DisplayName;
        ProfileDisplayTitleBox.Text = profile.DisplayTitle;
        ProfileBioBox.Text = profile.Bio;
        ProfileVisibilityBox.SelectedValue = profile.IsPublic ? "Public" : "Local";
        HideProfileHistoryBox.IsChecked = profile.HideHistory;
        HideProfileRatingsBox.IsChecked = profile.HideRatings;
        HideProfileFavoritesBox.IsChecked = profile.HideFavorites;
        ProfileAvatarSizeSlider.Value = profile.AvatarSize;
        ProfileAvatarZoomSlider.Value = profile.AvatarZoomPercent;
        ProfileAvatarFocusXSlider.Value = profile.AvatarFocusXPercent;
        ProfileAvatarFocusYSlider.Value = profile.AvatarFocusYPercent;
        RefreshProfilePreview();
    }

    private bool UpdateProfileDraftFromControls()
    {
        var name = ProfileDisplayNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ProfileManagementStatusText.Text = "Informe um nome para o perfil.";
            SelectSection(SettingsSection.ProfilePrivacy);
            ProfileDisplayNameBox.Focus();
            return false;
        }

        profileDraft = profileDraft with
        {
            DisplayName = name,
            DisplayTitle = ProfileDisplayTitleBox.Text.Trim(),
            Bio = ProfileBioBox.Text.Trim(),
            IsPublic = string.Equals(ProfileVisibilityBox.SelectedValue?.ToString(), "Public", StringComparison.OrdinalIgnoreCase),
            HideHistory = HideProfileHistoryBox.IsChecked == true,
            HideRatings = HideProfileRatingsBox.IsChecked == true,
            HideFavorites = HideProfileFavoritesBox.IsChecked == true,
            AvatarSize = Math.Round(ProfileAvatarSizeSlider.Value),
            AvatarZoomPercent = (int)Math.Round(ProfileAvatarZoomSlider.Value),
            AvatarFocusXPercent = (int)Math.Round(ProfileAvatarFocusXSlider.Value),
            AvatarFocusYPercent = (int)Math.Round(ProfileAvatarFocusYSlider.Value)
        };
        return true;
    }

    private void RefreshProfilePreview()
    {
        if (ProfileAvatarPreviewBrush is null || ProfileBannerPreview is null) return;
        try
        {
            var avatar = !string.IsNullOrWhiteSpace(profileDraft.AvatarPath) && File.Exists(profileDraft.AvatarPath)
                ? profileDraft.AvatarPath
                : "Assets/Profile/1.png";
            ProfileAvatarPreviewBrush.ImageSource = ComfortableImageSource.Load(avatar, 420);
        }
        catch { ProfileAvatarPreviewBrush.ImageSource = ComfortableImageSource.Load("Assets/Profile/1.png", 420); }

        try
        {
            var banner = !string.IsNullOrWhiteSpace(profileDraft.BannerPath) && File.Exists(profileDraft.BannerPath)
                ? profileDraft.BannerPath
                : "Assets/History/2.png";
            ProfileBannerPreview.Source = ComfortableImageSource.Load(banner, 900);
        }
        catch { ProfileBannerPreview.Source = ComfortableImageSource.Load("Assets/History/2.png", 900); }

        var zoom = Math.Max(100, ProfileAvatarZoomSlider?.Value ?? profileDraft.AvatarZoomPercent);
        var visible = 100d / zoom;
        var focusX = (ProfileAvatarFocusXSlider?.Value ?? profileDraft.AvatarFocusXPercent) / 100d;
        var focusY = (ProfileAvatarFocusYSlider?.Value ?? profileDraft.AvatarFocusYPercent) / 100d;
        ProfileAvatarPreviewBrush.ViewboxUnits = BrushMappingMode.RelativeToBoundingBox;
        ProfileAvatarPreviewBrush.Viewbox = new Rect(focusX * (1 - visible), focusY * (1 - visible), visible, visible);
    }

    private void ProfileAvatarSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ProfileAvatarSizeText is not null) ProfileAvatarSizeText.Text = $"{Math.Round(ProfileAvatarSizeSlider.Value):0} px";
        if (ProfileAvatarZoomText is not null) ProfileAvatarZoomText.Text = $"{Math.Round(ProfileAvatarZoomSlider.Value):0}%";
        RefreshProfilePreview();
    }

    private void LocalProfileSelectorBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileManagementStatusText is null || LocalProfileSelectorBox.SelectedItem is not ProfileSettings selected) return;
        ProfileManagementStatusText.Text = selected.ProfileId == ProfileSettingsStore.ActiveProfileId
            ? "Este é o perfil ativo."
            : $"{selected.DisplayName} está pronto para ser ativado.";
    }

    private async void CreateProfile_Click(object sender, RoutedEventArgs e)
    {
        ProfileSettings? created = null;
        try
        {
            created = ProfileSettingsStore.Create(NewProfileNameBox.Text);
            await App.PrepareProfileDatabaseAsync(created.ProfileId);
            NewProfileNameBox.Clear();
            ReloadLocalProfiles(created.ProfileId);
            ProfileManagementStatusText.Text = $"✓ Perfil {created.DisplayName} criado com a mesma Biblioteca e uma jornada independente.";
        }
        catch (Exception exception)
        {
            if (created is not null) ProfileSettingsStore.Delete(created.ProfileId);
            ProfileManagementStatusText.Text = $"Não foi possível criar o perfil: {exception.Message}";
        }
    }

    private async void SwitchProfile_Click(object sender, RoutedEventArgs e)
    {
        if (LocalProfileSelectorBox.SelectedValue is not Guid selectedId || selectedId == ProfileSettingsStore.ActiveProfileId) return;
        var selected = LocalProfiles.First(profile => profile.ProfileId == selectedId);
        if (MessageBox.Show($"Trocar para {selected.DisplayName}? O AniT será reiniciado para manter as jornadas totalmente separadas.", "Trocar perfil", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (!UpdateProfileDraftFromControls()) return;
        ProfileSettingsStore.Save(profileDraft);
        IsEnabled = false;
        try { await App.SwitchProfileAsync(selectedId); }
        catch (Exception exception)
        {
            IsEnabled = true;
            ProfileManagementStatusText.Text = $"Não foi possível trocar de perfil: {exception.Message}";
        }
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (LocalProfileSelectorBox.SelectedItem is not ProfileSettings selected) return;
        if (selected.ProfileId == ProfileSettingsStore.ActiveProfileId)
        {
            ProfileManagementStatusText.Text = "Troque de perfil antes de excluir o perfil ativo.";
            return;
        }
        if (MessageBox.Show($"Excluir o perfil {selected.DisplayName} e toda a jornada separada dele?", "Excluir perfil", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (!ProfileSettingsStore.Delete(selected.ProfileId))
        {
            ProfileManagementStatusText.Text = "O perfil não pôde ser excluído.";
            return;
        }
        ReloadLocalProfiles(ProfileSettingsStore.ActiveProfileId);
        ProfileManagementStatusText.Text = "✓ Perfil e banco pessoal removidos.";
    }

    private void EditProfileArtwork_Click(object sender, RoutedEventArgs e)
    {
        UpdateProfileDraftFromControls();
        var fallback = !string.IsNullOrWhiteSpace(profileDraft.AvatarPath) ? profileDraft.AvatarPath : "Assets/Profile/1.png";
        var dialog = new ProfileEditWindow(profileDraft, fallback) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SavedSettings is not { } saved) return;
        try
        {
            profileDraft = saved with { AvatarPath = ProfileSettingsStore.PersistAvatar(saved.AvatarPath) };
            ProfileDisplayNameBox.Text = profileDraft.DisplayName;
            ProfileBioBox.Text = profileDraft.Bio;
            ProfileAvatarSizeSlider.Value = profileDraft.AvatarSize;
            RefreshProfilePreview();
            ProfileManagementStatusText.Text = "Avatar e capa atualizados. Salve as configurações para aplicar.";
        }
        catch (Exception exception) { ProfileManagementStatusText.Text = exception.Message; }
    }

    private async void ExportPersonalData_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Exportar meus dados pessoais do AniT",
            Filter = "Dados pessoais do AniT (*.json)|*.json",
            FileName = $"AniT-dados-{DateTime.Now:yyyy-MM-dd}.json",
            AddExtension = true,
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await App.ExportPersonalDataAsync(dialog.FileName);
            ProfileManagementStatusText.Text = "✓ Dados pessoais exportados em um arquivo JSON legível.";
        }
        catch (Exception exception) { ProfileManagementStatusText.Text = $"Não foi possível exportar: {exception.Message}"; }
    }

    private async void ClearProfileHistory_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Limpar todo o histórico e progresso deste perfil? Biblioteca, notas e favoritos serão preservados. Esta ação não pode ser desfeita sem um backup.", "Limpar histórico", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try
        {
            await App.ClearWatchHistoryAsync();
            ProfileManagementStatusText.Text = "✓ Histórico e progresso removidos; sua Biblioteca foi preservada.";
        }
        catch (Exception exception) { ProfileManagementStatusText.Text = $"Não foi possível limpar o histórico: {exception.Message}"; }
    }

    private void AdvancedNetworkSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OnlineConnectionLimitText is not null) OnlineConnectionLimitText.Text = $"{Math.Round(OnlineConnectionLimitSlider.Value):0}";
        if (OnlineTimeoutText is not null) OnlineTimeoutText.Text = $"{Math.Round(OnlineTimeoutSlider.Value):0} s";
        RefreshDeveloperInfo();
    }

    private void LoadShortcutControls(IReadOnlyDictionary<string, string> shortcuts)
    {
        string Get(string key) => shortcuts.TryGetValue(key, out var value)
            ? value
            : AniTSystemSettings.DefaultKeyboardShortcuts[key];
        ShortcutHomeBox.Text = Get("Home");
        ShortcutLibraryBox.Text = Get("Library");
        ShortcutSearchBox.Text = Get("Search");
        ShortcutHistoryBox.Text = Get("History");
        ShortcutSettingsBox.Text = Get("Settings");
    }

    private bool TryReadShortcuts(out IReadOnlyDictionary<string, string> shortcuts)
    {
        var candidates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Home"] = ShortcutHomeBox.Text.Trim(),
            ["Library"] = ShortcutLibraryBox.Text.Trim(),
            ["Search"] = ShortcutSearchBox.Text.Trim(),
            ["History"] = ShortcutHistoryBox.Text.Trim(),
            ["Settings"] = ShortcutSettingsBox.Text.Trim()
        };
        foreach (var item in candidates)
        {
            if (!ShortcutManager.TryParseGesture(item.Value, out _))
            {
                ShortcutValidationText.Text = $"O atalho de {item.Key} não é válido: “{item.Value}”.";
                shortcuts = candidates;
                return false;
            }
        }
        var duplicate = candidates.GroupBy(item => item.Value, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            ShortcutValidationText.Text = $"O atalho “{duplicate.Key}” foi usado em mais de uma ação.";
            shortcuts = candidates;
            return false;
        }
        ShortcutValidationText.Text = string.Empty;
        shortcuts = candidates;
        return true;
    }

    private void ResetShortcuts_Click(object sender, RoutedEventArgs e)
    {
        LoadShortcutControls(AniTSystemSettings.DefaultKeyboardShortcuts);
        ShortcutValidationText.Text = "✓ Atalhos padrão carregados. Salve para aplicar.";
    }

    private void ResetAdvanced_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Restaurar rede, diagnóstico, atalhos e modo desenvolvedor para os padrões?",
                "Restaurar Avançado", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        var defaults = AniTSystemSettings.Default;
        OnlineConnectionLimitSlider.Value = defaults.OnlineConnectionLimit;
        OfflineModeBox.IsChecked = defaults.OfflineMode;
        OnlineTimeoutSlider.Value = defaults.OnlineSourceTimeoutSeconds;
        DiagnosticLoggingBox.IsChecked = defaults.DiagnosticLoggingEnabled;
        DeveloperModeBox.IsChecked = defaults.DeveloperMode;
        LoadShortcutControls(defaults.KeyboardShortcuts ?? AniTSystemSettings.DefaultKeyboardShortcuts);
        AdvancedOperationStatusText.Text = "✓ Padrões avançados carregados. Use “Salvar configurações” para aplicar.";
    }

    private void DeveloperMode_Changed(object sender, RoutedEventArgs e) => RefreshDeveloperInfo();

    private void RefreshDeveloperInfo()
    {
        if (DeveloperInfoPanel is null || DeveloperInfoText is null) return;
        DeveloperInfoPanel.Visibility = DeveloperModeBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (DeveloperInfoPanel.Visibility != Visibility.Visible) return;
        var settings = AniTSystemSettingsStore.Load();
        var cache = settings.ImageCacheDirectory ?? AniTSystemSettings.Default.ImageCacheDirectory ?? "—";
        DeveloperInfoText.Text =
            $"Versão: {System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version}\n" +
            $"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}\n" +
            $"Sistema: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}\n" +
            $"Processo: {(Environment.Is64BitProcess ? "64 bits" : "32 bits")}\n" +
            $"Banco: {App.DatabasePath}\nDados: {App.DataRootPath}\nCache: {cache}\n" +
            $"Perfil: {ProfileSettingsStore.ActiveProfileId:N}\n" +
            $"Rede: {(OfflineModeBox.IsChecked == true ? "offline" : "online")} · {Math.Round(OnlineConnectionLimitSlider.Value):0} conexões · {Math.Round(OnlineTimeoutSlider.Value):0}s";
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void OpenCacheFolder_Click(object sender, RoutedEventArgs e)
    {
        try { OpenFolder(NormalizeDirectoryInput(CacheDirectoryBox.Text, AniTSystemSettings.Default.ImageCacheDirectory!)); }
        catch (Exception exception) { AdvancedOperationStatusText.Text = $"Não foi possível abrir o cache: {exception.Message}"; }
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        try { OpenFolder(App.DataRootPath); }
        catch (Exception exception) { AdvancedOperationStatusText.Text = $"Não foi possível abrir os dados: {exception.Message}"; }
    }

    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
    {
        try { OpenFolder(AniTDiagnostics.LogDirectory); }
        catch (Exception exception) { AdvancedOperationStatusText.Text = $"Não foi possível abrir os logs: {exception.Message}"; }
    }

    private bool PersistAdvancedControls()
    {
        if (!TryReadShortcuts(out var shortcuts)) return false;
        var current = AniTSystemSettingsStore.Load();
        AniTSystemSettingsStore.Save(current with
        {
            OnlineConnectionLimit = (int)Math.Round(OnlineConnectionLimitSlider.Value),
            OfflineMode = OfflineModeBox.IsChecked == true,
            OnlineSourceTimeoutSeconds = (int)Math.Round(OnlineTimeoutSlider.Value),
            DiagnosticLoggingEnabled = DiagnosticLoggingBox.IsChecked == true,
            KeyboardShortcuts = shortcuts,
            DeveloperMode = DeveloperModeBox.IsChecked == true
        });
        return true;
    }

    private async void ReprocessContent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string targetName } || !Enum.TryParse<AdvancedReprocessTarget>(targetName, out var target)) return;
        if (!PersistAdvancedControls()) return;
        if (target is AdvancedReprocessTarget.Metadata or AdvancedReprocessTarget.Images && OfflineModeBox.IsChecked == true)
        {
            AdvancedOperationStatusText.Text = "Desative o modo offline para reprocessar fontes online.";
            return;
        }
        try
        {
            IsEnabled = false;
            AdvancedOperationStatusText.Text = $"Reprocessando {targetName.ToLowerInvariant()}…";
            var count = await App.ReprocessContentAsync(target);
            AdvancedOperationStatusText.Text = target == AdvancedReprocessTarget.Files
                ? $"✓ {count} pasta(s) monitorada(s) relida(s)."
                : $"✓ {count} título(s) reprocessado(s).";
        }
        catch (Exception exception)
        {
            AniTDiagnostics.Write("REPROCESSAR", $"Falha em {target}", exception);
            AdvancedOperationStatusText.Text = $"Não foi possível concluir: {exception.Message}";
        }
        finally { IsEnabled = true; }
    }

    private void AutomaticBackupRetentionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AutomaticBackupRetentionText is not null)
            AutomaticBackupRetentionText.Text = $"{Math.Round(AutomaticBackupRetentionSlider.Value):0} arquivos";
    }

    private void ChooseAutomaticBackupDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Escolha onde o AniT guardará os backups automáticos",
            InitialDirectory = Directory.Exists(AutomaticBackupDirectoryBox.Text) ? AutomaticBackupDirectoryBox.Text : null
        };
        if (dialog.ShowDialog(this) == true) AutomaticBackupDirectoryBox.Text = dialog.FolderName;
    }

    private AniTSystemSettings PersistBackupControls()
    {
        var current = AniTSystemSettingsStore.Load();
        var updated = current with
        {
            AutomaticBackupEnabled = AutomaticBackupEnabledBox.IsChecked == true,
            AutomaticBackupFrequency = Enum.TryParse<AutomaticBackupFrequency>(AutomaticBackupFrequencyBox.SelectedValue?.ToString(), out var frequency) ? frequency : AutomaticBackupFrequency.Daily,
            AutomaticBackupRetention = (int)Math.Round(AutomaticBackupRetentionSlider.Value),
            AutomaticBackupDirectory = NormalizeDirectoryInput(AutomaticBackupDirectoryBox.Text, AniTSystemSettings.Default.AutomaticBackupDirectory!),
            PersonalDataExportFormat = Enum.TryParse<PersonalDataExportFormat>(PersonalDataExportFormatBox.SelectedValue?.ToString(), out var format) ? format : PersonalDataExportFormat.Json,
            PrepareFutureSync = PrepareFutureSyncBox.IsChecked == true
        };
        AniTSystemSettingsStore.Save(updated);
        return AniTSystemSettingsStore.Load();
    }

    private async void RunAutomaticBackupNow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PersistBackupControls();
            IsEnabled = false;
            BackupStatusText.Text = "Criando snapshot completo e aplicando a retenção…";
            var result = await App.RunAutomaticBackupAsync(force: true);
            BackupStatusText.Text = result is null
                ? "Outro backup já está em andamento."
                : $"✓ Backup salvo em {result.BackupPath}. {result.RemovedOlderBackups} backup(s) antigo(s) removido(s).";
        }
        catch (Exception exception) { BackupStatusText.Text = $"Não foi possível criar o backup: {exception.Message}"; }
        finally { IsEnabled = true; }
    }

    private async void ExportConfiguredData_Click(object sender, RoutedEventArgs e)
    {
        var settings = PersistBackupControls();
        var csv = settings.PersonalDataExportFormat == PersonalDataExportFormat.Csv;
        var dialog = new SaveFileDialog
        {
            Title = csv ? "Exportar dados do AniT em CSV" : "Exportar dados do AniT em JSON",
            Filter = csv ? "Planilha CSV (*.csv)|*.csv" : "Dados JSON (*.json)|*.json",
            FileName = $"AniT-dados-{DateTime.Now:yyyy-MM-dd}.{(csv ? "csv" : "json")}",
            AddExtension = true,
            DefaultExt = csv ? ".csv" : ".json"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (csv) await App.ExportPersonalDataCsvAsync(dialog.FileName);
            else await App.ExportPersonalDataAsync(dialog.FileName);
            BackupStatusText.Text = $"✓ Dados exportados em {(csv ? "CSV" : "JSON")}: {dialog.FileName}";
        }
        catch (Exception exception) { BackupStatusText.Text = $"Não foi possível exportar os dados: {exception.Message}"; }
    }

    private async void DiagnoseDatabase_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            DatabaseDiagnosticStatusText.Text = "Analisando integridade e relacionamentos…";
            var result = await App.DiagnoseDatabaseAsync();
            DatabaseDiagnosticStatusText.Text = result.IsHealthy
                ? $"✓ Banco íntegro · {FormatBytes(result.DatabaseSizeBytes)} · nenhuma relação inválida."
                : $"Atenção: {result.IntegrityResult}; {result.ForeignKeyProblems} relação(ões) inválida(s).";
        }
        catch (Exception exception) { DatabaseDiagnosticStatusText.Text = $"Falha no diagnóstico: {exception.Message}"; }
    }

    private async void RepairDatabase_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("O AniT criará um backup de recuperação e depois reconstruirá índices, otimizará e compactará o banco. Isso não substitui a restauração de um backup quando houver corrupção estrutural. Deseja continuar?",
                "Manutenção do banco de dados", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try
        {
            IsEnabled = false;
            DatabaseDiagnosticStatusText.Text = "Criando ponto de recuperação e executando manutenção…";
            var result = await App.RepairDatabaseAsync();
            DatabaseDiagnosticStatusText.Text = result.Diagnostic.IsHealthy
                ? $"✓ Manutenção concluída; banco íntegro · {FormatBytes(result.Diagnostic.DatabaseSizeBytes)}. Recuperação: {result.RecoveryBackup}"
                : $"Manutenção concluída, mas o banco ainda tem alertas: {result.Diagnostic.IntegrityResult}. Restaure o backup indicado se necessário: {result.RecoveryBackup}";
        }
        catch (Exception exception) { DatabaseDiagnosticStatusText.Text = $"A manutenção não foi concluída: {exception.Message}"; }
        finally { IsEnabled = true; }
    }

    private void ResetSettingsSection_Click(object sender, RoutedEventArgs e)
    {
        if (!Enum.TryParse<SettingsSection>(ResetSettingsSectionBox.SelectedValue?.ToString(), out var section)) return;
        var label = (ResetSettingsSectionBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "seção";
        if (MessageBox.Show($"Restaurar somente “{label}” para os padrões do AniT? Seus animes, perfil e histórico não serão removidos.",
                "Restaurar padrões", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        var current = AniTSystemSettingsStore.Load();
        var defaults = AniTSystemSettings.Default;
        var updated = section switch
        {
            SettingsSection.General => current with { HomeBannerIntervalSeconds = defaults.HomeBannerIntervalSeconds, HomeBannerAutoRotate = defaults.HomeBannerAutoRotate, HomeSectionOrder = defaults.HomeSectionOrder, HiddenHomeSections = defaults.HiddenHomeSections, HomeContinueItems = defaults.HomeContinueItems, HomeRecentItems = defaults.HomeRecentItems, HomeFavoriteItems = defaults.HomeFavoriteItems, HomeTopRatedItems = defaults.HomeTopRatedItems, HomeContinueBehavior = defaults.HomeContinueBehavior, HideCompletedFromContinue = defaults.HideCompletedFromContinue, HomeContentPriority = defaults.HomeContentPriority },
            SettingsSection.Appearance => current with { ThemeMode = defaults.ThemeMode, AccentColor = defaults.AccentColor, GlowIntensity = defaults.GlowIntensity, CardSize = defaults.CardSize, CardsPerRow = defaults.CardsPerRow, CornerRadius = defaults.CornerRadius, AnimationIntensity = defaults.AnimationIntensity, ReduceMotion = defaults.ReduceMotion, InterfaceScalePercent = defaults.InterfaceScalePercent, TextScalePercent = defaults.TextScalePercent, PageMascots = defaults.PageMascots },
            SettingsSection.Shelf => current with { ScanLibraryOnStartup = defaults.ScanLibraryOnStartup, RefreshMetadataOnStartup = defaults.RefreshMetadataOnStartup, LibraryBackgroundScanMinutes = defaults.LibraryBackgroundScanMinutes, VideoExtensions = defaults.VideoExtensions, RecognizeSeasonEpisodeCodes = defaults.RecognizeSeasonEpisodeCodes, RecognizeEpisodePrefixes = defaults.RecognizeEpisodePrefixes, RecognizeDashNumbers = defaults.RecognizeDashNumbers, RecognizeBracketNumbers = defaults.RecognizeBracketNumbers, RecognizeTrailingNumbers = defaults.RecognizeTrailingNumbers, UseFolderSeason = defaults.UseFolderSeason, RecognizeSpecials = defaults.RecognizeSpecials, DuplicateHandling = defaults.DuplicateHandling, IgnoredFolders = defaults.IgnoredFolders, IgnoredFiles = defaults.IgnoredFiles, IgnoredWords = defaults.IgnoredWords },
            SettingsSection.Playback => current with { PlaybackPlayerMode = defaults.PlaybackPlayerMode, ResumePlayback = defaults.ResumePlayback, WatchedThresholdPercent = defaults.WatchedThresholdPercent, AutoPlayNextEpisode = defaults.AutoPlayNextEpisode, SkipOpening = defaults.SkipOpening, SkipEnding = defaults.SkipEnding, PreferredAudioLanguage = defaults.PreferredAudioLanguage, PreferredSubtitleLanguage = defaults.PreferredSubtitleLanguage, DefaultPlaybackSpeed = defaults.DefaultPlaybackSpeed, StartPlaybackFullscreen = defaults.StartPlaybackFullscreen, ProgressSaveIntervalSeconds = defaults.ProgressSaveIntervalSeconds },
            SettingsSection.Organization => current with { EpisodeNumberDisplayFormat = defaults.EpisodeNumberDisplayFormat, AnimeTitlePreference = defaults.AnimeTitlePreference, LibraryDefaultSort = defaults.LibraryDefaultSort, CustomTags = defaults.CustomTags, UserCollections = defaults.UserCollections, FavoritesFirstRule = defaults.FavoritesFirstRule, InProgressFirstRule = defaults.InProgressFirstRule },
            SettingsSection.Notifications => current with { NotifyNewEpisodes = defaults.NotifyNewEpisodes, NotifyMetadataUpdates = defaults.NotifyMetadataUpdates, NotifyAchievements = defaults.NotifyAchievements, WeeklyWatchSummary = defaults.WeeklyWatchSummary, QuietHoursEnabled = defaults.QuietHoursEnabled, QuietHoursStartHour = defaults.QuietHoursStartHour, QuietHoursEndHour = defaults.QuietHoursEndHour, NotificationDeliveryMode = defaults.NotificationDeliveryMode },
            SettingsSection.Images => current with { ImageSources = defaults.ImageSources, ArtworkSourcePriority = defaults.ArtworkSourcePriority, ImageMaxDimension = defaults.ImageMaxDimension, ImageCacheLimitMb = defaults.ImageCacheLimitMb, OnlySfwArtwork = defaults.OnlySfwArtwork, PreferLocalArtwork = defaults.PreferLocalArtwork, ArtworkRefreshDays = defaults.ArtworkRefreshDays, ArtworkStretchMode = defaults.ArtworkStretchMode, ArtworkFocusXPercent = defaults.ArtworkFocusXPercent, ArtworkFocusYPercent = defaults.ArtworkFocusYPercent, LockedArtwork = defaults.LockedArtwork },
            SettingsSection.Backup => current with { AutomaticBackupEnabled = defaults.AutomaticBackupEnabled, AutomaticBackupFrequency = defaults.AutomaticBackupFrequency, AutomaticBackupRetention = defaults.AutomaticBackupRetention, AutomaticBackupDirectory = defaults.AutomaticBackupDirectory, PersonalDataExportFormat = defaults.PersonalDataExportFormat, PrepareFutureSync = defaults.PrepareFutureSync },
            SettingsSection.Advanced => current with { OnlineConnectionLimit = defaults.OnlineConnectionLimit, OfflineMode = defaults.OfflineMode, OnlineSourceTimeoutSeconds = defaults.OnlineSourceTimeoutSeconds, DiagnosticLoggingEnabled = defaults.DiagnosticLoggingEnabled, KeyboardShortcuts = defaults.KeyboardShortcuts, DeveloperMode = defaults.DeveloperMode },
            _ => current
        };
        AniTSystemSettingsStore.Save(updated);
        App.ApplyLibrarySettings(AniTSystemSettingsStore.Load());
        var replacement = new SystemSettingsWindow(section);
        replacement.Show();
        Close();
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.0} GB",
        >= 1024L * 1024 => $"{bytes / (1024d * 1024):0.0} MB",
        >= 1024 => $"{bytes / 1024d:0.0} KB",
        _ => $"{bytes} B"
    };

    private async void ExportBackup_Click(object sender, RoutedEventArgs e) =>
        await BackupController.ExportAsync(this, message => BackupStatusText.Text = message);

    private async void ImportBackup_Click(object sender, RoutedEventArgs e) =>
        await BackupController.ImportAsync(this, message => BackupStatusText.Text = message);

    private void Home_Click(object sender, RoutedEventArgs e) => AppNavigation.Home(this);
    private void Explore_Click(object sender, RoutedEventArgs e) => AppNavigation.Explore(this);
    private void Library_Click(object sender, RoutedEventArgs e) => AppNavigation.OpenLibrary(this);
    private void Calendar_Click(object sender, RoutedEventArgs e) => AppNavigation.Calendar(this);
    private void History_Click(object sender, RoutedEventArgs e) => AppNavigation.History(this);
    private void Achievements_Click(object sender, RoutedEventArgs e) => AppNavigation.Achievements(this);
    private void Profile_Click(object sender, RoutedEventArgs e) => AppNavigation.Profile(this);
}

public sealed record NotificationHourChoice(int Hour, string Label);

public sealed class PageMascotSettingItem
{
    public static IReadOnlyList<(string Page, string Label)> Pages { get; } =
    [
        ("Inicio", "Início"), ("Explorar", "Explorar"), ("Biblioteca", "Biblioteca"),
        ("Calendario", "Calendário"), ("Historico", "Histórico"), ("Perfil", "Perfil"),
        ("Anime", "Página do anime"), ("Configuracoes", "Configurações")
    ];
    public static IReadOnlyList<MascotChoice> Choices { get; } =
    [
        new("default", "Padrão da página"), new("normal", "Baki-Pi clássico"),
        new("happy", "Baki-Pi feliz"), new("happy2", "Baki-Pi animado"),
        new("angry", "Baki-Pi determinado"), new("sad", "Baki-Pi triste"),
        new("boring", "Baki-Pi entediado"), new("hidden", "Não exibir")
    ];

    public PageMascotSettingItem(string page, string label, string selectedMascot)
    {
        Page = page;
        Label = label;
        SelectedMascot = selectedMascot;
    }

    public string Page { get; }
    public string Label { get; }
    public string SelectedMascot { get; set; }
    public IReadOnlyList<MascotChoice> MascotChoices => Choices;
}

public sealed record MascotChoice(string Id, string Name)
{
    public override string ToString() => Name;
}

public sealed class HomeSectionSettingItem
{
    private static readonly IReadOnlyDictionary<string, (string Label, string Description)> Definitions =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["continue"] = ("Continuar assistindo", "Episódios em andamento e progresso salvo"),
            ["recent"] = ("Adicionados recentemente", "Novidades encontradas nas pastas monitoradas"),
            ["highlights"] = ("Favoritos e bem avaliados", "Seus destaques pessoais e melhores notas")
        };

    private HomeSectionSettingItem(string id, string label, string description, bool isVisible)
    {
        Id = id;
        Label = label;
        Description = description;
        IsVisible = isVisible;
    }

    public string Id { get; }
    public string Label { get; }
    public string Description { get; }
    public bool IsVisible { get; set; }

    public static HomeSectionSettingItem Create(string id, bool isVisible)
    {
        var normalized = id.ToLowerInvariant();
        var definition = Definitions.TryGetValue(normalized, out var value) ? value : (Label: normalized, Description: string.Empty);
        return new HomeSectionSettingItem(normalized, definition.Label, definition.Description, isVisible);
    }
}

public sealed class SystemImageSourceItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string SearchUrlTemplate { get; init; } = string.Empty;
    public bool UseForProfileBanners { get; set; }
    public bool UseForAnimeCovers { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsSfw { get; set; }

    public static SystemImageSourceItem From(CustomImageSourceSettings settings) => new()
    {
        Id = settings.Id,
        Name = settings.Name,
        SearchUrlTemplate = settings.SearchUrlTemplate,
        UseForProfileBanners = settings.UseForProfileBanners,
        UseForAnimeCovers = settings.UseForAnimeCovers,
        IsEnabled = settings.IsEnabled,
        IsSfw = settings.IsSfw
    };

    public CustomImageSourceSettings ToSettings() => new(
        Id,
        Name,
        SearchUrlTemplate,
        UseForProfileBanners,
        UseForAnimeCovers,
        IsEnabled,
        IsSfw);
}

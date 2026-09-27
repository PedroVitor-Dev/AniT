using AniT.Infrastructure;
using System.Text.Json;

namespace AniT.Tests;

public sealed class AniTSystemSettingsTests
{
    [Theory]
    [InlineData("https://localhost/search?q={query}")]
    [InlineData("https://127.0.0.1/search?q={query}")]
    [InlineData("https://192.168.1.20/search?q={query}")]
    [InlineData("http://example.com/search?q={query}")]
    public void CustomImageSources_RejectNonPublicOrInsecureAddresses(string template)
    {
        var source = new CustomImageSourceSettings(Guid.NewGuid(), "Fonte", template, true, true, true, true);

        Assert.False(AniTSystemSettingsStore.TryValidateSource(source, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void LegacyJson_UsesSafeDefaultsForNewPreferences()
    {
        var settings = JsonSerializer.Deserialize<AniTSystemSettings>("""
            {
              "HomeBannerIntervalSeconds": 12,
              "ImageSources": []
            }
            """);

        Assert.NotNull(settings);
        Assert.True(settings.ScanLibraryOnStartup);
        Assert.True(settings.RefreshMetadataOnStartup);
        Assert.True(settings.EnableNavigationLoading);
        Assert.Equal(620, settings.NavigationLoadingMilliseconds);
        Assert.Equal(15, settings.LibraryBackgroundScanMinutes);
        Assert.True(settings.RecognizeSpecials);
    }

    [Fact]
    public void NewPreferences_RoundTripWithoutLosingValues()
    {
        var expected = new AniTSystemSettings(9, [], false, true, false, 350);

        var actual = JsonSerializer.Deserialize<AniTSystemSettings>(JsonSerializer.Serialize(expected));

        Assert.NotNull(actual);
        Assert.Equal(expected.HomeBannerIntervalSeconds, actual.HomeBannerIntervalSeconds);
        Assert.Equal(expected.ScanLibraryOnStartup, actual.ScanLibraryOnStartup);
        Assert.Equal(expected.RefreshMetadataOnStartup, actual.RefreshMetadataOnStartup);
        Assert.Equal(expected.EnableNavigationLoading, actual.EnableNavigationLoading);
        Assert.Equal(expected.NavigationLoadingMilliseconds, actual.NavigationLoadingMilliseconds);
        Assert.Empty(actual.ImageSources);
    }

    [Fact]
    public void LibraryPreferences_RoundTripWithoutLosingValues()
    {
        var expected = new AniTSystemSettings(
            8,
            [],
            LibraryBackgroundScanMinutes: 30,
            VideoExtensions: [".mkv", ".ts"],
            RecognizeBracketNumbers: false,
            RecognizeSpecials: true,
            ArtworkDirectory: @"D:\AniT\Artes",
            ImageCacheDirectory: @"D:\AniT\Cache",
            DuplicateHandling: LibraryDuplicateHandling.SendToReview,
            IgnoredFolders: ["Extras"],
            IgnoredFiles: ["sample*"],
            IgnoredWords: ["trailer"]);

        var actual = JsonSerializer.Deserialize<AniTSystemSettings>(JsonSerializer.Serialize(expected));

        Assert.NotNull(actual);
        Assert.Equal(30, actual.LibraryBackgroundScanMinutes);
        Assert.Equal([".mkv", ".ts"], actual.VideoExtensions);
        Assert.False(actual.RecognizeBracketNumbers);
        Assert.Equal(LibraryDuplicateHandling.SendToReview, actual.DuplicateHandling);
        Assert.Equal(["Extras"], actual.IgnoredFolders);
        Assert.Equal(["sample*"], actual.IgnoredFiles);
        Assert.Equal(["trailer"], actual.IgnoredWords);
    }

    [Fact]
    public void AppearancePreferences_RoundTripWithoutLosingPerPageMascots()
    {
        var expected = AniTSystemSettings.Default with
        {
            ThemeMode = AppearanceThemeMode.Automatic,
            AccentColor = "#8B5CF6",
            GlowIntensity = 42,
            CardSize = AppearanceCardSize.Large,
            CardsPerRow = 5,
            CornerRadius = 22,
            AnimationIntensity = 35,
            ReduceMotion = true,
            InterfaceScalePercent = 110,
            TextScalePercent = 120,
            PageMascots = new Dictionary<string, string>
            {
                ["Inicio"] = "happy",
                ["Historico"] = "boring"
            }
        };

        var actual = JsonSerializer.Deserialize<AniTSystemSettings>(JsonSerializer.Serialize(expected));

        Assert.NotNull(actual);
        Assert.Equal(AppearanceThemeMode.Automatic, actual.ThemeMode);
        Assert.Equal("#8B5CF6", actual.AccentColor);
        Assert.Equal(42, actual.GlowIntensity);
        Assert.Equal(AppearanceCardSize.Large, actual.CardSize);
        Assert.Equal(5, actual.CardsPerRow);
        Assert.Equal(22, actual.CornerRadius);
        Assert.Equal(35, actual.AnimationIntensity);
        Assert.True(actual.ReduceMotion);
        Assert.Equal(110, actual.InterfaceScalePercent);
        Assert.Equal(120, actual.TextScalePercent);
        Assert.Equal("happy", actual.PageMascots!["Inicio"]);
        Assert.Equal("boring", actual.PageMascots["Historico"]);
    }

    [Fact]
    public void HomePagePreferences_RoundTripWithoutLosingLayoutAndBehavior()
    {
        var expected = AniTSystemSettings.Default with
        {
            HomeBannerAutoRotate = false,
            HomeSectionOrder = ["highlights", "continue", "recent"],
            HiddenHomeSections = ["recent"],
            HomeContinueItems = 6,
            HomeRecentItems = 8,
            HomeFavoriteItems = 2,
            HomeTopRatedItems = 6,
            HomeContinueBehavior = HomeContinueBehavior.OpenAnimeDetails,
            HideCompletedFromContinue = false,
            HomeContentPriority = HomeContentPriority.FavoritesFirst
        };

        var actual = JsonSerializer.Deserialize<AniTSystemSettings>(JsonSerializer.Serialize(expected));

        Assert.NotNull(actual);
        Assert.False(actual.HomeBannerAutoRotate);
        Assert.Equal(["highlights", "continue", "recent"], actual.HomeSectionOrder);
        Assert.Equal(["recent"], actual.HiddenHomeSections);
        Assert.Equal(6, actual.HomeContinueItems);
        Assert.Equal(8, actual.HomeRecentItems);
        Assert.Equal(2, actual.HomeFavoriteItems);
        Assert.Equal(6, actual.HomeTopRatedItems);
        Assert.Equal(HomeContinueBehavior.OpenAnimeDetails, actual.HomeContinueBehavior);
        Assert.False(actual.HideCompletedFromContinue);
        Assert.Equal(HomeContentPriority.FavoritesFirst, actual.HomeContentPriority);
    }

    [Fact]
    public void ArtworkPreferences_RoundTripWithoutLosingSafetyCacheAndLocks()
    {
        var animeId = Guid.NewGuid();
        var expected = AniTSystemSettings.Default with
        {
            ArtworkSourcePriority = ArtworkSourcePriority.CustomFirst,
            ImageMaxDimension = 1920,
            ImageCacheLimitMb = 4096,
            OnlySfwArtwork = false,
            PreferLocalArtwork = false,
            ArtworkRefreshDays = 30,
            ArtworkStretchMode = ArtworkStretchMode.Manual,
            ArtworkFocusXPercent = 35,
            ArtworkFocusYPercent = 70,
            LockedArtwork = new Dictionary<string, string> { [animeId.ToString("D")] = @"D:\Artes\favorita.jpg" },
            ImageSources = [new CustomImageSourceSettings(Guid.NewGuid(), "Galeria", "https://example.com/?q={query}", true, true, true, false)]
        };

        var actual = JsonSerializer.Deserialize<AniTSystemSettings>(JsonSerializer.Serialize(expected));

        Assert.NotNull(actual);
        Assert.Equal(ArtworkSourcePriority.CustomFirst, actual.ArtworkSourcePriority);
        Assert.Equal(1920, actual.ImageMaxDimension);
        Assert.Equal(4096, actual.ImageCacheLimitMb);
        Assert.False(actual.OnlySfwArtwork);
        Assert.False(actual.PreferLocalArtwork);
        Assert.Equal(30, actual.ArtworkRefreshDays);
        Assert.Equal(ArtworkStretchMode.Manual, actual.ArtworkStretchMode);
        Assert.Equal(35, actual.ArtworkFocusXPercent);
        Assert.Equal(70, actual.ArtworkFocusYPercent);
        Assert.Equal(@"D:\Artes\favorita.jpg", actual.LockedArtwork![animeId.ToString("D")]);
        Assert.False(actual.ImageSources.Single().IsSfw);
    }

    [Fact]
    public void PlaybackPreferences_RoundTripWithoutLosingPlayerProgressAndLanguageChoices()
    {
        var expected = AniTSystemSettings.Default with
        {
            PlaybackPlayerMode = PlaybackPlayerMode.SystemDefault,
            ResumePlayback = false,
            WatchedThresholdPercent = 85,
            AutoPlayNextEpisode = true,
            SkipOpening = true,
            SkipEnding = true,
            PreferredAudioLanguage = "pt-BR",
            PreferredSubtitleLanguage = "off",
            DefaultPlaybackSpeed = 1.25,
            StartPlaybackFullscreen = true,
            ProgressSaveIntervalSeconds = 12
        };

        var actual = JsonSerializer.Deserialize<AniTSystemSettings>(JsonSerializer.Serialize(expected));

        Assert.NotNull(actual);
        Assert.Equal(PlaybackPlayerMode.SystemDefault, actual.PlaybackPlayerMode);
        Assert.False(actual.ResumePlayback);
        Assert.Equal(85, actual.WatchedThresholdPercent);
        Assert.True(actual.AutoPlayNextEpisode);
        Assert.True(actual.SkipOpening);
        Assert.True(actual.SkipEnding);
        Assert.Equal("pt-BR", actual.PreferredAudioLanguage);
        Assert.Equal("off", actual.PreferredSubtitleLanguage);
        Assert.Equal(1.25, actual.DefaultPlaybackSpeed);
        Assert.True(actual.StartPlaybackFullscreen);
        Assert.Equal(12, actual.ProgressSaveIntervalSeconds);
    }

    [Fact]
    public void OrganizationPreferences_RoundTripWithoutLosingFormatsCollectionsAndRules()
    {
        var expected = AniTSystemSettings.Default with
        {
            EpisodeNumberDisplayFormat = EpisodeNumberDisplayFormat.SeasonEpisode,
            AnimeTitlePreference = AnimeTitlePreference.Portuguese,
            LibraryDefaultSort = LibraryDefaultSort.Favorites,
            CustomTags = ["Assistir com amigos", "Clássico"],
            UserCollections = ["Fim de semana", "Meus essenciais"],
            FavoritesFirstRule = true,
            InProgressFirstRule = true
        };

        var actual = JsonSerializer.Deserialize<AniTSystemSettings>(JsonSerializer.Serialize(expected));

        Assert.NotNull(actual);
        Assert.Equal(EpisodeNumberDisplayFormat.SeasonEpisode, actual.EpisodeNumberDisplayFormat);
        Assert.Equal(AnimeTitlePreference.Portuguese, actual.AnimeTitlePreference);
        Assert.Equal(LibraryDefaultSort.Favorites, actual.LibraryDefaultSort);
        Assert.Equal(["Assistir com amigos", "Clássico"], actual.CustomTags);
        Assert.Equal(["Fim de semana", "Meus essenciais"], actual.UserCollections);
        Assert.True(actual.FavoritesFirstRule);
        Assert.True(actual.InProgressFirstRule);
    }

    [Fact]
    public void NotificationPreferences_RoundTripWithoutLosingChannelsAndQuietHours()
    {
        var expected = AniTSystemSettings.Default with
        {
            NotifyNewEpisodes = false,
            NotifyMetadataUpdates = true,
            NotifyAchievements = false,
            WeeklyWatchSummary = true,
            QuietHoursEnabled = true,
            QuietHoursStartHour = 23,
            QuietHoursEndHour = 7,
            NotificationDeliveryMode = NotificationDeliveryMode.WindowsAndInApp
        };

        var actual = JsonSerializer.Deserialize<AniTSystemSettings>(JsonSerializer.Serialize(expected));

        Assert.NotNull(actual);
        Assert.False(actual.NotifyNewEpisodes);
        Assert.True(actual.NotifyMetadataUpdates);
        Assert.False(actual.NotifyAchievements);
        Assert.True(actual.WeeklyWatchSummary);
        Assert.True(actual.QuietHoursEnabled);
        Assert.Equal(23, actual.QuietHoursStartHour);
        Assert.Equal(7, actual.QuietHoursEndHour);
        Assert.Equal(NotificationDeliveryMode.WindowsAndInApp, actual.NotificationDeliveryMode);
    }

    [Theory]
    [InlineData(23, true)]
    [InlineData(3, true)]
    [InlineData(8, false)]
    [InlineData(15, false)]
    public void QuietHours_SupportsIntervalsThatCrossMidnight(int hour, bool expected)
    {
        var settings = AniTSystemSettings.Default with
        {
            QuietHoursEnabled = true,
            QuietHoursStartHour = 22,
            QuietHoursEndHour = 8
        };
        var now = new DateTimeOffset(2026, 9, 23, hour, 0, 0, TimeSpan.Zero);

        Assert.Equal(expected, NotificationPreferences.IsQuietTime(settings, now));
    }
}

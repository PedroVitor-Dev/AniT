namespace AniT.Infrastructure;

public static class NotificationPreferences
{
    public static bool IsEnabled(AniTSystemSettings settings, AniTNotificationKind kind) => kind switch
    {
        AniTNotificationKind.NewEpisodes => settings.NotifyNewEpisodes,
        AniTNotificationKind.MetadataUpdated => settings.NotifyMetadataUpdates,
        AniTNotificationKind.AchievementUnlocked => settings.NotifyAchievements,
        AniTNotificationKind.WeeklySummary => settings.WeeklyWatchSummary,
        _ => false
    };

    public static bool IsQuietTime(AniTSystemSettings settings, DateTimeOffset now)
    {
        if (!settings.QuietHoursEnabled) return false;
        var current = now.Hour;
        var start = settings.QuietHoursStartHour;
        var end = settings.QuietHoursEndHour;
        if (start == end) return true;
        return start < end
            ? current >= start && current < end
            : current >= start || current < end;
    }

    public static bool CanDeliver(AniTSystemSettings settings, AniTNotificationKind kind, DateTimeOffset now) =>
        IsEnabled(settings, kind) && !IsQuietTime(settings, now);
}

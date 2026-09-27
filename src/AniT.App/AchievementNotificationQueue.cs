using System.Collections.Concurrent;
using AniT.Core.Achievements;

namespace AniT.App;

internal static class AchievementNotificationQueue
{
    private static readonly ConcurrentQueue<AchievementUnlock> Queue = new();
    private static readonly HashSet<int> QueuedIds = [];
    private static readonly object Sync = new();
    private static IAchievementService? service;
    private static bool processing;

    public static void Initialize(IAchievementService achievementService)
    {
        service = achievementService;
        achievementService.AchievementsUnlocked += (_, unlocks) => Enqueue(unlocks);
    }

    public static void Enqueue(IEnumerable<AchievementUnlock> unlocks)
    {
        var added = false;
        lock (Sync)
        {
            foreach (var unlock in unlocks)
            {
                if (!QueuedIds.Add(unlock.Definition.Id)) continue;
                Queue.Enqueue(unlock);
                added = true;
            }
            if (!added || processing) return;
            processing = true;
        }

        _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(ProcessAsync);
    }

    private static async Task ProcessAsync()
    {
        try
        {
            while (Queue.TryDequeue(out var unlock))
            {
                var systemSettings = global::AniT.Infrastructure.AniTSystemSettingsStore.Load();
                var achievementNotificationsEnabled = systemSettings.NotifyAchievements;
                var quiet = global::AniT.Infrastructure.NotificationPreferences.IsQuietTime(systemSettings, DateTimeOffset.Now);
                if (achievementNotificationsEnabled && !quiet)
                {
                    var settings = AchievementSettingsStore.Load();
                    if (settings.ShowNotifications)
                    {
                        if (settings.PlaySound)
                            _ = Task.Run(() => AchievementSoundService.Play(unlock.Definition.Rarity, settings.Volume));
                        var toast = new AchievementToastWindow(unlock, settings);
                        await toast.ShowToastAsync();
                        await Task.Delay(220);
                    }
                    AniTNotificationService.NotifyAchievementWindows(
                        "Conquista desbloqueada",
                        $"{unlock.Definition.Name} · +{unlock.Points} AniPoints");
                }
                if (service is not null && (!achievementNotificationsEnabled || !quiet))
                    await service.MarkPopupShownAsync(unlock.Definition.Id);
            }
        }
        finally
        {
            lock (Sync)
            {
                processing = false;
                QueuedIds.Clear();
                if (!Queue.IsEmpty)
                {
                    processing = true;
                    _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(ProcessAsync);
                }
            }
        }
    }
}

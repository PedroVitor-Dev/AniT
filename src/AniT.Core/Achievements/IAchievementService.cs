namespace AniT.Core.Achievements;

public interface IAchievementService
{
    event EventHandler<IReadOnlyList<AchievementUnlock>>? AchievementsUnlocked;

    Task<IReadOnlyList<AchievementProgress>> RecalculateAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AchievementProgress>> GetProgressAsync(CancellationToken cancellationToken = default);
    Task RecordAsync(AchievementEvent achievementEvent, CancellationToken cancellationToken = default);
    Task RecordWatchCheckpointAsync(long watchedSeconds, DateTimeOffset occurredAt, CancellationToken cancellationToken = default);
    Task MarkPopupShownAsync(int achievementId, CancellationToken cancellationToken = default);
}

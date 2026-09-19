namespace AniT.Core.Achievements;

public enum AchievementCategory
{
    FirstSteps = 1,
    Consistency = 2,
    Marathon = 3,
    Ratings = 4,
    Library = 5,
    Genres = 6,
    WatchTime = 7,
    Rankings = 8,
    ProfileBackup = 9,
    SecretLegendary = 10
}

public enum AchievementRarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Secret,
    Legendary,
    SupremeLegendary
}

public enum AchievementProgressType
{
    Binary,
    Counter,
    DurationSeconds
}

public enum AchievementEventType
{
    ApplicationStarted,
    EpisodeStarted,
    EpisodeCompleted,
    RatingChanged,
    ReviewCreated,
    AnimeFavorited,
    LibraryChanged,
    ProfileUpdated,
    AvatarChanged,
    BackupCreated,
    BackupRestored,
    LibraryImported,
    NextEpisodeRequested
}

public sealed record AchievementDefinition(
    int Id,
    string Code,
    string Name,
    string Description,
    AchievementCategory Category,
    AchievementRarity Rarity,
    string IconPath,
    bool IsSecret,
    bool IsHiddenUntilUnlocked,
    bool IsProgressive,
    long TargetValue,
    string MetricKey,
    AchievementProgressType ProgressType = AchievementProgressType.Counter,
    string? ChainCode = null,
    int ChainOrder = 0,
    string Unit = "");

public sealed class UserAchievement
{
    public int AchievementId { get; set; }
    public long CurrentValue { get; set; }
    public bool IsUnlocked { get; set; }
    public DateTimeOffset? UnlockedAt { get; set; }
    public bool PopupShown { get; set; }
}

public sealed class AchievementHistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int AchievementId { get; set; }
    public DateTimeOffset UnlockedAt { get; set; }
    public int Points { get; set; }
}

public sealed class AchievementMetric
{
    public required string Key { get; set; }
    public long Value { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record AchievementEvent(
    AchievementEventType Type,
    Guid? SubjectId = null,
    long Value = 1,
    long PreviousValue = 0,
    DateTimeOffset? OccurredAt = null);

public sealed record AchievementUnlock(
    AchievementDefinition Definition,
    DateTimeOffset UnlockedAt,
    int Points);

public sealed record AchievementProgress(
    AchievementDefinition Definition,
    long CurrentValue,
    bool IsUnlocked,
    DateTimeOffset? UnlockedAt,
    bool PopupShown)
{
    public long DisplayValue => Math.Min(Math.Max(0, CurrentValue), Definition.TargetValue);
    public double Percent => Definition.TargetValue <= 0 ? 0 : Math.Clamp(DisplayValue * 100d / Definition.TargetValue, 0, 100);
    public int Points => AchievementPoints.For(Definition.Rarity);
}

public sealed record AchievementChain(
    string Code,
    string Name,
    IReadOnlyList<AchievementDefinition> Milestones);

public sealed record AchievementEvaluation(
    IReadOnlyList<UserAchievement> States,
    IReadOnlyList<AchievementUnlock> Unlocks);

public static class AchievementPoints
{
    public static int For(AchievementRarity rarity) => rarity switch
    {
        AchievementRarity.Common => 10,
        AchievementRarity.Uncommon => 20,
        AchievementRarity.Rare => 40,
        AchievementRarity.Epic => 80,
        AchievementRarity.Secret => 100,
        AchievementRarity.Legendary => 150,
        AchievementRarity.SupremeLegendary => 500,
        _ => 0
    };
}

public static class AchievementLabels
{
    public static string Category(AchievementCategory category) => category switch
    {
        AchievementCategory.FirstSteps => "Primeiros Passos",
        AchievementCategory.Consistency => "Consistência",
        AchievementCategory.Marathon => "Maratona",
        AchievementCategory.Ratings => "Avaliações",
        AchievementCategory.Library => "Biblioteca",
        AchievementCategory.Genres => "Gêneros",
        AchievementCategory.WatchTime => "Tempo Assistido",
        AchievementCategory.Rankings => "Rankings",
        AchievementCategory.ProfileBackup => "Perfil e Backup",
        AchievementCategory.SecretLegendary => "Secretas e Lendárias",
        _ => category.ToString()
    };

    public static string Rarity(AchievementRarity rarity) => rarity switch
    {
        AchievementRarity.Common => "Comum",
        AchievementRarity.Uncommon => "Incomum",
        AchievementRarity.Rare => "Rara",
        AchievementRarity.Epic => "Épica",
        AchievementRarity.Secret => "Secreta",
        AchievementRarity.Legendary => "Lendária",
        AchievementRarity.SupremeLegendary => "Lendária Suprema",
        _ => rarity.ToString()
    };
}

using AniT.Core.Achievements;

namespace AniT.Tests;

public sealed class AchievementEngineTests
{
    private readonly AchievementEngine engine = new();

    [Fact]
    public void Catalog_HasExactlyOneHundredStableDefinitions()
    {
        Assert.Equal(100, AchievementCatalog.All.Count);
        Assert.Equal(100, AchievementCatalog.All.Select(item => item.Id).Distinct().Count());
        Assert.Equal(100, AchievementCatalog.All.Select(item => item.Code).Distinct().Count());
    }

    [Fact]
    public void FirstEpisode_UnlocksFirstAchievement()
    {
        var result = Evaluate(("episodes_started", 1));
        AssertUnlocked(result, 1);
    }

    [Fact]
    public void ThreeDayStreak_UnlocksVoltou()
    {
        var streak = AchievementStreakCalculator.Calculate(
            [new DateTime(2026, 9, 15), new DateTime(2026, 9, 16), new DateTime(2026, 9, 17)],
            new DateTime(2026, 9, 17));
        Assert.Equal((3, 3), streak);
        AssertUnlocked(Evaluate(("longest_streak", streak.Longest)), 11);
    }

    [Fact]
    public void StreakReset_KeepsLongestButResetsCurrent()
    {
        var streak = AchievementStreakCalculator.Calculate(
            [new DateTime(2026, 9, 10), new DateTime(2026, 9, 11), new DateTime(2026, 9, 12)],
            new DateTime(2026, 9, 17));
        Assert.Equal(0, streak.Current);
        Assert.Equal(3, streak.Longest);
    }

    [Fact]
    public void ThousandDayStreak_UnlocksEntireConsistencyChain()
    {
        var result = Evaluate(("longest_streak", 1000));
        Assert.All(Enumerable.Range(11, 10), id => AssertUnlocked(result, id));
    }

    [Theory]
    [InlineData(5, 41)]
    [InlineData(2000, 50)]
    public void LibraryThresholds_UnlockExpectedAchievement(long count, int id)
    {
        AssertUnlocked(Evaluate(("library_anime_count", count)), id);
    }

    [Theory]
    [InlineData(1, 61)]
    [InlineData(5000, 70)]
    public void WatchTimeThresholds_UseSeconds(long hours, int id)
    {
        AssertUnlocked(Evaluate(("watch_seconds", hours * 3600)), id);
    }

    [Fact]
    public void LowRating_UnlocksSemPassarPano()
    {
        AssertUnlocked(Evaluate(("ratings_lte_3", 1)), 34);
    }

    [Theory]
    [InlineData("rating_one_to_ten", 1, 94)]
    [InlineData("consecutive_tens", 10, 95)]
    [InlineData("next_episode_streak", 10, 92)]
    [InlineData("secret_0333", 1, 91)]
    public void SecretMetrics_UnlockOnlyTheirAchievement(string metric, long value, int id)
    {
        AssertUnlocked(Evaluate((metric, value)), id);
    }

    [Fact]
    public void NinetyAchievements_UnlockMasterButNotSupreme()
    {
        var metrics = AchievementCatalog.All
            .Where(item => item.Id <= 90)
            .GroupBy(item => item.MetricKey)
            .ToDictionary(group => group.Key, group => group.Max(item => item.TargetValue));
        var result = engine.Evaluate(metrics, [], DateTimeOffset.UtcNow);
        AssertUnlocked(result, 99);
        Assert.DoesNotContain(result.States, item => item.AchievementId == 100 && item.IsUnlocked);
    }

    [Fact]
    public void NinetyNinePriorAchievements_UnlockSupreme()
    {
        var existing = Enumerable.Range(1, 98)
            .Select(id => new UserAchievement { AchievementId = id, IsUnlocked = true, UnlockedAt = DateTimeOffset.UtcNow })
            .ToArray();
        var result = engine.Evaluate(new Dictionary<string, long>(), existing, DateTimeOffset.UtcNow);
        AssertUnlocked(result, 99);
        AssertUnlocked(result, 100);
    }

    [Fact]
    public void AchievementCannotUnlockOrQueueTwice()
    {
        var first = Evaluate(("episodes_started", 1));
        Assert.Single(first.Unlocks, item => item.Definition.Id == 1);

        var second = engine.Evaluate(
            new Dictionary<string, long> { ["episodes_started"] = 1 },
            first.States,
            DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.DoesNotContain(second.Unlocks, item => item.Definition.Id == 1);
    }

    private AchievementEvaluation Evaluate(params (string Key, long Value)[] values) =>
        engine.Evaluate(values.ToDictionary(item => item.Key, item => item.Value), [], DateTimeOffset.UtcNow);

    private static void AssertUnlocked(AchievementEvaluation evaluation, int id) =>
        Assert.Contains(evaluation.States, item => item.AchievementId == id && item.IsUnlocked);
}

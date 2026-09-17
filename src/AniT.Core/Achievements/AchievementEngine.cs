namespace AniT.Core.Achievements;

public sealed class AchievementEngine
{
    public AchievementEvaluation Evaluate(
        IReadOnlyDictionary<string, long> metrics,
        IEnumerable<UserAchievement> existing,
        DateTimeOffset now)
    {
        var states = existing.ToDictionary(item => item.AchievementId);
        var unlocks = new List<AchievementUnlock>();

        foreach (var definition in AchievementCatalog.All.Where(item => item.Id < 99))
            EvaluateOne(definition, Metric(metrics, definition.MetricKey), states, unlocks, now);

        var unlockedBeforeMaster = states.Values.Count(item => item.IsUnlocked && item.AchievementId is < 99);
        EvaluateOne(AchievementCatalog.ById(99), unlockedBeforeMaster, states, unlocks, now);

        var unlockedBeforeSupreme = states.Values.Count(item => item.IsUnlocked && item.AchievementId is < 100);
        EvaluateOne(AchievementCatalog.ById(100), unlockedBeforeSupreme, states, unlocks, now);

        return new AchievementEvaluation(states.Values.OrderBy(item => item.AchievementId).ToArray(), unlocks);
    }

    private static void EvaluateOne(
        AchievementDefinition definition,
        long value,
        IDictionary<int, UserAchievement> states,
        ICollection<AchievementUnlock> unlocks,
        DateTimeOffset now)
    {
        if (!states.TryGetValue(definition.Id, out var state))
        {
            state = new UserAchievement { AchievementId = definition.Id };
            states.Add(definition.Id, state);
        }

        state.CurrentValue = Math.Max(0, value);
        if (state.IsUnlocked || state.CurrentValue < definition.TargetValue) return;

        state.IsUnlocked = true;
        state.UnlockedAt = now.ToUniversalTime();
        state.PopupShown = false;
        unlocks.Add(new AchievementUnlock(definition, state.UnlockedAt.Value, AchievementPoints.For(definition.Rarity)));
    }

    private static long Metric(IReadOnlyDictionary<string, long> metrics, string key) =>
        metrics.TryGetValue(key, out var value) ? Math.Max(0, value) : 0;
}

public static class AchievementStreakCalculator
{
    public static (int Current, int Longest) Calculate(IEnumerable<DateTime> dates, DateTime today)
    {
        var ordered = dates.Select(item => item.Date).Distinct().OrderBy(item => item).ToArray();
        if (ordered.Length == 0) return (0, 0);

        var longest = 1;
        var running = 1;
        for (var index = 1; index < ordered.Length; index++)
        {
            running = ordered[index] == ordered[index - 1].AddDays(1) ? running + 1 : 1;
            longest = Math.Max(longest, running);
        }

        var latest = ordered[^1];
        var current = latest < today.Date.AddDays(-1) ? 0 : 1;
        for (var index = ordered.Length - 2; current > 0 && index >= 0; index--)
        {
            if (ordered[index] != latest.AddDays(-current)) break;
            current++;
        }

        return (current, longest);
    }
}

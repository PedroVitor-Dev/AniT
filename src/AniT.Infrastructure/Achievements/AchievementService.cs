using AniT.Core;
using AniT.Core.Achievements;
using Microsoft.EntityFrameworkCore;

namespace AniT.Infrastructure.Achievements;

public sealed class AchievementService(Func<AniTDbContext> contextFactory) : IAchievementService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly AchievementEngine engine = new();

    public event EventHandler<IReadOnlyList<AchievementUnlock>>? AchievementsUnlocked;

    public Task<IReadOnlyList<AchievementProgress>> GetProgressAsync(CancellationToken cancellationToken = default) =>
        RecalculateAsync(cancellationToken);

    public async Task<IReadOnlyList<AchievementProgress>> RecalculateAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AchievementUnlock> unlocked;
        IReadOnlyList<AchievementProgress> progress;

        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var context = contextFactory();
            var metrics = await BuildMetricsAsync(context, cancellationToken);
            var existing = await context.UserAchievements.OrderBy(item => item.AchievementId).ToListAsync(cancellationToken);
            var existingIds = existing.Select(item => item.AchievementId).ToHashSet();
            var evaluation = engine.Evaluate(metrics, existing, DateTimeOffset.UtcNow);

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            foreach (var state in evaluation.States.Where(item => !existingIds.Contains(item.AchievementId)))
                context.UserAchievements.Add(state);

            var historicalIds = await context.AchievementHistory
                .Select(item => item.AchievementId)
                .ToHashSetAsync(cancellationToken);
            foreach (var item in evaluation.Unlocks.Where(item => !historicalIds.Contains(item.Definition.Id)))
            {
                context.AchievementHistory.Add(new AchievementHistoryEntry
                {
                    AchievementId = item.Definition.Id,
                    UnlockedAt = item.UnlockedAt,
                    Points = item.Points
                });
                Log($"Unlocked: {item.Definition.Code} (+{item.Points} AniPoints)");
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            unlocked = evaluation.Unlocks;
            progress = evaluation.States
                .Select(state => new AchievementProgress(
                    AchievementCatalog.ById(state.AchievementId),
                    state.CurrentValue,
                    state.IsUnlocked,
                    state.UnlockedAt,
                    state.PopupShown))
                .OrderBy(item => item.Definition.Id)
                .ToArray();
        }
        finally
        {
            gate.Release();
        }

        if (unlocked.Count > 0) AchievementsUnlocked?.Invoke(this, unlocked);
        return progress;
    }

    public async Task RecordAsync(AchievementEvent achievementEvent, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var context = contextFactory();
            var now = achievementEvent.OccurredAt ?? DateTimeOffset.Now;
            switch (achievementEvent.Type)
            {
                case AchievementEventType.ApplicationStarted:
                    await SetIfMissingAsync(context, "first_application_day", DateOnly.FromDateTime(now.LocalDateTime.Date).DayNumber, cancellationToken);
                    await SetAsync(context, "current_session_episode_count", 0, cancellationToken);
                    if (now.LocalDateTime.Hour == 3 && now.LocalDateTime.Minute == 33)
                        await SetMaxAsync(context, "secret_0333", 1, cancellationToken);
                    break;

                case AchievementEventType.EpisodeCompleted:
                    var current = await IncrementAsync(context, "current_session_episode_count", 1, cancellationToken);
                    await SetMaxAsync(context, "session_episode_max", current, cancellationToken);
                    break;

                case AchievementEventType.RatingChanged:
                    if (achievementEvent.PreviousValue == 1 && achievementEvent.Value == 5)
                        await SetMaxAsync(context, "rating_one_to_ten", 1, cancellationToken);
                    if (achievementEvent.Value == 5)
                        await IncrementAsync(context, "consecutive_tens", 1, cancellationToken);
                    else if (achievementEvent.Value > 0)
                        await SetAsync(context, "consecutive_tens", 0, cancellationToken);
                    break;

                case AchievementEventType.ProfileUpdated:
                    await SetMaxAsync(context, "profile_customized", 1, cancellationToken);
                    await SetMaxAsync(context, "profile_complete", 1, cancellationToken);
                    break;

                case AchievementEventType.AvatarChanged:
                    await SetMaxAsync(context, "avatar_changed", 1, cancellationToken);
                    break;

                case AchievementEventType.BackupCreated:
                    await IncrementAsync(context, "backups_created", 1, cancellationToken);
                    break;

                case AchievementEventType.BackupRestored:
                    await IncrementAsync(context, "backups_restored", 1, cancellationToken);
                    break;

                case AchievementEventType.LibraryImported:
                    await SetMaxAsync(context, "library_imported", 1, cancellationToken);
                    break;

                case AchievementEventType.NextEpisodeRequested:
                    await IncrementAsync(context, "next_episode_streak", 1, cancellationToken);
                    break;
            }

            await context.SaveChangesAsync(cancellationToken);
            Log($"Event: {achievementEvent.Type}");
        }
        finally
        {
            gate.Release();
        }

        await RecalculateAsync(cancellationToken);
    }

    public async Task MarkPopupShownAsync(int achievementId, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var context = contextFactory();
            var state = await context.UserAchievements.SingleOrDefaultAsync(item => item.AchievementId == achievementId, cancellationToken);
            if (state is null || state.PopupShown) return;
            state.PopupShown = true;
            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task RecordWatchCheckpointAsync(long watchedSeconds, DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        if (watchedSeconds <= 0 || occurredAt.LocalDateTime.Hour is < 0 or >= 6) return;

        var shouldEvaluate = false;
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var context = contextFactory();
            var accumulated = await IncrementAsync(context, "midnight_watch_seconds", watchedSeconds, cancellationToken);
            var alreadyUnlocked = await context.UserAchievements
                .AnyAsync(item => item.AchievementId == 93 && item.IsUnlocked, cancellationToken);
            shouldEvaluate = !alreadyUnlocked && accumulated >= TimeSpan.FromHours(6).TotalSeconds;
            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }

        if (shouldEvaluate) await RecalculateAsync(cancellationToken);
    }

    private static async Task<Dictionary<string, long>> BuildMetricsAsync(AniTDbContext context, CancellationToken cancellationToken)
    {
        var anime = await context.Anime
            .Include(item => item.Seasons)
            .ThenInclude(item => item.Episodes)
            .ThenInclude(item => item.PlaybackProgress)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var persisted = await context.AchievementMetrics.AsNoTracking().ToListAsync(cancellationToken);
        var allEpisodes = anime.SelectMany(item => item.Seasons.SelectMany(season => season.Episodes).Select(episode => new { Anime = item, Episode = episode })).ToArray();
        var started = allEpisodes.Where(item => item.Episode.Status != WatchStatus.NotStarted || item.Episode.PlaybackProgress is { PositionSeconds: > 0 }).ToArray();
        var completed = allEpisodes.Where(item => item.Episode.Status == WatchStatus.Completed).ToArray();
        var rated = allEpisodes.Where(item => item.Episode.Rating is > 0).ToArray();
        var completedAnime = anime.Count(item => item.Seasons.SelectMany(season => season.Episodes).Any() && item.Seasons.SelectMany(season => season.Episodes).All(episode => episode.Status == WatchStatus.Completed));
        var activity = started
            .Select(item => new Activity(
                (item.Episode.WatchedAt ?? item.Episode.PlaybackProgress?.LastPlayedAt)?.LocalDateTime.Date,
                WatchedSeconds(item.Episode)))
            .Where(item => item.Date is not null)
            .ToArray();
        var activityDates = activity.Select(item => item.Date!.Value).Distinct().ToArray();
        var streak = AchievementStreakCalculator.Calculate(activityDates, DateTime.Today);
        var byDay = activity.GroupBy(item => item.Date!.Value).ToArray();
        var ratingsTenPoint = rated.Select(item => ToTenPoint(item.Episode.Rating!.Value))
            .Concat(anime.Where(item => item.Rating is > 0).Select(item => ToTenPoint(item.Rating!.Value)))
            .ToArray();
        var ratedWorks = anime.Count(item => item.Rating is > 0 || item.Seasons.SelectMany(season => season.Episodes).Any(episode => episode.Rating is > 0));
        var reviews = allEpisodes.Count(item => !string.IsNullOrWhiteSpace(item.Episode.ReviewNotes)) + anime.Count(item => !string.IsNullOrWhiteSpace(item.ReviewNotes));
        var watchSeconds = allEpisodes.Sum(item => WatchedSeconds(item.Episode));
        var oldestActivity = activityDates.DefaultIfEmpty(DateTime.Today).Min();
        var newestActivity = activityDates.DefaultIfEmpty(DateTime.Today).Max();

        var metrics = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["episodes_started"] = started.LongLength,
            ["anime_started"] = started.Select(item => item.Anime.Id).Distinct().LongCount(),
            ["rated_episodes"] = rated.LongLength,
            ["completed_episodes"] = completed.LongLength,
            ["library_anime_count"] = anime.LongCount(),
            ["favorite_anime"] = anime.LongCount(item => item.IsFavorite),
            ["reviews_written"] = reviews,
            ["detailed_reviews"] = reviews,
            ["completed_anime"] = completedAnime,
            ["longest_streak"] = streak.Longest,
            ["current_streak"] = streak.Current,
            ["max_daily_episodes"] = byDay.Select(group => group.LongCount()).DefaultIfEmpty().Max(),
            ["max_daily_seconds"] = byDay.Select(group => group.Sum(item => item.Seconds)).DefaultIfEmpty().Max(),
            ["marathon_seconds_total"] = byDay.Where(group => group.Count() >= 3).Sum(group => group.Sum(item => item.Seconds)),
            ["rated_works"] = ratedWorks,
            ["ratings_gte_8"] = ratingsTenPoint.LongCount(item => item >= 8),
            ["ratings_4_to_6"] = ratingsTenPoint.LongCount(item => item is >= 4 and <= 6),
            ["ratings_lte_3"] = ratingsTenPoint.LongCount(item => item <= 3),
            ["perfect_ratings"] = ratingsTenPoint.LongCount(item => item >= 10),
            ["demanding_judge"] = ratedWorks >= 100 && ratingsTenPoint.Count(item => item >= 10) <= 10 ? 1 : 0,
            ["watch_seconds"] = watchSeconds,
            ["history_span_days"] = activityDates.Length == 0 ? 0 : (newestActivity - oldestActivity).Days + 1
        };

        foreach (var item in persisted)
            metrics[item.Key] = Math.Max(metrics.GetValueOrDefault(item.Key), item.Value);

        var firstDay = metrics.GetValueOrDefault("first_application_day");
        if (firstDay > 0)
            metrics["application_usage_days"] = Math.Max(1, DateOnly.FromDateTime(DateTime.Now).DayNumber - firstDay + 1);
        else if (anime.Count > 0)
            metrics["application_usage_days"] = Math.Max(1, (DateTimeOffset.Now - anime.Min(item => item.CreatedAt)).Days + 1);

        metrics["session_episode_max"] = Math.Max(metrics.GetValueOrDefault("session_episode_max"), metrics["max_daily_episodes"]);
        return metrics;
    }

    private static long WatchedSeconds(Episode episode)
    {
        var position = Math.Max(0, episode.PlaybackProgress?.PositionSeconds ?? 0);
        if (episode.Status != WatchStatus.Completed) return (long)position;
        var duration = Math.Max(position, episode.PlaybackProgress?.DurationSeconds ?? 0);
        return (long)(duration > 0 ? duration : TimeSpan.FromMinutes(24).TotalSeconds);
    }

    private static double ToTenPoint(double rating) => rating <= 5 ? rating * 2 : rating;

    private static async Task<AchievementMetric> MetricAsync(AniTDbContext context, string key, CancellationToken cancellationToken)
    {
        var metric = await context.AchievementMetrics.SingleOrDefaultAsync(item => item.Key == key, cancellationToken);
        if (metric is not null) return metric;
        metric = new AchievementMetric { Key = key };
        context.AchievementMetrics.Add(metric);
        return metric;
    }

    private static async Task<long> IncrementAsync(AniTDbContext context, string key, long amount, CancellationToken cancellationToken)
    {
        var metric = await MetricAsync(context, key, cancellationToken);
        metric.Value = Math.Max(0, metric.Value + amount);
        metric.UpdatedAt = DateTimeOffset.UtcNow;
        return metric.Value;
    }

    private static async Task SetAsync(AniTDbContext context, string key, long value, CancellationToken cancellationToken)
    {
        var metric = await MetricAsync(context, key, cancellationToken);
        metric.Value = Math.Max(0, value);
        metric.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static async Task SetMaxAsync(AniTDbContext context, string key, long value, CancellationToken cancellationToken)
    {
        var metric = await MetricAsync(context, key, cancellationToken);
        metric.Value = Math.Max(metric.Value, value);
        metric.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static async Task SetIfMissingAsync(AniTDbContext context, string key, long value, CancellationToken cancellationToken)
    {
        if (await context.AchievementMetrics.AnyAsync(item => item.Key == key, cancellationToken)) return;
        context.AchievementMetrics.Add(new AchievementMetric { Key = key, Value = Math.Max(0, value), UpdatedAt = DateTimeOffset.UtcNow });
    }

    private static void Log(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[Achievement] {message}");
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AniT", "Data");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "achievements.log"), $"{DateTimeOffset.Now:O} [Achievement] {message}{Environment.NewLine}");
        }
        catch { }
    }

    private sealed record Activity(DateTime? Date, long Seconds);
}

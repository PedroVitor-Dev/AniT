using AniT.Core;
using Microsoft.EntityFrameworkCore;

namespace AniT.Infrastructure;

public sealed class CatalogCorrectionService(AniTDbContext database)
{
    public async Task MergeAnimeAsync(Guid sourceAnimeId, Guid targetAnimeId, CancellationToken cancellationToken = default)
    {
        if (sourceAnimeId == targetAnimeId) throw new InvalidOperationException("Escolha dois títulos diferentes.");
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var source = await LoadAnimeAsync(sourceAnimeId, cancellationToken) ?? throw new InvalidOperationException("O título de origem não existe mais.");
        var target = await LoadAnimeAsync(targetAnimeId, cancellationToken) ?? throw new InvalidOperationException("O título de destino não existe mais.");

        foreach (var sourceSeason in source.Seasons.ToList())
        {
            var targetSeason = target.Seasons.FirstOrDefault(item => item.Number == sourceSeason.Number);
            if (targetSeason is null)
            {
                sourceSeason.AnimeId = target.Id;
                sourceSeason.Anime = target;
                target.Seasons.Add(sourceSeason);
                continue;
            }

            foreach (var sourceEpisode in sourceSeason.Episodes.ToList())
            {
                var targetEpisode = targetSeason.Episodes.FirstOrDefault(item => item.Number == sourceEpisode.Number);
                if (targetEpisode is null)
                {
                    sourceEpisode.SeasonId = targetSeason.Id;
                    sourceEpisode.Season = targetSeason;
                    targetSeason.Episodes.Add(sourceEpisode);
                    continue;
                }

                foreach (var file in sourceEpisode.MediaFiles.ToList())
                {
                    file.EpisodeId = targetEpisode.Id;
                    file.Episode = targetEpisode;
                    targetEpisode.MediaFiles.Add(file);
                }
                MergeProgress(sourceEpisode, targetEpisode);
                targetEpisode.Status = (WatchStatus)Math.Max((int)targetEpisode.Status, (int)sourceEpisode.Status);
                targetEpisode.WatchedAt = Latest(targetEpisode.WatchedAt, sourceEpisode.WatchedAt);
                targetEpisode.Rating ??= sourceEpisode.Rating;
                targetEpisode.ReviewNotes ??= sourceEpisode.ReviewNotes;
            }
        }

        var knownAliases = target.Aliases.Select(item => item.NormalizedAlias).ToHashSet(StringComparer.Ordinal);
        foreach (var alias in source.Aliases.ToList())
        {
            if (!knownAliases.Add(alias.NormalizedAlias)) continue;
            alias.AnimeId = target.Id;
            alias.Anime = target;
            target.Aliases.Add(alias);
        }
        var sourceTitleNormalized = AnimeTitleNormalizer.Normalize(source.Title);
        if (sourceTitleNormalized.Length > 0 && knownAliases.Add(sourceTitleNormalized))
            database.AnimeAliases.Add(new AnimeAlias
            {
                AnimeId = target.Id,
                Anime = target,
                Alias = source.Title,
                NormalizedAlias = sourceTitleNormalized,
                Source = AnimeAliasSource.Manual
            });
        target.IsFavorite |= source.IsFavorite;
        target.Rating ??= source.Rating;
        target.ReviewNotes ??= source.ReviewNotes;
        // Persist every re-parenting operation before deleting the old graph.
        // Deleting both a tracked aggregate and some of its moved dependants in
        // one batch can make SQLite cascades race EF's expected row counts.
        await database.SaveChangesAsync(cancellationToken);
        database.ChangeTracker.Clear();
        await database.Anime.Where(anime => anime.Id == sourceAnimeId).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<Guid> SplitEpisodesAsync(Guid sourceAnimeId, IEnumerable<Guid> episodeIds, string newTitle, CancellationToken cancellationToken = default)
    {
        var selected = episodeIds.ToHashSet();
        if (selected.Count == 0) throw new InvalidOperationException("Selecione ao menos um episódio para separar.");
        var title = newTitle.Trim();
        if (title.Length is < 1 or > 300) throw new InvalidOperationException("Informe um título com até 300 caracteres.");

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var source = await LoadAnimeAsync(sourceAnimeId, cancellationToken) ?? throw new InvalidOperationException("O título original não existe mais.");
        var episodes = source.Seasons.SelectMany(season => season.Episodes).Where(episode => selected.Contains(episode.Id)).ToList();
        if (episodes.Count != selected.Count) throw new InvalidOperationException("Alguns episódios selecionados não pertencem mais a este título.");

        var created = new Anime { Title = title };
        created.Aliases.Add(new AnimeAlias
        {
            AnimeId = created.Id,
            Alias = title,
            NormalizedAlias = AnimeTitleNormalizer.Normalize(title),
            Source = AnimeAliasSource.Manual
        });
        database.Anime.Add(created);
        foreach (var group in episodes.GroupBy(episode => episode.Season!.Number))
        {
            var season = new Season { AnimeId = created.Id, Anime = created, Number = group.Key };
            created.Seasons.Add(season);
            foreach (var episode in group)
            {
                episode.SeasonId = season.Id;
                episode.Season = season;
                season.Episodes.Add(episode);
            }
        }
        foreach (var emptySeason in source.Seasons.Where(season => season.Episodes.All(episode => selected.Contains(episode.Id))).ToList())
            database.Seasons.Remove(emptySeason);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return created.Id;
    }

    private async Task<Anime?> LoadAnimeAsync(Guid animeId, CancellationToken cancellationToken) => await database.Anime
        .Include(anime => anime.Aliases)
        .Include(anime => anime.Seasons).ThenInclude(season => season.Episodes).ThenInclude(episode => episode.MediaFiles)
        .Include(anime => anime.Seasons).ThenInclude(season => season.Episodes).ThenInclude(episode => episode.PlaybackProgress)
        .SingleOrDefaultAsync(anime => anime.Id == animeId, cancellationToken);

    private void MergeProgress(Episode source, Episode target)
    {
        if (source.PlaybackProgress is null) return;
        if (target.PlaybackProgress is null)
        {
            target.PlaybackProgress = new PlaybackProgress
            {
                EpisodeId = target.Id,
                PositionSeconds = source.PlaybackProgress.PositionSeconds,
                DurationSeconds = source.PlaybackProgress.DurationSeconds,
                LastPlayedAt = source.PlaybackProgress.LastPlayedAt
            };
        }
        else if (source.PlaybackProgress.PositionSeconds > target.PlaybackProgress.PositionSeconds)
        {
            target.PlaybackProgress.PositionSeconds = source.PlaybackProgress.PositionSeconds;
            target.PlaybackProgress.DurationSeconds = Math.Max(target.PlaybackProgress.DurationSeconds, source.PlaybackProgress.DurationSeconds);
            target.PlaybackProgress.LastPlayedAt = Latest(target.PlaybackProgress.LastPlayedAt, source.PlaybackProgress.LastPlayedAt)!.Value;
        }
        // The source episode is removed after this merge. Its one-to-one progress
        // is deleted by the database cascade; marking it for deletion here as
        // well would issue a second DELETE and trigger a concurrency exception.
    }

    private static DateTimeOffset? Latest(DateTimeOffset? first, DateTimeOffset? second) =>
        first is null ? second : second is null ? first : first > second ? first : second;
}

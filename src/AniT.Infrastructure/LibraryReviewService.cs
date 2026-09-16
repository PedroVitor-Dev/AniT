using AniT.Core;
using Microsoft.EntityFrameworkCore;

namespace AniT.Infrastructure;

public sealed class LibraryReviewService(AniTDbContext database)
{
    public async Task<MediaFile> ResolveAsync(
        Guid reviewItemId,
        Guid animeId,
        int seasonNumber,
        int episodeNumber,
        bool learnAlias,
        CancellationToken cancellationToken = default)
    {
        var review = await database.LibraryReviewItems
            .Include(item => item.LibraryRoot)
            .SingleAsync(item => item.Id == reviewItemId, cancellationToken);
        var anime = await database.Anime.Include(item => item.Aliases)
            .SingleAsync(item => item.Id == animeId, cancellationToken);
        var season = await database.Seasons.SingleOrDefaultAsync(
            item => item.AnimeId == animeId && item.Number == seasonNumber,
            cancellationToken);
        if (season is null)
        {
            season = new Season { AnimeId = animeId, Number = seasonNumber };
            database.Seasons.Add(season);
        }

        var episode = await database.Episodes.Include(item => item.MediaFiles).SingleOrDefaultAsync(
            item => item.SeasonId == season.Id && item.Number == episodeNumber,
            cancellationToken);
        if (episode is null)
        {
            episode = new Episode { Season = season, Number = episodeNumber };
            database.Episodes.Add(episode);
        }

        var mediaFile = await database.MediaFiles.SingleOrDefaultAsync(
            file => file.LibraryRootId == review.LibraryRootId && file.RelativePath == review.RelativePath,
            cancellationToken);
        if (mediaFile is null)
        {
            mediaFile = new MediaFile
            {
                Episode = episode,
                LibraryRootId = review.LibraryRootId,
                RelativePath = review.RelativePath,
                FileName = review.FileName,
                Extension = Path.GetExtension(review.FileName),
                SizeInBytes = review.SizeInBytes,
                LastModifiedAt = review.LastModifiedAt,
                QuickHash = review.QuickHash,
                Resolution = review.Resolution,
                ReleaseGroup = review.ReleaseGroup,
                Language = review.Language,
                IsPreferred = !episode.MediaFiles.Any(file => file.IsPreferred),
                LastSeenAt = review.LastSeenAt,
                Availability = MediaFileAvailability.Available
            };
            database.MediaFiles.Add(mediaFile);
        }
        else
        {
            var previousEpisodeId = mediaFile.EpisodeId;
            var wasPreferred = mediaFile.IsPreferred;
            mediaFile.Episode = episode;
            mediaFile.EpisodeId = episode.Id;
            mediaFile.IsPreferred = !episode.MediaFiles.Any(file => file.IsPreferred && file.Id != mediaFile.Id);
            if (wasPreferred && previousEpisodeId != episode.Id)
            {
                var oldFallback = await database.MediaFiles
                    .Where(file => file.EpisodeId == previousEpisodeId && file.Id != mediaFile.Id && file.Availability == MediaFileAvailability.Available)
                    .OrderByDescending(file => file.Resolution)
                    .FirstOrDefaultAsync(cancellationToken);
                if (oldFallback is not null) oldFallback.IsPreferred = true;
            }
        }

        if (learnAlias && !string.IsNullOrWhiteSpace(review.CandidateTitle))
        {
            var normalized = AnimeTitleNormalizer.Normalize(review.CandidateTitle);
            if (normalized.Length > 0 && !anime.Aliases.Any(alias => alias.NormalizedAlias == normalized))
            {
                database.AnimeAliases.Add(new AnimeAlias
                {
                    AnimeId = anime.Id,
                    Alias = review.CandidateTitle,
                    NormalizedAlias = normalized,
                    Source = AnimeAliasSource.FilenameLearned
                });
            }
        }

        review.Status = LibraryReviewStatus.Resolved;
        await database.SaveChangesAsync(cancellationToken);
        return mediaFile;
    }

    public async Task SetPreferredAsync(Guid mediaFileId, CancellationToken cancellationToken = default)
    {
        var selected = await database.MediaFiles.SingleAsync(item => item.Id == mediaFileId, cancellationToken);
        var versions = await database.MediaFiles.Where(item => item.EpisodeId == selected.EpisodeId).ToListAsync(cancellationToken);
        foreach (var version in versions) version.IsPreferred = version.Id == mediaFileId;
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task CorrectAssociationAsync(Guid mediaFileId, Guid episodeId, CancellationToken cancellationToken = default)
    {
        var file = await database.MediaFiles.SingleAsync(item => item.Id == mediaFileId, cancellationToken);
        var previousEpisodeId = file.EpisodeId;
        var wasPreferred = file.IsPreferred;
        file.EpisodeId = episodeId;
        file.IsPreferred = !await database.MediaFiles.AnyAsync(item => item.EpisodeId == episodeId && item.IsPreferred, cancellationToken);
        if (wasPreferred)
        {
            var oldFallback = await database.MediaFiles
                .Where(item => item.EpisodeId == previousEpisodeId && item.Id != mediaFileId && item.Availability == MediaFileAvailability.Available)
                .OrderByDescending(item => item.Resolution)
                .FirstOrDefaultAsync(cancellationToken);
            if (oldFallback is not null) oldFallback.IsPreferred = true;
        }
        await database.SaveChangesAsync(cancellationToken);
    }
}

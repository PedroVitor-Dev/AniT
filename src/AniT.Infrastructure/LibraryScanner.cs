using AniT.Core;
using Microsoft.EntityFrameworkCore;

namespace AniT.Infrastructure;

public sealed record LibraryScanResult(
    int FilesFound,
    int EpisodesAdded,
    int FilesSkipped,
    IReadOnlyList<string> UnrecognizedFiles,
    int FilesMatched = 0,
    int NeedsReview = 0,
    int PhysicalDuplicates = 0,
    int FilesMoved = 0,
    int FilesMissing = 0,
    int Errors = 0,
    bool RootUnavailable = false);

public sealed record LibraryScanProgress(
    int FilesProcessed,
    int TotalFiles,
    string CurrentFile,
    int FilesMatched = 0,
    int NeedsReview = 0,
    int PhysicalDuplicates = 0);

public sealed class LibraryScanner
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".mov", ".webm", ".m4v", ".wmv"
    };

    private readonly AniTDbContext database;
    private readonly IFileFingerprintService fingerprintService;
    private readonly AnimeMatcher matcher;

    public LibraryScanner(
        AniTDbContext database,
        IFileFingerprintService? fingerprintService = null,
        AnimeMatcher? matcher = null)
    {
        this.database = database;
        this.fingerprintService = fingerprintService ?? new QuickFileFingerprintService();
        this.matcher = matcher ?? new AnimeMatcher();
    }

    public async Task<LibraryScanResult> ScanAsync(
        LibraryRoot root,
        IProgress<LibraryScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var trackedRoot = await database.LibraryRoots.FirstOrDefaultAsync(item => item.Id == root.Id, cancellationToken) ?? root;
        if (!trackedRoot.IsEnabled) return new LibraryScanResult(0, 0, 0, [], RootUnavailable: false);

        if (!Directory.Exists(trackedRoot.Path))
        {
            var unavailableAt = DateTimeOffset.UtcNow;
            trackedRoot.LastUnavailableAt = unavailableAt;
            var affected = await database.MediaFiles.Where(file => file.LibraryRootId == trackedRoot.Id).ToListAsync(cancellationToken);
            foreach (var file in affected) file.Availability = MediaFileAvailability.RootUnavailable;
            await database.SaveChangesAsync(cancellationToken);
            return new LibraryScanResult(0, 0, 0, [], RootUnavailable: true);
        }

        List<string> files;
        try
        {
            files = await Task.Run(() => Directory
                .EnumerateFiles(
                    trackedRoot.Path,
                    "*",
                    trackedRoot.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(path => VideoExtensions.Contains(Path.GetExtension(path)))
                .ToList(), cancellationToken);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            trackedRoot.LastUnavailableAt = DateTimeOffset.UtcNow;
            await database.SaveChangesAsync(cancellationToken);
            return new LibraryScanResult(0, 0, 0, [], Errors: 1, RootUnavailable: true);
        }

        var scanStartedAt = DateTimeOffset.UtcNow;
        var discoveredPaths = files
            .Select(path => NormalizeRelativePath(Path.GetRelativePath(trackedRoot.Path, path)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allExistingFiles = await database.MediaFiles
            .Include(file => file.Episode)
            .ToListAsync(cancellationToken);
        var existingFiles = allExistingFiles.Where(file => file.LibraryRootId == trackedRoot.Id).ToList();
        var existingByPath = existingFiles.ToDictionary(file => NormalizeRelativePath(file.RelativePath), StringComparer.OrdinalIgnoreCase);
        var existingByFingerprint = allExistingFiles
            .Where(file => !string.IsNullOrWhiteSpace(file.QuickHash))
            .GroupBy(file => FingerprintKey(file.SizeInBytes, file.QuickHash!), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var pendingByPath = await database.LibraryReviewItems
            .Where(item => item.LibraryRootId == trackedRoot.Id && item.Status == LibraryReviewStatus.Pending)
            .ToDictionaryAsync(item => NormalizeRelativePath(item.RelativePath), StringComparer.OrdinalIgnoreCase, cancellationToken);
        var anime = await database.Anime.Include(item => item.Aliases).ToListAsync(cancellationToken);
        var seasons = await database.Seasons.ToListAsync(cancellationToken);
        var episodes = await database.Episodes.Include(item => item.MediaFiles).ToListAsync(cancellationToken);
        var matchEntries = anime.Select(ToMatchEntry).ToList();

        var unrecognized = new List<string>();
        var addedEpisodes = 0;
        var skipped = 0;
        var matched = 0;
        var needsReview = 0;
        var duplicates = 0;
        var moved = 0;
        var errors = 0;
        var processed = 0;

        foreach (var path in files)
        {
            var relativePath = NormalizeRelativePath(Path.GetRelativePath(trackedRoot.Path, path));
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = new FileInfo(path);
                if (existingByPath.TryGetValue(relativePath, out var knownFile)
                    && knownFile.SizeInBytes == info.Length
                    && knownFile.LastModifiedAt.UtcDateTime == info.LastWriteTimeUtc
                    && !string.IsNullOrWhiteSpace(knownFile.QuickHash)
                    && !string.IsNullOrWhiteSpace(knownFile.FileName))
                {
                    MarkAvailable(knownFile, scanStartedAt);
                    skipped++;
                    continue;
                }

                var fingerprint = await fingerprintService.ComputeQuickFingerprintAsync(path, cancellationToken);
                var parsed = EpisodeFileNameParser.Parse(path, trackedRoot.Path);

                if (knownFile is not null)
                {
                    if (!string.IsNullOrWhiteSpace(knownFile.QuickHash)
                        && existingByFingerprint.TryGetValue(FingerprintKey(knownFile.SizeInBytes, knownFile.QuickHash), out var oldGroup))
                        oldGroup.Remove(knownFile);
                    UpdatePhysicalMetadata(knownFile, info, relativePath, fingerprint, parsed, scanStartedAt);
                    var knownKey = FingerprintKey(info.Length, fingerprint);
                    if (!existingByFingerprint.TryGetValue(knownKey, out var knownGroup)) existingByFingerprint[knownKey] = [knownFile];
                    else if (!knownGroup.Contains(knownFile)) knownGroup.Add(knownFile);
                    if (parsed.CanAutoImport)
                    {
                        var replacementMatch = matcher.Match(parsed, matchEntries);
                        var knownEpisode = knownFile.Episode ?? episodes.FirstOrDefault(item => item.Id == knownFile.EpisodeId);
                        var knownSeason = knownEpisode is null ? null : seasons.FirstOrDefault(item => item.Id == knownEpisode.SeasonId);
                        var knownAnime = knownSeason is null ? null : anime.FirstOrDefault(item => item.Id == knownSeason.AnimeId);
                        var parsedEpisode = Convert.ToInt32(parsed.EpisodeNumber!.Value);
                        var appearsReplaced = replacementMatch.Best is null
                            ? knownAnime is not null && parsed.NormalizedTitle != AnimeTitleNormalizer.Normalize(knownAnime.Title)
                            : replacementMatch.Best.AnimeId != knownSeason?.AnimeId
                                || parsed.SeasonNumber != knownSeason.Number
                                || parsedEpisode != knownEpisode!.Number;
                        if (appearsReplaced)
                        {
                            UpsertReviewItem(pendingByPath, trackedRoot.Id, relativePath, info, fingerprint, parsed, replacementMatch, scanStartedAt);
                            needsReview++;
                        }
                    }
                    matched++;
                    continue;
                }

                var fingerprintKey = FingerprintKey(info.Length, fingerprint);
                if (existingByFingerprint.TryGetValue(fingerprintKey, out var sameContent))
                {
                    var movedFile = sameContent.FirstOrDefault(candidate =>
                        candidate.Availability != MediaFileAvailability.Available
                        || (candidate.LibraryRootId == trackedRoot.Id
                            && !discoveredPaths.Contains(NormalizeRelativePath(candidate.RelativePath))));
                    if (movedFile is not null)
                    {
                        existingByPath.Remove(NormalizeRelativePath(movedFile.RelativePath));
                        movedFile.LibraryRootId = trackedRoot.Id;
                        movedFile.LibraryRoot = trackedRoot;
                        UpdatePhysicalMetadata(movedFile, info, relativePath, fingerprint, parsed, scanStartedAt);
                        existingByPath[relativePath] = movedFile;
                        if (!existingFiles.Contains(movedFile)) existingFiles.Add(movedFile);
                        moved++;
                        matched++;
                        continue;
                    }

                    var original = sameContent[0];
                    var duplicate = CreateMediaFile(original.EpisodeId, trackedRoot.Id, info, relativePath, fingerprint, parsed, scanStartedAt);
                    duplicate.DuplicateOfMediaFileId = original.Id;
                    duplicate.IsPreferred = false;
                    database.MediaFiles.Add(duplicate);
                    allExistingFiles.Add(duplicate);
                    existingFiles.Add(duplicate);
                    sameContent.Add(duplicate);
                    existingByPath[relativePath] = duplicate;
                    if (pendingByPath.TryGetValue(relativePath, out var duplicateReview)) duplicateReview.Status = LibraryReviewStatus.Resolved;
                    duplicates++;
                    matched++;
                    continue;
                }

                if (!parsed.CanAutoImport)
                {
                    UpsertReviewItem(pendingByPath, trackedRoot.Id, relativePath, info, fingerprint, parsed, null, scanStartedAt);
                    needsReview++;
                    unrecognized.Add(relativePath);
                    continue;
                }

                var match = matcher.Match(parsed, matchEntries);
                Anime targetAnime;
                if (match.Best is null)
                {
                    targetAnime = new Anime { Title = parsed.CandidateTitle! };
                    var canonicalAlias = new AnimeAlias
                    {
                        AnimeId = targetAnime.Id,
                        Alias = targetAnime.Title,
                        NormalizedAlias = AnimeTitleNormalizer.Normalize(targetAnime.Title),
                        Source = AnimeAliasSource.Canonical
                    };
                    targetAnime.Aliases.Add(canonicalAlias);
                    database.Anime.Add(targetAnime);
                    anime.Add(targetAnime);
                    matchEntries.Add(ToMatchEntry(targetAnime));
                }
                else if (!match.CanAutoMatch)
                {
                    UpsertReviewItem(pendingByPath, trackedRoot.Id, relativePath, info, fingerprint, parsed, match, scanStartedAt);
                    needsReview++;
                    unrecognized.Add(relativePath);
                    continue;
                }
                else
                {
                    targetAnime = anime.Single(item => item.Id == match.Best.AnimeId);
                }

                var season = seasons.FirstOrDefault(item => item.AnimeId == targetAnime.Id && item.Number == parsed.SeasonNumber);
                if (season is null)
                {
                    season = new Season { AnimeId = targetAnime.Id, Number = parsed.SeasonNumber, Anime = targetAnime };
                    seasons.Add(season);
                    database.Seasons.Add(season);
                }

                var episodeNumber = Convert.ToInt32(parsed.EpisodeNumber!.Value);
                var episode = episodes.FirstOrDefault(item => item.SeasonId == season.Id && item.Number == episodeNumber);
                if (episode is null)
                {
                    episode = new Episode
                    {
                        SeasonId = season.Id,
                        Season = season,
                        Number = episodeNumber,
                        Title = parsed.EpisodeTitle
                    };
                    episodes.Add(episode);
                    database.Episodes.Add(episode);
                    addedEpisodes++;
                }

                var mediaFile = CreateMediaFile(episode.Id, trackedRoot.Id, info, relativePath, fingerprint, parsed, scanStartedAt);
                mediaFile.Episode = episode;
                mediaFile.IsPreferred = !episode.MediaFiles.Any(file => file.IsPreferred && file.Availability == MediaFileAvailability.Available);
                episode.MediaFiles.Add(mediaFile);
                database.MediaFiles.Add(mediaFile);
                allExistingFiles.Add(mediaFile);
                existingFiles.Add(mediaFile);
                existingByPath[relativePath] = mediaFile;
                existingByFingerprint[fingerprintKey] = [mediaFile];
                if (pendingByPath.TryGetValue(relativePath, out var resolvedReview)) resolvedReview.Status = LibraryReviewStatus.Resolved;
                matched++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                errors++;
                unrecognized.Add(relativePath);
            }
            finally
            {
                processed++;
                progress?.Report(new LibraryScanProgress(processed, files.Count, Path.GetFileName(path), matched, needsReview, duplicates));
                if (processed % 250 == 0) await database.SaveChangesAsync(cancellationToken);
            }
        }

        var missing = 0;
        foreach (var file in existingFiles.Where(file => !discoveredPaths.Contains(NormalizeRelativePath(file.RelativePath))))
        {
            if (file.LastSeenAt >= scanStartedAt) continue;
            file.Availability = MediaFileAvailability.Missing;
            file.MissingSince ??= scanStartedAt;
            missing++;
        }

        trackedRoot.LastScanAt = DateTimeOffset.UtcNow;
        trackedRoot.LastUnavailableAt = null;
        await database.SaveChangesAsync(cancellationToken);
        System.Diagnostics.Debug.WriteLine($"AniT scan: {files.Count} files, {matched} matched, {needsReview} review, {duplicates} duplicates, {errors} errors.");
        return new LibraryScanResult(files.Count, addedEpisodes, skipped, unrecognized, matched, needsReview, duplicates, moved, missing, errors);
    }

    private void UpsertReviewItem(
        IDictionary<string, LibraryReviewItem> pendingByPath,
        Guid rootId,
        string relativePath,
        FileInfo info,
        string fingerprint,
        ParsedMediaFile parsed,
        AnimeMatchResult? match,
        DateTimeOffset seenAt)
    {
        if (!pendingByPath.TryGetValue(relativePath, out var item))
        {
            item = new LibraryReviewItem
            {
                LibraryRootId = rootId,
                RelativePath = relativePath,
                FileName = info.Name
            };
            pendingByPath[relativePath] = item;
            database.LibraryReviewItems.Add(item);
        }

        item.FileName = info.Name;
        item.SizeInBytes = info.Length;
        item.LastModifiedAt = info.LastWriteTimeUtc;
        item.QuickHash = fingerprint;
        item.CandidateTitle = parsed.CandidateTitle;
        item.NormalizedTitle = parsed.NormalizedTitle;
        item.SuggestedSeasonNumber = parsed.SeasonNumber;
        item.SuggestedEpisodeNumber = parsed.EpisodeNumber;
        item.SuggestedAnimeId = match?.Best?.AnimeId;
        item.Confidence = match?.Best?.Confidence ?? 0;
        item.Reasons = match?.Best is null ? parsed.Kind.ToString() : string.Join(", ", match.Best.Reasons);
        item.Resolution = parsed.Resolution;
        item.ReleaseGroup = parsed.ReleaseGroup;
        item.Language = parsed.Language;
        item.LastSeenAt = seenAt;
        item.Status = LibraryReviewStatus.Pending;
    }

    private static AnimeMatchEntry ToMatchEntry(Anime anime) => new(
        anime.Id,
        anime.Title,
        anime.EnglishTitle,
        anime.Aliases.Select(alias => alias.Alias).ToArray());

    private static MediaFile CreateMediaFile(
        Guid episodeId,
        Guid rootId,
        FileInfo info,
        string relativePath,
        string fingerprint,
        ParsedMediaFile parsed,
        DateTimeOffset seenAt)
    {
        var file = new MediaFile
        {
            EpisodeId = episodeId,
            LibraryRootId = rootId,
            RelativePath = relativePath
        };
        UpdatePhysicalMetadata(file, info, relativePath, fingerprint, parsed, seenAt);
        return file;
    }

    private static void UpdatePhysicalMetadata(
        MediaFile file,
        FileInfo info,
        string relativePath,
        string fingerprint,
        ParsedMediaFile parsed,
        DateTimeOffset seenAt)
    {
        file.RelativePath = relativePath;
        file.FileName = info.Name;
        file.Extension = info.Extension;
        file.SizeInBytes = info.Length;
        file.LastModifiedAt = info.LastWriteTimeUtc;
        file.QuickHash = fingerprint;
        file.Resolution = parsed.Resolution;
        file.ReleaseGroup = parsed.ReleaseGroup;
        file.Language = parsed.Language;
        MarkAvailable(file, seenAt);
    }

    private static void MarkAvailable(MediaFile file, DateTimeOffset seenAt)
    {
        file.LastSeenAt = seenAt;
        file.MissingSince = null;
        file.Availability = MediaFileAvailability.Available;
    }

    private static string NormalizeRelativePath(string path) => path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    private static string FingerprintKey(long size, string hash) => $"{size}:{hash}";
}

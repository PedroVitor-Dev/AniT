using AniT.Core;
using Microsoft.EntityFrameworkCore;

namespace AniT.Infrastructure;

public sealed record LibraryScanResult(int FilesFound, int EpisodesAdded, int FilesSkipped, IReadOnlyList<string> UnrecognizedFiles);

public sealed class LibraryScanner(AniTDbContext database)
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".mov", ".webm", ".m4v", ".wmv"
    };

    public async Task<LibraryScanResult> ScanAsync(LibraryRoot root, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(root.Path)) throw new DirectoryNotFoundException($"A pasta '{root.Path}' não existe mais.");

        var files = Directory.EnumerateFiles(root.Path, "*", SearchOption.AllDirectories)
            .Where(path => VideoExtensions.Contains(Path.GetExtension(path)))
            .ToList();
        var unrecognized = new List<string>();
        var added = 0;
        var skipped = 0;

        foreach (var path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(root.Path, path);
            if (await database.MediaFiles.AnyAsync(file => file.LibraryRootId == root.Id && file.RelativePath == relativePath, cancellationToken))
            {
                skipped++;
                continue;
            }

            if (!EpisodeFileNameParser.TryParse(path, root.Path, out var identity))
            {
                unrecognized.Add(relativePath);
                continue;
            }

            var anime = await database.Anime.FirstOrDefaultAsync(item => item.Title == identity.Title, cancellationToken);
            if (anime is null)
            {
                anime = new Anime { Title = identity.Title };
                database.Anime.Add(anime);
            }

            var season = await database.Seasons.FirstOrDefaultAsync(item => item.AnimeId == anime.Id && item.Number == identity.SeasonNumber, cancellationToken);
            if (season is null)
            {
                season = new Season { Anime = anime, Number = identity.SeasonNumber };
                database.Seasons.Add(season);
            }

            var episode = await database.Episodes.FirstOrDefaultAsync(item => item.SeasonId == season.Id && item.Number == identity.EpisodeNumber, cancellationToken);
            if (episode is not null)
            {
                skipped++;
                continue;
            }

            var info = new FileInfo(path);
            episode = new Episode { Season = season, Number = identity.EpisodeNumber, Title = identity.EpisodeTitle };
            database.Episodes.Add(episode);
            database.MediaFiles.Add(new MediaFile
            {
                Episode = episode,
                LibraryRootId = root.Id,
                RelativePath = relativePath,
                SizeInBytes = info.Length,
                LastModifiedAt = info.LastWriteTimeUtc
            });
            added++;
        }

        await database.SaveChangesAsync(cancellationToken);
        return new LibraryScanResult(files.Count, added, skipped, unrecognized);
    }
}

using AniT.Core;
using Microsoft.EntityFrameworkCore;

namespace AniT.Infrastructure;

public sealed class LibraryOrganizer(AniTDbContext database)
{
    private static readonly string[] SidecarExtensions = [".srt", ".ass", ".ssa", ".vtt", ".sub"];

    public async Task<FileOrganizationPlan> BuildPlanAsync(
        IEnumerable<Guid> mediaFileIds,
        string destinationRoot,
        string fileTemplate = "{AnimeTitle} - S{Season:00}E{Episode:00}",
        CancellationToken cancellationToken = default)
    {
        var ids = mediaFileIds.ToHashSet();
        var files = await database.MediaFiles
            .Where(file => ids.Contains(file.Id))
            .Include(file => file.LibraryRoot)
            .Include(file => file.Episode)!.ThenInclude(episode => episode!.Season)!.ThenInclude(season => season!.Anime)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var operations = new List<FileOrganizationOperation>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = Path.Combine(file.LibraryRoot!.Path, file.RelativePath);
            var anime = file.Episode!.Season!.Anime!;
            var safeAnimeTitle = Sanitize(anime.Title);
            var baseName = fileTemplate
                .Replace("{AnimeTitle}", safeAnimeTitle, StringComparison.Ordinal)
                .Replace("{Season:00}", file.Episode.Season.Number.ToString("00"), StringComparison.Ordinal)
                .Replace("{Episode:00}", file.Episode.Number.ToString("00"), StringComparison.Ordinal);
            if (!string.IsNullOrWhiteSpace(file.Resolution)) baseName += $" [{Sanitize(file.Resolution)}]";
            if (!string.IsNullOrWhiteSpace(file.Language)) baseName += $" [{Sanitize(file.Language)}]";
            var destination = Path.Combine(destinationRoot, safeAnimeTitle, $"Season {file.Episode.Season.Number:00}", baseName + file.Extension);
            var sidecars = FindSidecars(source, destination);
            var sourceExists = File.Exists(source);
            var conflict = File.Exists(destination) && !string.Equals(source, destination, StringComparison.OrdinalIgnoreCase);
            var sidecarConflict = sidecars.Any(sidecar => File.Exists(sidecar.DestinationPath));
            var message = !sourceExists
                ? "O arquivo de origem não está disponível."
                : conflict ? "O destino já existe."
                : sidecarConflict ? "Uma legenda já existe no destino."
                : null;
            operations.Add(new FileOrganizationOperation(file.Id, source, destination, sidecars, !sourceExists || conflict || sidecarConflict, message));
        }
        return new FileOrganizationPlan(operations);
    }

    public async Task<FileOrganizationResult> ExecuteAsync(
        FileOrganizationPlan plan,
        IEnumerable<Guid> selectedMediaFileIds,
        CancellationToken cancellationToken = default)
    {
        var selected = selectedMediaFileIds.ToHashSet();
        var errors = new List<string>();
        var completed = 0;
        var roots = await database.LibraryRoots.ToListAsync(cancellationToken);
        foreach (var operation in plan.Operations.Where(item => selected.Contains(item.MediaFileId)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operation.HasConflict)
            {
                errors.Add(operation.ValidationMessage ?? $"Conflito em {Path.GetFileName(operation.DestinationPath)}");
                continue;
            }
            var completedMoves = new List<(string Source, string Destination)>();
            try
            {
                if (!File.Exists(operation.SourcePath)) throw new FileNotFoundException("O arquivo de origem não está mais disponível.", operation.SourcePath);
                if (File.Exists(operation.DestinationPath)) throw new IOException("O destino passou a existir depois da prévia.");
                if (operation.Sidecars.Any(sidecar => File.Exists(sidecar.DestinationPath))) throw new IOException("Um sidecar passou a existir no destino depois da prévia.");
                Directory.CreateDirectory(Path.GetDirectoryName(operation.DestinationPath)!);
                File.Move(operation.SourcePath, operation.DestinationPath, overwrite: false);
                completedMoves.Add((operation.SourcePath, operation.DestinationPath));
                foreach (var sidecar in operation.Sidecars)
                {
                    File.Move(sidecar.SourcePath, sidecar.DestinationPath, overwrite: false);
                    completedMoves.Add((sidecar.SourcePath, sidecar.DestinationPath));
                }

                var file = await database.MediaFiles.Include(item => item.LibraryRoot)
                    .SingleAsync(item => item.Id == operation.MediaFileId, cancellationToken);
                var matchingRoot = roots
                    .Where(root => IsPathInside(operation.DestinationPath, root.Path))
                    .OrderByDescending(root => root.Path.Length)
                    .FirstOrDefault();
                if (matchingRoot is null)
                {
                    var seasonDirectory = Path.GetDirectoryName(operation.DestinationPath)!;
                    var animeDirectory = Path.GetDirectoryName(seasonDirectory)!;
                    var rootDirectory = Path.GetDirectoryName(animeDirectory)!;
                    matchingRoot = new LibraryRoot
                    {
                        Path = rootDirectory,
                        DisplayName = Path.GetFileName(rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                        IncludeSubfolders = true
                    };
                    roots.Add(matchingRoot);
                    database.LibraryRoots.Add(matchingRoot);
                }
                file.LibraryRootId = matchingRoot.Id;
                file.RelativePath = Path.GetRelativePath(matchingRoot.Path, operation.DestinationPath);
                file.FileName = Path.GetFileName(operation.DestinationPath);
                file.LastModifiedAt = File.GetLastWriteTimeUtc(operation.DestinationPath);
                file.LastSeenAt = DateTimeOffset.UtcNow;
                await database.SaveChangesAsync(cancellationToken);
                completed++;
            }
            catch (OperationCanceledException)
            {
                RollBackMoves(completedMoves);
                throw;
            }
            catch (Exception exception)
            {
                try
                {
                    RollBackMoves(completedMoves);
                    database.ChangeTracker.Clear();
                    roots = await database.LibraryRoots.ToListAsync(CancellationToken.None);
                    errors.Add($"{Path.GetFileName(operation.SourcePath)}: {exception.Message}");
                }
                catch (Exception rollbackException)
                {
                    errors.Add($"{Path.GetFileName(operation.SourcePath)}: falha original: {exception.Message}; rollback incompleto: {rollbackException.Message}");
                }
            }
        }
        return new FileOrganizationResult(completed, errors);
    }

    private static void RollBackMoves(IEnumerable<(string Source, string Destination)> moves)
    {
        foreach (var move in moves.Reverse())
        {
            if (File.Exists(move.Destination) && !File.Exists(move.Source))
                File.Move(move.Destination, move.Source, overwrite: false);
        }
    }

    private static IReadOnlyList<FileSidecarMove> FindSidecars(string videoSource, string videoDestination)
    {
        var sourceWithoutExtension = Path.Combine(Path.GetDirectoryName(videoSource)!, Path.GetFileNameWithoutExtension(videoSource));
        var destinationWithoutExtension = Path.Combine(Path.GetDirectoryName(videoDestination)!, Path.GetFileNameWithoutExtension(videoDestination));
        return SidecarExtensions
            .Select(extension => new FileSidecarMove(sourceWithoutExtension + extension, destinationWithoutExtension + extension))
            .Where(move => File.Exists(move.SourcePath))
            .ToList();
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
    }

    private static bool IsPathInside(string candidate, string root)
    {
        var fullCandidate = Path.GetFullPath(candidate);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }
}

using System.Globalization;
using System.Text.RegularExpressions;

namespace AniT.Core;

public sealed record EpisodeIdentity(string Title, int SeasonNumber, int EpisodeNumber, string? EpisodeTitle);

public static partial class EpisodeFileNameParser
{
    private static readonly string[] SeasonFolderNames = ["season", "temporada", "saison", "s"];

    public static bool TryParse(string filePath, string libraryRoot, out EpisodeIdentity identity)
    {
        identity = default!;
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var cleanName = Cleanup(fileName);
        var match = SeasonEpisodePattern().Match(cleanName);
        var seasonNumber = 1;
        var episodeNumber = 0;

        if (match.Success)
        {
            seasonNumber = ParseNumber(match.Groups["season"].Value, 1);
            episodeNumber = ParseNumber(match.Groups["episode"].Value, 0);
        }
        else
        {
            match = EpisodePrefixPattern().Match(cleanName);
            if (!match.Success)
            {
                match = LastNumberPattern().Match(cleanName);
            }

            if (!match.Success) return false;
            episodeNumber = ParseNumber(match.Groups["episode"].Value, 0);
        }

        if (episodeNumber <= 0) return false;

        var relativeDirectory = Path.GetRelativePath(libraryRoot, Path.GetDirectoryName(filePath)!);
        var folders = relativeDirectory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(folder => !string.IsNullOrWhiteSpace(folder) && folder != ".")
            .ToArray();

        if (folders.Length > 0 && TryGetSeasonFromFolder(folders[^1], out var folderSeason))
        {
            seasonNumber = folderSeason;
            folders = folders[..^1];
        }

        var title = folders.Length > 0 ? folders[0] : ExtractTitle(cleanName, match);
        title = Cleanup(title);
        if (string.IsNullOrWhiteSpace(title)) return false;

        var episodeTitle = ExtractEpisodeTitle(cleanName, match);
        identity = new EpisodeIdentity(title, seasonNumber, episodeNumber, episodeTitle);
        return true;
    }

    private static string ExtractTitle(string name, Match episodeMatch)
    {
        var beforeEpisode = name[..episodeMatch.Index].Trim(' ', '-', '.', '_');
        return string.IsNullOrWhiteSpace(beforeEpisode) ? name : beforeEpisode;
    }

    private static string? ExtractEpisodeTitle(string name, Match episodeMatch)
    {
        var end = episodeMatch.Index + episodeMatch.Length;
        if (end >= name.Length) return null;
        var title = name[end..].Trim(' ', '-', '.', '_');
        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    private static bool TryGetSeasonFromFolder(string folder, out int seasonNumber)
    {
        var match = SeasonFolderPattern().Match(folder.Trim());
        seasonNumber = match.Success ? ParseNumber(match.Groups["season"].Value, 1) : 1;
        return match.Success;
    }

    private static string Cleanup(string value) =>
        WhitespacePattern().Replace(BracketPattern().Replace(value, string.Empty), " ").Trim();

    private static int ParseNumber(string value, int fallback) =>
        int.TryParse(value.Split('.', ',')[0], CultureInfo.InvariantCulture, out var number) ? number : fallback;

    [GeneratedRegex(@"(?i)\bS(?<season>\d{1,2})[ ._-]*E(?:P)?(?<episode>\d{1,3}(?:[.,]\d+)?)\b")]
    private static partial Regex SeasonEpisodePattern();

    [GeneratedRegex(@"(?i)\bE(?:P)?[ ._-]?(?<episode>\d{1,3}(?:[.,]\d+)?)\b")]
    private static partial Regex EpisodePrefixPattern();

    [GeneratedRegex(@"(?:^|\s-\s|\s)(?<episode>\d{1,3})(?=\s|$)")]
    private static partial Regex LastNumberPattern();

    [GeneratedRegex(@"(?i)^(?:season|temporada|saison|s)[ ._-]?(?<season>\d{1,2})$")]
    private static partial Regex SeasonFolderPattern();

    [GeneratedRegex(@"\[[^\]]*\]|\([^\)]*\)")]
    private static partial Regex BracketPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}

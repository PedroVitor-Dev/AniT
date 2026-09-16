using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AniT.Core;

public enum ParsedEpisodeKind { Regular, Decimal, Special, MultiEpisode, Unknown }

public sealed record EpisodeIdentity(string Title, int SeasonNumber, int EpisodeNumber, string? EpisodeTitle);

public sealed record ParsedMediaFile(
    string OriginalFileName,
    string? CandidateTitle,
    string NormalizedTitle,
    string? FolderTitle,
    int SeasonNumber,
    double? EpisodeNumber,
    double? EndingEpisodeNumber,
    ParsedEpisodeKind Kind,
    string? EpisodeTitle,
    string? Resolution,
    string? ReleaseGroup,
    string? Language,
    string? SourceName)
{
    public bool CanAutoImport => Kind == ParsedEpisodeKind.Regular
        && EpisodeNumber is > 0
        && Math.Abs(EpisodeNumber.Value % 1) < 0.0001
        && !string.IsNullOrWhiteSpace(CandidateTitle);
}

public static partial class AnimeTitleNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(character)) builder.Append(char.ToLowerInvariant(character));
            else if (character == '&') builder.Append(" and ");
            else builder.Append(' ');
        }

        return WhitespacePattern().Replace(builder.ToString(), " ").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}

public static partial class EpisodeFileNameParser
{
    private static readonly HashSet<string> GenericFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "anime", "animes", "downloads", "download", "videos", "video", "media", "biblioteca"
    };

    private static readonly Regex[] TechnicalTagPatterns =
    [
        ResolutionPattern(), SourcePattern(), CodecPattern(), AudioPattern(), LanguagePattern(), BitDepthPattern(), HashPattern()
    ];

    public static bool TryParse(string filePath, string libraryRoot, out EpisodeIdentity identity)
    {
        var parsed = Parse(filePath, libraryRoot);
        if (!parsed.CanAutoImport)
        {
            identity = default!;
            return false;
        }

        identity = new EpisodeIdentity(
            parsed.CandidateTitle!,
            parsed.SeasonNumber,
            Convert.ToInt32(parsed.EpisodeNumber!.Value),
            parsed.EpisodeTitle);
        return true;
    }

    public static ParsedMediaFile Parse(string filePath, string libraryRoot)
    {
        var originalFileName = Path.GetFileName(filePath);
        var rawName = Path.GetFileNameWithoutExtension(filePath);
        var folderTitle = FindFolderTitle(filePath, libraryRoot, out var folderSeason);
        var seasonNumber = folderSeason ?? 1;
        var resolution = FindValue(rawName, ResolutionPattern());
        var source = FindValue(rawName, SourcePattern());
        var language = FindValue(rawName, LanguagePattern());
        var releaseGroup = ExtractReleaseGroup(rawName);

        var working = RemoveNoise(rawName, releaseGroup);
        var match = SeasonEpisodePattern().Match(working);
        var kind = ParsedEpisodeKind.Unknown;
        double? episode = null;
        double? endingEpisode = null;

        if (match.Success)
        {
            seasonNumber = ParseInt(match.Groups["season"].Value, seasonNumber);
            episode = ParseDouble(match.Groups["episode"].Value);
            endingEpisode = ParseDouble(match.Groups["ending"].Value);
            kind = endingEpisode is not null ? ParsedEpisodeKind.MultiEpisode : ClassifyNumber(episode);
        }
        else
        {
            match = CrossPattern().Match(working);
            if (match.Success)
            {
                seasonNumber = ParseInt(match.Groups["season"].Value, seasonNumber);
                episode = ParseDouble(match.Groups["episode"].Value);
                kind = ClassifyNumber(episode);
            }
            else
            {
                match = EpisodePrefixPattern().Match(working);
                if (!match.Success) match = DashEpisodePattern().Match(working);
                if (!match.Success) match = BracketEpisodePattern().Match(rawName);
                if (!match.Success) match = TrailingEpisodePattern().Match(working);
                if (match.Success)
                {
                    episode = ParseDouble(match.Groups["episode"].Value);
                    endingEpisode = ParseDouble(match.Groups["ending"].Value);
                    kind = endingEpisode is not null ? ParsedEpisodeKind.MultiEpisode : ClassifyNumber(episode);
                }
            }
        }

        if (!match.Success)
        {
            var special = SpecialPattern().Match(working);
            if (special.Success)
            {
                match = special;
                kind = ParsedEpisodeKind.Special;
                seasonNumber = 0;
            }
        }

        var fileTitle = match.Success ? ExtractTitle(working, match) : CleanupSeparators(working);
        var candidateTitle = ChooseCandidateTitle(fileTitle, folderTitle);
        var episodeTitle = match.Success ? ExtractEpisodeTitle(working, match) : null;
        return new ParsedMediaFile(
            originalFileName,
            candidateTitle,
            AnimeTitleNormalizer.Normalize(candidateTitle),
            folderTitle,
            seasonNumber,
            episode,
            endingEpisode,
            kind,
            episodeTitle,
            resolution,
            releaseGroup,
            language,
            source);
    }

    private static ParsedEpisodeKind ClassifyNumber(double? number) => number is null
        ? ParsedEpisodeKind.Unknown
        : Math.Abs(number.Value % 1) > 0.0001 ? ParsedEpisodeKind.Decimal : ParsedEpisodeKind.Regular;

    private static string? ChooseCandidateTitle(string? fileTitle, string? folderTitle)
    {
        var cleanFileTitle = CleanupSeparators(fileTitle ?? string.Empty);
        if (string.IsNullOrWhiteSpace(cleanFileTitle)) return folderTitle;
        if (NumberOnlyPattern().IsMatch(cleanFileTitle) && !string.IsNullOrWhiteSpace(folderTitle)) return folderTitle;
        if (!string.IsNullOrWhiteSpace(folderTitle)
            && (cleanFileTitle.Length <= 2 || AnimeTitleNormalizer.Normalize(cleanFileTitle) is "episode" or "episodio")) return folderTitle;
        return cleanFileTitle;
    }

    private static string? FindFolderTitle(string filePath, string libraryRoot, out int? seasonNumber)
    {
        seasonNumber = null;
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory)) return null;
        var relative = Path.GetRelativePath(libraryRoot, directory);
        var folders = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Where(folder => folder != ".")
            .ToArray();
        for (var index = folders.Length - 1; index >= 0; index--)
        {
            var folder = CleanupSeparators(folders[index]);
            var seasonMatch = SeasonFolderPattern().Match(folder);
            if (seasonMatch.Success)
            {
                seasonNumber ??= ParseInt(seasonMatch.Groups["season"].Value, 1);
                continue;
            }
            if (GenericFolders.Contains(folder) || ResolutionPattern().IsMatch(folder)) continue;
            return folder;
        }
        return null;
    }

    private static string RemoveNoise(string value, string? releaseGroup)
    {
        var result = value;
        if (!string.IsNullOrWhiteSpace(releaseGroup))
            result = Regex.Replace(result, $@"^\s*\[{Regex.Escape(releaseGroup)}\]\s*", string.Empty, RegexOptions.IgnoreCase);

        result = BracketContentPattern().Replace(result, match => IsTechnicalTag(match.Groups["content"].Value) ? " " : match.Value);
        result = ParenthesisContentPattern().Replace(result, match => IsTechnicalTag(match.Groups["content"].Value) ? " " : match.Value);
        foreach (var pattern in TechnicalTagPatterns) result = pattern.Replace(result, " ");
        return CleanupSeparators(result);
    }

    private static bool IsTechnicalTag(string value) => TechnicalTagPatterns.Any(pattern => pattern.IsMatch(value.Trim()));

    private static string? ExtractReleaseGroup(string value)
    {
        var match = LeadingBracketPattern().Match(value);
        if (!match.Success) return null;
        var candidate = match.Groups["content"].Value.Trim();
        return IsTechnicalTag(candidate) || NumberOnlyPattern().IsMatch(candidate) ? null : candidate;
    }

    private static string? FindValue(string value, Regex pattern)
    {
        var match = pattern.Match(value);
        return match.Success ? match.Value.Trim('[', ']', '(', ')', ' ') : null;
    }

    private static string ExtractTitle(string name, Match episodeMatch) =>
        CleanupSeparators(name[..episodeMatch.Index]);

    private static string? ExtractEpisodeTitle(string name, Match episodeMatch)
    {
        var end = episodeMatch.Index + episodeMatch.Length;
        if (end >= name.Length) return null;
        var value = CleanupSeparators(name[end..]);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string CleanupSeparators(string value)
    {
        var spaced = SeparatorPattern().Replace(value, " ");
        return WhitespacePattern().Replace(spaced, " ").Trim(' ', '-', '.', '_');
    }

    private static int ParseInt(string value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : fallback;

    private static double? ParseDouble(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return double.TryParse(value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    [GeneratedRegex(@"(?i)\bS(?<season>\d{1,2})[ ._-]*E(?:P)?(?<episode>\d{1,4}(?:[.,]\d+)?)(?:[ ._-]*(?:-|to|E)(?<ending>\d{1,4}(?:[.,]\d+)?))?(?:v\d+)?\b")]
    private static partial Regex SeasonEpisodePattern();
    [GeneratedRegex(@"(?i)\b(?<season>\d{1,2})x(?<episode>\d{1,4}(?:[.,]\d+)?)\b")]
    private static partial Regex CrossPattern();
    [GeneratedRegex(@"(?i)\b(?:E|EP|Episode|Epis[oó]dio)[ ._-]?(?<episode>\d{1,4}(?:[.,]\d+)?)(?:v\d+)?\b")]
    private static partial Regex EpisodePrefixPattern();
    [GeneratedRegex(@"(?i)(?:^|\s)-\s*(?<episode>\d{1,4}(?:[.,]\d+)?)(?:\s*-\s*(?<ending>\d{1,4}(?:[.,]\d+)?))?(?:v\d+)?\b")]
    private static partial Regex DashEpisodePattern();
    [GeneratedRegex(@"(?i)\[(?<episode>\d{1,4}(?:[.,]\d+)?)\](?:v\d+)?")]
    private static partial Regex BracketEpisodePattern();
    [GeneratedRegex(@"(?i)(?:^|\s)(?<episode>\d{1,4}(?:[.,]\d+)?)(?:v\d+)?$")]
    private static partial Regex TrailingEpisodePattern();
    [GeneratedRegex(@"(?i)\b(?<special>OVA|OAD|Special|Recap|NCOP|NCED|Opening|Ending|PV|Trailer)\b")]
    private static partial Regex SpecialPattern();
    [GeneratedRegex(@"(?i)^(?:season|temporada|saison|s)[ ._-]?(?<season>\d{1,2})$")]
    private static partial Regex SeasonFolderPattern();
    [GeneratedRegex(@"(?i)\b(?:480|576|720|1080|1440|2160|4320)p\b")]
    private static partial Regex ResolutionPattern();
    [GeneratedRegex(@"(?i)\b(?:WEB[- .]?DL|WEBRip|WEB|BluRay|BDRip|BD|HDTV|DVD)\b")]
    private static partial Regex SourcePattern();
    [GeneratedRegex(@"(?i)\b(?:x264|x265|H\.?264|H\.?265|HEVC|AV1)\b")]
    private static partial Regex CodecPattern();
    [GeneratedRegex(@"(?i)\b(?:AAC|FLAC|Opus|Dual[ ._-]?Audio|Multi[ ._-]?Subs?)\b")]
    private static partial Regex AudioPattern();
    [GeneratedRegex(@"(?i)\b(?:PT[- .]?BR|ENG|JPN|POR|Portugu[eê]s|English|Japanese)\b")]
    private static partial Regex LanguagePattern();
    [GeneratedRegex(@"(?i)\b(?:8|10)[ ._-]?bit\b")]
    private static partial Regex BitDepthPattern();
    [GeneratedRegex(@"(?i)\b[0-9A-F]{8}\b")]
    private static partial Regex HashPattern();
    [GeneratedRegex(@"^\s*\[(?<content>[^\]]+)\]")]
    private static partial Regex LeadingBracketPattern();
    [GeneratedRegex(@"\[(?<content>[^\]]+)\]")]
    private static partial Regex BracketContentPattern();
    [GeneratedRegex(@"\((?<content>[^\)]+)\)")]
    private static partial Regex ParenthesisContentPattern();
    [GeneratedRegex(@"(?<!\d)\.(?!\d)|_+")]
    private static partial Regex SeparatorPattern();
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
    [GeneratedRegex(@"^\d+(?:[.,]\d+)?$")]
    private static partial Regex NumberOnlyPattern();
}

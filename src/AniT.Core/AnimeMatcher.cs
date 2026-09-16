namespace AniT.Core;

public enum AnimeMatchReason
{
    ExactCanonicalTitle,
    ExactAlias,
    ExactEnglishTitle,
    ExactFolderTitle,
    FuzzyCanonicalTitle,
    FuzzyAlias,
    FolderSupportsCandidate,
    AmbiguousCandidates,
    NoCandidate
}

public sealed record AnimeMatchEntry(
    Guid AnimeId,
    string CanonicalTitle,
    string? EnglishTitle,
    IReadOnlyCollection<string> Aliases);

public sealed record AnimeMatchCandidate(
    Guid AnimeId,
    string CanonicalTitle,
    double Confidence,
    IReadOnlyList<AnimeMatchReason> Reasons);

public sealed record AnimeMatchResult(
    AnimeMatchCandidate? Best,
    IReadOnlyList<AnimeMatchCandidate> Candidates,
    bool CanAutoMatch,
    bool IsAmbiguous)
{
    public static AnimeMatchResult None { get; } = new(null, [], false, false);
}

public static class AnimeMatchThresholds
{
    public const double Automatic = 0.92;
    public const double ReviewSuggested = 0.72;
    public const double MinimumCandidate = 0.58;
    public const double AmbiguityMargin = 0.08;
}

/// <summary>
/// Deterministic, conservative matcher. Exact evidence has fixed confidence;
/// approximate evidence is bounded and requires a clear margin over runner-up.
/// </summary>
public sealed class AnimeMatcher
{
    public AnimeMatchResult Match(ParsedMediaFile parsed, IEnumerable<AnimeMatchEntry> entries)
    {
        var title = parsed.NormalizedTitle;
        var folder = AnimeTitleNormalizer.Normalize(parsed.FolderTitle);
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(folder)) return AnimeMatchResult.None;

        var candidates = entries
            .Select(entry => Score(entry, title, folder))
            .Where(candidate => candidate.Confidence >= AnimeMatchThresholds.MinimumCandidate)
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.CanonicalTitle, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (candidates.Count == 0) return AnimeMatchResult.None;

        var best = candidates[0];
        var ambiguous = candidates.Count > 1
            && best.Confidence - candidates[1].Confidence < AnimeMatchThresholds.AmbiguityMargin;
        if (ambiguous)
        {
            best = best with { Reasons = [.. best.Reasons, AnimeMatchReason.AmbiguousCandidates] };
            candidates[0] = best;
        }

        return new AnimeMatchResult(
            best,
            candidates.Take(5).ToList(),
            !ambiguous && best.Confidence >= AnimeMatchThresholds.Automatic,
            ambiguous);
    }

    private static AnimeMatchCandidate Score(AnimeMatchEntry entry, string title, string folder)
    {
        var canonical = AnimeTitleNormalizer.Normalize(entry.CanonicalTitle);
        var english = AnimeTitleNormalizer.Normalize(entry.EnglishTitle);
        var aliases = entry.Aliases.Select(AnimeTitleNormalizer.Normalize).Where(value => value.Length > 0).Distinct().ToArray();
        var reasons = new List<AnimeMatchReason>();
        double score;

        if (title.Length > 0 && title == canonical)
        {
            score = 0.99;
            reasons.Add(AnimeMatchReason.ExactCanonicalTitle);
        }
        else if (title.Length > 0 && english.Length > 0 && title == english)
        {
            score = 0.985;
            reasons.Add(AnimeMatchReason.ExactEnglishTitle);
        }
        else if (title.Length > 0 && aliases.Contains(title))
        {
            score = 0.98;
            reasons.Add(AnimeMatchReason.ExactAlias);
        }
        else if (folder.Length > 0 && (folder == canonical || folder == english || aliases.Contains(folder)))
        {
            score = 0.96;
            reasons.Add(AnimeMatchReason.ExactFolderTitle);
        }
        else
        {
            var canonicalSimilarity = Similarity(title, canonical);
            var englishSimilarity = Similarity(title, english);
            var aliasSimilarity = aliases.Select(alias => Similarity(title, alias)).DefaultIfEmpty(0).Max();
            var bestTitleSimilarity = Math.Max(canonicalSimilarity, Math.Max(englishSimilarity, aliasSimilarity));
            score = 0.45 + bestTitleSimilarity * 0.45;
            reasons.Add(aliasSimilarity > Math.Max(canonicalSimilarity, englishSimilarity)
                ? AnimeMatchReason.FuzzyAlias
                : AnimeMatchReason.FuzzyCanonicalTitle);

            var folderSimilarity = Math.Max(
                Similarity(folder, canonical),
                Math.Max(Similarity(folder, english), aliases.Select(alias => Similarity(folder, alias)).DefaultIfEmpty(0).Max()));
            if (folderSimilarity >= 0.9)
            {
                score = Math.Min(0.95, score + 0.08);
                reasons.Add(AnimeMatchReason.FolderSupportsCandidate);
            }
        }

        return new AnimeMatchCandidate(entry.AnimeId, entry.CanonicalTitle, Math.Round(score, 4), reasons);
    }

    internal static double Similarity(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0) return 0;
        if (left == right) return 1;
        var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var rightTokens = right.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var union = leftTokens.Union(rightTokens).Count();
        var tokenScore = union == 0 ? 0 : (double)leftTokens.Intersect(rightTokens).Count() / union;
        var distance = Levenshtein(left, right);
        var editScore = 1d - (double)distance / Math.Max(left.Length, right.Length);
        return Math.Clamp(editScore * 0.65 + tokenScore * 0.35, 0, 1);
    }

    private static int Levenshtein(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= right.Length; column++)
            {
                var substitution = previous[column - 1] + (left[row - 1] == right[column - 1] ? 0 : 1);
                current[column] = Math.Min(Math.Min(current[column - 1] + 1, previous[column] + 1), substitution);
            }
            (previous, current) = (current, previous);
        }
        return previous[right.Length];
    }
}

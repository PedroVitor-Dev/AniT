namespace AniT.Core;

public enum AnimeMatchReason
{
    ExactCanonicalTitle,
    ExactAlias,
    ExactEnglishTitle,
    ExactOriginalTitle,
    ExactFolderTitle,
    EmbeddedKnownTitle,
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
    IReadOnlyCollection<string> Aliases,
    string? OriginalTitle = null);

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
        var original = AnimeTitleNormalizer.Normalize(entry.OriginalTitle);
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
        else if (title.Length > 0 && original.Length > 0 && title == original)
        {
            score = 0.982;
            reasons.Add(AnimeMatchReason.ExactOriginalTitle);
        }
        else if (title.Length > 0 && aliases.Contains(title))
        {
            score = 0.98;
            reasons.Add(AnimeMatchReason.ExactAlias);
        }
        else if (folder.Length > 0 && (folder == canonical || folder == english || folder == original || aliases.Contains(folder)))
        {
            score = 0.96;
            reasons.Add(AnimeMatchReason.ExactFolderTitle);
        }
        else if (FindEmbeddedTitle(title, canonical, english, original, aliases) is { } embedded)
        {
            score = embedded.IsSuffix ? 0.975 : 0.94;
            reasons.Add(AnimeMatchReason.EmbeddedKnownTitle);
        }
        else
        {
            var canonicalSimilarity = Similarity(title, canonical);
            var englishSimilarity = Similarity(title, english);
            var originalSimilarity = Similarity(title, original);
            var aliasSimilarity = aliases.Select(alias => Similarity(title, alias)).DefaultIfEmpty(0).Max();
            var bestTitleSimilarity = Math.Max(Math.Max(canonicalSimilarity, englishSimilarity), Math.Max(originalSimilarity, aliasSimilarity));
            score = 0.45 + bestTitleSimilarity * 0.45;
            reasons.Add(aliasSimilarity > Math.Max(canonicalSimilarity, englishSimilarity)
                ? AnimeMatchReason.FuzzyAlias
                : AnimeMatchReason.FuzzyCanonicalTitle);

            var folderSimilarity = Math.Max(
                Math.Max(Similarity(folder, canonical), Similarity(folder, english)),
                Math.Max(Similarity(folder, original), aliases.Select(alias => Similarity(folder, alias)).DefaultIfEmpty(0).Max()));
            if (folderSimilarity >= 0.9)
            {
                score = Math.Min(0.95, score + 0.08);
                reasons.Add(AnimeMatchReason.FolderSupportsCandidate);
            }
        }

        return new AnimeMatchCandidate(entry.AnimeId, entry.CanonicalTitle, Math.Round(score, 4), reasons);
    }

    private static EmbeddedTitleMatch? FindEmbeddedTitle(
        string candidate,
        string canonical,
        string english,
        string original,
        IReadOnlyCollection<string> aliases)
    {
        if (candidate.Length == 0) return null;
        var candidateTokens = Tokens(candidate);
        if (candidateTokens.Length == 0) return null;

        return new[] { canonical, english, original }
            .Concat(aliases)
            .Where(value => value.Length > 0 && value != candidate)
            .Distinct(StringComparer.Ordinal)
            .Select(value => new { Value = value, Tokens = Tokens(value) })
            .Where(item => item.Tokens.Length > 0
                && (item.Tokens.Length > 1 || item.Value.Length >= 5 || EndsWith(candidateTokens, item.Tokens))
                && ContainsContiguous(candidateTokens, item.Tokens))
            .Select(item => new EmbeddedTitleMatch(
                EndsWith(candidateTokens, item.Tokens),
                item.Tokens.Length,
                item.Value.Length))
            .OrderByDescending(item => item.IsSuffix)
            .ThenByDescending(item => item.TokenCount)
            .ThenByDescending(item => item.CharacterCount)
            .FirstOrDefault();
    }

    private static string[] Tokens(string value) => value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static bool ContainsContiguous(IReadOnlyList<string> candidate, IReadOnlyList<string> expected)
    {
        if (expected.Count > candidate.Count) return false;
        for (var start = 0; start <= candidate.Count - expected.Count; start++)
        {
            var matches = true;
            for (var index = 0; index < expected.Count; index++)
            {
                if (candidate[start + index] == expected[index]) continue;
                matches = false;
                break;
            }
            if (matches) return true;
        }
        return false;
    }

    private static bool EndsWith(IReadOnlyList<string> candidate, IReadOnlyList<string> expected)
    {
        if (expected.Count > candidate.Count) return false;
        var offset = candidate.Count - expected.Count;
        for (var index = 0; index < expected.Count; index++)
        {
            if (candidate[offset + index] != expected[index]) return false;
        }
        return true;
    }

    private sealed record EmbeddedTitleMatch(bool IsSuffix, int TokenCount, int CharacterCount);

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

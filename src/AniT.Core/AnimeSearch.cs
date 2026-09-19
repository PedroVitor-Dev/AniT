namespace AniT.Core;

public static class AnimeSearch
{
    public static bool Matches(string? query, params string?[] searchableValues)
    {
        var normalizedQuery = AnimeTitleNormalizer.Normalize(query);
        if (normalizedQuery.Length == 0) return true;

        var searchableText = string.Join(
            ' ',
            searchableValues
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(AnimeTitleNormalizer.Normalize));

        return normalizedQuery
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(term => searchableText.Contains(term, StringComparison.Ordinal));
    }
}

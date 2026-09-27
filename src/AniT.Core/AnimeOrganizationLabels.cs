using System.Text.Json;

namespace AniT.Core;

public static class AnimeOrganizationLabels
{
    public static IReadOnlyList<string> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        try
        {
            return (JsonSerializer.Deserialize<string[]>(value) ?? [])
                .Select(item => item?.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch (JsonException)
        {
            // Early preview builds stored newline-separated labels. Preserve those values.
            return value.Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
    }

    public static string? Serialize(IEnumerable<string> values)
    {
        var normalized = values
            .Select(item => item?.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        return normalized.Length == 0 ? null : JsonSerializer.Serialize(normalized);
    }

    public static bool Contains(string? value, string label) =>
        Parse(value).Contains(label, StringComparer.CurrentCultureIgnoreCase);
}

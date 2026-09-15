using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AniT.Infrastructure;

public sealed record AnimeMetadataResult(string? EnglishTitle, string? CoverPath, string? Synopsis, double? CriticScore);

public sealed partial class AnimeCoverProvider
{
    private const string AniListEndpoint = "https://graphql.anilist.co";
    private const string MetadataQuery = """
        query ($search: String) {
          Media(search: $search, type: ANIME) {
            title {
              romaji
              english
              native
            }
            coverImage {
              extraLarge
              large
            }
            description(asHtml: false)
            averageScore
          }
        }
        """;

    private static readonly HttpClient SharedClient = CreateSharedClient();
    private readonly HttpClient httpClient;
    private readonly string coversDirectory;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> animeLocks = new();

    public AnimeCoverProvider(string coversDirectory, HttpClient? httpClient = null)
    {
        this.coversDirectory = coversDirectory;
        this.httpClient = httpClient ?? SharedClient;
    }

    public async Task<AnimeMetadataResult> EnsureMetadataAsync(
        Guid animeId,
        string japaneseTitle,
        string? englishTitle,
        string? savedCoverPath,
        CancellationToken cancellationToken = default,
        string? savedSynopsis = null,
        double? savedCriticScore = null,
        bool forceRefresh = false)
    {
        var existingCoverPath = IsUsableCover(savedCoverPath) ? savedCoverPath : null;
        Directory.CreateDirectory(coversDirectory);
        var cachedPath = Path.Combine(coversDirectory, $"{animeId:N}.jpg");
        existingCoverPath ??= IsUsableCover(cachedPath) ? cachedPath : null;
        if (!forceRefresh
            && !string.IsNullOrWhiteSpace(englishTitle)
            && existingCoverPath is not null
            && !string.IsNullOrWhiteSpace(savedSynopsis)
            && savedCriticScore is not null)
        {
            return new AnimeMetadataResult(englishTitle, existingCoverPath, savedSynopsis, savedCriticScore);
        }

        var animeLock = animeLocks.GetOrAdd(animeId, _ => new SemaphoreSlim(1, 1));
        await animeLock.WaitAsync(cancellationToken);
        try
        {
            existingCoverPath = IsUsableCover(savedCoverPath) ? savedCoverPath : IsUsableCover(cachedPath) ? cachedPath : null;
            if (!forceRefresh
                && !string.IsNullOrWhiteSpace(englishTitle)
                && existingCoverPath is not null
                && !string.IsNullOrWhiteSpace(savedSynopsis)
                && savedCriticScore is not null)
            {
                return new AnimeMetadataResult(englishTitle, existingCoverPath, savedSynopsis, savedCriticScore);
            }

            // The folder title is the most precise identity we have. In particular, a stale
            // English title without "Season 2" can otherwise make AniList return season one.
            var metadata = await FindMetadataAsync(japaneseTitle, cancellationToken);
            if (metadata is null && !string.IsNullOrWhiteSpace(englishTitle))
            {
                metadata = await FindMetadataAsync(englishTitle, cancellationToken);
            }

            var resolvedEnglishTitle = metadata?.EnglishTitle ?? englishTitle;
            var resolvedCoverUrl = metadata?.CoverUrl;
            var resolvedSynopsis = string.IsNullOrWhiteSpace(savedSynopsis) ? metadata?.Synopsis : savedSynopsis;
            var resolvedCriticScore = savedCriticScore ?? metadata?.CriticScore;

            var resolvedCoverPath = existingCoverPath;
            if (resolvedCoverUrl is not null && (forceRefresh || resolvedCoverPath is null))
            {
                resolvedCoverPath = await DownloadCoverAsync(resolvedCoverUrl, cachedPath, cancellationToken) ?? existingCoverPath;
            }

            return new AnimeMetadataResult(resolvedEnglishTitle, resolvedCoverPath, resolvedSynopsis, resolvedCriticScore);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException)
        {
            return new AnimeMetadataResult(englishTitle, existingCoverPath, savedSynopsis, savedCriticScore);
        }
        finally
        {
            animeLock.Release();
        }
    }

    public async Task<string?> EnsureCoverAsync(
        Guid animeId,
        string title,
        string? savedCoverPath,
        CancellationToken cancellationToken = default)
    {
        if (IsUsableCover(savedCoverPath)) return savedCoverPath;
        var cachedPath = Path.Combine(coversDirectory, $"{animeId:N}.jpg");
        if (IsUsableCover(cachedPath)) return cachedPath;
        return (await EnsureMetadataAsync(animeId, title, null, savedCoverPath, cancellationToken)).CoverPath;
    }

    private async Task<AniListMetadata?> FindMetadataAsync(string title, CancellationToken cancellationToken)
    {
        foreach (var candidate in GetSearchCandidates(title))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, AniListEndpoint)
            {
                Content = JsonContent.Create(new
                {
                    query = MetadataQuery,
                    variables = new { search = candidate }
                })
            };

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) continue;
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("data", out var data)
                || !data.TryGetProperty("Media", out var media)
                || media.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            string? english = null;
            if (media.TryGetProperty("title", out var titles)) TryReadText(titles, "english", out english);

            string? coverUrl = null;
            if (media.TryGetProperty("coverImage", out var cover))
            {
                if (!TryReadText(cover, "extraLarge", out coverUrl)) TryReadText(cover, "large", out coverUrl);
            }

            string? synopsis = null;
            if (TryReadText(media, "description", out var description)) synopsis = NormalizeSynopsis(description!);

            double? criticScore = null;
            if (media.TryGetProperty("averageScore", out var score) && score.ValueKind == JsonValueKind.Number)
            {
                criticScore = score.GetDouble();
            }

            if (!string.IsNullOrWhiteSpace(english)
                || Uri.TryCreate(coverUrl, UriKind.Absolute, out _)
                || !string.IsNullOrWhiteSpace(synopsis)
                || criticScore is not null)
            {
                return new AniListMetadata(english, coverUrl, synopsis, criticScore);
            }
        }

        return null;
    }

    private async Task<string?> DownloadCoverAsync(string coverUrl, string cachedPath, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(coverUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
        {
            return null;
        }

        var temporaryPath = cachedPath + ".download";
        try
        {
            await using (var destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(destination, cancellationToken);
            }

            if (new FileInfo(temporaryPath).Length == 0) return null;
            File.Move(temporaryPath, cachedPath, true);
            return cachedPath;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static IEnumerable<string> GetSearchCandidates(string title)
    {
        var cleaned = WhitespaceRegex().Replace(title.Trim(), " ");
        var candidates = new[] { cleaned, CreateSearchFriendlyTitle(cleaned) }
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var candidate in candidates) yield return candidate;
        foreach (var candidate in candidates)
        {
            var withoutSeason = SeasonSuffixRegex().Replace(candidate, string.Empty).Trim(' ', '-', '–', '—');
            if (!string.IsNullOrWhiteSpace(withoutSeason)
                && !candidates.Contains(withoutSeason, StringComparer.OrdinalIgnoreCase))
            {
                yield return withoutSeason;
            }
        }
    }

    private static string CreateSearchFriendlyTitle(string title)
    {
        var decomposed = title.Normalize(NormalizationForm.FormD);
        var withoutDiacritics = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                withoutDiacritics.Append(character);
            }
        }

        var spacedCompanyName = KabushikigaishaRegex().Replace(withoutDiacritics.ToString(), "Kabushiki Gaisha");
        return WhitespaceRegex().Replace(SearchPunctuationRegex().Replace(spacedCompanyName, " "), " ").Trim();
    }

    private static bool TryReadText(JsonElement parent, string propertyName, out string? value)
    {
        value = null;
        if (!parent.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string NormalizeSynopsis(string value)
    {
        var withoutTags = HtmlTagRegex().Replace(value, " ");
        return WhitespaceRegex().Replace(WebUtility.HtmlDecode(withoutTags), " ").Trim();
    }

    private static bool IsUsableCover(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        try
        {
            return new FileInfo(path).Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static HttpClient CreateSharedClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AniT/0.1");
        return client;
    }

    private sealed record AniListMetadata(string? EnglishTitle, string? CoverUrl, string? Synopsis, double? CriticScore);

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\bKabushikigaisha\b", RegexOptions.IgnoreCase)]
    private static partial Regex KabushikigaishaRegex();

    [GeneratedRegex(@"[-–—_:]+")]
    private static partial Regex SearchPunctuationRegex();

    [GeneratedRegex(@"(?:\s*[-–—:]?\s*)(?:\d+(?:st|nd|rd|th)\s+season|season\s*\d+|temporada\s*\d+|\d+[aª]?\s+temporada)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonSuffixRegex();
}

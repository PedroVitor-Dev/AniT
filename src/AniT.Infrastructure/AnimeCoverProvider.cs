using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AniT.Infrastructure;

public sealed partial class AnimeCoverProvider
{
    private const string AniListEndpoint = "https://graphql.anilist.co";
    private const string CoverQuery = """
        query ($search: String) {
          Media(search: $search, type: ANIME) {
            coverImage {
              extraLarge
              large
            }
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

    public async Task<string?> EnsureCoverAsync(
        Guid animeId,
        string title,
        string? savedCoverPath,
        CancellationToken cancellationToken = default)
    {
        if (IsUsableCover(savedCoverPath)) return savedCoverPath;

        Directory.CreateDirectory(coversDirectory);
        var cachedPath = Path.Combine(coversDirectory, $"{animeId:N}.jpg");
        if (IsUsableCover(cachedPath)) return cachedPath;

        var animeLock = animeLocks.GetOrAdd(animeId, _ => new SemaphoreSlim(1, 1));
        await animeLock.WaitAsync(cancellationToken);
        try
        {
            if (IsUsableCover(cachedPath)) return cachedPath;

            var coverUrl = await FindCoverUrlAsync(title, cancellationToken);
            if (coverUrl is null) return null;

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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            animeLock.Release();
        }
    }

    private async Task<string?> FindCoverUrlAsync(string title, CancellationToken cancellationToken)
    {
        foreach (var candidate in GetSearchCandidates(title))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, AniListEndpoint)
            {
                Content = JsonContent.Create(new
                {
                    query = CoverQuery,
                    variables = new { search = candidate }
                })
            };

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("data", out var data)
                || !data.TryGetProperty("Media", out var media)
                || media.ValueKind == JsonValueKind.Null
                || !media.TryGetProperty("coverImage", out var cover))
            {
                continue;
            }

            if (TryReadUrl(cover, "extraLarge", out var extraLarge)) return extraLarge;
            if (TryReadUrl(cover, "large", out var large)) return large;
        }

        return null;
    }

    private static IEnumerable<string> GetSearchCandidates(string title)
    {
        var cleaned = WhitespaceRegex().Replace(title.Trim(), " ");
        if (!string.IsNullOrWhiteSpace(cleaned)) yield return cleaned;

        var withoutSeason = SeasonSuffixRegex().Replace(cleaned, string.Empty).Trim(' ', '-', '–', '—');
        if (!string.IsNullOrWhiteSpace(withoutSeason) && !string.Equals(cleaned, withoutSeason, StringComparison.OrdinalIgnoreCase))
        {
            yield return withoutSeason;
        }
    }

    private static bool TryReadUrl(JsonElement cover, string propertyName, out string? url)
    {
        url = null;
        if (!cover.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String) return false;
        url = property.GetString();
        return Uri.TryCreate(url, UriKind.Absolute, out _);
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

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"(?:\s*[-–—:]?\s*)(?:\d+(?:st|nd|rd|th)\s+season|season\s*\d+|temporada\s*\d+|\d+[aª]?\s+temporada)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonSuffixRegex();
}

using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AniT.Infrastructure;

public sealed record AnimeMetadataResult(string? EnglishTitle, string? CoverPath, string? Synopsis, double? CriticScore, string? Genres, string? BannerPath = null);

public sealed partial class AnimeCoverProvider
{
    private const string AniListEndpoint = "https://graphql.anilist.co";
    private const string TranslationEndpoint = "https://api.mymemory.translated.net/get";
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
            bannerImage
            description(asHtml: false)
            averageScore
            genres
          }
        }
        """;

    private static readonly HttpClient SharedClient = CreateSharedClient();
    private readonly HttpClient httpClient;
    private readonly string coversDirectory;
    private readonly ProfileBannerProvider customImageProvider;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> animeLocks = new();

    public AnimeCoverProvider(string coversDirectory, HttpClient? httpClient = null)
    {
        this.coversDirectory = coversDirectory;
        this.httpClient = httpClient ?? SharedClient;
        customImageProvider = new ProfileBannerProvider(coversDirectory, this.httpClient);
    }

    public async Task<AnimeMetadataResult> EnsureMetadataAsync(
        Guid animeId,
        string japaneseTitle,
        string? englishTitle,
        string? savedCoverPath,
        CancellationToken cancellationToken = default,
        string? savedSynopsis = null,
        double? savedCriticScore = null,
        bool forceRefresh = false,
        string? savedGenres = null)
    {
        var existingCoverPath = IsUsableCover(savedCoverPath) ? savedCoverPath : null;
        Directory.CreateDirectory(coversDirectory);
        var cachedPath = Path.Combine(coversDirectory, $"{animeId:N}.jpg");
        var cachedBannerPath = Path.Combine(coversDirectory, $"{animeId:N}-banner.jpg");
        var missingBannerMarker = Path.Combine(coversDirectory, $"{animeId:N}-banner.unavailable");
        existingCoverPath ??= IsUsableCover(cachedPath) ? cachedPath : null;
        var existingBannerPath = IsUsableCover(cachedBannerPath) ? cachedBannerPath : null;
        var bannerResolved = existingBannerPath is not null || File.Exists(missingBannerMarker);
        if (!forceRefresh
            && !string.IsNullOrWhiteSpace(englishTitle)
            && existingCoverPath is not null
            && IsLikelyPortugueseSynopsis(savedSynopsis)
            && savedCriticScore is not null
            && !string.IsNullOrWhiteSpace(savedGenres)
            && bannerResolved)
        {
            return new AnimeMetadataResult(englishTitle, existingCoverPath, savedSynopsis, savedCriticScore, savedGenres, existingBannerPath);
        }

        var animeLock = animeLocks.GetOrAdd(animeId, _ => new SemaphoreSlim(1, 1));
        await animeLock.WaitAsync(cancellationToken);
        try
        {
            existingCoverPath = IsUsableCover(savedCoverPath) ? savedCoverPath : IsUsableCover(cachedPath) ? cachedPath : null;
            existingBannerPath = IsUsableCover(cachedBannerPath) ? cachedBannerPath : null;
            bannerResolved = existingBannerPath is not null || File.Exists(missingBannerMarker);
            if (!forceRefresh
                && !string.IsNullOrWhiteSpace(englishTitle)
                && existingCoverPath is not null
                && IsLikelyPortugueseSynopsis(savedSynopsis)
                && savedCriticScore is not null
                && !string.IsNullOrWhiteSpace(savedGenres)
                && bannerResolved)
            {
                return new AnimeMetadataResult(englishTitle, existingCoverPath, savedSynopsis, savedCriticScore, savedGenres, existingBannerPath);
            }

            var needsRemoteMetadata = forceRefresh
                || string.IsNullOrWhiteSpace(englishTitle)
                || existingCoverPath is null
                || string.IsNullOrWhiteSpace(savedSynopsis)
                || savedCriticScore is null
                || string.IsNullOrWhiteSpace(savedGenres)
                || !bannerResolved;

            Task<string?>? savedSynopsisTranslation = null;
            if (!forceRefresh
                && !string.IsNullOrWhiteSpace(savedSynopsis)
                && !IsLikelyPortugueseSynopsis(savedSynopsis))
            {
                // Start translating immediately. When a banner also needs to be resolved,
                // this request runs while AniList metadata is being fetched instead of after it.
                savedSynopsisTranslation = EnsurePortugueseSynopsisAsync(savedSynopsis, cancellationToken);
            }

            AniListMetadata? metadata = null;
            if (needsRemoteMetadata)
            {
                // The folder title is the most precise identity we have. In particular, a stale
                // English title without "Season 2" can otherwise make AniList return season one.
                metadata = await FindMetadataAsync(japaneseTitle, cancellationToken);
                if (metadata is null && !string.IsNullOrWhiteSpace(englishTitle))
                {
                    metadata = await FindMetadataAsync(englishTitle, cancellationToken);
                }
            }

            var resolvedEnglishTitle = metadata?.EnglishTitle ?? englishTitle;
            var resolvedCoverUrl = metadata?.CoverUrl;
            var synopsisCandidate = !forceRefresh && !string.IsNullOrWhiteSpace(savedSynopsis)
                ? savedSynopsis
                : metadata?.Synopsis ?? savedSynopsis;
            var resolvedSynopsis = await (savedSynopsisTranslation
                ?? EnsurePortugueseSynopsisAsync(synopsisCandidate, cancellationToken))
                ?? (IsLikelyPortugueseSynopsis(savedSynopsis) ? savedSynopsis : synopsisCandidate);
            var resolvedCriticScore = savedCriticScore ?? metadata?.CriticScore;
            var resolvedGenres = string.IsNullOrWhiteSpace(savedGenres) ? metadata?.Genres : savedGenres;

            var resolvedBannerPath = existingBannerPath;
            if (forceRefresh || !bannerResolved)
            {
                if (metadata is not null && Uri.TryCreate(metadata.BannerUrl, UriKind.Absolute, out _))
                {
                    resolvedBannerPath = await DownloadCoverAsync(metadata.BannerUrl!, cachedBannerPath, cancellationToken) ?? existingBannerPath;
                }

                resolvedBannerPath ??= await TryCustomImageSourceAsync(
                    japaneseTitle,
                    useForBanner: true,
                    cachedBannerPath,
                    cancellationToken);
                if (resolvedBannerPath is not null)
                {
                    if (File.Exists(missingBannerMarker)) File.Delete(missingBannerMarker);
                }
                else await File.WriteAllTextAsync(missingBannerMarker, string.Empty, cancellationToken);
            }

            var resolvedCoverPath = existingCoverPath;
            if (resolvedCoverUrl is not null && (forceRefresh || resolvedCoverPath is null))
            {
                // WPF keeps image files open while they are visible. A forced refresh must not
                // overwrite that locked file; download a new version and switch the DB reference.
                var downloadPath = forceRefresh
                    ? Path.Combine(coversDirectory, $"{animeId:N}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.jpg")
                    : cachedPath;
                resolvedCoverPath = await DownloadCoverAsync(resolvedCoverUrl, downloadPath, cancellationToken) ?? existingCoverPath;
            }

            if (forceRefresh || resolvedCoverPath is null)
            {
                var customCoverPath = forceRefresh
                    ? Path.Combine(coversDirectory, $"{animeId:N}-custom-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.jpg")
                    : cachedPath;
                resolvedCoverPath ??= await TryCustomImageSourceAsync(
                    japaneseTitle,
                    useForBanner: false,
                    customCoverPath,
                    cancellationToken);
            }

            return new AnimeMetadataResult(resolvedEnglishTitle, resolvedCoverPath, resolvedSynopsis, resolvedCriticScore, resolvedGenres, resolvedBannerPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException)
        {
            return new AnimeMetadataResult(englishTitle, existingCoverPath, savedSynopsis, savedCriticScore, savedGenres, existingBannerPath);
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

    public string? GetCachedBannerPath(Guid animeId)
    {
        var path = Path.Combine(coversDirectory, $"{animeId:N}-banner.jpg");
        return IsUsableCover(path) ? path : null;
    }

    public bool ShouldRefreshBanner(Guid animeId)
    {
        var unavailableMarker = Path.Combine(coversDirectory, $"{animeId:N}-banner.unavailable");
        return GetCachedBannerPath(animeId) is null && !File.Exists(unavailableMarker);
    }

    private async Task<string?> TryCustomImageSourceAsync(
        string title,
        bool useForBanner,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var sources = AniTSystemSettingsStore.Load().ImageSources
            .Where(source => source.IsEnabled)
            .Where(source => useForBanner ? source.UseForProfileBanners : source.UseForAnimeCovers)
            .ToArray();
        foreach (var source in sources)
        {
            try
            {
                var candidates = await customImageProvider.SearchCustomSourceAsync(source, title, cancellationToken);
                foreach (var candidate in candidates.Take(3))
                {
                    var downloaded = await DownloadCoverAsync(candidate.DownloadUrl, destinationPath, cancellationToken);
                    if (downloaded is not null) return downloaded;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException)
            {
                // Custom sources are optional fallbacks. A broken source must not stop metadata loading.
            }
        }

        return null;
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

            string? bannerUrl = null;
            TryReadText(media, "bannerImage", out bannerUrl);

            string? synopsis = null;
            if (TryReadText(media, "description", out var description)) synopsis = NormalizeSynopsis(description!);

            double? criticScore = null;
            if (media.TryGetProperty("averageScore", out var score) && score.ValueKind == JsonValueKind.Number)
            {
                criticScore = score.GetDouble();
            }

            string? genres = null;
            if (media.TryGetProperty("genres", out var genreValues) && genreValues.ValueKind == JsonValueKind.Array)
            {
                genres = string.Join('|', genreValues.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(english)
                || Uri.TryCreate(coverUrl, UriKind.Absolute, out _)
                || !string.IsNullOrWhiteSpace(synopsis)
                || criticScore is not null
                || !string.IsNullOrWhiteSpace(genres))
            {
                return new AniListMetadata(english, coverUrl, bannerUrl, synopsis, criticScore, genres);
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

    private async Task<string?> EnsurePortugueseSynopsisAsync(string? synopsis, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(synopsis) || IsLikelyPortugueseSynopsis(synopsis)) return synopsis;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            var translationTasks = SplitForTranslation(synopsis)
                .Select(chunk => TranslateChunkAsync(chunk, timeout.Token))
                .ToArray();
            var translatedChunks = await Task.WhenAll(translationTasks);
            if (translatedChunks.Any(string.IsNullOrWhiteSpace)) return null;

            var result = string.Join(' ', translatedChunks!).Trim();
            return IsLikelyPortugueseSynopsis(result) ? result : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException)
        {
            return null;
        }
    }

    private async Task<string?> TranslateChunkAsync(string chunk, CancellationToken cancellationToken)
    {
        var requestUri = $"{TranslationEndpoint}?q={Uri.EscapeDataString(chunk)}&langpair=en%7Cpt-BR";
        using var response = await httpClient.GetAsync(requestUri, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("responseData", out var responseData)
            || !TryReadText(responseData, "translatedText", out var translated)
            || string.IsNullOrWhiteSpace(translated))
        {
            return null;
        }

        var normalized = NormalizeSynopsis(translated);
        return normalized.Contains("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase) ? null : normalized;
    }

    private static IEnumerable<string> SplitForTranslation(string value)
    {
        const int maximumChunkLength = 420;
        var remaining = value.Trim();
        while (remaining.Length > maximumChunkLength)
        {
            var splitAt = remaining.LastIndexOfAny(['.', '!', '?', ';'], maximumChunkLength - 1, maximumChunkLength);
            if (splitAt < maximumChunkLength / 2)
            {
                splitAt = remaining.LastIndexOf(' ', maximumChunkLength - 1, maximumChunkLength);
            }
            if (splitAt < 1) splitAt = maximumChunkLength;
            else splitAt++;

            yield return remaining[..splitAt].Trim();
            remaining = remaining[splitAt..].TrimStart();
        }

        if (remaining.Length > 0) yield return remaining;
    }

    public static bool IsLikelyPortugueseSynopsis(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var portugueseMatches = PortugueseWordsRegex().Matches(value).Count;
        var englishMatches = EnglishWordsRegex().Matches(value).Count;
        return portugueseMatches > 0 && portugueseMatches >= englishMatches;
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

    private sealed record AniListMetadata(string? EnglishTitle, string? CoverUrl, string? BannerUrl, string? Synopsis, double? CriticScore, string? Genres);

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"\b(?:um|uma|que|não|para|com|dos|das|nos|nas|seu|sua|onde|quando|esta|este|são|será|entre|sobre|história|episódio|temporada|escola|mundo|vivem|começa)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PortugueseWordsRegex();

    [GeneratedRegex(@"\b(?:the|this|that|with|from|where|their|will|school|world|season|episode|story|live|begins|various|become|into|after|before)\b", RegexOptions.IgnoreCase)]
    private static partial Regex EnglishWordsRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\bKabushikigaisha\b", RegexOptions.IgnoreCase)]
    private static partial Regex KabushikigaishaRegex();

    [GeneratedRegex(@"[-–—_:]+")]
    private static partial Regex SearchPunctuationRegex();

    [GeneratedRegex(@"(?:\s*[-–—:]?\s*)(?:\d+(?:st|nd|rd|th)\s+season|season\s*\d+|temporada\s*\d+|\d+[aª]?\s+temporada)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonSuffixRegex();
}

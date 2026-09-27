using System.Net.Http.Json;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AniT.Infrastructure;

public enum ProfileBannerSource
{
    All,
    Wallhaven,
    AniList,
    MyAnimeList,
    Kitsu,
    Danbooru,
    Safebooru,
    Gelbooru
}

public sealed record ProfileBannerCandidate(
    string Id,
    string Title,
    string Source,
    string PreviewUrl,
    string DownloadUrl,
    string SourcePageUrl,
    int Width,
    int Height)
{
    public string ResolutionLabel => Width > 0 && Height > 0 ? $"{Width} × {Height}" : "Arte em alta qualidade";
}

public sealed record ProfileBannerSearchResult(
    IReadOnlyList<ProfileBannerCandidate> Items,
    bool NetworkUnavailable,
    string? Message = null);

public sealed class ProfileBannerProvider
{
    private const string WallhavenEndpoint = "https://wallhaven.cc/api/v1/search";
    private const string AniListEndpoint = "https://graphql.anilist.co";
    private const string JikanEndpoint = "https://api.jikan.moe/v4/anime";
    private const string KitsuEndpoint = "https://kitsu.io/api/edge/anime";
    private const string DanbooruEndpoint = "https://danbooru.donmai.us/posts.json";
    private const string SafebooruEndpoint = "https://safebooru.org/index.php";
    private const string GelbooruEndpoint = "https://gelbooru.com/index.php";
    private const long MaximumDownloadBytes = 30 * 1024 * 1024;
    private const string AniListQuery = """
        query ($search: String) {
          Page(page: 1, perPage: 10) {
            media(search: $search, type: ANIME, sort: POPULARITY_DESC) {
              id
              siteUrl
              title { english romaji native }
              bannerImage
            }
          }
        }
        """;

    private static readonly HttpClient SharedClient = CreateSharedClient();
    private static readonly Regex MetaTagRegex = new("<meta\\s+[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ImageTagRegex = new("<img\\s+[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ContentAttributeRegex = new("(?:content|src)\\s*=\\s*[\\\"'](?<url>[^\\\"']+)[\\\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly string cacheDirectory;
    private readonly HttpClient httpClient;

    public ProfileBannerProvider(string cacheDirectory, HttpClient? httpClient = null)
    {
        this.cacheDirectory = cacheDirectory;
        this.httpClient = httpClient ?? SharedClient;
    }

    public async Task<ProfileBannerSearchResult> SearchAsync(
        string? query,
        ProfileBannerSource source = ProfileBannerSource.All,
        CancellationToken cancellationToken = default)
        => await SearchAsync(query, source, null, null, cancellationToken);

    public async Task<ProfileBannerSearchResult> SearchAsync(
        string? query,
        ProfileBannerSource source,
        IReadOnlyCollection<CustomImageSourceSettings>? customSources,
        Guid? selectedCustomSourceId,
        CancellationToken cancellationToken = default)
    {
        var imageSettings = AniTSystemSettingsStore.Load();
        var normalizedQuery = string.IsNullOrWhiteSpace(query) ? "anime" : query.Trim();
        var items = new List<ProfileBannerCandidate>();
        var attemptedProviders = 0;
        var successfulProviders = 0;
        var builtInSearches = new List<Task<ProviderSearchResult>>();
        if (selectedCustomSourceId is null)
        {
            AddBuiltInSearch(ProfileBannerSource.Wallhaven, SearchWallhavenAsync);
            AddBuiltInSearch(ProfileBannerSource.AniList, SearchAniListAsync);
            AddBuiltInSearch(ProfileBannerSource.MyAnimeList, SearchMyAnimeListAsync);
            AddBuiltInSearch(ProfileBannerSource.Kitsu, SearchKitsuAsync);
            AddBuiltInSearch(ProfileBannerSource.Danbooru, SearchDanbooruAsync);
            AddBuiltInSearch(ProfileBannerSource.Safebooru, SearchSafebooruAsync);
            AddBuiltInSearch(ProfileBannerSource.Gelbooru, SearchGelbooruAsync);
        }

        void AddBuiltInSearch(
            ProfileBannerSource providerSource,
            Func<string, CancellationToken, Task<IReadOnlyList<ProfileBannerCandidate>>> search)
        {
            if (source is not ProfileBannerSource.All && source != providerSource) return;
            builtInSearches.Add(TrySearchProviderAsync(search, normalizedQuery, cancellationToken));
        }

        if (builtInSearches.Count > 0)
        {
            var providerResults = await Task.WhenAll(builtInSearches);
            attemptedProviders += providerResults.Length;
            successfulProviders += providerResults.Count(result => result.Success);
            AddInterleaved(items, providerResults.Select(result => result.Items));
        }

        var enabledCustomSources = (customSources ?? [])
            .Where(item => item.IsEnabled && item.UseForProfileBanners)
            .Where(item => !imageSettings.OnlySfwArtwork || item.IsSfw)
            .Where(item => selectedCustomSourceId is null || item.Id == selectedCustomSourceId)
            .ToArray();
        foreach (var customSource in enabledCustomSources)
        {
            attemptedProviders++;
            try
            {
                items.AddRange(await SearchCustomSourceAsync(customSource, normalizedQuery, cancellationToken));
                successfulProviders++;
            }
            catch (Exception exception) when (IsRecoverableNetworkFailure(exception, cancellationToken)) { }
        }

        var uniqueItems = items
            .Where(item => Uri.TryCreate(item.DownloadUrl, UriKind.Absolute, out var uri)
                           && uri.Scheme == Uri.UriSchemeHttps
                           && AniTNetworkSecurity.IsPotentiallyPublicUri(uri))
            .GroupBy(item => item.DownloadUrl, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => SourceRank(item, imageSettings.ArtworkSourcePriority))
            .ThenByDescending(item => item.Width * (long)item.Height)
            .Select(item => LimitCandidateQuality(item, imageSettings.ImageMaxDimension))
            .Take(60)
            .ToList();

        if (successfulProviders == 0 && attemptedProviders > 0)
        {
            return new ProfileBannerSearchResult(
                uniqueItems,
                true,
                "Não foi possível alcançar as galerias agora. Sua capa salva continua disponível offline.");
        }

        return new ProfileBannerSearchResult(
            uniqueItems,
            false,
            uniqueItems.Count == 0 ? "Nenhuma arte em formato de banner foi encontrada para essa busca." : null);
    }

    private static int SourceRank(ProfileBannerCandidate candidate, ArtworkSourcePriority priority)
    {
        var isAniList = candidate.Source.StartsWith("AniList", StringComparison.OrdinalIgnoreCase);
        var isOfficial = isAniList || candidate.Source.StartsWith("MyAnimeList", StringComparison.OrdinalIgnoreCase) || candidate.Source.StartsWith("Kitsu", StringComparison.OrdinalIgnoreCase);
        var isCustom = candidate.Source.Contains("personalizada", StringComparison.OrdinalIgnoreCase);
        return priority switch
        {
            ArtworkSourcePriority.CustomFirst => isCustom ? 0 : isOfficial ? 1 : 2,
            ArtworkSourcePriority.OfficialFirst => isOfficial && !isAniList ? 0 : isAniList ? 1 : isCustom ? 3 : 2,
            _ => isAniList ? 0 : isOfficial ? 1 : isCustom ? 3 : 2
        };
    }

    private static ProfileBannerCandidate LimitCandidateQuality(ProfileBannerCandidate candidate, int maxDimension)
    {
        if (maxDimension <= 0 || Math.Max(candidate.Width, candidate.Height) <= maxDimension || candidate.PreviewUrl == candidate.DownloadUrl) return candidate;
        return candidate with { DownloadUrl = candidate.PreviewUrl };
    }

    public async Task<IReadOnlyList<ProfileBannerCandidate>> SearchCustomSourceAsync(
        CustomImageSourceSettings source,
        string query,
        CancellationToken cancellationToken)
    {
        if (!AniTSystemSettingsStore.TryValidateSource(source, out _)) return [];
        var encodedQuery = Uri.EscapeDataString(query);
        var searchUrl = source.SearchUrlTemplate.Contains("{query}", StringComparison.OrdinalIgnoreCase)
            ? source.SearchUrlTemplate.Replace("{query}", encodedQuery, StringComparison.OrdinalIgnoreCase)
            : $"{source.SearchUrlTemplate}{(source.SearchUrlTemplate.Contains('?') ? '&' : '?')}q={encodedQuery}";

        using var response = await httpClient.GetAsync(searchUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
        {
            return [CreateCustomCandidate(source, searchUrl, searchUrl, 0)];
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var baseUri = new Uri(searchUrl);
        var urls = new List<string>();
        foreach (Match tag in MetaTagRegex.Matches(html))
        {
            if (!tag.Value.Contains("og:image", StringComparison.OrdinalIgnoreCase)
                && !tag.Value.Contains("twitter:image", StringComparison.OrdinalIgnoreCase)) continue;
            AddImageUrl(urls, tag.Value, baseUri);
        }

        foreach (Match tag in ImageTagRegex.Matches(html)) AddImageUrl(urls, tag.Value, baseUri);

        return urls
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Select((url, index) => CreateCustomCandidate(source, url, searchUrl, index))
            .ToArray();
    }

    private static void AddImageUrl(List<string> urls, string htmlTag, Uri baseUri)
    {
        var match = ContentAttributeRegex.Match(htmlTag);
        if (!match.Success) return;
        var value = WebUtility.HtmlDecode(match.Groups["url"].Value.Trim());
        if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;
        if (!Uri.TryCreate(baseUri, value, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !AniTNetworkSecurity.IsPotentiallyPublicUri(uri)) return;
        urls.Add(uri.AbsoluteUri);
    }

    private static ProfileBannerCandidate CreateCustomCandidate(
        CustomImageSourceSettings source,
        string imageUrl,
        string sourcePageUrl,
        int index)
    {
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(imageUrl))).ToLowerInvariant()[..16];
        return new ProfileBannerCandidate(
            $"custom-{source.Id:N}-{id}",
            index == 0 ? source.Name : $"{source.Name} · arte {index + 1}",
            $"{source.Name} · fonte personalizada",
            imageUrl,
            imageUrl,
            sourcePageUrl,
            0,
            0);
    }

    public async Task<string> CacheSelectedAsync(ProfileBannerCandidate candidate, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(cacheDirectory);
        var extension = GetSafeExtension(candidate.DownloadUrl);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(candidate.DownloadUrl))).ToLowerInvariant();
        var cachedPath = Path.Combine(cacheDirectory, $"banner-{hash}{extension}");
        if (IsUsableFile(cachedPath)) return cachedPath;

        using var response = await httpClient.GetAsync(candidate.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
            throw new InvalidDataException("A galeria não retornou uma imagem válida.");
        if (response.Content.Headers.ContentLength is > MaximumDownloadBytes)
            throw new InvalidDataException("A imagem escolhida ultrapassa o limite de 30 MB.");

        var temporaryPath = cachedPath + ".download";
        try
        {
            long totalBytes = 0;
            {
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    totalBytes += bytesRead;
                    if (totalBytes > MaximumDownloadBytes)
                        throw new InvalidDataException("A imagem escolhida ultrapassa o limite de 30 MB.");
                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                }

                await destination.FlushAsync(cancellationToken);
            }

            if (totalBytes == 0) throw new InvalidDataException("A imagem escolhida está vazia.");
            File.Move(temporaryPath, cachedPath, true);
            return cachedPath;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private async Task<IReadOnlyList<ProfileBannerCandidate>> SearchWallhavenAsync(string query, CancellationToken cancellationToken)
    {
        var uri = $"{WallhavenEndpoint}?q={Uri.EscapeDataString(query)}&categories=010&purity=100&sorting=toplist&topRange=1y&atleast=2560x1080&ratios=21x9&page=1";
        using var response = await httpClient.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return [];

        var results = new List<ProfileBannerCandidate>();
        foreach (var item in data.EnumerateArray())
        {
            var id = ReadString(item, "id");
            var downloadUrl = ReadString(item, "path");
            var sourcePageUrl = ReadString(item, "url") ?? (id is null ? null : $"https://wallhaven.cc/w/{id}");
            var width = ReadInt(item, "dimension_x");
            var height = ReadInt(item, "dimension_y");
            string? previewUrl = null;
            if (item.TryGetProperty("thumbs", out var thumbs)) previewUrl = ReadString(thumbs, "large") ?? ReadString(thumbs, "original");

            if (string.IsNullOrWhiteSpace(id)
                || string.IsNullOrWhiteSpace(downloadUrl)
                || string.IsNullOrWhiteSpace(previewUrl)
                || string.IsNullOrWhiteSpace(sourcePageUrl)
                || width < 2560
                || height < 1080
                || width / (double)height is < 2.0 or > 2.65)
            {
                continue;
            }

            results.Add(new ProfileBannerCandidate(
                $"wallhaven-{id}",
                $"Fan art {id.ToUpperInvariant()}",
                "Wallhaven · SFW",
                previewUrl,
                downloadUrl,
                sourcePageUrl,
                width,
                height));
            if (results.Count == 12) break;
        }

        return results;
    }

    private async Task<IReadOnlyList<ProfileBannerCandidate>> SearchAniListAsync(string query, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, AniListEndpoint)
        {
            Content = JsonContent.Create(new { query = AniListQuery, variables = new { search = query } })
        };
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("Page", out var page)
            || !page.TryGetProperty("media", out var media)
            || media.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<ProfileBannerCandidate>();
        foreach (var item in media.EnumerateArray())
        {
            var id = ReadInt(item, "id");
            var bannerUrl = ReadString(item, "bannerImage");
            if (id <= 0 || string.IsNullOrWhiteSpace(bannerUrl)) continue;
            var sourcePageUrl = ReadString(item, "siteUrl") ?? $"https://anilist.co/anime/{id}";
            var title = item.TryGetProperty("title", out var titles)
                ? ReadString(titles, "english") ?? ReadString(titles, "romaji") ?? ReadString(titles, "native")
                : null;

            results.Add(new ProfileBannerCandidate(
                $"anilist-{id}",
                title ?? $"Anime #{id}",
                "AniList · arte oficial",
                bannerUrl,
                bannerUrl,
                sourcePageUrl,
                0,
                0));
        }

        return results;
    }

    private async Task<IReadOnlyList<ProfileBannerCandidate>> SearchMyAnimeListAsync(string query, CancellationToken cancellationToken)
    {
        var uri = $"{JikanEndpoint}?q={Uri.EscapeDataString(query)}&limit=8&sfw=true&order_by=score&sort=desc";
        using var response = await httpClient.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return [];

        var results = new List<ProfileBannerCandidate>();
        foreach (var item in data.EnumerateArray())
        {
            var id = ReadInt(item, "mal_id");
            var pageUrl = ReadString(item, "url") ?? (id > 0 ? $"https://myanimelist.net/anime/{id}" : null);
            var title = ReadString(item, "title_english") ?? ReadString(item, "title") ?? ReadString(item, "title_japanese");
            string? previewUrl = null;
            string? downloadUrl = null;
            if (item.TryGetProperty("images", out var images) && images.TryGetProperty("jpg", out var jpg))
            {
                previewUrl = ReadString(jpg, "image_url") ?? ReadString(jpg, "small_image_url");
                downloadUrl = ReadString(jpg, "large_image_url") ?? previewUrl;
            }

            if (id <= 0 || string.IsNullOrWhiteSpace(downloadUrl) || string.IsNullOrWhiteSpace(pageUrl)) continue;
            results.Add(new ProfileBannerCandidate(
                $"mal-{id}",
                title ?? $"Anime #{id}",
                "MyAnimeList · Jikan",
                previewUrl ?? downloadUrl,
                downloadUrl,
                pageUrl,
                0,
                0));
        }

        return results;
    }

    private async Task<IReadOnlyList<ProfileBannerCandidate>> SearchKitsuAsync(string query, CancellationToken cancellationToken)
    {
        var uri = $"{KitsuEndpoint}?filter%5Btext%5D={Uri.EscapeDataString(query)}&page%5Blimit%5D=8";
        using var response = await httpClient.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return [];

        var results = new List<ProfileBannerCandidate>();
        foreach (var item in data.EnumerateArray())
        {
            var id = ReadString(item, "id");
            if (!item.TryGetProperty("attributes", out var attributes)) continue;
            var title = ReadString(attributes, "canonicalTitle");
            var slug = ReadString(attributes, "slug");
            var image = attributes.TryGetProperty("coverImage", out var coverImage) && coverImage.ValueKind == JsonValueKind.Object
                ? coverImage
                : attributes.TryGetProperty("posterImage", out var posterImage) && posterImage.ValueKind == JsonValueKind.Object
                    ? posterImage
                    : default;
            if (image.ValueKind != JsonValueKind.Object) continue;
            var downloadUrl = ReadString(image, "original") ?? ReadString(image, "large") ?? ReadString(image, "medium");
            var previewUrl = ReadString(image, "small") ?? ReadString(image, "medium") ?? downloadUrl;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(downloadUrl)) continue;

            results.Add(new ProfileBannerCandidate(
                $"kitsu-{id}",
                title ?? $"Anime #{id}",
                "Kitsu · arte oficial",
                previewUrl ?? downloadUrl,
                downloadUrl,
                string.IsNullOrWhiteSpace(slug) ? $"https://kitsu.app/anime/{id}" : $"https://kitsu.app/anime/{slug}",
                0,
                0));
        }

        return results;
    }

    private async Task<IReadOnlyList<ProfileBannerCandidate>> SearchDanbooruAsync(string query, CancellationToken cancellationToken)
    {
        var tags = $"{ToBooruTag(query)} rating:general order:score";
        var uri = $"{DanbooruEndpoint}?limit=8&tags={Uri.EscapeDataString(tags)}";
        using var response = await httpClient.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return [];

        var results = new List<ProfileBannerCandidate>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var id = ReadInt(item, "id");
            var downloadUrl = ReadString(item, "file_url") ?? ReadString(item, "large_file_url");
            var previewUrl = ReadString(item, "preview_file_url") ?? ReadString(item, "large_file_url") ?? downloadUrl;
            var width = ReadFlexibleInt(item, "image_width");
            var height = ReadFlexibleInt(item, "image_height");
            if (id <= 0 || string.IsNullOrWhiteSpace(downloadUrl)) continue;
            results.Add(new ProfileBannerCandidate(
                $"danbooru-{id}",
                $"Fan art #{id}",
                "Danbooru · somente geral",
                previewUrl ?? downloadUrl,
                downloadUrl,
                $"https://danbooru.donmai.us/posts/{id}",
                width,
                height));
        }

        return results;
    }

    private Task<IReadOnlyList<ProfileBannerCandidate>> SearchSafebooruAsync(string query, CancellationToken cancellationToken) =>
        SearchLegacyBooruAsync(
            SafebooruEndpoint,
            "safebooru",
            "Safebooru · conteúdo seguro",
            "https://safebooru.org/index.php?page=post&s=view&id=",
            $"{ToBooruTag(query)} rating:safe",
            cancellationToken);

    private Task<IReadOnlyList<ProfileBannerCandidate>> SearchGelbooruAsync(string query, CancellationToken cancellationToken) =>
        SearchLegacyBooruAsync(
            GelbooruEndpoint,
            "gelbooru",
            "Gelbooru · somente geral",
            "https://gelbooru.com/index.php?page=post&s=view&id=",
            $"{ToBooruTag(query)} rating:general",
            cancellationToken);

    private async Task<IReadOnlyList<ProfileBannerCandidate>> SearchLegacyBooruAsync(
        string endpoint,
        string sourceKey,
        string sourceLabel,
        string postUrlPrefix,
        string tags,
        CancellationToken cancellationToken)
    {
        var uri = $"{endpoint}?page=dapi&s=post&q=index&json=1&limit=8&tags={Uri.EscapeDataString(tags)}";
        using var response = await httpClient.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        JsonElement posts;
        if (root.ValueKind == JsonValueKind.Array) posts = root;
        else if (!root.TryGetProperty("post", out posts) || posts.ValueKind != JsonValueKind.Array) return [];

        var results = new List<ProfileBannerCandidate>();
        foreach (var item in posts.EnumerateArray())
        {
            var id = ReadFlexibleInt(item, "id");
            var downloadUrl = NormalizeImageUrl(ReadString(item, "file_url"), endpoint);
            var previewUrl = NormalizeImageUrl(
                ReadString(item, "preview_url") ?? ReadString(item, "sample_url"),
                endpoint) ?? downloadUrl;
            var width = ReadFlexibleInt(item, "width");
            var height = ReadFlexibleInt(item, "height");
            if (id <= 0 || string.IsNullOrWhiteSpace(downloadUrl)) continue;
            results.Add(new ProfileBannerCandidate(
                $"{sourceKey}-{id}",
                $"Fan art #{id}",
                sourceLabel,
                previewUrl ?? downloadUrl,
                downloadUrl,
                postUrlPrefix + id,
                width,
                height));
        }

        return results;
    }

    private static async Task<ProviderSearchResult> TrySearchProviderAsync(
        Func<string, CancellationToken, Task<IReadOnlyList<ProfileBannerCandidate>>> search,
        string query,
        CancellationToken cancellationToken)
    {
        try
        {
            return new ProviderSearchResult(true, await search(query, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new ProviderSearchResult(false, []);
        }
    }

    private static void AddInterleaved(
        List<ProfileBannerCandidate> destination,
        IEnumerable<IReadOnlyList<ProfileBannerCandidate>> providerItems)
    {
        var sources = providerItems.ToArray();
        var largest = sources.Length == 0 ? 0 : sources.Max(items => items.Count);
        for (var index = 0; index < largest; index++)
        {
            foreach (var items in sources)
            {
                if (index < items.Count) destination.Add(items[index]);
            }
        }
    }

    private static string ToBooruTag(string query) =>
        Regex.Replace(query.Trim().ToLowerInvariant(), @"[^\p{L}\p{Nd}]+", "_").Trim('_');

    private static string? NormalizeImageUrl(string? value, string endpoint)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.StartsWith("//", StringComparison.Ordinal)) return "https:" + value;
        return Uri.TryCreate(new Uri(endpoint), value, out var uri) && uri.Scheme is "http" or "https"
            ? uri.AbsoluteUri
            : null;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : 0;

    private static int ReadFlexibleInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number) ? number : 0;
    }

    private static string GetSafeExtension(string url)
    {
        var extension = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? Path.GetExtension(uri.AbsolutePath).ToLowerInvariant() : string.Empty;
        return extension is ".png" or ".jpg" or ".jpeg" ? extension : ".jpg";
    }

    private static bool IsUsableFile(string path)
    {
        try { return File.Exists(path) && new FileInfo(path).Length > 0; }
        catch (IOException) { return false; }
    }

    private static bool IsRecoverableNetworkFailure(Exception exception, CancellationToken callerToken) =>
        exception is HttpRequestException or IOException or JsonException
        || exception is OperationCanceledException && !callerToken.IsCancellationRequested;

    private static HttpClient CreateSharedClient()
    {
        var client = new HttpClient(new AniTOnlineRequestHandler()) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AniT/1.0 (profile-banner-picker)");
        return client;
    }

    private sealed record ProviderSearchResult(bool Success, IReadOnlyList<ProfileBannerCandidate> Items);
}

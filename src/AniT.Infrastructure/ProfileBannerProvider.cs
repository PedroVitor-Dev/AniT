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
    AniList
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
        var normalizedQuery = string.IsNullOrWhiteSpace(query) ? "anime" : query.Trim();
        var items = new List<ProfileBannerCandidate>();
        var attemptedProviders = 0;
        var successfulProviders = 0;

        if (selectedCustomSourceId is null && source is ProfileBannerSource.All or ProfileBannerSource.Wallhaven)
        {
            attemptedProviders++;
            try
            {
                items.AddRange(await SearchWallhavenAsync(normalizedQuery, cancellationToken));
                successfulProviders++;
            }
            catch (Exception exception) when (IsRecoverableNetworkFailure(exception, cancellationToken)) { }
        }

        if (selectedCustomSourceId is null && source is ProfileBannerSource.All or ProfileBannerSource.AniList)
        {
            attemptedProviders++;
            try
            {
                items.AddRange(await SearchAniListAsync(normalizedQuery, cancellationToken));
                successfulProviders++;
            }
            catch (Exception exception) when (IsRecoverableNetworkFailure(exception, cancellationToken)) { }
        }

        var enabledCustomSources = (customSources ?? [])
            .Where(item => item.IsEnabled && item.UseForProfileBanners)
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
            .Where(item => Uri.TryCreate(item.DownloadUrl, UriKind.Absolute, out _))
            .GroupBy(item => item.DownloadUrl, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(20)
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
        if (!Uri.TryCreate(baseUri, value, out var uri) || uri.Scheme is not ("http" or "https")) return;
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

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : 0;

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
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AniT/0.1 (profile-banner-picker)");
        return client;
    }
}

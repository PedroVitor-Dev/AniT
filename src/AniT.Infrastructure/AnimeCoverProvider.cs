using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AniT.Infrastructure;

public sealed record AnimeMetadataResult(
    string? EnglishTitle,
    string? CoverPath,
    string? Synopsis,
    double? CriticScore,
    string? Genres,
    string? BannerPath = null,
    string? OriginalTitle = null,
    IReadOnlyList<string>? Aliases = null,
    int? ReleaseYear = null,
    string? Studio = null);

public sealed partial class AnimeCoverProvider
{
    private const long MaximumArtworkDownloadBytes = 30L * 1024 * 1024;
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
            seasonYear
            studios(isMain: true) {
              nodes {
                name
              }
            }
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
        string? savedGenres = null,
        int? savedReleaseYear = null,
        string? savedStudio = null,
        bool refreshCatalogDetails = false,
        bool refreshArtwork = true)
    {
        var imageSettings = AniTSystemSettingsStore.Load();
        var artworkLocked = imageSettings.LockedArtwork?.ContainsKey(animeId.ToString("D")) == true;
        var existingCoverPath = IsUsableCover(savedCoverPath) ? savedCoverPath : null;
        Directory.CreateDirectory(coversDirectory);
        var cachedPath = Path.Combine(coversDirectory, $"{animeId:N}.jpg");
        var cachedBannerPath = Path.Combine(coversDirectory, $"{animeId:N}-banner.jpg");
        var missingBannerMarker = Path.Combine(coversDirectory, $"{animeId:N}-banner.unavailable");
        existingCoverPath ??= IsUsableCover(cachedPath) ? cachedPath : null;
        var existingBannerPath = IsUsableCover(cachedBannerPath) ? cachedBannerPath : null;
        var bannerResolved = existingBannerPath is not null || File.Exists(missingBannerMarker);
        var artworkRefreshDue = refreshArtwork && !artworkLocked && IsRefreshDue(existingCoverPath ?? existingBannerPath, imageSettings.ArtworkRefreshDays);
        if (!forceRefresh
            && !artworkRefreshDue
            && !refreshCatalogDetails
            && !string.IsNullOrWhiteSpace(englishTitle)
            && existingCoverPath is not null
            && IsLikelyPortugueseSynopsis(savedSynopsis)
            && savedCriticScore is not null
            && !string.IsNullOrWhiteSpace(savedGenres)
            && bannerResolved)
        {
            return new AnimeMetadataResult(englishTitle, existingCoverPath, savedSynopsis, savedCriticScore, savedGenres, existingBannerPath, ReleaseYear: savedReleaseYear, Studio: savedStudio);
        }

        var animeLock = animeLocks.GetOrAdd(animeId, _ => new SemaphoreSlim(1, 1));
        await animeLock.WaitAsync(cancellationToken);
        try
        {
            existingCoverPath = IsUsableCover(savedCoverPath) ? savedCoverPath : IsUsableCover(cachedPath) ? cachedPath : null;
            existingBannerPath = IsUsableCover(cachedBannerPath) ? cachedBannerPath : null;
            bannerResolved = existingBannerPath is not null || File.Exists(missingBannerMarker);
            artworkRefreshDue = !artworkLocked && IsRefreshDue(existingCoverPath ?? existingBannerPath, imageSettings.ArtworkRefreshDays);
            if (!forceRefresh
                && !artworkRefreshDue
                && !refreshCatalogDetails
                && !string.IsNullOrWhiteSpace(englishTitle)
                && existingCoverPath is not null
                && IsLikelyPortugueseSynopsis(savedSynopsis)
                && savedCriticScore is not null
                && !string.IsNullOrWhiteSpace(savedGenres)
                && bannerResolved)
            {
                return new AnimeMetadataResult(englishTitle, existingCoverPath, savedSynopsis, savedCriticScore, savedGenres, existingBannerPath, ReleaseYear: savedReleaseYear, Studio: savedStudio);
            }

            var needsRemoteMetadata = forceRefresh
                || artworkRefreshDue
                || refreshCatalogDetails
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
            var resolvedOriginalTitle = metadata?.NativeTitle ?? metadata?.RomajiTitle;
            var resolvedAliases = new[] { metadata?.RomajiTitle, metadata?.EnglishTitle, metadata?.NativeTitle }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var resolvedCoverUrl = metadata?.CoverUrl;
            var synopsisCandidate = !forceRefresh && !string.IsNullOrWhiteSpace(savedSynopsis)
                ? savedSynopsis
                : metadata?.Synopsis ?? savedSynopsis;
            var resolvedSynopsis = await (savedSynopsisTranslation
                ?? EnsurePortugueseSynopsisAsync(synopsisCandidate, cancellationToken))
                ?? (IsLikelyPortugueseSynopsis(savedSynopsis) ? savedSynopsis : synopsisCandidate);
            var resolvedCriticScore = savedCriticScore ?? metadata?.CriticScore;
            var resolvedGenres = string.IsNullOrWhiteSpace(savedGenres) ? metadata?.Genres : savedGenres;
            var resolvedReleaseYear = savedReleaseYear ?? metadata?.ReleaseYear;
            var resolvedStudio = string.IsNullOrWhiteSpace(savedStudio) ? metadata?.Studio : savedStudio;

            var resolvedBannerPath = existingBannerPath;
            if (refreshArtwork && (forceRefresh || artworkRefreshDue || !bannerResolved))
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
            if (refreshArtwork && imageSettings.ArtworkSourcePriority == ArtworkSourcePriority.CustomFirst && (forceRefresh || artworkRefreshDue || resolvedCoverPath is null))
            {
                var customFirstPath = forceRefresh || artworkRefreshDue
                    ? Path.Combine(coversDirectory, $"{animeId:N}-custom-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.jpg")
                    : cachedPath;
                resolvedCoverPath = await TryCustomImageSourceAsync(japaneseTitle, useForBanner: false, customFirstPath, cancellationToken) ?? resolvedCoverPath;
            }

            if (refreshArtwork && resolvedCoverUrl is not null && imageSettings.ArtworkSourcePriority != ArtworkSourcePriority.CustomFirst && (forceRefresh || artworkRefreshDue || resolvedCoverPath is null))
            {
                // WPF keeps image files open while they are visible. A forced refresh must not
                // overwrite that locked file; download a new version and switch the DB reference.
                var downloadPath = forceRefresh || artworkRefreshDue
                    ? Path.Combine(coversDirectory, $"{animeId:N}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.jpg")
                    : cachedPath;
                resolvedCoverPath = await DownloadCoverAsync(resolvedCoverUrl, downloadPath, cancellationToken) ?? existingCoverPath;
            }

            if (refreshArtwork && (forceRefresh || artworkRefreshDue || resolvedCoverPath is null))
            {
                var customCoverPath = forceRefresh || artworkRefreshDue
                    ? Path.Combine(coversDirectory, $"{animeId:N}-custom-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.jpg")
                    : cachedPath;
                resolvedCoverPath ??= await TryCustomImageSourceAsync(
                    japaneseTitle,
                    useForBanner: false,
                    customCoverPath,
                    cancellationToken);
            }

            return new AnimeMetadataResult(resolvedEnglishTitle, resolvedCoverPath, resolvedSynopsis, resolvedCriticScore, resolvedGenres, resolvedBannerPath, resolvedOriginalTitle, resolvedAliases, resolvedReleaseYear, resolvedStudio);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException)
        {
            return new AnimeMetadataResult(englishTitle, existingCoverPath, savedSynopsis, savedCriticScore, savedGenres, existingBannerPath, ReleaseYear: savedReleaseYear, Studio: savedStudio);
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
        var settings = AniTSystemSettingsStore.Load();
        if (settings.LockedArtwork?.ContainsKey(animeId.ToString("D")) == true) return false;
        var unavailableMarker = Path.Combine(coversDirectory, $"{animeId:N}-banner.unavailable");
        var bannerPath = GetCachedBannerPath(animeId);
        var coverPath = Path.Combine(coversDirectory, $"{animeId:N}.jpg");
        return bannerPath is null && !File.Exists(unavailableMarker)
               || IsRefreshDue(bannerPath, settings.ArtworkRefreshDays)
               || IsRefreshDue(coverPath, settings.ArtworkRefreshDays);
    }

    public async Task<IReadOnlyList<string>> EnsureArtworkGalleryAsync(
        Guid animeId,
        string title,
        string? coverPath,
        string? bannerPath,
        CancellationToken cancellationToken = default)
    {
        const int galleryLimit = 5;
        var settings = AniTSystemSettingsStore.Load();
        string? lockedArtwork = null;
        settings.LockedArtwork?.TryGetValue(animeId.ToString("D"), out lockedArtwork);
        var galleryDirectory = Path.Combine(coversDirectory, "Gallery", animeId.ToString("N"));
        Directory.CreateDirectory(galleryDirectory);

        if (string.IsNullOrWhiteSpace(lockedArtwork) && settings.ArtworkRefreshDays > 0)
        {
            foreach (var stale in Directory.EnumerateFiles(galleryDirectory).Where(path => IsRefreshDue(path, settings.ArtworkRefreshDays)))
            {
                try { File.Delete(stale); } catch (IOException) { }
            }
        }

        var paths = new List<string>(galleryLimit);
        AddUsablePath(paths, lockedArtwork);
        if (settings.PreferLocalArtwork)
        {
            AddUsablePath(paths, bannerPath);
            AddUsablePath(paths, coverPath);
        }
        foreach (var cached in Directory.EnumerateFiles(galleryDirectory)
                     .Where(IsUsableCover)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            AddUsablePath(paths, cached);
            if (paths.Count == galleryLimit) return paths;
        }
        if (!settings.PreferLocalArtwork)
        {
            AddUsablePath(paths, bannerPath);
            AddUsablePath(paths, coverPath);
        }

        var animeLock = animeLocks.GetOrAdd(animeId, _ => new SemaphoreSlim(1, 1));
        await animeLock.WaitAsync(cancellationToken);
        try
        {
            foreach (var cached in Directory.EnumerateFiles(galleryDirectory)
                         .Where(IsUsableCover)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                AddUsablePath(paths, cached);
                if (paths.Count == galleryLimit) return paths;
            }

            var search = await customImageProvider.SearchAsync(
                title,
                ProfileBannerSource.All,
                settings.ImageSources,
                selectedCustomSourceId: null,
                cancellationToken);
            var galleryIndex = Directory.EnumerateFiles(galleryDirectory).Count() + 1;
            foreach (var candidate in search.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (paths.Count == galleryLimit) break;
                try
                {
                    var sharedCachePath = await customImageProvider.CacheSelectedAsync(candidate, cancellationToken);
                    var extension = Path.GetExtension(sharedCachePath);
                    if (string.IsNullOrWhiteSpace(extension)) extension = ".jpg";
                    var galleryPath = Path.Combine(galleryDirectory, $"art-{galleryIndex:00}{extension}");
                    galleryIndex++;
                    if (!File.Exists(galleryPath)) File.Copy(sharedCachePath, galleryPath, overwrite: false);
                    AddUsablePath(paths, galleryPath);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException)
                {
                    // One unavailable candidate must not prevent the remaining cached gallery.
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException)
        {
            // Offline mode keeps the official cover/banner and every previously cached fan art.
        }
        finally
        {
            animeLock.Release();
        }

        return paths.Take(galleryLimit).ToArray();
    }

    private static void AddUsablePath(List<string> paths, string? candidate)
    {
        if (!IsUsableCover(candidate)
            || paths.Contains(candidate!, StringComparer.OrdinalIgnoreCase)) return;
        paths.Add(candidate!);
    }

    private async Task<string?> TryCustomImageSourceAsync(
        string title,
        bool useForBanner,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var settings = AniTSystemSettingsStore.Load();
        var sources = settings.ImageSources
            .Where(source => source.IsEnabled)
            .Where(source => !settings.OnlySfwArtwork || source.IsSfw)
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

    private static bool IsRefreshDue(string? path, int refreshDays)
    {
        if (refreshDays <= 0 || !IsUsableCover(path)) return false;
        try { return File.GetLastWriteTimeUtc(path!) <= DateTime.UtcNow.AddDays(-refreshDays); }
        catch (IOException) { return false; }
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

            string? romaji = null;
            string? english = null;
            string? native = null;
            if (media.TryGetProperty("title", out var titles))
            {
                TryReadText(titles, "romaji", out romaji);
                TryReadText(titles, "english", out english);
                TryReadText(titles, "native", out native);
            }

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

            int? releaseYear = null;
            if (media.TryGetProperty("seasonYear", out var yearValue) && yearValue.ValueKind == JsonValueKind.Number)
            {
                releaseYear = yearValue.GetInt32();
            }

            string? studio = null;
            if (media.TryGetProperty("studios", out var studios)
                && studios.TryGetProperty("nodes", out var studioNodes)
                && studioNodes.ValueKind == JsonValueKind.Array)
            {
                studio = studioNodes.EnumerateArray()
                    .Select(node => TryReadText(node, "name", out var name) ? name : null)
                    .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
            }

            if (!string.IsNullOrWhiteSpace(english)
                || Uri.TryCreate(coverUrl, UriKind.Absolute, out _)
                || !string.IsNullOrWhiteSpace(synopsis)
                || criticScore is not null
                || !string.IsNullOrWhiteSpace(genres))
            {
                return new AniListMetadata(english, romaji, native, coverUrl, bannerUrl, synopsis, criticScore, genres, releaseYear, studio);
            }
        }

        return null;
    }

    private async Task<string?> DownloadCoverAsync(string coverUrl, string cachedPath, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(coverUrl, UriKind.Absolute, out var artworkUri)
            || artworkUri.Scheme != Uri.UriSchemeHttps
            || !AniTNetworkSecurity.IsPotentiallyPublicUri(artworkUri)) return null;
        using var response = await httpClient.GetAsync(coverUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
        {
            return null;
        }
        if (response.Content.Headers.ContentLength is > MaximumArtworkDownloadBytes) return null;

        var temporaryPath = cachedPath + ".download";
        try
        {
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long totalBytes = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    totalBytes += read;
                    if (totalBytes > MaximumArtworkDownloadBytes) return null;
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
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
        var client = new HttpClient(new AniTOnlineRequestHandler()) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AniT/1.0");
        return client;
    }

    private sealed record AniListMetadata(string? EnglishTitle, string? RomajiTitle, string? NativeTitle, string? CoverUrl, string? BannerUrl, string? Synopsis, double? CriticScore, string? Genres, int? ReleaseYear, string? Studio);

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

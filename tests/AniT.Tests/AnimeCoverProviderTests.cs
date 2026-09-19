using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AniT.Infrastructure;

namespace AniT.Tests;

public sealed class AnimeCoverProviderTests
{
    [Fact]
    public async Task EnsureCoverAsync_DownloadsAndCachesAniListCover()
    {
        var calls = 0;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            calls++;
            if (request.Method == HttpMethod.Post)
            {
                return JsonResponse("""
                    {"data":{"Media":{"coverImage":{"extraLarge":"https://images.example/anime.jpg","large":null}}}}
                    """);
            }

            var imageResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xD9])
            };
            imageResponse.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return imageResponse;
        }));
        var directory = Path.Combine(Path.GetTempPath(), "AniT.Tests", Guid.NewGuid().ToString("N"));

        try
        {
            var provider = new AnimeCoverProvider(directory, httpClient);
            var animeId = Guid.NewGuid();

            var firstResult = await provider.EnsureCoverAsync(animeId, "Frieren", null);
            var secondResult = await provider.EnsureCoverAsync(animeId, "Frieren", null);

            Assert.Equal(firstResult, secondResult);
            Assert.True(File.Exists(firstResult));
            Assert.Equal(2, calls);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task EnsureCoverAsync_KeepsExistingLocalCoverWithoutNetworkRequest()
    {
        var calls = 0;
        using var httpClient = new HttpClient(new StubHandler(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }));
        var directory = Path.Combine(Path.GetTempPath(), "AniT.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var existingCover = Path.Combine(directory, "custom.jpg");
        await File.WriteAllBytesAsync(existingCover, [0xFF, 0xD8, 0xFF, 0xD9]);

        try
        {
            var provider = new AnimeCoverProvider(directory, httpClient);
            var result = await provider.EnsureCoverAsync(Guid.NewGuid(), "Frieren", existingCover);

            Assert.Equal(existingCover, result);
            Assert.Equal(0, calls);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task EnsureMetadataAsync_UsesFolderTitleToChooseTheRightSeason()
    {
        var postCount = 0;
        var requestBodies = new List<string>();
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                postCount++;
                requestBodies.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                if (postCount == 1) return new HttpResponseMessage(HttpStatusCode.NotFound);
                return JsonResponse("""{"data":{"Media":{"title":{"english":"Magilumiere Magical Girls Inc. Season 2"},"coverImage":{"extraLarge":"https://images.example/japanese-search.jpg","large":null},"description":"A magical company &amp; its heroines.","averageScore":82}}}""");
            }

            if (request.RequestUri!.Host.Equals("api.mymemory.translated.net", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""{"responseData":{"translatedText":"Uma empresa mágica e suas heroínas."},"responseStatus":200}""");
            }

            Assert.Equal("https://images.example/japanese-search.jpg", request.RequestUri!.AbsoluteUri);
            var imageResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xD9])
            };
            imageResponse.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return imageResponse;
        }));
        var directory = Path.Combine(Path.GetTempPath(), "AniT.Tests", Guid.NewGuid().ToString("N"));

        try
        {
            var provider = new AnimeCoverProvider(directory, httpClient);
            var result = await provider.EnsureMetadataAsync(
                Guid.NewGuid(),
                "Kabushikigaisha Magi-Lumière 2nd Season",
                null,
                null);

            Assert.Equal("Magilumiere Magical Girls Inc. Season 2", result.EnglishTitle);
            Assert.True(File.Exists(result.CoverPath));
            Assert.Equal("Uma empresa mágica e suas heroínas.", result.Synopsis);
            Assert.Equal(82, result.CriticScore);
            Assert.Equal(2, postCount);
            Assert.Contains("Kabushikigaisha Magi-Lumi", requestBodies[0]);
            Assert.Contains("Kabushiki Gaisha Magi Lumiere 2nd Season", requestBodies[1]);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task EnsureMetadataAsync_ForceRefreshCorrectsAStaleSeasonMatch()
    {
        var requestedCover = false;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return JsonResponse("""{"data":{"Media":{"title":{"english":"Anime AzurLane: Slow Ahead! Season 2"},"coverImage":{"extraLarge":"https://images.example/season-2.jpg","large":null},"description":"The second season.","averageScore":67}}}""");
            }


            if (request.RequestUri!.Host.Equals("api.mymemory.translated.net", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""{"responseData":{"translatedText":"A segunda temporada."},"responseStatus":200}""");
            }

            requestedCover = true;
            var imageResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([0xFF, 0xD8, 0x02, 0xFF, 0xD9])
            };
            imageResponse.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return imageResponse;
        }));
        var directory = Path.Combine(Path.GetTempPath(), "AniT.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var animeId = Guid.NewGuid();
        var staleCover = Path.Combine(directory, $"{animeId:N}.jpg");
        await File.WriteAllBytesAsync(staleCover, [0xFF, 0xD8, 0x01, 0xFF, 0xD9]);

        try
        {
            var provider = new AnimeCoverProvider(directory, httpClient);
            var result = await provider.EnsureMetadataAsync(
                animeId,
                "Azur Lane Bisoku Zenshin! Ni!!",
                "Anime AzurLane: Slow Ahead!",
                staleCover,
                savedSynopsis: "Old synopsis",
                savedCriticScore: 60,
                forceRefresh: true);

            Assert.Equal("Anime AzurLane: Slow Ahead! Season 2", result.EnglishTitle);
            Assert.NotEqual(staleCover, result.CoverPath);
            Assert.StartsWith(Path.Combine(directory, $"{animeId:N}-"), result.CoverPath);
            Assert.True(requestedCover);
            Assert.Equal(0x01, (await File.ReadAllBytesAsync(staleCover))[2]);
            Assert.Equal(0x02, (await File.ReadAllBytesAsync(result.CoverPath!))[2]);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task EnsureMetadataAsync_DownloadsAndCachesBannerArtwork()
    {
        var postCount = 0;
        var bannerDownloadCount = 0;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                postCount++;
                return JsonResponse("""{"data":{"Media":{"title":{"english":"Anime Example"},"coverImage":{"extraLarge":null,"large":null},"bannerImage":"https://images.example/banner.jpg","description":"Uma história sobre um mundo mágico.","averageScore":80,"genres":["Fantasy"]}}}""");
            }

            Assert.Equal("https://images.example/banner.jpg", request.RequestUri!.AbsoluteUri);
            bannerDownloadCount++;
            var imageResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([0xFF, 0xD8, 0x03, 0xFF, 0xD9])
            };
            imageResponse.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return imageResponse;
        }));
        var directory = Path.Combine(Path.GetTempPath(), "AniT.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var cover = Path.Combine(directory, "cover.jpg");
        await File.WriteAllBytesAsync(cover, [0xFF, 0xD8, 0x01, 0xFF, 0xD9]);

        try
        {
            var provider = new AnimeCoverProvider(directory, httpClient);
            var animeId = Guid.NewGuid();
            var result = await provider.EnsureMetadataAsync(
                animeId,
                "Anime Example",
                "Anime Example",
                cover,
                savedSynopsis: "Uma história sobre um mundo mágico.",
                savedCriticScore: 80,
                savedGenres: "Fantasy");

            Assert.Equal(1, postCount);
            Assert.Equal(1, bannerDownloadCount);
            Assert.True(File.Exists(result.BannerPath));
            Assert.Equal(result.BannerPath, provider.GetCachedBannerPath(animeId));
            Assert.False(provider.ShouldRefreshBanner(animeId));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task EnsureMetadataAsync_TranslatesSavedSynopsisWithoutRepeatingAniListLookup()
    {
        var postCount = 0;
        var translationCount = 0;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                postCount++;
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            translationCount++;
            return JsonResponse("""{"responseData":{"translatedText":"Uma aventura em um mundo peculiar."},"responseStatus":200}""");
        }));
        var directory = Path.Combine(Path.GetTempPath(), "AniT.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var animeId = Guid.NewGuid();
        var cover = Path.Combine(directory, "cover.jpg");
        var banner = Path.Combine(directory, $"{animeId:N}-banner.jpg");
        await File.WriteAllBytesAsync(cover, [0xFF, 0xD8, 0x01, 0xFF, 0xD9]);
        await File.WriteAllBytesAsync(banner, [0xFF, 0xD8, 0x02, 0xFF, 0xD9]);

        try
        {
            var provider = new AnimeCoverProvider(directory, httpClient);
            var result = await provider.EnsureMetadataAsync(
                animeId,
                "Anime Example",
                "Anime Example",
                cover,
                savedSynopsis: "An adventure in a peculiar world.",
                savedCriticScore: 80,
                savedGenres: "Fantasy");

            Assert.Equal(0, postCount);
            Assert.Equal(1, translationCount);
            Assert.Equal("Uma aventura em um mundo peculiar.", result.Synopsis);
            Assert.Equal(banner, result.BannerPath);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}

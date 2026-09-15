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
    public async Task EnsureMetadataAsync_ResolvesEnglishTitleBeforeChoosingCover()
    {
        var postCount = 0;
        var requestBodies = new List<string>();
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                postCount++;
                requestBodies.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                return JsonResponse(postCount == 1
                    ? """{"data":{"Media":{"title":{"english":"Magilumiere Magical Girls Inc. Season 2"},"coverImage":{"extraLarge":"https://images.example/japanese-search.jpg","large":null}}}}"""
                    : """{"data":{"Media":{"title":{"english":"Magilumiere Magical Girls Inc. Season 2"},"coverImage":{"extraLarge":"https://images.example/english-search.jpg","large":null}}}}""");
            }

            Assert.Equal("https://images.example/english-search.jpg", request.RequestUri!.AbsoluteUri);
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
                "Kabushiki Gaisha Magi Lumiere 2nd Season",
                null,
                null);

            Assert.Equal("Magilumiere Magical Girls Inc. Season 2", result.EnglishTitle);
            Assert.True(File.Exists(result.CoverPath));
            Assert.Equal(2, postCount);
            Assert.Contains("Kabushiki Gaisha Magi Lumiere 2nd Season", requestBodies[0]);
            Assert.Contains("Magilumiere Magical Girls Inc. Season 2", requestBodies[1]);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
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

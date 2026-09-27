using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AniT.Infrastructure;

namespace AniT.Tests;

public sealed class ProfileBannerProviderTests
{
    [Fact]
    public async Task SearchAsync_ReturnsOnlyFullHdWallhavenBanners()
    {
        Uri? requestedUri = null;
        using var client = new HttpClient(new StubHandler(request =>
        {
            requestedUri = request.RequestUri;
            return JsonResponse("""
            {"data":[
              {"id":"abc123","url":"https://wallhaven.cc/w/abc123","path":"https://w.wallhaven.cc/full/ab/wallhaven-abc123.jpg","dimension_x":3840,"dimension_y":1600,"thumbs":{"large":"https://th.wallhaven.cc/lg/ab/abc123.jpg"}},
              {"id":"small1","url":"https://wallhaven.cc/w/small1","path":"https://w.wallhaven.cc/full/sm/wallhaven-small1.jpg","dimension_x":1280,"dimension_y":720,"thumbs":{"large":"https://th.wallhaven.cc/lg/sm/small1.jpg"}}
            ]}
            """);
        }));
        var provider = new ProfileBannerProvider(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), client);

        var result = await provider.SearchAsync("Frieren", ProfileBannerSource.Wallhaven);

        var banner = Assert.Single(result.Items);
        Assert.False(result.NetworkUnavailable);
        Assert.Equal(3840, banner.Width);
        Assert.Equal(1600, banner.Height);
        Assert.Contains("categories=010", requestedUri?.Query ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ratios=21x9", requestedUri?.Query ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchAsync_ReportsOfflineWhenProviderCannotBeReached()
    {
        using var client = new HttpClient(new StubHandler(_ => throw new HttpRequestException("offline")));
        var provider = new ProfileBannerProvider(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), client);

        var result = await provider.SearchAsync("anime", ProfileBannerSource.Wallhaven);

        Assert.True(result.NetworkUnavailable);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task SearchAsync_ParsesAniListOfficialBanners()
    {
        using var client = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            return JsonResponse("""
                {"data":{"Page":{"media":[
                  {"id":52991,"siteUrl":"https://anilist.co/anime/52991","title":{"english":"Frieren: Beyond Journey's End","romaji":"Sousou no Frieren","native":null},"bannerImage":"https://s4.anilist.co/file/anilistcdn/media/anime/banner/example.jpg"},
                  {"id":1,"siteUrl":"https://anilist.co/anime/1","title":{"english":"No Banner","romaji":null,"native":null},"bannerImage":null}
                ]}}}
                """);
        }));
        var provider = new ProfileBannerProvider(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), client);

        var result = await provider.SearchAsync("Frieren", ProfileBannerSource.AniList);

        var banner = Assert.Single(result.Items);
        Assert.Equal("Frieren: Beyond Journey's End", banner.Title);
        Assert.Equal("AniList · arte oficial", banner.Source);
        Assert.False(result.NetworkUnavailable);
    }

    [Theory]
    [InlineData(ProfileBannerSource.MyAnimeList, "MyAnimeList · Jikan")]
    [InlineData(ProfileBannerSource.Kitsu, "Kitsu · arte oficial")]
    [InlineData(ProfileBannerSource.Danbooru, "Danbooru · somente geral")]
    [InlineData(ProfileBannerSource.Safebooru, "Safebooru · conteúdo seguro")]
    [InlineData(ProfileBannerSource.Gelbooru, "Gelbooru · somente geral")]
    public async Task SearchAsync_ParsesAdditionalBuiltInSources(ProfileBannerSource source, string expectedLabel)
    {
        Uri? requestedUri = null;
        using var client = new HttpClient(new StubHandler(request =>
        {
            requestedUri = request.RequestUri;
            return source switch
            {
                ProfileBannerSource.MyAnimeList => JsonResponse("""
                    {"data":[{"mal_id":1,"url":"https://myanimelist.net/anime/1","title":"Anime","title_english":"Anime EN","images":{"jpg":{"image_url":"https://img.test/mal-preview.jpg","large_image_url":"https://img.test/mal.jpg"}}}]}
                    """),
                ProfileBannerSource.Kitsu => JsonResponse("""
                    {"data":[{"id":"1","attributes":{"canonicalTitle":"Anime","slug":"anime","coverImage":{"small":"https://img.test/kitsu-preview.jpg","original":"https://img.test/kitsu.jpg"},"posterImage":null}}]}
                    """),
                ProfileBannerSource.Danbooru => JsonResponse("""
                    [{"id":1,"file_url":"https://img.test/danbooru.jpg","preview_file_url":"https://img.test/danbooru-preview.jpg","image_width":2400,"image_height":1600}]
                    """),
                ProfileBannerSource.Safebooru => JsonResponse("""
                    [{"id":"1","file_url":"https://img.test/safebooru.jpg","preview_url":"https://img.test/safebooru-preview.jpg","width":"2400","height":"1600"}]
                    """),
                ProfileBannerSource.Gelbooru => JsonResponse("""
                    {"post":[{"id":1,"file_url":"https://img.test/gelbooru.jpg","preview_url":"https://img.test/gelbooru-preview.jpg","width":2400,"height":1600}]}
                    """),
                _ => throw new InvalidOperationException()
            };
        }));
        var provider = new ProfileBannerProvider(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), client);

        var result = await provider.SearchAsync("Anime Example", source);

        var candidate = Assert.Single(result.Items);
        Assert.Equal(expectedLabel, candidate.Source);
        Assert.Equal("https://img.test/", new Uri(candidate.DownloadUrl).GetLeftPart(UriPartial.Authority) + "/");
        Assert.False(result.NetworkUnavailable);
        Assert.NotNull(requestedUri);
        var decodedQuery = Uri.UnescapeDataString(requestedUri!.Query);
        if (source == ProfileBannerSource.MyAnimeList) Assert.Contains("sfw=true", decodedQuery, StringComparison.OrdinalIgnoreCase);
        if (source == ProfileBannerSource.Danbooru || source == ProfileBannerSource.Gelbooru) Assert.Contains("rating:general", decodedQuery, StringComparison.OrdinalIgnoreCase);
        if (source == ProfileBannerSource.Safebooru) Assert.Contains("rating:safe", decodedQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CacheSelectedAsync_DownloadsOnlyOnce()
    {
        var calls = 0;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xD9]) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return response;
        }));
        var directory = Path.Combine(Path.GetTempPath(), "AniT.Tests", Guid.NewGuid().ToString("N"));
        var provider = new ProfileBannerProvider(directory, client);
        var candidate = new ProfileBannerCandidate("one", "Banner", "Wallhaven", "https://example.test/thumb.jpg", "https://example.test/full.jpg", "https://example.test/page", 3840, 2160);

        try
        {
            var first = await provider.CacheSelectedAsync(candidate);
            var second = await provider.CacheSelectedAsync(candidate);

            Assert.Equal(first, second);
            Assert.True(File.Exists(first));
            Assert.Equal(1, calls);
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

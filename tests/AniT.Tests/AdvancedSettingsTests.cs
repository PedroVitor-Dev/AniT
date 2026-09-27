using AniT.Infrastructure;
using System.Net;
using System.Text.Json;

namespace AniT.Tests;

public sealed class AdvancedSettingsTests
{
    [Fact]
    public void AdvancedPreferences_RoundTripWithoutLosingNetworkLogsShortcutsOrDeveloperMode()
    {
        var expected = AniTSystemSettings.Default with
        {
            OnlineConnectionLimit = 7,
            OfflineMode = true,
            OnlineSourceTimeoutSeconds = 33,
            DiagnosticLoggingEnabled = true,
            KeyboardShortcuts = new Dictionary<string, string> { ["Home"] = "Ctrl+1", ["Library"] = "Ctrl+2" },
            DeveloperMode = true
        };

        var actual = JsonSerializer.Deserialize<AniTSystemSettings>(JsonSerializer.Serialize(expected));

        Assert.NotNull(actual);
        Assert.Equal(7, actual.OnlineConnectionLimit);
        Assert.True(actual.OfflineMode);
        Assert.Equal(33, actual.OnlineSourceTimeoutSeconds);
        Assert.True(actual.DiagnosticLoggingEnabled);
        Assert.Equal("Ctrl+1", actual.KeyboardShortcuts!["Home"]);
        Assert.True(actual.DeveloperMode);
    }

    [Fact]
    public async Task OnlinePolicy_StopsRequestsInFullyOfflineMode()
    {
        var inner = new TrackingHandler(TimeSpan.Zero);
        using var client = new HttpClient(new AniTOnlineRequestHandler(inner,
            () => AniTSystemSettings.Default with { OfflineMode = true }));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://example.test/anime"));

        Assert.Contains("offline", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, inner.TotalRequests);
    }

    [Fact]
    public async Task OnlinePolicy_RespectsConfiguredConnectionLimit()
    {
        var inner = new TrackingHandler(TimeSpan.FromMilliseconds(45));
        using var client = new HttpClient(new AniTOnlineRequestHandler(inner,
            () => AniTSystemSettings.Default with { OnlineConnectionLimit = 2, OnlineSourceTimeoutSeconds = 5 }));

        await Task.WhenAll(Enumerable.Range(0, 8).Select(index => client.GetAsync($"https://example.test/{index}")));

        Assert.Equal(8, inner.TotalRequests);
        Assert.InRange(inner.MaximumConcurrentRequests, 1, 2);
    }

    [Theory]
    [InlineData("https://localhost/gallery")]
    [InlineData("https://127.0.0.1/gallery")]
    [InlineData("https://10.0.0.5/gallery")]
    [InlineData("https://172.16.10.20/gallery")]
    [InlineData("https://192.168.1.10/gallery")]
    [InlineData("https://[::1]/gallery")]
    [InlineData("file:///C:/private/gallery")]
    public void OnlinePolicy_RejectsLocalOrReservedSourceUris(string value)
    {
        Assert.False(AniTNetworkSecurity.IsPotentiallyPublicUri(new Uri(value)));
    }

    [Theory]
    [InlineData("https://anilist.co/anime/1")]
    [InlineData("https://1.1.1.1/image.jpg")]
    public void OnlinePolicy_AllowsPublicHttpsUris(string value)
    {
        Assert.True(AniTNetworkSecurity.IsPotentiallyPublicUri(new Uri(value)));
    }

    private sealed class TrackingHandler(TimeSpan delay) : HttpMessageHandler
    {
        private int activeRequests;
        private int maximumConcurrentRequests;
        private int totalRequests;
        public int MaximumConcurrentRequests => maximumConcurrentRequests;
        public int TotalRequests => totalRequests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref totalRequests);
            var active = Interlocked.Increment(ref activeRequests);
            var observed = maximumConcurrentRequests;
            while (active > observed)
            {
                var previous = Interlocked.CompareExchange(ref maximumConcurrentRequests, active, observed);
                if (previous == observed) break;
                observed = previous;
            }
            try
            {
                if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
            }
            finally { Interlocked.Decrement(ref activeRequests); }
        }
    }
}

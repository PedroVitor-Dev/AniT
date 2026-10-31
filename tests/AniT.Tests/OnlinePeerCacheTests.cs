using System.Text.Json;
using AniT.Infrastructure;
using Xunit;

namespace AniT.Tests;

public class OnlinePeerCacheTests
{
    [Fact]
    public async Task ReconnectHintsSurviveRestartAreBoundedAndIsolatedByTorrent()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try {
            var cache = new OnlinePeerCache(path);
            for (int i = 1; i <= 40; i++) cache.Remember("one", new Uri($"ipv4://127.0.0.1:{1000+i}"));
            cache.Remember("two", new Uri("ipv4://127.0.0.1:2000"));
            cache.Remember("one", new Uri("https://example.com:443"));
            await cache.FlushAsync();
            var restored = new OnlinePeerCache(path);
            Assert.Equal(32, (await restored.ReadAsync("one", default)).Count);
            Assert.Single(await restored.ReadAsync("two", default));
            Assert.Empty(await restored.ReadAsync("other", default));
        } finally { File.Delete(path); }
    }

    [Fact]
    public async Task ExpiredFutureMalformedAndCorruptHintsAreIgnored()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new[] {
                new OnlinePeerCache.Entry("one", "ipv4://127.0.0.1:2000", DateTimeOffset.UtcNow.AddDays(-2)),
                new OnlinePeerCache.Entry("one", "ipv4://127.0.0.1:2001", DateTimeOffset.UtcNow.AddDays(2)),
                new OnlinePeerCache.Entry("one", "https://example.com", DateTimeOffset.UtcNow)
            }));
            var cache = new OnlinePeerCache(path);
            Assert.Empty(await cache.ReadAsync("one", default));
            await File.WriteAllTextAsync(path, "broken");
            Assert.Empty(await cache.ReadAsync("one", default));
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.ReadAsync("one", cancel.Token));
        } finally { File.Delete(path); }
    }
}

using System.Net;
using System.Net.Http.Headers;
using AniT.Theater;

namespace AniT.Tests;

public sealed class TheaterStreamingTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public async Task SustainedStreamingPreservesBytesAndDeliversControls()
    {
        using var source = new Source(16 * 1024 * 1024 + 31);
        await using var host = TheaterRoom.Host(await MediaIdentity.FromFileAsync(source.Path), IPAddress.Loopback, sharedFilePath: source.Path, mode: TheaterMode.DirectStream);
        await using var guest = await TheaterRoom.JoinAsync(host.Invite!.Encode());
        var url = await guest.StartStreamingAsync();
        await host.SetReadyAsync(true); await guest.SetReadyAsync(true); await Until(() => host.BothReady);
        using var http = Client();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var download = http.GetByteArrayAsync(url);
        var control = System.Diagnostics.Stopwatch.StartNew();
        await host.PublishAsync(10, false); await Until(() => guest.LatestState?.PositionSeconds == 10);
        var controlMs = control.Elapsed.TotalMilliseconds;
        Assert.Equal(source.Bytes, await download);
        output.WriteLine($"Loopback: {source.Bytes.Length / 1048576d / watch.Elapsed.TotalSeconds:0.0} MiB/s; control {controlMs:0.0} ms; elapsed {watch.Elapsed.TotalMilliseconds:0} ms");
        Assert.False(guest.HasMedia);
    }

    [Fact]
    public async Task StreamingStartsWithoutCompleteCopyAndSupportsSeekingOverPinnedRoom()
    {
        using var source = new Source();
        var identity = await MediaIdentity.FromFileAsync(source.Path);
        await using var host = TheaterRoom.Host(identity, IPAddress.Loopback, sharedFilePath: source.Path, mode: TheaterMode.DirectStream);
        await using var guest = await TheaterRoom.JoinAsync(host.Invite!.Encode());
        await Until(() => guest.Clock.IsReady);
        Assert.Equal(TheaterMode.DirectStream, guest.Mode); Assert.False(guest.HasMedia); Assert.False(guest.CanPrepare);
        await Assert.ThrowsAsync<InvalidOperationException>(() => guest.SetReadyAsync(true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => guest.DownloadAsync(source.Path + ".download"));
        var url = await guest.StartStreamingAsync();
        Assert.Equal(url, await guest.StartStreamingAsync()); Assert.True(guest.CanPrepare); Assert.True(url.IsLoopback);
        Assert.Equal(TheaterStreamProxy.StartupBlockSize, guest.StreamBytesReceived); Assert.True(guest.StreamBytesReceived < identity.Length);
        using var http = Client();
        using (var head = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, url)))
        { Assert.Equal(HttpStatusCode.OK, head.StatusCode); Assert.Equal(identity.Length, head.Content.Headers.ContentLength); Assert.Contains("bytes", head.Headers.AcceptRanges); }
        var received = guest.StreamBytesReceived;
        Assert.Equal(source.Bytes[..100], await RangeAsync(http, url, 0, 99)); Assert.Equal(received, guest.StreamBytesReceived);
        Assert.Equal(source.Bytes[700000..701001], await RangeAsync(http, url, 700000, 701000));
        Assert.Equal(source.Bytes[^100..], await RangeAsync(http, url, source.Bytes.Length - 100, source.Bytes.Length - 1));
        Assert.True(guest.StreamBytesReceived < identity.Length); Assert.False(guest.HasMedia);
        await host.SetReadyAsync(true); await guest.SetReadyAsync(true); await Until(() => host.BothReady);
        await host.PublishAsync(120, true); await Until(() => guest.LatestState?.PositionSeconds == 120);
        await guest.SetReadyAsync(false); await Until(() => !host.BothReady);
    }

    [Fact]
    public async Task LocalEndpointRejectsWrongSecretMethodsAndInvalidRanges()
    {
        using var source = new Source();
        await using var host = TheaterRoom.Host(await MediaIdentity.FromFileAsync(source.Path), IPAddress.Loopback, sharedFilePath: source.Path, mode: TheaterMode.DirectStream);
        await using var guest = await TheaterRoom.JoinAsync(host.Invite!.Encode());
        var url = await guest.StartStreamingAsync(); using var http = Client(); var received = guest.StreamBytesReceived;
        using var wrong = await http.GetAsync(new Uri(url, "/wrong/episode.mkv")); Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
        using var post = await http.PostAsync(url, new StringContent("ignored")); Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Get, url); request.Headers.TryAddWithoutValidation("Range", "bytes=999999999-");
        using var invalid = await http.SendAsync(request); Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, invalid.StatusCode);
        Assert.Equal(received, guest.StreamBytesReceived);
        Assert.True(guest.IsConnected);
    }

    [Theory]
    [InlineData("bytes=0-99", true, 0, 99)]
    [InlineData("bytes=900-", true, 900, 999)]
    [InlineData("bytes=-25", true, 975, 999)]
    [InlineData("bytes=990-999999", true, 990, 999)]
    [InlineData("bytes=-2000", true, 0, 999)]
    [InlineData("bytes=1000-", false, 0, 0)]
    [InlineData("bytes=0-1,5-9", false, 0, 0)]
    [InlineData("bytes=10-9", false, 0, 0)]
    [InlineData("bytes=--1", false, 0, 0)]
    [InlineData("bytes=-0", false, 0, 0)]
    [InlineData("bytes=0-92233720368547758070", false, 0, 0)]
    [InlineData("items=0-1", false, 0, 0)]
    public void HttpRangesAreBounded(string header, bool valid, long first, long last)
    {
        Assert.Equal(valid, TheaterStreamProxy.TryRange(header, 1000, out var actualFirst, out var actualLast));
        if (valid) { Assert.Equal(first, actualFirst); Assert.Equal(last, actualLast); }
    }

    [Fact]
    public async Task DisconnectStopsStreamingAndDisposalClosesLocalEndpoint()
    {
        using var source = new Source();
        await using var host = TheaterRoom.Host(await MediaIdentity.FromFileAsync(source.Path), IPAddress.Loopback, sharedFilePath: source.Path, mode: TheaterMode.DirectStream);
        await using var guest = await TheaterRoom.JoinAsync(host.Invite!.Encode());
        var url = await guest.StartStreamingAsync();
        await host.DisposeAsync(); await Until(() => !guest.IsConnected);
        Assert.False(guest.BothReady);
        await guest.DisposeAsync();
        using var http = Client(); await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync(url));
        Assert.Empty(Directory.GetFiles(source.Folder, "*.download"));
    }

    [Fact]
    public async Task StreamingRequiresAuthorizedFileAndLocksOriginalUntilRoomCloses()
    {
        using var source = new Source(); var identity = await MediaIdentity.FromFileAsync(source.Path);
        Assert.Throws<ArgumentException>(() => TheaterRoom.Host(identity, IPAddress.Loopback, mode: TheaterMode.DirectStream));
        await using var host = TheaterRoom.Host(identity, IPAddress.Loopback, sharedFilePath: source.Path, mode: TheaterMode.DirectStream);
        if (OperatingSystem.IsWindows()) Assert.Throws<IOException>(() => File.OpenWrite(source.Path));
        await host.DisposeAsync(); using var writable = File.OpenWrite(source.Path); Assert.True(writable.CanWrite);
        await using var peerHost = TheaterRoom.Host(identity, IPAddress.Loopback);
        await using var peerGuest = await TheaterRoom.JoinAsync(peerHost.Invite!.Encode(), identity);
        await Assert.ThrowsAsync<InvalidOperationException>(() => peerGuest.StartStreamingAsync());
    }

    private static HttpClient Client() => new(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) };
    private static async Task<byte[]> RangeAsync(HttpClient client, Uri uri, long first, long last)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri); request.Headers.Range = new RangeHeaderValue(first, last);
        using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(first, response.Content.Headers.ContentRange!.From); Assert.Equal(last, response.Content.Headers.ContentRange.To);
        return await response.Content.ReadAsByteArrayAsync();
    }
    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }
    private sealed class Source : IDisposable
    {
        public string Folder { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "anit-stream-test-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Folder, "episode.mkv");
        public byte[] Bytes { get; }
        public Source(int size = 8 * 1024 * 1024 + 31) { Bytes = new byte[size]; Directory.CreateDirectory(Folder); new Random(19).NextBytes(Bytes); File.WriteAllBytes(Path, Bytes); }
        public void Dispose() => Directory.Delete(Folder, true);
    }
}

using System.Net;
using System.Text.Json;

namespace AniT.Infrastructure;

// Profile-local hints, never an authority: the torrent handshake still validates each peer.
internal sealed class OnlinePeerCache(string path)
{
    internal sealed record Entry(string Torrent, string Address, DateTimeOffset Seen);
    readonly object sync = new();
    readonly List<Entry> pending = [];
    readonly SemaphoreSlim io = new(1, 1);
    internal static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    static bool Valid(string address, out Uri uri)
    {
        uri = null!;
        return Uri.TryCreate(address, UriKind.Absolute, out uri!)
            && uri.Scheme is "ipv4" or "ipv6" && uri.Port is > 0 and <= 65535
            && IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip)
            && !IPAddress.Any.Equals(ip) && !IPAddress.IPv6Any.Equals(ip)
            && !IPAddress.Broadcast.Equals(ip) && !ip.IsIPv6Multicast;
    }

    internal void Remember(string torrent, Uri address)
    {
        if (!Valid(address.AbsoluteUri, out _)) return;
        lock (sync) {
            pending.RemoveAll(e => e.Torrent == torrent && e.Address == address.AbsoluteUri);
            pending.Add(new(torrent, address.AbsoluteUri, DateTimeOffset.UtcNow));
            if (pending.Count > 2048) pending.RemoveRange(0, pending.Count - 2048);
        }
    }

    async Task<List<Entry>> LoadAsync(CancellationToken token)
    {
        try {
            if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024) return [];
            return JsonSerializer.Deserialize<List<Entry>>(await File.ReadAllTextAsync(path, token)) ?? [];
        } catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    static List<Entry> Prune(IEnumerable<Entry> entries) => entries
        .Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Torrent)
            && e.Seen <= DateTimeOffset.UtcNow && DateTimeOffset.UtcNow - e.Seen < Retention && Valid(e.Address, out _))
        .OrderByDescending(e => e.Seen).DistinctBy(e => (e.Torrent, e.Address))
        .GroupBy(e => e.Torrent).Take(64).SelectMany(g => g.Take(32)).ToList();

    internal async Task<IReadOnlyList<Uri>> ReadAsync(string torrent, CancellationToken token)
    {
        await io.WaitAsync(token);
        try {
            var entries = await LoadAsync(token);
            lock (sync) entries.AddRange(pending);
            token.ThrowIfCancellationRequested();
            return Prune(entries).Where(e => e.Torrent == torrent).Select(e => new Uri(e.Address)).ToArray();
        } finally { io.Release(); }
    }

    internal async Task FlushAsync()
    {
        await io.WaitAsync();
        try {
            var entries = await LoadAsync(CancellationToken.None);
            lock (sync) entries.AddRange(pending);
            var temp = path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(Prune(entries)));
            File.Move(temp, path, true);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Hints must not break playback. */ }
        finally { io.Release(); }
    }
}

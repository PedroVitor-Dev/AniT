using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using MonoTorrent;
using MonoTorrent.Client;

static EngineSettings Settings(string cache, int port = 0) => new EngineSettingsBuilder {
    CacheDirectory = cache, DhtEndPoint = null, AllowLocalPeerDiscovery = false, AllowPortForwarding = false,
    ListenEndPoints = new Dictionary<string, IPEndPoint> { ["ipv4"] = new(IPAddress.Loopback, port) },
    MaximumUploadRate = 0, MaximumDownloadRate = 0, UsePartialFiles = true
}.ToSettings();
static void Emit(object value) { Console.WriteLine(JsonSerializer.Serialize(value)); Console.Out.Flush(); }

if (args.Length == 0) throw new ArgumentException("Use fixture, seed or mono; see README.md.");
var mode = args[0]; var root = Path.GetFullPath(args[1]); Directory.CreateDirectory(root);
if (mode == "fixture") {
    var original = Path.Combine(root, "Original"); Directory.CreateDirectory(original);
    for (var i = 0; i < 3; i++) {
        var data = new byte[16 * 1024 * 1024]; new Random(100 + i).NextBytes(data);
        await File.WriteAllBytesAsync(Path.Combine(original, $"Authorized - {i+1:00}.bin"), data);
    }
    var pieceLength = int.Parse(args[2]);
    var metadata = await new TorrentCreator { PieceLength = pieceLength }.CreateAsync(new TorrentFileSource(original));
    await File.WriteAllBytesAsync(Path.Combine(root, "fixture.torrent"), metadata.Encode());
    Emit(new { fixture = root, piece_bytes = pieceLength }); return;
}
var torrent = await Torrent.LoadAsync(Path.Combine(root, "fixture.torrent"));
if (mode == "seed") {
    var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
    using var engine = new ClientEngine(Settings(Path.Combine(root, "seed-" + Guid.NewGuid().ToString("N")), port));
    var seed = await engine.AddAsync(torrent, root); await seed.HashCheckAsync(false); await seed.StartAsync();
    if (!seed.Complete) throw new InvalidOperationException("Fixture seed is incomplete.");
    await using var relay = new TorrentPeerRelay(port, 1024 * 1024);
    Emit(new { ready = true, port = relay.Port, wire_bytes_per_second = 1024 * 1024 });
    await Console.In.ReadLineAsync(); await seed.StopAsync(); return;
}
if (mode != "mono") throw new ArgumentException("Unknown mode");
var destination = Path.GetFullPath(args[2]); var seedPort = int.Parse(args[3]); var warm = args[4] == "warm";
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
if (warm) {
    var target = Path.Combine(destination, "Original", "Authorized - 02.bin");
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    File.Copy(Path.Combine(root, "Original", "Authorized - 02.bin"), target);
}
using var client = new ClientEngine(Settings(Path.Combine(destination, "engine")));
var timer = Stopwatch.StartNew();
var manager = await client.AddStreamingAsync(torrent, destination);
var files = manager.Files.OrderBy(f => f.Path, StringComparer.Ordinal).ToArray(); var media = files[1];
foreach (var file in files) await manager.SetFilePriorityAsync(file, file == media ? Priority.Normal : Priority.DoNotDownload);
double? peerMs = null, dataMs = null;
manager.PeerConnected += (_, _) => Interlocked.CompareExchange(ref peerMsBox, timer.ElapsedMilliseconds, -1);
await manager.AddPeerAsync(new PeerInfo(new Uri($"ipv4://127.0.0.1:{seedPort}")));
var sampling = Task.Run(async () => {
    while (!deadline.IsCancellationRequested) {
        if (manager.Monitor.DataBytesReceived > 0) { dataMs = timer.Elapsed.TotalMilliseconds; return; }
        try { await Task.Delay(10, deadline.Token); } catch (OperationCanceledException) { return; }
    }
});
var reads = new List<object>();
try {
    if (warm) {
        await manager.HashCheckAsync(false);
        if (!media.BitField.AllTrue) throw new InvalidOperationException("Warm fixture did not verify completely");
    }
    await manager.StartAsync();
    using var stream = await manager.StreamProvider!.CreateStreamAsync(media, false, deadline.Token);
    // Identical byte ranges and order on both engines; seek away while most data is missing.
    foreach (var offset in new long[] { 0, media.Length * 3 / 4, media.Length / 4, 0 }) {
        var before = timer.Elapsed.TotalMilliseconds;
        stream.Seek(offset, SeekOrigin.Begin);
        // StreamProvider can expose blocks before the entire SHA-1 piece is complete.
        // Both contenders must wait for whole-piece verification, as the app should.
        var firstPiece = media.StartPieceIndex + (int)(offset / torrent.PieceLength);
        var lastPiece = media.StartPieceIndex + (int)((offset + 65535) / torrent.PieceLength);
        var data = new byte[65536];
        var reading = stream.ReadExactlyAsync(data, deadline.Token).AsTask();
        while (Enumerable.Range(firstPiece, lastPiece-firstPiece+1).Any(i => !manager.Bitfield[i]))
            await Task.Delay(10, deadline.Token);
        await reading;
        await using var original = File.OpenRead(Path.Combine(root, "Original", "Authorized - 02.bin")); original.Position = offset;
        var expected = new byte[data.Length]; await original.ReadExactlyAsync(expected, deadline.Token);
        if (!data.SequenceEqual(expected)) throw new InvalidDataException("Verified range differs from source");
        reads.Add(new { offset, ready_ms = timer.Elapsed.TotalMilliseconds, wait_ms = timer.Elapsed.TotalMilliseconds - before, bytes = data.Length, verified = true });
    }
    peerMs = Interlocked.Read(ref peerMsBox) is var observed && observed >= 0 ? observed : null;
    Emit(new { engine = "MonoTorrent", version = "3.0.2", warm, piece_bytes = torrent.PieceLength,
        peer_ms = peerMs, first_data_ms = dataMs, reads, received_bytes = manager.Monitor.DataBytesReceived,
        selected_percent = media.BitField.PercentComplete,
        unselected_pieces = files.Where(f => f != media).Sum(f => f.BitField.TrueCount), total_ms = timer.Elapsed.TotalMilliseconds });
} finally { deadline.Cancel(); await sampling; await manager.StopAsync(TimeSpan.FromSeconds(2)); }

partial class Program { static long peerMsBox = -1; }

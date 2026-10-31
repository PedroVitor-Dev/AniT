using System.Text.Json;
using System.Threading.Channels;
using AniT.Core;

namespace AniT.Infrastructure;

/// <summary>Bounded non-blocking logging, retained locally with size/count limits.</summary>
public sealed class OnlineTraceJournal : IAsyncDisposable
{
    readonly Channel<OnlinePlaybackMetric> queue = Channel.CreateBounded<OnlinePlaybackMetric>(new BoundedChannelOptions(2048) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    readonly Task worker;
    long dropped;
    public long DroppedEvents => Interlocked.Read(ref dropped);
    public bool WriteFailed { get; private set; }
    public OnlineTraceJournal(string folder) => worker = Task.Run(() => RunAsync(folder));
    public void Write(OnlinePlaybackMetric metric) { if (!queue.Writer.TryWrite(metric)) Interlocked.Increment(ref dropped); }
    async Task RunAsync(string folder)
    {
        StreamWriter? output = null;
        long bytes = 0;
        try {
            await foreach (var metric in queue.Reader.ReadAllAsync()) {
                if (output is null || bytes >= 8 * 1024 * 1024) {
                    if (output is not null) await output.DisposeAsync();
                    Directory.CreateDirectory(folder);
                    var path = Path.Combine(folder, $"online-trace-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");
                    output = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 16384, true)); bytes = 0;
                    foreach (var old in new DirectoryInfo(folder).GetFiles("online-trace-*.jsonl").OrderByDescending(item => item.CreationTimeUtc).Skip(10)) {
                        try { old.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    }
                }
                var line = JsonSerializer.Serialize(metric); await output.WriteLineAsync(line);
                bytes += System.Text.Encoding.UTF8.GetByteCount(line) + 2;
                // One-second samples are inexpensive; flushing keeps incomplete sessions inspectable.
                await output.FlushAsync();
            }
        } catch (Exception) { WriteFailed = true; }
        finally { if (output is not null) { try { await output.DisposeAsync(); } catch { WriteFailed = true; } } }
    }
    public async ValueTask DisposeAsync() { queue.Writer.TryComplete(); await worker.ConfigureAwait(false); }
}

using System.Diagnostics;
using MonoTorrent;
using MonoTorrent.Client;
using MonoTorrent.Trackers;
using ReusableTasks;

namespace AniT.Infrastructure;

// Metadata discovery is isolated: cancellation must not wait for tracker stop-announces
// or leave an invisible manager registered in the playback engine.
internal static class OnlineMetadataResolver
{
    internal static async Task<ReadOnlyMemory<byte>> ResolveAsync(MagnetLink magnet, EngineSettings settings,
        IProgress<OnlineImportProgress>? progress, CancellationToken token, TimeSpan? limit = null, Factories? sourceFactories = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(limit ?? TimeSpan.FromSeconds(120));
        var originals = sourceFactories ?? Factories.Default;
        var factories = originals;
        foreach (var scheme in new[] { "http", "https", "udp" })
            factories = factories.WithTrackerCreator(scheme, uri => new BoundedMetadataTracker(originals.CreateTracker(uri) ?? throw new NotSupportedException("Protocolo de tracker indisponível.")));
        using var resolver = new ClientEngine(settings, factories);
        var watch = Stopwatch.StartNew();
        var pending = resolver.DownloadMetadataAsync(magnet, deadline.Token);
        try {
            while (!pending.IsCompleted) {
                token.ThrowIfCancellationRequested();
                var peers = resolver.ConnectionManager.OpenConnections;
                var status = peers == 0 ? "Buscando participantes via DHT e trackers do link." : "Solicitando os metadados aos participantes conectados.";
                progress?.Report(new(8, "Buscando a lista de episódios",
                    $"{peers} conexões · {watch.Elapsed.Minutes:00}:{watch.Elapsed.Seconds:00} de espera\n{status}\nSomente os metadados; nenhum episódio está sendo baixado.", true));
                await Task.WhenAny(pending, Task.Delay(1000, deadline.Token)).WaitAsync(deadline.Token);
            }
            return await pending.WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) {
            throw new TimeoutException("Não foi possível obter a lista de episódios dentro do prazo. As fontes não entregaram os metadados. Tente novamente ou importe o arquivo .torrent dessa mesma fonte.");
        }
        finally {
            deadline.Cancel();
            // Dispose closes the isolated listener immediately. Observe the library's async
            // cleanup without making Cancel wait for unavailable remote trackers.
            _ = ObserveCompletionAsync(pending);
        }
    }
    static async Task ObserveCompletionAsync(Task task) { try { await task.ConfigureAwait(false); } catch { } }
}

internal sealed class BoundedMetadataTracker(ITracker inner, TimeSpan? limit = null) : ITracker
{
    public bool CanScrape => inner.CanScrape;
    public TimeSpan MinUpdateInterval => inner.MinUpdateInterval;
    public TimeSpan UpdateInterval => inner.UpdateInterval;
    public TimeSpan TimeSinceLastAnnounce => inner.TimeSinceLastAnnounce;
    public TrackerState Status => inner.Status;
    public Uri Uri => inner.Uri;
    public string WarningMessage => inner.WarningMessage;
    public string FailureMessage => inner.FailureMessage;
    public async ReusableTask<AnnounceResponse> AnnounceAsync(AnnounceRequest request, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(limit ?? TimeSpan.FromSeconds(request.ClientEvent == TorrentEvent.Stopped ? 2 : 8));
        var task = inner.AnnounceAsync(request, deadline.Token).AsTask();
        try {
            var response = await task.WaitAsync(deadline.Token);
            // TrackerTier falls through to its next tracker only on an exception.
            if (response.State != TrackerState.Ok) throw new IOException("O tracker não entregou uma resposta válida de metadados.");
            return response;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) {
            throw new TimeoutException("O tracker não respondeu à consulta de metadados dentro do prazo.");
        }
        finally { _ = ObserveAsync(task); }
    }
    static async Task ObserveAsync(Task task) { try { await task.ConfigureAwait(false); } catch { } }
    public ReusableTask<ScrapeResponse> ScrapeAsync(ScrapeRequest request, CancellationToken token) => inner.ScrapeAsync(request, token);
}

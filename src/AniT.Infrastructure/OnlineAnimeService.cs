using System.Net;
using System.Text.Json;
using AniT.Core;
using Microsoft.EntityFrameworkCore;
using MonoTorrent;
using MonoTorrent.Client;

namespace AniT.Infrastructure;

public sealed record OnlineEpisode(Guid EpisodeId, string Title, string FileName, double Progress, long Size, bool IsCloud, bool IsSelected = false);
public sealed record OnlineTransfer(string Id, string Name, string State, double Progress, long DownloadSpeed, int Peers, bool Paused, IReadOnlyList<OnlineEpisode> Episodes, bool WaitingForSelection = false);
public sealed record OnlineJob(string Id, string Source, string SavePath, string Title, bool Paused, DateTimeOffset AcceptedAt, int? SelectedFileIndex = null);

/// <summary>One lazy, profile-scoped engine. Completed files keep their original episode identity.</summary>
public sealed class OnlineAnimeService : IAsyncDisposable
{
    readonly string databasePath;
    readonly string statePath;
    readonly ClientEngine engine;
    readonly SemaphoreSlim gate = new(1, 1);
    readonly SemaphoreSlim streamGate = new(1, 1);
    readonly Dictionary<string, (OnlineJob Job, TorrentManager Manager)> downloads = new();
    readonly CancellationTokenSource lifetime = new();
    OnlineStreamServer? stream;
    bool restored;
    readonly Dictionary<string, OnlineJob> savedJobs = new();
    readonly HashSet<string> prepared = new();
    public string? LastError { get; private set; }
    public Action<string>? Diagnostic { get; set; }

    public OnlineAnimeService(string databasePath, string stateDirectory, EngineSettings? settings = null)
    {
        this.databasePath = databasePath;
        Directory.CreateDirectory(stateDirectory);
        statePath = Path.Combine(stateDirectory, "downloads.json");
        engine = new ClientEngine(settings ?? new EngineSettingsBuilder {
            CacheDirectory = Path.Combine(stateDirectory, "engine"),
            AutoSaveLoadFastResume = true, AutoSaveLoadMagnetLinkMetadata = true,
            UsePartialFiles = true, AllowPortForwarding = false,
            DhtEndPoint = new IPEndPoint(IPAddress.Any, 0),
            ListenEndPoints = new Dictionary<string, IPEndPoint> { ["ipv4"] = new(IPAddress.Any, 0) }
        }.ToSettings());
    }

    public static bool IsVideo(string path) => AniTSystemSettings.DefaultVideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static string ContainedPath(string root, string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/');
        if (Path.IsPathRooted(relative) || parts.Any(p => p is "." or ".." || p.Contains(':') || p.Length == 0))
            throw new InvalidDataException("O torrent contém um caminho inválido.");
        var full = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        if (!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("O arquivo está fora da pasta de download.");
        return full;
    }

    public async Task RestoreAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try {
            if (restored) return;
            var jobs = File.Exists(statePath) ? JsonSerializer.Deserialize<List<OnlineJob>>(await File.ReadAllTextAsync(statePath, token)) ?? [] : [];
            restored = true;
            foreach (var job in jobs) savedJobs[job.Id] = job;
            foreach (var job in jobs) {
                try {
                    var manager = await AddManagerAsync(job, token);
                    // Opening the app never restarts an entire season, including legacy manifests.
                    var waiting = job with { Paused = true, SelectedFileIndex = null };
                    downloads.Add(job.Id, (waiting, manager));
                    savedJobs[job.Id] = waiting;
                } catch (Exception ex) when (ex is not OperationCanceledException) { LastError = $"Não foi possível retomar {job.Title}: {ex.Message}"; }
            }
        } finally { gate.Release(); }
    }

    public static string NormalizeMagnet(string source)
    {
        source = source.Trim();
        if (!source.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Magnet link inválido.");
        var query = source[8..].Split('&', StringSplitOptions.RemoveEmptyEntries).Select(part => {
            var pair = part.Split('=', 2);
            if (pair.Length != 2) throw new ArgumentException("Parâmetro inválido no magnet link.");
            var value = Uri.UnescapeDataString(pair[1].Replace("+", "%20"));
            var key = Uri.UnescapeDataString(pair[0]).ToLowerInvariant();
            return key + "=" + (key == "xt" ? value : Uri.EscapeDataString(value));
        });
        var normalized = "magnet:?" + string.Join("&", query);
        _ = MagnetLink.Parse(normalized);
        return normalized;
    }

    async Task<TorrentManager> AddManagerAsync(OnlineJob job, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        TorrentManager manager;
        if (job.Source.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)) {
            var magnet = MagnetLink.Parse(NormalizeMagnet(job.Source));
            manager = await engine.AddStreamingAsync(magnet, job.SavePath);
            if (!manager.HasMetadata) {
                await engine.RemoveAsync(manager);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
                timeout.CancelAfter(TimeSpan.FromMinutes(2));
                // This API explicitly runs metadata-only mode: no episode pieces are requested.
                var metadata = await engine.DownloadMetadataAsync(magnet, timeout.Token);
                var path = Path.Combine(Path.GetDirectoryName(statePath)!, "sources", job.Id + ".torrent");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, metadata.ToArray(), token);
                manager = await engine.AddStreamingAsync(await Torrent.LoadAsync(path), job.SavePath);
            }
        } else manager = await engine.AddStreamingAsync(await Torrent.LoadAsync(job.Source), job.SavePath);
        await SelectSafeFilesAsync(manager, job.SavePath, null);
        return manager;
    }

    static async Task SelectSafeFilesAsync(TorrentManager manager, string savePath, int? selected)
    {
        if (selected is { } index && (index < 0 || index >= manager.Files.Count || !IsVideo(manager.Files[index].Path)))
            throw new InvalidDataException("O episódio não corresponde a um vídeo válido do torrent.");
        var selectedFile = selected is { } fileIndex ? manager.Files[fileIndex] : null;
        foreach (var file in manager.Files) {
            ContainedPath(savePath, Path.GetRelativePath(savePath, file.DownloadCompleteFullPath));
            var subtitle = false;
            if (selectedFile is not null && Path.GetDirectoryName(file.Path) == Path.GetDirectoryName(selectedFile.Path)) {
                var ext = Path.GetExtension(file.Path).ToLowerInvariant();
                var stem = Path.GetFileNameWithoutExtension(selectedFile.Path);
                var subtitleStem = Path.GetFileNameWithoutExtension(file.Path);
                subtitle = ext is ".srt" or ".ass" or ".ssa" or ".vtt"
                    && (subtitleStem.Equals(stem, StringComparison.OrdinalIgnoreCase) || subtitleStem.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase));
            }
            await manager.SetFilePriorityAsync(file, ReferenceEquals(file, selectedFile) || subtitle ? Priority.Normal : Priority.DoNotDownload);
        }
    }

    public async Task<string> AddAsync(string source, string destination, string title, bool authorized, CancellationToken token = default)
    {
        if (!authorized) throw new InvalidOperationException("Confirme que possui autorização para baixar e compartilhar este conteúdo.");
        if (string.IsNullOrWhiteSpace(destination) || !Directory.Exists(destination)) throw new DirectoryNotFoundException("Escolha uma pasta existente para os episódios.");
        source = source.Trim();
        if (source.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)) source = NormalizeMagnet(source);
        else if (!File.Exists(source) && (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
            throw new ArgumentException("Cole um magnet link, uma URL HTTP/HTTPS de .torrent ou escolha um arquivo .torrent.");
        await RestoreAsync(token);
        await gate.WaitAsync(token);
        TorrentManager? manager = null;
        string? pendingId = null;
        try {
            var id = Guid.NewGuid().ToString("N");
            pendingId = id;
            var jobDirectory = Path.Combine(Path.GetDirectoryName(statePath)!, "sources");
            Directory.CreateDirectory(jobDirectory);
            if (!source.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)) {
                var metadataPath = Path.Combine(jobDirectory, id + ".torrent");
                if (File.Exists(source)) {
                    if (new FileInfo(source).Length > 8 * 1024 * 1024) throw new InvalidDataException("Arquivo .torrent muito grande (máximo de 8 MB).");
                    File.Copy(source, metadataPath);
                } else {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                    using var response = await client.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, token);
                    response.EnsureSuccessStatusCode();
                    await using var input = await response.Content.ReadAsStreamAsync(token);
                    await using var output = File.Create(metadataPath);
                    var buffer = new byte[65536]; int count; long total = 0;
                    while ((count = await input.ReadAsync(buffer, token)) > 0) {
                        if ((total += count) > 8 * 1024 * 1024) throw new InvalidDataException("Arquivo .torrent muito grande (máximo de 8 MB).");
                        await output.WriteAsync(buffer.AsMemory(0, count), token);
                    }
                }
                source = metadataPath;
            }
            var job = new OnlineJob(id, source, Path.Combine(Path.GetFullPath(destination), "AniT Online", id), title.Trim(), true, DateTimeOffset.UtcNow);
            Directory.CreateDirectory(job.SavePath);
            manager = await AddManagerAsync(job, token);
            if (downloads.Values.Any(d => d.Manager.InfoHashes.Equals(manager.InfoHashes))) throw new InvalidOperationException("Este torrent já foi adicionado.");
            // The catalog is imported before any video download starts.
            var durableMetadata = Path.Combine(jobDirectory, id + ".torrent");
            if (!File.Exists(durableMetadata) && File.Exists(manager.MetadataPath)) File.Copy(manager.MetadataPath, durableMetadata);
            if (File.Exists(durableMetadata)) job = job with { Source = durableMetadata };
            if (!manager.Files.Any(f => f.Length > 0 && IsVideo(f.Path))) throw new InvalidDataException("O torrent não contém episódios em um formato de vídeo compatível.");
            job = job with { Title = string.IsNullOrWhiteSpace(job.Title) ? manager.Name : job.Title };
            savedJobs[id] = job;
            await SaveAsync(token);
            await ImportAsync(job, manager, token);
            downloads.Add(id, (job, manager));
            prepared.Add(id);
            return id;
        } catch {
            if (pendingId is not null && !downloads.ContainsKey(pendingId) && savedJobs.Remove(pendingId)) await SaveAsync(CancellationToken.None);
            if (manager is not null && !downloads.Values.Any(d => ReferenceEquals(d.Manager, manager))) {
                await manager.StopAsync(); await engine.RemoveAsync(manager);
            }
            throw;
        } finally { gate.Release(); }
    }

    async Task ImportAsync(OnlineJob job, TorrentManager manager, CancellationToken token)
    {
        await using var db = AniTDatabase.Create(databasePath);
        if (await db.MediaFiles.AnyAsync(f => f.OnlineTorrentId == job.Id, token)) return;
        var root = new LibraryRoot { Path = job.SavePath, DisplayName = "Online Anime Player · " + job.Title, IsEnabled = false };
        db.LibraryRoots.Add(root);
        var anime = await db.Anime.Include(a => a.Seasons).ThenInclude(s => s.Episodes).FirstOrDefaultAsync(a => a.Title == job.Title, token);
        if (anime is null) { anime = new Anime { Title = job.Title }; db.Anime.Add(anime); }
        var ordered = manager.Files.Select((file, index) => (file, index)).Where(x => x.file.Length > 0 && IsVideo(x.file.Path)).OrderBy(x => x.file.Path, StringComparer.OrdinalIgnoreCase).ToList();
        var ordinal = 0;
        foreach (var (file, index) in ordered) {
            ordinal++;
            var parsed = EpisodeFileNameParser.Parse(file.DownloadCompleteFullPath, job.SavePath);
            var seasonNumber = parsed.CanAutoImport ? parsed.SeasonNumber : 1;
            var number = parsed.CanAutoImport ? (int)parsed.EpisodeNumber!.Value : ordinal;
            var season = anime.Seasons.FirstOrDefault(s => s.Number == seasonNumber);
            if (season is null) { season = new Season { AnimeId = anime.Id, Number = seasonNumber }; anime.Seasons.Add(season); db.Seasons.Add(season); }
            var episode = season.Episodes.FirstOrDefault(e => e.Number == number);
            if (episode is null) { episode = new Episode { SeasonId = season.Id, Number = number, Title = parsed.EpisodeTitle }; season.Episodes.Add(episode); db.Episodes.Add(episode); }
            db.MediaFiles.Add(new MediaFile {
                EpisodeId = episode.Id, LibraryRootId = root.Id,
                RelativePath = Path.GetRelativePath(job.SavePath, file.DownloadCompleteFullPath),
                FileName = Path.GetFileName(file.Path), Extension = Path.GetExtension(file.Path), SizeInBytes = file.Length,
                OnlineTorrentId = job.Id, OnlineFileIndex = index, IsCloud = !file.BitField.AllTrue,
                IsPreferred = episode.MediaFiles.Count == 0, Resolution = parsed.Resolution, Language = parsed.Language
            });
        }
        await db.SaveChangesAsync(token);
    }

    public async Task<IReadOnlyList<OnlineTransfer>> SnapshotAsync(CancellationToken token = default)
    {
        await RestoreAsync(token);
        await gate.WaitAsync(token);
        try {
            await using var db = AniTDatabase.Create(databasePath);
            var files = await db.MediaFiles.Where(f => f.OnlineTorrentId != null).Include(f => f.Episode)!.ThenInclude(e => e!.Season).ToListAsync(token);
            var result = new List<OnlineTransfer>();
            foreach (var (id, (job, manager)) in downloads) {
                if (!manager.HasMetadata) { result.Add(new(id, job.Title, "Buscando metadados e participantes…", 0, 0, manager.OpenConnections, job.Paused, [])); continue; }
                if (!prepared.Contains(id)) {
                    await SelectSafeFilesAsync(manager, job.SavePath, job.SelectedFileIndex);
                    await ImportAsync(job, manager, token);
                    prepared.Add(id);
                }
                var episodes = new List<OnlineEpisode>();
                foreach (var file in files.Where(f => f.OnlineTorrentId == id)) {
                    if (file.OnlineFileIndex < 0 || file.OnlineFileIndex >= manager.Files.Count) continue;
                    var torrentFile = manager.Files[file.OnlineFileIndex];
                    var complete = torrentFile.BitField.AllTrue && File.Exists(torrentFile.DownloadCompleteFullPath);
                    if (complete) { file.IsCloud = false; file.RelativePath = Path.GetRelativePath(job.SavePath, torrentFile.DownloadCompleteFullPath); }
                    episodes.Add(new(file.EpisodeId, $"S{file.Episode!.Season!.Number:00}E{file.Episode.Number:00}", file.FileName, torrentFile.BitField.PercentComplete, file.SizeInBytes, file.IsCloud, job.SelectedFileIndex == file.OnlineFileIndex));
                }
                var selectedFile = job.SelectedFileIndex is { } selectedIndex ? manager.Files[selectedIndex] : null;
                var waiting = selectedFile is null || selectedFile.BitField.AllTrue;
                var state = manager.Error is not null ? "Erro: " + manager.Error.Reason
                    : selectedFile is null ? "Abra um episódio para começar · nenhum vídeo baixando"
                    : selectedFile.BitField.AllTrue ? "Episódio concluído · abra outro quando quiser"
                    : job.Paused ? "Download do episódio pausado"
                    : manager.OpenConnections == 0 ? "Aguardando fontes para o episódio selecionado…" : "Baixando somente o episódio selecionado";
                result.Add(new(id, job.Title, state, selectedFile?.BitField.PercentComplete ?? 0, manager.Monitor.DownloadRate,
                    manager.OpenConnections, job.Paused, episodes, waiting));
                if (selectedFile is not null && manager.Files.Where(f => f.Priority != Priority.DoNotDownload).All(f => f.BitField.AllTrue)
                    && manager.State is TorrentState.Seeding or TorrentState.Downloading) await manager.StopAsync();
            }
            await db.SaveChangesAsync(token);
            return result;
        } finally { gate.Release(); }
    }

    public async Task SetPausedAsync(string id, bool paused, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try {
            var (job, manager) = downloads[id];
            if (!paused && job.SelectedFileIndex is null) throw new InvalidOperationException("Abra um episódio para iniciar o download.");
            if (paused) { if (manager.State is not TorrentState.Stopped) await manager.PauseAsync(); } else await manager.StartAsync();
            downloads[id] = (job with { Paused = paused }, manager);
            savedJobs[id] = job with { Paused = paused };
            await SaveAsync(token);
        } finally { gate.Release(); }
    }

    public async Task<Uri> OpenStreamAsync(MediaFile file, CancellationToken token = default, IProgress<OnlinePreparationProgress>? progress = null)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        token = linked.Token;
        await RestoreAsync(token);
        await streamGate.WaitAsync(token);
        try {
            TorrentManager manager;
            await gate.WaitAsync(token);
            try {
                if (!downloads.TryGetValue(file.OnlineTorrentId!, out var entry)) throw new InvalidOperationException("O download não foi encontrado neste perfil.");
                if (stream is not null) { await stream.DisposeAsync(); stream = null; }
                foreach (var (id, other) in downloads.ToArray()) {
                    if (other.Manager.State is not TorrentState.Stopped) await other.Manager.StopAsync();
                    await SelectSafeFilesAsync(other.Manager, other.Job.SavePath, null);
                    var inactive = other.Job with { Paused = true, SelectedFileIndex = null };
                    downloads[id] = (inactive, other.Manager); savedJobs[id] = inactive;
                }
                manager = entry.Manager;
                await SelectSafeFilesAsync(manager, entry.Job.SavePath, file.OnlineFileIndex);
                var selected = entry.Job with { Paused = false, SelectedFileIndex = file.OnlineFileIndex };
                downloads[selected.Id] = (selected, manager); savedJobs[selected.Id] = selected;
                await SaveAsync(token);
                await manager.StartAsync();
            } finally { gate.Release(); }
            var media = manager.Files[file.OnlineFileIndex];
            using var reporting = CancellationTokenSource.CreateLinkedTokenSource(token);
            var reportTask = Task.Run(async () => {
                while (!reporting.IsCancellationRequested) {
                    progress?.Report(new OnlinePreparationProgress(manager.Monitor.DataBytesReceived, manager.Monitor.DownloadRate,
                        manager.OpenConnections, media.BitField.PercentComplete, manager.Error?.Reason.ToString()));
                    try { await Task.Delay(500, reporting.Token); } catch (OperationCanceledException) { break; }
                }
            });
            try {
                var input = await manager.StreamProvider!.CreateStreamAsync(media, true, token);
                stream = new OnlineStreamServer(input, Path.GetFileName(media.Path)) { Diagnostic = message => Diagnostic?.Invoke(message) };
                return stream.Address;
            } catch {
                await manager.PauseAsync();
                await gate.WaitAsync(CancellationToken.None);
                try {
                    var entry = downloads[file.OnlineTorrentId!];
                    downloads[entry.Job.Id] = (entry.Job with { Paused = true }, manager);
                    savedJobs[entry.Job.Id] = entry.Job with { Paused = true }; await SaveAsync(CancellationToken.None);
                } finally { gate.Release(); }
                throw;
            } finally { reporting.Cancel(); await reportTask; }
        } finally { streamGate.Release(); }
    }

    public async Task PausePlaybackAsync(Uri? expectedAddress = null)
    {
        await streamGate.WaitAsync();
        try {
            if (expectedAddress is not null && stream?.Address != expectedAddress) return;
            await gate.WaitAsync();
            try {
                if (stream is not null) { await stream.DisposeAsync(); stream = null; }
                foreach (var (id, entry) in downloads.ToArray()) {
                    if (entry.Manager.State is not TorrentState.Stopped) await entry.Manager.PauseAsync();
                    var paused = entry.Job with { Paused = true };
                    downloads[id] = (paused, entry.Manager); savedJobs[id] = paused;
                }
                await SaveAsync(CancellationToken.None);
            } finally { gate.Release(); }
        } finally { streamGate.Release(); }
    }

    async Task SaveAsync(CancellationToken token)
    {
        var temp = statePath + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(savedJobs.Values), token);
        File.Move(temp, statePath, true);
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        await streamGate.WaitAsync();
        await gate.WaitAsync();
        try {
            if (stream is not null) await stream.DisposeAsync();
            foreach (var (_, manager) in downloads.Values) await manager.StopAsync();
        } finally { engine.Dispose(); gate.Release(); streamGate.Release(); lifetime.Dispose(); }
    }
}

public sealed record OnlinePreparationProgress(long ReceivedBytes, long BytesPerSecond, int Peers, double Percent, string? Error);

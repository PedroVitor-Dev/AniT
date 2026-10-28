using System.Net;
using System.Text.Json;
using AniT.Core;
using Microsoft.EntityFrameworkCore;
using MonoTorrent;
using MonoTorrent.Client;

namespace AniT.Infrastructure;

public sealed record OnlineEpisode(Guid EpisodeId, string Title, string FileName, double Progress, long Size, bool IsCloud);
public sealed record OnlineTransfer(string Id, string Name, string State, double Progress, long DownloadSpeed, int Peers, bool Paused, IReadOnlyList<OnlineEpisode> Episodes);
public sealed record OnlineJob(string Id, string Source, string SavePath, string Title, bool Paused, DateTimeOffset AcceptedAt);

/// <summary>One lazy, profile-scoped engine. Completed files keep their original episode identity.</summary>
public sealed class OnlineAnimeService : IAsyncDisposable
{
    readonly string databasePath;
    readonly string statePath;
    readonly ClientEngine engine;
    readonly SemaphoreSlim gate = new(1, 1);
    readonly Dictionary<string, (OnlineJob Job, TorrentManager Manager)> downloads = new();
    readonly CancellationTokenSource lifetime = new();
    OnlineStreamServer? stream;
    bool restored;
    readonly Dictionary<string, OnlineJob> savedJobs = new();
    readonly HashSet<string> prepared = new();
    public string? LastError { get; private set; }

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
                    downloads.Add(job.Id, (job, manager));
                    if (!job.Paused) await manager.StartAsync();
                } catch (Exception ex) when (ex is not OperationCanceledException) { LastError = $"Não foi possível retomar {job.Title}: {ex.Message}"; }
            }
        } finally { gate.Release(); }
    }

    async Task<TorrentManager> AddManagerAsync(OnlineJob job, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        TorrentManager manager;
        if (job.Source.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            manager = await engine.AddStreamingAsync(MagnetLink.Parse(job.Source), job.SavePath);
        else manager = await engine.AddStreamingAsync(await Torrent.LoadAsync(job.Source), job.SavePath);
        if (manager.HasMetadata) await SelectSafeFilesAsync(manager, job.SavePath);
        return manager;
    }

    static async Task SelectSafeFilesAsync(TorrentManager manager, string savePath)
    {
        foreach (var file in manager.Files) {
            ContainedPath(savePath, Path.GetRelativePath(savePath, file.DownloadCompleteFullPath));
            var ext = Path.GetExtension(file.Path).ToLowerInvariant();
            await manager.SetFilePriorityAsync(file, IsVideo(file.Path) || ext is ".srt" or ".ass" or ".ssa" or ".vtt" ? Priority.Normal : Priority.DoNotDownload);
        }
    }

    public async Task<string> AddAsync(string source, string destination, string title, bool authorized, CancellationToken token = default)
    {
        if (!authorized) throw new InvalidOperationException("Confirme que possui autorização para baixar e compartilhar este conteúdo.");
        if (string.IsNullOrWhiteSpace(destination) || !Directory.Exists(destination)) throw new DirectoryNotFoundException("Escolha uma pasta existente para os episódios.");
        source = source.Trim();
        if (source.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)) _ = MagnetLink.Parse(source);
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
            var job = new OnlineJob(id, source, Path.Combine(Path.GetFullPath(destination), "AniT Online", id), title.Trim(), false, DateTimeOffset.UtcNow);
            Directory.CreateDirectory(job.SavePath);
            manager = await AddManagerAsync(job, token);
            if (downloads.Values.Any(d => d.Manager.InfoHashes.Equals(manager.InfoHashes))) throw new InvalidOperationException("Este torrent já foi adicionado.");
            await manager.StartAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            await manager.WaitForMetadataAsync(timeout.Token);
            await SelectSafeFilesAsync(manager, job.SavePath);
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
                    await SelectSafeFilesAsync(manager, job.SavePath);
                    await ImportAsync(job, manager, token);
                    prepared.Add(id);
                }
                var episodes = new List<OnlineEpisode>();
                foreach (var file in files.Where(f => f.OnlineTorrentId == id)) {
                    if (file.OnlineFileIndex < 0 || file.OnlineFileIndex >= manager.Files.Count) continue;
                    var torrentFile = manager.Files[file.OnlineFileIndex];
                    var complete = torrentFile.BitField.AllTrue && File.Exists(torrentFile.DownloadCompleteFullPath);
                    if (complete) { file.IsCloud = false; file.RelativePath = Path.GetRelativePath(job.SavePath, torrentFile.DownloadCompleteFullPath); }
                    episodes.Add(new(file.EpisodeId, $"S{file.Episode!.Season!.Number:00}E{file.Episode.Number:00}", file.FileName, torrentFile.BitField.PercentComplete, file.SizeInBytes, file.IsCloud));
                }
                result.Add(new(id, job.Title, manager.Error is not null ? "Erro: " + manager.Error.Reason : job.Paused ? "Pausado" : episodes.All(e => !e.IsCloud) ? "Download concluído" : manager.OpenConnections == 0 ? "Aguardando participantes com os arquivos…" : "Baixando · pronto para assistir", manager.PartialProgress, manager.Monitor.DownloadRate, manager.OpenConnections, job.Paused, episodes));
                // No background seeding once all selected videos and subtitles have finished.
                if (manager.Files.Where(f => f.Priority != Priority.DoNotDownload).All(f => f.BitField.AllTrue) && manager.State is TorrentState.Seeding or TorrentState.Downloading) await manager.StopAsync();
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
            if (paused) await manager.PauseAsync(); else await manager.StartAsync();
            downloads[id] = (job with { Paused = paused }, manager);
            savedJobs[id] = job with { Paused = paused };
            await SaveAsync(token);
        } finally { gate.Release(); }
    }

    public async Task<Uri> OpenStreamAsync(MediaFile file, CancellationToken token = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        token = linked.Token;
        await RestoreAsync(token);
        await gate.WaitAsync(token);
        try {
            if (!downloads.TryGetValue(file.OnlineTorrentId!, out var entry)) throw new InvalidOperationException("O download não foi encontrado neste perfil. Abra Online Anime Player para verificar.");
            await entry.Manager.StartAsync();
            await entry.Manager.WaitForMetadataAsync(token);
            await SelectSafeFilesAsync(entry.Manager, entry.Job.SavePath);
            if (stream is not null) { await stream.DisposeAsync(); stream = null; }
            var media = entry.Manager.Files[file.OnlineFileIndex];
            var input = await entry.Manager.StreamProvider!.CreateStreamAsync(media, true, token);
            stream = new OnlineStreamServer(input, Path.GetFileName(media.Path));
            downloads[entry.Job.Id] = (entry.Job with { Paused = false }, entry.Manager);
            savedJobs[entry.Job.Id] = entry.Job with { Paused = false };
            await SaveAsync(token);
            return stream.Address;
        } finally { gate.Release(); }
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
        await gate.WaitAsync();
        try {
            if (stream is not null) await stream.DisposeAsync();
            foreach (var (_, manager) in downloads.Values) await manager.StopAsync();
        } finally { engine.Dispose(); gate.Release(); lifetime.Dispose(); }
    }
}

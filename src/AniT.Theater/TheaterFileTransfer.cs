using System.Security.Cryptography;

namespace AniT.Theater;

public sealed partial class TheaterRoom
{
    private string? sharedPath;
    private volatile bool localHasMedia, remoteHasMedia;
    private long transferredBytes, progressNotifiedAt;
    private DownloadReceiver? receiver;
    private int downloading;
    public bool HasMedia => localHasMedia;
    public bool RemoteHasMedia => IsHost ? EveryGuest(p => p.HasMedia) : remoteHasMedia;
    public bool SharingAvailable { get; private set; }
    public string OfferedName { get; private set; } = "episodio.mkv";
    public MediaIdentity Episode => media;
    public long TransferredBytes => Interlocked.Read(ref transferredBytes);
    public bool IsDownloading => Volatile.Read(ref downloading) != 0;

    private static string SafeName(string? value)
    {
        // Only a display/default filename crosses the wire, never a host path.
        if (string.IsNullOrWhiteSpace(value) || value.Length > 120 || value.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c))) return "episodio.mkv";
        var extension = Path.GetExtension(value).ToLowerInvariant();
        return extension is ".mkv" or ".mp4" or ".avi" or ".webm" or ".mov" or ".m4v" or ".ts" or ".m2ts" or ".wmv" ? value.TrimEnd('.', ' ') : "episodio.mkv";
    }

    /// <summary>Receives into a unique partial file, verifies all bytes and atomically publishes without overwriting an existing file.</summary>
    public async Task<string> DownloadAsync(string destination, CancellationToken cancellationToken = default)
    {
        if (Mode != TheaterMode.PeerSync || IsHost || !SharingAvailable || !PreparationStarted || HasMedia || !IsConnected) throw new InvalidOperationException("O download não está disponível nesta sala.");
        if (Interlocked.CompareExchange(ref downloading, 1, 0) != 0) throw new InvalidOperationException("Já existe um download em andamento.");
        string? partial = null;
        DownloadReceiver? receiving = null;
        bool requested = false;
        using var active = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        try
        {
            var final = Path.GetFullPath(destination);
            if (File.Exists(final) || Directory.Exists(final)) throw new IOException("Já existe um arquivo nesse destino. Escolha outro nome.");
            var parent = Path.GetDirectoryName(final)!;
            if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("Selecione uma pasta existente.");
            var drive = new DriveInfo(Path.GetPathRoot(final)!);
            if (drive.IsReady && drive.AvailableFreeSpace < media.Length) throw new IOException("Não há espaço livre suficiente para baixar o episódio.");
            partial = Path.Combine(parent, ".anit-part-" + Guid.NewGuid().ToString("N"));
            receiving = new DownloadReceiver(new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan));
            receiver = receiving;
            Interlocked.Exchange(ref transferredBytes, 0);
            var link = peer ?? throw new IOException("Host desconectado.");
            requested = true;
            await link.Channel.SendAsync(new("download"), active.Token).ConfigureAwait(false);
            Interlocked.Exchange(ref transferAt, ClockEstimator.NowMs);
            await receiving.Completed.Task.WaitAsync(active.Token).ConfigureAwait(false);
            await ReportDownloadAsync(EpisodeTransferPhase.Verifying, active.Token, true).ConfigureAwait(false);
            await receiving.File.FlushAsync(active.Token).ConfigureAwait(false);
            await receiving.File.DisposeAsync().ConfigureAwait(false);
            if (!media.Matches(await MediaIdentity.FromFileAsync(partial, active.Token).ConfigureAwait(false))) throw new InvalidDataException("A verificação do download falhou. Nenhum arquivo foi liberado para reprodução.");
            active.Token.ThrowIfCancellationRequested();
            File.Move(partial, final, overwrite: false);
            partial = null;
            localHasMedia = true; Interlocked.Exchange(ref transferFinishedAt, ClockEstimator.NowMs);
            Volatile.Write(ref transferProgress, new(media.Length, EpisodeTransferPhase.Verified));
            await link.Channel.SendAsync(new("file-verified", Media: media), active.Token).ConfigureAwait(false);
            Changed?.Invoke();
            return final;
        }
        catch
        {
            // Stop the reader before disposing its output stream. Cancellation leaves the room,
            // allowing a clean rejoin rather than reusing any partially received protocol state.
            if (requested) { peer?.Dispose(); try { await background.ConfigureAwait(false); } catch (Exception) { } }
            throw;
        }
        finally
        {
            receiver = null;
            if (receiving is not null) await receiving.File.DisposeAsync().ConfigureAwait(false);
            if (partial is not null) { try { File.Delete(partial); } catch (IOException) { } }
            Interlocked.Exchange(ref downloading, 0);
            Changed?.Invoke();
        }
    }

    private async Task ReceiveChunkAsync(RoomPacket packet, CancellationToken cancellationToken)
    {
        var receiving = receiver ?? throw new InvalidDataException("Download não solicitado.");
        if (receiving.Completed.Task.IsCompleted || packet.Data is not { Length: > 0 and <= 4096 } data || packet.Offset != receiving.Bytes || data.Length > media.Length - receiving.Bytes)
            throw new InvalidDataException("Bloco de download inválido.");
        await receiving.File.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        receiving.Bytes += data.Length;
        ReportProgress(receiving.Bytes);
        await ReportDownloadAsync(EpisodeTransferPhase.Receiving, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendFileAsync(Peer link, CancellationToken cancellationToken)
    {
        try
        {
            await using var file = new FileStream(sharedPath!, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (file.Length != media.Length) throw new IOException("O arquivo compartilhado mudou.");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[4096]; long offset = 0;
            int count;
            while ((count = await file.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (link.Removed) return;
                hash.AppendData(buffer, 0, count);
                await link.Channel.SendAsync(new("chunk", Data: buffer[..count], Offset: offset), cancellationToken).ConfigureAwait(false);
                offset += count; ReportProgress(offset);
            }
            if (offset != media.Length || !media.Matches(new(offset, Convert.ToHexString(hash.GetHashAndReset())))) throw new IOException("O conteúdo compartilhado mudou. Crie outra sala.");
            link.FileSent = true;
            await link.Channel.SendAsync(new("file-end", Offset: offset), cancellationToken).ConfigureAwait(false);
            Changed?.Invoke();
        }
        catch (Exception)
        {
            Notice?.Invoke("O envio do episódio foi interrompido. Confira o arquivo e peça ao participante para entrar novamente.");
            link.Dispose();
        }
    }

    private void ReportProgress(long bytes)
    {
        Interlocked.Exchange(ref transferredBytes, bytes);
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref progressNotifiedAt) < 200 && bytes != media.Length) return;
        Interlocked.Exchange(ref progressNotifiedAt, now);
        Changed?.Invoke();
    }

    private sealed class DownloadReceiver(FileStream file)
    {
        public FileStream File { get; } = file;
        public long Bytes;
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AniT.Theater;

/// <summary>Host-authoritative room for up to four guests. TLS is pinned by the invitation; only the explicitly shared episode can be transferred.</summary>
public sealed partial class TheaterRoom : IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private MediaIdentity media;
    private TcpListener? listener;
    private X509Certificate2? certificate;
    private Task background = Task.CompletedTask;
    private readonly SemaphoreSlim admissionGate = new(1, 1), connectionSlots = new(20, 20);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, Task> inbound = new();
    private int connectionSequence, connectedGuestCount;
    private volatile Peer? peer; // Guest's host connection only.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, Peer> guests = new();
    private Peer[] ActiveGuests => guests.Values.Where(p => !p.Removed).ToArray();
    public const int MaximumGuests = 4;
    private bool EveryGuest(Func<Peer, bool> predicate) { var members = ActiveGuests; return members.Length > 0 && members.All(predicate); }
    private async Task BroadcastAsync(RoomPacket packet, CancellationToken token)
    {
        var targets = IsHost ? ActiveGuests : peer is { } host ? new[] { host } : Array.Empty<Peer>();
        await Task.WhenAll(targets.Select(async target => { try { await target.Channel.SendAsync(packet, token).ConfigureAwait(false); } catch { target.Dispose(); throw; } })).ConfigureAwait(false);
    }
    private volatile bool localReady;
    private volatile bool remoteReady;
    private long revision;
    private long receivedRevision;
    private int disposed;
    private readonly TaskCompletionSource disposalComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TimelineState? latestState;

    private TheaterRoom(bool host, MediaIdentity media) { IsHost = host; this.media = media; localHasMedia = media.IsValid; }
    internal Exception? LastError { get; private set; }
    public bool IsHost { get; }
    public TheaterInvite? Invite { get; private set; }
    public bool IsConnected => IsHost ? Volatile.Read(ref connectedGuestCount) > 0 : peer is not null;
    public bool LocalReady => localReady;
    public bool RemoteReady => IsHost ? EveryGuest(p => p.Ready) : remoteReady && remoteRoster.Length >= 2 && remoteRoster.All(p => p.Id == localParticipantId || p.Ready);
    public bool BothReady => IsConnected && (Mode == TheaterMode.DirectStream || HasMedia && RemoteHasMedia) && LocalReady && RemoteReady;
    public TheaterMode Mode { get; private set; }
    public bool CanPrepare => HasMedia || Mode == TheaterMode.DirectStream && streamProxy is not null;
    public ClockEstimator Clock { get; } = new();
    public TimelineState? LatestState => Volatile.Read(ref latestState);
    public event Action? Changed;
    public event Action<string>? Notice;

    public static TheaterRoom Host(MediaIdentity media, IPAddress advertisedAddress, int port = 0, string? sharedFilePath = null, TheaterMode mode = TheaterMode.PeerSync, string? displayName = null)
    {
        if (!media.IsValid || advertisedAddress.AddressFamily != AddressFamily.InterNetwork || advertisedAddress.Equals(IPAddress.Any) || port is < 0 or > 65535)
            throw new ArgumentException("Arquivo, endereço IPv4 ou porta inválidos.");
        if (!Enum.IsDefined(mode) || mode == TheaterMode.DirectStream && sharedFilePath is null) throw new ArgumentException("Autorize o arquivo para transmitir nesta sala.");
        var room = new TheaterRoom(true, media) { Mode = mode, localDisplayName = ParticipantName(displayName, "Host") };
        room.hostParticipantId = room.localParticipantId;
        if (sharedFilePath is not null)
        {
            var info = new FileInfo(Path.GetFullPath(sharedFilePath));
            if (!info.Exists || info.Length != media.Length) throw new IOException("O arquivo autorizado não está disponível.");
            room.sharedPath = info.FullName; room.OfferedName = SafeName(info.Name); room.SharingAvailable = true;
        }
        try
        {
            if (mode == TheaterMode.DirectStream) room.streamFile = new FileStream(room.sharedPath!, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.RandomAccess);
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var generated = new CertificateRequest("CN=AniT Movie Theater", key, HashAlgorithmName.SHA256)
                .CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            // Schannel cannot use CreateSelfSigned ephemeral private keys directly.
            var pfx = generated.Export(X509ContentType.Pfx);
            try { room.certificate = X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.UserKeySet); }
            finally { CryptographicOperations.ZeroMemory(pfx); }
            room.listener = new TcpListener(IPAddress.IsLoopback(advertisedAddress) ? IPAddress.Loopback : IPAddress.Any, port);
            room.listener.Start(20);
            room.Invite = new(advertisedAddress, ((IPEndPoint)room.listener.LocalEndpoint).Port, TheaterInvite.NewToken(), Convert.ToHexString(SHA256.HashData(room.certificate.RawData)));
            room.background = room.AcceptLoopAsync();
            return room;
        }
        catch { room.listener?.Stop(); room.certificate?.Dispose(); room.streamFile?.Dispose(); room.lifetime.Dispose(); throw; }
    }

    public static async Task<TheaterRoom> JoinAsync(string invitation, MediaIdentity? media = null, CancellationToken cancellationToken = default, string? displayName = null)
    {
        if (media is not null && !media.IsValid) throw new ArgumentException("Arquivo inválido.");
        var invite = TheaterInvite.Parse(invitation);
        var room = new TheaterRoom(false, media ?? new MediaIdentity(0, string.Empty)) { localDisplayName = ParticipantName(displayName, "Convidado") };
        Peer? link = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            link = await ConnectTlsPeerAsync(invite, timeout.Token).ConfigureAwait(false);
            await link.Channel.SendAsync(new("hello", Token: invite.Token, Media: media, DisplayName: room.localDisplayName), timeout.Token).ConfigureAwait(false);
            var welcome = await link.Channel.ReadAsync(timeout.Token).ConfigureAwait(false);
            if (welcome.Type == "reject") throw new InvalidDataException(welcome.Error switch
            {
                "media" => "Os arquivos são diferentes. Selecione a mesma versão do episódio do host.",
                "busy" => "A sala está cheia: até quatro convidados podem participar.",
                _ => "A sala recusou o convite. Peça um novo ao host."
            });
            if (welcome.Type != "welcome" || !Enum.IsDefined(welcome.Mode) || welcome.Mode == TheaterMode.DirectStream && !welcome.Sharing || welcome.Media is not { IsValid: true } || (!welcome.Sharing && !welcome.Media.Matches(media))) throw new InvalidDataException("Resposta de sala incompatível.");
            if (welcome.ParticipantId == Guid.Empty || welcome.HostId == Guid.Empty || welcome.ParticipantId == welcome.HostId) throw new InvalidDataException("Identificação da sala inválida.");
            room.localParticipantId = welcome.ParticipantId; room.hostParticipantId = welcome.HostId;
            room.hostDisplayName = ParticipantName(welcome.DisplayName, "Host");
            room.Mode = welcome.Mode; room.media = welcome.Media; room.localHasMedia = welcome.Mode == TheaterMode.PeerSync && welcome.Media.Matches(media);
            room.remoteHasMedia = true; room.SharingAvailable = welcome.Sharing; room.OfferedName = SafeName(welcome.Name);
            if (welcome.Mode == TheaterMode.DirectStream)
            {
                if (welcome.Token is not { Length: 64 } key || !key.All(Uri.IsHexDigit)) throw new InvalidDataException("Chave do canal de vídeo inválida.");
                link.MediaToken = key;
                room.hostInvite = invite;
            }
            room.hostInvite = invite;
            link.VoiceToken = welcome.VoiceToken ?? throw new InvalidDataException("Chave de voz ausente.");
            room.peer = link;
            room.remoteReady = welcome.Ready;
            if (welcome.PreparationAt < 0) throw new InvalidDataException("Preparação inválida.");
            room.preparationAt = welcome.PreparationAt;
            room.ReceiveRoster(welcome);
            room.background = room.RunPeerAsync(link);
            return room;
        }
        catch { link?.Dispose(); await room.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public async Task SetReadyAsync(bool ready, CancellationToken cancellationToken = default)
    {
        if (ready && !CanPrepare) throw new InvalidOperationException("Baixe e verifique o episódio antes de preparar a reprodução.");
        if (!IsHost && ready && (!IsConnected || !Clock.IsReady)) throw new InvalidOperationException("Aguarde a medição da conexão antes de preparar a reprodução.");
        if (ready && Mode == TheaterMode.PeerSync && !PreparationStarted) throw new InvalidOperationException("Aguarde o host iniciar a preparação.");
        if (!ready) { InvalidatePreparationApproval(); ClearStreamBuffering(); }
        localReady = ready;
        await BroadcastAsync(new("ready", Ready: ready), cancellationToken).ConfigureAwait(false);
        await BroadcastRosterAsync().ConfigureAwait(false);
        Changed?.Invoke();
    }

    public async Task PublishAsync(double positionSeconds, bool playing, CancellationToken cancellationToken = default, long? effectiveHostTimeMs = null, int? audioTrack = null, int? subtitleTrack = null)
    {
        if (!IsHost || !BothReady || !PlaybackAuthorized) throw new InvalidOperationException("Todos os participantes precisam estar prontos.");
        if (playing && StreamHold) throw new InvalidOperationException("A sessão aguarda o buffer e a retomada do host.");
        var state = new TimelineState(Interlocked.Increment(ref revision), positionSeconds, playing, effectiveHostTimeMs ?? ClockEstimator.NowMs, audioTrack, subtitleTrack, BufferHold: StreamHold, BufferResumeRevision: Interlocked.Read(ref bufferResumeRevision));
        if (!state.IsValid) throw new ArgumentException("Posição de reprodução inválida.");
        await Task.WhenAll(ActiveGuests.Select(link => link.Channel.SendAsync(new("state", State: state with { BufferResumeRevision = link.Buffer?.Waiting == false ? link.Buffer.Revision : 0 }), cancellationToken))).ConfigureAwait(false);
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var client = await listener!.AcceptTcpClientAsync(lifetime.Token).ConfigureAwait(false);
                if (!connectionSlots.Wait(0)) { client.Dispose(); continue; }
                var id = Interlocked.Increment(ref connectionSequence);
                var task = HandleConnectionAsync(client); inbound[id] = task;
                _ = RemoveConnectionAsync(id, task);
            }
        }
        catch (Exception) when (lifetime.IsCancellationRequested) { }
    }

    private async Task RemoveConnectionAsync(int id, Task task)
    {
        try { await task.ConfigureAwait(false); }
        finally { inbound.TryRemove(id, out _); }
    }

    private async Task HandleConnectionAsync(TcpClient client)
    {
        Peer? link = null;
        SslStream? tls = null;
        try
        {
            client.NoDelay = true;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            tls = new SslStream(client.GetStream(), false);
            await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, timeout.Token).ConfigureAwait(false);
            link = new Peer(client, new PacketChannel(tls));
            var hello = await link.Channel.ReadAsync(timeout.Token).ConfigureAwait(false);
            if (hello.Type == "voice-hello")
            {
                await AcceptVoiceChannelAsync(link, hello.Token, timeout.Token).ConfigureAwait(false); return;
            }
            if (hello.Type == "media-hello")
            {
                await AcceptMediaChannelAsync(link, hello.Token, timeout.Token).ConfigureAwait(false);
                return;
            }
            if (hello.Type != "hello" || !Invite!.AcceptsToken(hello.Token))
            {
                await link.Channel.SendAsync(new("reject", Error: "invite"), timeout.Token).ConfigureAwait(false);
                Notice?.Invoke("Uma conexão foi recusada: convite inválido.");
                return;
            }
            await admissionGate.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                if (!Invite!.AcceptsToken(hello.Token)) { await link.Channel.SendAsync(new("reject", Error: "invite"), timeout.Token).ConfigureAwait(false); return; }
                if (ActiveGuests.Length >= MaximumGuests)
                {
                    await link.Channel.SendAsync(new("reject", Error: "busy"), timeout.Token).ConfigureAwait(false);
                    return;
                }
                if (!media.Matches(hello.Media) && !SharingAvailable)
                {
                    await link.Channel.SendAsync(new("reject", Error: "media"), timeout.Token).ConfigureAwait(false);
                    Notice?.Invoke("O participante selecionou um arquivo diferente. A sala continua aguardando.");
                    return;
                }
                link.DisplayName = ParticipantName(hello.DisplayName, "Convidado");
                link.HasMedia = media.Matches(hello.Media);
                link.Progress = new(link.HasMedia ? media.Length : 0, link.HasMedia ? EpisodeTransferPhase.Verified : EpisodeTransferPhase.Waiting);
                // Joining locks group playback until every member is prepared again.
                playbackAuthorized = false;
                await link.Channel.SendAsync(new("welcome", Token: Mode == TheaterMode.DirectStream ? link.MediaToken : null, VoiceToken: link.VoiceToken, Media: media, Ready: localReady, Sharing: SharingAvailable, Name: OfferedName, Mode: Mode, PreparationAt: PreparationStartedAtMs, ParticipantId: link.ParticipantId, HostId: localParticipantId, DisplayName: localDisplayName, Roster: Participants.Append(SnapshotPeer(link)).ToArray(), Request: Interlocked.Increment(ref rosterRevision)), timeout.Token).ConfigureAwait(false);
                guests[link.ParticipantId] = link;
                Volatile.Write(ref connectedGuestCount, guests.Count);
            }
            finally { admissionGate.Release(); }
            await RunPeerAsync(link).ConfigureAwait(false);
        }
        catch (Exception) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { LastError = error; Notice?.Invoke("A conexão não foi concluída. A sala continua aguardando."); }
        finally { if (link is null) tls?.Dispose(); else link.Dispose(); client.Dispose(); connectionSlots.Release(); }
    }

    private async Task RunPeerAsync(Peer link)
    {
        using var active = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var heartbeat = IsHost ? Task.CompletedTask : PingLoopAsync(link, active.Token);
        Task transfer = Task.CompletedTask; bool requested = false;
        try
        {
            if (IsHost) { await link.Channel.SendAsync(new("ready", Ready: localReady), active.Token).ConfigureAwait(false); await BroadcastRosterAsync().ConfigureAwait(false); }
            Changed?.Invoke();
            while (!active.IsCancellationRequested)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(active.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var packet = await link.Channel.ReadAsync(timeout.Token).ConfigureAwait(false);
                if (link.Removed && packet.Type != "removed-ack") continue;
                switch (packet.Type)
                {
                    case "roster" when !IsHost:
                        ReceiveRoster(packet); break;
                    case "voice-policy" when !IsHost:
                        ReceiveVoicePolicy(packet);
                        break;
                    case "removed-ack" when IsHost && link.Removed:
                        link.RemovalAcknowledged.TrySetResult();
                        break;
                    case "removed" when !IsHost:
                        ExitReason = TheaterExitReason.RemovedByHost;
                        await link.Channel.SendAsync(new("removed-ack"), active.Token).ConfigureAwait(false);
                        Notice?.Invoke("Você foi removido da sala pelo host. Peça um novo convite para entrar novamente.");
                        return;
                    case "ready":
                        if (IsHost && packet.Ready && Mode == TheaterMode.PeerSync && !link.HasMedia) throw new InvalidDataException("O participante ainda não verificou o arquivo.");
                        if (Mode == TheaterMode.PeerSync && packet.Ready && !PreparationStarted) throw new InvalidDataException("Preparação não liberada.");
                        if (!packet.Ready) { InvalidatePreparationApproval(); ClearStreamBuffering(); }
                        if (IsHost) { link.Ready = packet.Ready; if (!packet.Ready) link.Confirmed = false; } else remoteReady = packet.Ready;
                        await BroadcastRosterAsync().ConfigureAwait(false);
                        Changed?.Invoke();
                        break;
                    case "download" when IsHost && Mode == TheaterMode.PeerSync && PreparationStarted && SharingAvailable && !requested && !link.HasMedia && !link.Ready:
                        requested = true;
                        transfer = SendFileAsync(link, active.Token);
                        break;
                    case "chunk" when !IsHost && Mode == TheaterMode.PeerSync:
                        await ReceiveChunkAsync(packet, active.Token).ConfigureAwait(false);
                        break;
                    case "file-end" when !IsHost && Mode == TheaterMode.PeerSync:
                        var receiving = receiver ?? throw new InvalidDataException("Download não solicitado.");
                        if (receiving.Bytes != media.Length || packet.Offset != media.Length) throw new InvalidDataException("Download incompleto.");
                        receiving.Completed.TrySetResult();
                        break;
                    case "file-verified" when IsHost && Mode == TheaterMode.PeerSync && requested && link.FileSent && media.Matches(packet.Media):
                        link.HasMedia = true; Interlocked.Exchange(ref transferFinishedAt, ClockEstimator.NowMs); link.Progress = new(media.Length, EpisodeTransferPhase.Verified); await BroadcastRosterAsync().ConfigureAwait(false); Changed?.Invoke();
                        break;
                    case "prepare" when !IsHost && Mode == TheaterMode.PeerSync:
                        if (packet.Clock <= 0) throw new InvalidDataException("Preparação inválida.");
                        Interlocked.Exchange(ref preparationAt, packet.Clock); Changed?.Invoke();
                        break;
                    case "confirmed" when IsHost && Mode == TheaterMode.PeerSync && PreparationStarted && link.HasMedia && link.Ready:
                        link.Confirmed = true; await BroadcastRosterAsync().ConfigureAwait(false); Changed?.Invoke();
                        break;
                    case "download-progress" when IsHost && Mode == TheaterMode.PeerSync && PreparationStarted && requested && !link.HasMedia:
                        ReceiveDownloadProgress(link, packet.Progress);
                        break;
                    case "stream-connection" when IsHost && Mode == TheaterMode.DirectStream && link.Media is not null:
                        ReceiveConnectionInfo(link, packet);
                        break;
                    case "buffer-status" when IsHost && Mode == TheaterMode.DirectStream && link.Media is not null:
                        ReceiveBufferStatus(link, packet.Buffer);
                        break;
                    case "ping" when IsHost:
                        await link.Channel.SendAsync(new("pong", Clock: ClockEstimator.NowMs, Echo: packet.Clock), active.Token).ConfigureAwait(false);
                        break;
                    case "pong" when !IsHost:
                        Clock.Observe(packet.Echo, ClockEstimator.NowMs, packet.Clock);
                        Changed?.Invoke();
                        break;
                    case "state" when !IsHost:
                        if (packet.State is not { IsValid: true } state) throw new InvalidDataException("Estado inválido.");
                        if (state.Revision > receivedRevision)
                        {
                            if (Mode == TheaterMode.PeerSync && state.Playing && (!PreparationStarted || !GuestConfirmed || !LocalReady)) throw new InvalidDataException("Início sem preparação confirmada.");
                            if (Mode == TheaterMode.PeerSync && state.Playing) { if (!playbackAuthorized) Interlocked.Exchange(ref approvedStartAt, state.HostTimeMs); playbackAuthorized = true; }
                            ObserveHostBufferHold(state);
                            receivedRevision = state.Revision;
                            Volatile.Write(ref latestState, state);
                        }
                        break;
                    default: throw new InvalidDataException("Mensagem incompatível com o papel do participante.");
                }
            }
        }
        catch (Exception) when (active.IsCancellationRequested) { }
        catch (Exception) { if (!link.Removed) Notice?.Invoke("Participante desconectado. A reprodução será pausada."); }
        finally
        {
            if (!IsHost && ExitReason == TheaterExitReason.None) ExitReason = TheaterExitReason.HostDisconnected;
            active.Cancel();
            link.Dispose();
            foreach (var pending in streamBlocks.Values) pending.Complete.TrySetException(new IOException("Streaming desconectado."));
            if (link.Voice is { } voice) { try { await voice.Background.ConfigureAwait(false); } catch { } }
            try { await link.MediaBackground.ConfigureAwait(false); } catch (Exception) { }
            receiver?.Completed.TrySetException(new IOException("A transferência foi interrompida. Entre novamente para tentar baixar."));
            try { await transfer.ConfigureAwait(false); } catch (Exception) { }
            try { await heartbeat.ConfigureAwait(false); } catch (Exception) { }
            await admissionGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (IsHost)
                {
                    guests.TryRemove(link.ParticipantId, out _);
                    playbackAuthorized = false;
                    if (guests.IsEmpty) { ClearStreamBuffering(newSession: true); localReady = false; Interlocked.Exchange(ref preparationAt, 0); InvalidatePreparationApproval(); }
                    // Publish disconnection only after the corresponding room state has been reset.
                    Volatile.Write(ref connectedGuestCount, guests.Count);
                }
                else if (ReferenceEquals(peer, link))
                {
                    peer = null;
                    ClearStreamBuffering(newSession: true);
                    localReady = remoteReady = false; remoteHasMedia = false;
                    Interlocked.Exchange(ref preparationAt, 0); InvalidatePreparationApproval();
                    Volatile.Write(ref latestState, null);
                }
            }
            finally { admissionGate.Release(); }
            link.Ended.TrySetResult();
            await BroadcastRosterAsync().ConfigureAwait(false);
            Changed?.Invoke();
        }
    }

    private static async Task PingLoopAsync(Peer link, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await link.Channel.SendAsync(new("ping", Clock: ClockEstimator.NowMs), cancellationToken).ConfigureAwait(false);
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception) { link.Dispose(); }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0) _ = FinishDisposeAsync();
        return new(disposalComplete.Task);
    }

    private async Task FinishDisposeAsync()
    {
        try
        {
            lifetime.Cancel();
            listener?.Stop();
            peer?.Dispose();
            foreach (var guest in guests.Values) guest.Dispose();
            try { await background.ConfigureAwait(false); } catch (Exception) { }
            try { await Task.WhenAll(inbound.Values).ConfigureAwait(false); } catch (Exception) { }
            await streamStartGate.WaitAsync().ConfigureAwait(false);
            try { if (streamProxy is not null) await streamProxy.DisposeAsync().ConfigureAwait(false); }
            finally { streamStartGate.Release(); }
            try { await Task.WhenAll(streamFetches.Values).ConfigureAwait(false); } catch (Exception) { }
            streamFile?.Dispose();
            certificate?.Dispose();
            lifetime.Dispose();
            disposalComplete.TrySetResult();
        }
        catch (Exception error) { disposalComplete.TrySetException(error); }
    }

    private sealed class Peer(TcpClient client, PacketChannel channel) : IDisposable
    {
        private readonly object mediaLock = new();
        private Peer? media;
        private int disposed;
        public PacketChannel Channel { get; } = channel;
        public string MediaToken { get; set; } = TheaterInvite.NewToken();
        public Peer? Media => Volatile.Read(ref media);
        public Task MediaBackground { get; set; } = Task.CompletedTask;
        public volatile bool FileSent, Ready, HasMedia, Confirmed;
        public EpisodeTransferProgress Progress = new(0, EpisodeTransferPhase.Waiting);
        public StreamBufferStatus? Buffer;
        public StreamConnectionInfo? Connection;
        public long BufferRevision, ConnectionRevision;
        public string VoiceToken { get; set; } = TheaterInvite.NewToken();
        public VoiceLink? Voice;
        public volatile bool Muted = true, Deafened;
        public Guid ParticipantId { get; } = Guid.NewGuid();
        public string DisplayName { get; set; } = "Convidado";
        public volatile bool MicrophoneBlocked, Removed;
        public long VoiceRevision;
        public TaskCompletionSource RemovalAcknowledged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Ended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool AcceptsMediaToken(string? candidate) => candidate is { Length: 64 }
            && CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(MediaToken), System.Text.Encoding.ASCII.GetBytes(candidate));

        public bool TryAttachMedia(Peer connection)
        {
            lock (mediaLock)
            {
                if (disposed != 0 || media is not null) return false;
                media = connection; return true;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            Peer? child;
            lock (mediaLock) child = media;
            client.Dispose(); Channel.Dispose(); child?.Dispose(); Voice?.Dispose();
        }
    }
}

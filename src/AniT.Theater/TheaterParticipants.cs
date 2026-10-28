namespace AniT.Theater;

public enum TheaterExitReason { None, HostDisconnected, RemovedByHost }
public sealed record TheaterParticipant(Guid Id, string DisplayName, bool IsHost, bool Ready, bool HasFile, bool Buffering, bool MicrophoneBlocked, bool VoiceConnected = false, bool Muted = true, bool Deafened = false, bool Speaking = false, int TransferPercent = 0, bool Confirmed = false)
{
    public string SessionLabel => Buffering ? "Aguardando dados do vídeo" : Confirmed ? "Presença confirmada · vídeo pronto" : Ready ? "Vídeo pronto" : HasFile ? "Arquivo verificado · aguardando o player" : TransferPercent > 0 ? $"Baixando e verificando · {TransferPercent}%" : "Conectado · aguardando o vídeo";
    public string RoleLabel => IsHost ? "Host" : "Convidado";
    public string MicrophoneAction => MicrophoneBlocked ? "Liberar microfone" : "Bloquear microfone";
    public string VoiceLabel => !VoiceConnected ? "Fora do chat de voz" : MicrophoneBlocked ? "Microfone bloqueado pelo host" : Deafened ? "Áudio desativado" : Muted ? "Microfone silenciado" : Speaking ? "Falando" : "No chat de voz";
}

public sealed partial class TheaterRoom
{
    private Guid localParticipantId = Guid.NewGuid(), hostParticipantId;
    private string localDisplayName = "Host", hostDisplayName = "Host";
    private volatile bool localMicrophoneBlocked;
    private long receivedVoiceRevision;
    public TheaterExitReason ExitReason { get; private set; }
    public bool MicrophoneBlocked => !IsHost && localMicrophoneBlocked;
    public IReadOnlyList<TheaterParticipant> Participants
    {
        get
        {
            var local = SnapshotLocal();
            if (!IsHost) return remoteRoster.Where(p => p.Id != localParticipantId).Select(p => p with { Speaking = IsSpeaking(p.Id) }).Append(local).OrderByDescending(p => p.IsHost).ToArray();
            return new[] { local }.Concat(ActiveGuests.Select(SnapshotPeer)).ToArray();
        }
    }

    private TheaterParticipant SnapshotPeer(Peer p) => new(p.ParticipantId, p.DisplayName, false, p.Ready, p.HasMedia, p.Buffer?.Waiting == true, p.MicrophoneBlocked, p.Voice is not null, p.Muted, p.Deafened, IsSpeaking(p.ParticipantId), (int)Math.Clamp(100d * p.Progress.Bytes / media.Length, 0, 100), p.Confirmed);
    private TheaterParticipant[] remoteRoster = Array.Empty<TheaterParticipant>();
    private long rosterRevision, receivedRosterRevision;
    private TheaterParticipant SnapshotLocal() => new(localParticipantId, localDisplayName, IsHost, LocalReady, HasMedia, !IsHost && GuestBuffering, MicrophoneBlocked, VoiceEnabled, VoiceMuted, VoiceDeafened, IsSpeaking(localParticipantId), Confirmed: !IsHost && GuestConfirmed);
    private async Task BroadcastRosterAsync()
    {
        if (!IsHost || lifetime.IsCancellationRequested) return;
        var packet = new RoomPacket("roster", Request: Interlocked.Increment(ref rosterRevision), Roster: Participants.ToArray(), Sharing: PlaybackAuthorized);
        await Task.WhenAll(ActiveGuests.Select(async target => { try { await target.Channel.SendAsync(packet, lifetime.Token).ConfigureAwait(false); } catch { target.Dispose(); } })).ConfigureAwait(false);
    }
    private void ReceiveRoster(RoomPacket packet)
    {
        if (packet.Request <= receivedRosterRevision) return;
        if (packet.Roster is not { Length: >= 2 and <= 5 } list || list.Select(p => p.Id).Distinct().Count() != list.Length || list.Count(p => p.IsHost) != 1 || !list.Any(p => p.Id == hostParticipantId && p.IsHost) || !list.Any(p => p.Id == localParticipantId && !p.IsHost)) throw new InvalidDataException("Participantes inválidos.");
        remoteRoster = list.Select(p => p with { DisplayName = ParticipantName(p.DisplayName, "Convidado") }).ToArray();
        foreach (var id in voiceDecoders.Keys.Where(id => !remoteRoster.Any(p => p.Id == id && p.VoiceConnected))) { voiceDecoders.TryRemove(id, out _); speakingUntil.TryRemove(id, out _); }
        receivedRosterRevision = packet.Request; if (Mode == TheaterMode.PeerSync && !packet.Sharing) playbackAuthorized = false; Changed?.Invoke();
    }

    internal static string ParticipantName(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var cleaned = new string(value.Where(c => !char.IsControl(c)).Take(60).ToArray()).Trim();
        return cleaned.Length == 0 ? fallback : cleaned;
    }

    public bool IsVoiceTransmissionAllowed(Guid participantId) => IsHost && guests.TryGetValue(participantId, out var current) && !current.Removed && !current.MicrophoneBlocked;

    public async Task SetMicrophoneBlockedAsync(Guid participantId, bool blocked, CancellationToken cancellationToken = default)
    {
        if (!IsHost) throw new InvalidOperationException("Somente o host pode gerenciar o microfone dos convidados.");
        await admissionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            guests.TryGetValue(participantId, out var current);
            if (current is null || current.Removed) throw new InvalidOperationException("Este convidado já saiu da sala.");
            current.MicrophoneBlocked = blocked;
            Interlocked.Increment(ref current.VoiceRevision);
            speakingUntil.TryRemove(participantId, out _);
            Changed?.Invoke();
            try { await current.Channel.SendAsync(new("voice-policy", ParticipantId: participantId, MicBlocked: blocked, Request: current.VoiceRevision), cancellationToken).ConfigureAwait(false); }
            catch { current.Dispose(); throw; }
        }
        finally { admissionGate.Release(); }
        await BroadcastRosterAsync().ConfigureAwait(false);
    }

    public async Task RemoveParticipantAsync(Guid participantId, CancellationToken cancellationToken = default)
    {
        if (!IsHost) throw new InvalidOperationException("Somente o host pode expulsar convidados.");
        Peer current;
        await admissionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!guests.TryGetValue(participantId, out var found)) throw new InvalidOperationException("O convidado já saiu da sala.");
            current = found;
            if (current.Removed || current.ParticipantId != participantId) throw new InvalidOperationException("Este convidado já saiu da sala.");
            current.Removed = true;
            if (current.Media is { } mediaLink) mediaLink.Removed = true;
            // Rotating the invitation invalidates every old copy, including the removed guest's copy.
            Invite = Invite! with { Token = TheaterInvite.NewToken() };
            playbackAuthorized = false;
            Changed?.Invoke();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                await current.Channel.SendAsync(new("removed"), timeout.Token).ConfigureAwait(false);
                await current.RemovalAcknowledged.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { /* Removal still closes both channels when notification cannot be delivered. */ }
            finally { current.Dispose(); }
        }
        finally { admissionGate.Release(); }
        await current.Ended.Task.ConfigureAwait(false);
        Changed?.Invoke();
    }

    private void ReceiveVoicePolicy(RoomPacket packet)
    {
        if (packet.ParticipantId != localParticipantId || packet.Request <= 0) throw new InvalidDataException("Permissão de voz inválida.");
        if (packet.Request <= receivedVoiceRevision) return;
        receivedVoiceRevision = packet.Request; localMicrophoneBlocked = packet.MicBlocked; if (packet.MicBlocked) speakingUntil.TryRemove(localParticipantId, out _); Changed?.Invoke();
    }
}

using System.Collections.Concurrent;
using System.Threading.Channels;
using Concentus;
using Concentus.Enums;

namespace AniT.Theater;

public sealed record TheaterVoiceAudio(Guid ParticipantId, short[] Samples);

public sealed partial class TheaterRoom
{
    public const int VoiceSampleRate = 48000, VoiceFrameSamples = 960;
    private readonly SemaphoreSlim voiceGate = new(1, 1);
    private readonly object encoderGate = new();
    private readonly IOpusEncoder voiceEncoder = OpusCodecFactory.CreateEncoder(VoiceSampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
    private readonly ConcurrentDictionary<Guid, long> speakingUntil = new();
    private readonly ConcurrentDictionary<Guid, VoiceDecoder> voiceDecoders = new();
    private long voiceSequence, localVoiceEpoch;
    private volatile bool voiceEnabled, voiceMuted = true, voiceDeafened;
    public bool VoiceEnabled => voiceEnabled;
    public bool VoiceMuted => voiceMuted;
    public bool VoiceDeafened => voiceDeafened;
    public bool CanTransmitVoice => VoiceEnabled && !VoiceMuted && !VoiceDeafened && !MicrophoneBlocked && (IsHost || IsConnected);
    public event Action<TheaterVoiceAudio>? VoiceAudioReceived;
    public bool IsSpeaking(Guid id) => speakingUntil.TryGetValue(id, out var until) && until > ClockEstimator.NowMs;

    public async Task StartVoiceAsync(CancellationToken cancellationToken = default)
    {
        await voiceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (VoiceEnabled) return;
            ObjectDisposedException.ThrowIf(disposed != 0, this);
            voiceMuted = true; voiceDeafened = false;
            if (!IsHost)
            {
                var control = peer ?? throw new IOException("Host desconectado.");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                while (!Clock.IsReady) await Task.Delay(10, timeout.Token).ConfigureAwait(false);
                var transport = await ConnectTlsPeerAsync(hostInvite!, timeout.Token).ConfigureAwait(false);
                try
                {
                    await transport.Channel.SendAsync(new("voice-hello", Token: control.VoiceToken), timeout.Token).ConfigureAwait(false);
                    if ((await transport.Channel.ReadAsync(timeout.Token).ConfigureAwait(false)).Type != "voice-welcome") throw new IOException("Canal de voz recusado.");
                    if (!ReferenceEquals(control, peer)) throw new IOException("Sala encerrada.");
                    var voice = new VoiceLink(transport);
                    control.Voice = voice;
                    voiceEnabled = true;
                    voice.Background = RunVoiceAsync(control, voice);
                }
                catch { transport.Dispose(); throw; }
            }
            else voiceEnabled = true;
            await UpdateVoiceStateAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { voiceGate.Release(); }
    }

    public async Task SetVoiceStateAsync(bool muted, bool deafened, CancellationToken cancellationToken = default)
    {
        if (!VoiceEnabled) throw new InvalidOperationException("Entre no chat de voz primeiro.");
        Interlocked.Increment(ref localVoiceEpoch);
        voiceMuted = muted || deafened; voiceDeafened = deafened;
        if (VoiceMuted) speakingUntil.TryRemove(localParticipantId, out _);
        await UpdateVoiceStateAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task UpdateVoiceStateAsync(CancellationToken token)
    {
        if (IsHost) await BroadcastRosterAsync().ConfigureAwait(false);
        else if (peer?.Voice is { } voice) await voice.Transport.Channel.SendAsync(new("voice-status", Ready: VoiceMuted, Sharing: VoiceDeafened), token).ConfigureAwait(false);
        Changed?.Invoke();
    }

    public async Task StopVoiceAsync()
    {
        await voiceGate.WaitAsync().ConfigureAwait(false);
        try
        {
            voiceEnabled = false; voiceMuted = true; voiceDeafened = false;
            speakingUntil.TryRemove(localParticipantId, out _);
            if (!IsHost && peer is { } control && control.Voice is { } link)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try { await link.Transport.Channel.SendAsync(new("voice-goodbye"), timeout.Token).ConfigureAwait(false); await link.Goodbye.Task.WaitAsync(timeout.Token).ConfigureAwait(false); } catch (Exception) { }
                link.Dispose(); await link.Background.ConfigureAwait(false);
            }
            await BroadcastRosterAsync().ConfigureAwait(false); Changed?.Invoke();
        }
        finally { voiceGate.Release(); }
    }

    // Called only with explicitly enabled microphone capture. No recording or loopback capture.
    public void SubmitVoice(short[] samples)
    {
        if (samples.Length != VoiceFrameSamples) throw new ArgumentException("A voz exige quadros de 20 ms.");
        if (!CanTransmitVoice) return;
        byte[] encoded;
        lock (encoderGate)
        {
            voiceEncoder.Bitrate = 24000;
            var bytes = new byte[512];
            var count = voiceEncoder.Encode(samples.AsSpan(), VoiceFrameSamples, bytes.AsSpan(), bytes.Length);
            encoded = bytes[..count];
        }
        if (!CanTransmitVoice) return;
        MarkSpeaking(localParticipantId, samples);
        var packet = new RoomPacket("voice-frame", ParticipantId: localParticipantId, Data: encoded, Request: Interlocked.Read(ref localVoiceEpoch), Offset: Interlocked.Increment(ref voiceSequence), Clock: (long)(ClockEstimator.NowMs + (IsHost ? 0 : Clock.OffsetMs)));
        if (IsHost) RelayVoice(packet);
        else peer?.Voice?.Queue.Writer.TryWrite(packet);
    }

    private void MarkSpeaking(Guid id, short[] samples)
    {
        var energy = samples.Sum(s => (double)s * s) / samples.Length;
        if (energy >= 250 * 250) speakingUntil[id] = ClockEstimator.NowMs + 220;
    }

    private async Task AcceptVoiceChannelAsync(Peer transport, string? token, CancellationToken tokenCancel)
    {
        Peer? control;
        await admissionGate.WaitAsync(tokenCancel).ConfigureAwait(false);
        try
        {
            control = ActiveGuests.FirstOrDefault(p => p.Voice is null && VoiceTokenMatches(p.VoiceToken, token));
            if (control is not null) { control.Muted = true; control.Deafened = false; Interlocked.Increment(ref control.VoiceRevision); control.Voice = new VoiceLink(transport); }
        }
        finally { admissionGate.Release(); }
        if (control is null) { await transport.Channel.SendAsync(new("reject", Error: "voice-channel"), tokenCancel).ConfigureAwait(false); return; }
        var link = control.Voice!;
        try
        {
            await transport.Channel.SendAsync(new("voice-welcome"), tokenCancel).ConfigureAwait(false);
            link.Background = RunVoiceAsync(control, link);
            await link.Background.ConfigureAwait(false);
        }
        finally { link.Dispose(); }
    }

    private static bool VoiceTokenMatches(string expected, string? actual) => actual is { Length: 64 } && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(expected), System.Text.Encoding.ASCII.GetBytes(actual));

    private async Task RunVoiceAsync(Peer control, VoiceLink link)
    {
        var writing = SendVoiceQueueAsync(link);
        long rateWindow = ClockEstimator.NowMs; int frameCount = 0;
        try
        {
            await BroadcastRosterAsync().ConfigureAwait(false);
            while (!lifetime.IsCancellationRequested)
            {
                var packet = await link.Transport.Channel.ReadAsync(lifetime.Token).ConfigureAwait(false);
                if (control.Removed) break;
                if (IsHost && packet.Type == "voice-goodbye")
                {
                    if (ReferenceEquals(control.Voice, link)) { control.Voice = null; control.Muted = true; }
                    await link.Transport.Channel.SendAsync(new("voice-bye"), lifetime.Token).ConfigureAwait(false); return;
                }
                if (!IsHost && packet.Type == "voice-bye") { link.Goodbye.TrySetResult(); return; }
                if (IsHost && packet.Type == "voice-status")
                {
                    control.Muted = packet.Ready || packet.Sharing; control.Deafened = packet.Sharing;
                    if (control.Muted) speakingUntil.TryRemove(control.ParticipantId, out _);
                    Interlocked.Increment(ref control.VoiceRevision);
                    await BroadcastRosterAsync().ConfigureAwait(false); continue;
                }
                if (packet.Type != "voice-frame" || packet.Data is not { Length: >= 1 and <= 512 }) throw new InvalidDataException("Quadro de voz inválido.");
                var now = ClockEstimator.NowMs;
                if (now - rateWindow >= 1000) { rateWindow = now; frameCount = 0; }
                if (++frameCount > (IsHost ? 80 : 320)) throw new InvalidDataException("Limite de quadros de voz excedido.");
                var id = IsHost ? control.ParticipantId : packet.ParticipantId;
                if (IsHost && (!IsVoiceTransmissionAllowed(id) || control.Muted || control.Deafened)) continue;
                if (!IsHost && !remoteRoster.Any(p => p.Id == id && !p.MicrophoneBlocked && p.VoiceConnected && !p.Muted)) continue;
                if (packet.Offset <= 0) throw new InvalidDataException("Sequência de voz inválida.");
                var hostNow = ClockEstimator.NowMs + (IsHost ? 0 : Clock.OffsetMs);
                if (packet.Clock <= 0 || hostNow - packet.Clock > 350 || packet.Clock - hostNow > 2000) continue;
                var permission = Volatile.Read(ref control.VoiceRevision);
                var decoder = voiceDecoders.GetOrAdd(id, _ => new VoiceDecoder());
                if (packet.Offset <= decoder.Sequence) continue;
                var missing = decoder.Sequence == 0 ? 0 : packet.Offset - decoder.Sequence - 1;
                var samples = new short[VoiceFrameSamples];
                if (missing > 3) decoder.Codec.ResetState();
                else for (var i = 0; i < missing; i++) decoder.Codec.Decode(ReadOnlySpan<byte>.Empty, samples.AsSpan(), samples.Length, false);
                decoder.Sequence = packet.Offset;
                if (decoder.Codec.Decode(packet.Data.AsSpan(), samples.AsSpan(), samples.Length, false) != VoiceFrameSamples) throw new InvalidDataException("Duração de voz inválida.");
                if (IsHost && (!IsVoiceTransmissionAllowed(id) || control.Muted || permission != Volatile.Read(ref control.VoiceRevision))) continue;
                MarkSpeaking(id, samples);
                if (VoiceEnabled && !VoiceDeafened) VoiceAudioReceived?.Invoke(new(id, samples));
                if (IsHost) RelayVoice(packet with { ParticipantId = id, Request = permission });
            }
        }
        catch (Exception) when (lifetime.IsCancellationRequested || link.IsDisposed || control.Removed) { }
        catch (Exception) { Notice?.Invoke("Chat de voz desconectado. O vídeo continua; entre novamente na voz."); }
        finally
        {
            link.Dispose();
            try { await writing.ConfigureAwait(false); } catch { }
            var current = control.Voice is null || ReferenceEquals(control.Voice, link);
            if (ReferenceEquals(control.Voice, link)) control.Voice = null;
            if (current)
            {
                speakingUntil.TryRemove(IsHost ? control.ParticipantId : localParticipantId, out _);
                if (!IsHost) { voiceEnabled = false; voiceMuted = true; voiceDecoders.Clear(); }
                else { control.Muted = true; voiceDecoders.TryRemove(control.ParticipantId, out _); }
            }
            await BroadcastRosterAsync().ConfigureAwait(false); Changed?.Invoke();
        }
    }

    private void RelayVoice(RoomPacket packet)
    {
        foreach (var member in ActiveGuests)
            if (member.ParticipantId != packet.ParticipantId) member.Voice?.Queue.Writer.TryWrite(packet);
    }

    private async Task SendVoiceQueueAsync(VoiceLink link)
    {
        try
        {
            await foreach (var packet in link.Queue.Reader.ReadAllAsync(link.Cancel.Token).ConfigureAwait(false))
            {
                if (IsHost && packet.ParticipantId != localParticipantId && (!guests.TryGetValue(packet.ParticipantId, out var sender) || sender.Removed || sender.MicrophoneBlocked || sender.Muted || sender.VoiceRevision != packet.Request)) continue;
                if (packet.ParticipantId == localParticipantId && (!CanTransmitVoice || packet.Request != Interlocked.Read(ref localVoiceEpoch))) continue;
                var age = ClockEstimator.NowMs + (IsHost ? 0 : Clock.OffsetMs) - packet.Clock;
                if (age > 150) continue;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(link.Cancel.Token);
                timeout.CancelAfter(300);
                await link.Transport.Channel.SendAsync(packet, timeout.Token).ConfigureAwait(false);
            }
        }
        catch { link.Dispose(); }
    }

    private sealed class VoiceDecoder
    {
        public IOpusDecoder Codec { get; } = OpusCodecFactory.CreateDecoder(VoiceSampleRate, 1);
        public long Sequence;
    }

    private sealed class VoiceLink(Peer transport) : IDisposable
    {
        public Peer Transport { get; } = transport;
        public CancellationTokenSource Cancel { get; } = new();
        public Channel<RoomPacket> Queue { get; } = Channel.CreateBounded<RoomPacket>(new BoundedChannelOptions(3) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        public Task Background { get; set; } = Task.CompletedTask;
        public TaskCompletionSource Goodbye { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int disposed;
        public bool IsDisposed => Volatile.Read(ref disposed) != 0;
        public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) != 0) return; Cancel.Cancel(); Queue.Writer.TryComplete(); Transport.Dispose(); }
    }
}

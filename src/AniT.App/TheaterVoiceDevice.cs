using System;
using System.Collections.Generic;
using System.Linq;
using AniT.Theater;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AniT.App;

/// <summary>Opt-in microphone only. Never captures system sound; all buffers are bounded.</summary>
internal sealed class TheaterVoiceDevice : IDisposable
{
    private readonly TheaterRoom room;
    private readonly object gate = new();
    private readonly Dictionary<Guid, BufferedWaveProvider> sources = new();
    private readonly MixingSampleProvider mixer = new(WaveFormat.CreateIeeeFloatWaveFormat(TheaterRoom.VoiceSampleRate, 1)) { ReadFully = true };
    private readonly Dictionary<Guid, ISampleProvider> providers = new();
    private WaveOutEvent? output;
    private WaveInEvent? input;
    private bool disposed;
    private readonly short[] frame = new short[TheaterRoom.VoiceFrameSamples];
    private int frameOffset;
    public event Action<string>? Failed;

    public TheaterVoiceDevice(TheaterRoom room)
    {
        this.room = room;
        try
        {
            output = new WaveOutEvent { DesiredLatency = 60, NumberOfBuffers = 3 };
            output.PlaybackStopped += (_, args) => { if (args.Exception is not null) Failed?.Invoke("O dispositivo de saída de áudio foi desconectado. Entre novamente na voz."); };
            output.Init(new VoiceLimiter(mixer)); output.Play();
            room.VoiceAudioReceived += Receive;
        }
        catch { output?.Dispose(); throw; }
    }

    public void Refresh()
    {
        lock (gate)
        {
            if (disposed) return;
            if (room.VoiceDeafened || !room.VoiceEnabled) foreach (var source in sources.Values) source.ClearBuffer();
            var members = room.Participants;
            foreach (var id in sources.Keys.ToArray())
                if (!members.Any(p => p.Id == id && p.VoiceConnected && !p.MicrophoneBlocked && !p.Muted)) sources[id].ClearBuffer();
            if (room.CanTransmitVoice)
            {
                if (input is not null) return;
                frameOffset = 0;
                var capture = new WaveInEvent { WaveFormat = new WaveFormat(TheaterRoom.VoiceSampleRate, 16, 1), BufferMilliseconds = 20, NumberOfBuffers = 3 };
                input = capture;
                capture.DataAvailable += Capture;
                capture.RecordingStopped += (_, args) => { if (args.Exception is not null) Failed?.Invoke("Não foi possível usar o microfone. Confira o dispositivo e as permissões do Windows."); };
                try { capture.StartRecording(); }
                catch { input = null; capture.Dispose(); throw; }
            }
            else StopCapture();
        }
    }

    private void Capture(object? sender, WaveInEventArgs args)
    {
        lock (gate)
        {
            if (disposed || !ReferenceEquals(sender, input) || !room.CanTransmitVoice) return;
            for (var offset = 0; offset + 1 < args.BytesRecorded; offset += 2)
            {
                frame[frameOffset++] = (short)(args.Buffer[offset] | args.Buffer[offset + 1] << 8);
                if (frameOffset != frame.Length) continue;
                try { room.SubmitVoice(frame); }
                catch (Exception) { StopCapture(); Failed?.Invoke("A captura de voz foi interrompida. Entre novamente na voz."); return; }
                frameOffset = 0;
            }
        }
    }

    private void Receive(TheaterVoiceAudio audio)
    {
        lock (gate)
        {
            if (disposed || room.VoiceDeafened || !room.VoiceEnabled) return;
            if (!sources.TryGetValue(audio.ParticipantId, out var source))
            {
                if (sources.Count >= TheaterRoom.MaximumGuests)
                {
                    var active = room.Participants.Select(p => p.Id).ToHashSet();
                    foreach (var stale in sources.Keys.Where(id => !active.Contains(id)).ToArray()) { mixer.RemoveMixerInput(providers[stale]); providers.Remove(stale); sources.Remove(stale); }
                    if (sources.Count >= TheaterRoom.MaximumGuests) return;
                }
                source = new BufferedWaveProvider(new WaveFormat(TheaterRoom.VoiceSampleRate, 16, 1)) { BufferDuration = TimeSpan.FromMilliseconds(160), DiscardOnBufferOverflow = true, ReadFully = true };
                sources.Add(audio.ParticipantId, source);
                var provider = source.ToSampleProvider(); providers.Add(audio.ParticipantId, provider); mixer.AddMixerInput(provider);
            }
            if (source.BufferedDuration.TotalMilliseconds > 100) source.ClearBuffer();
            var bytes = new byte[audio.Samples.Length * 2];
            Buffer.BlockCopy(audio.Samples, 0, bytes, 0, bytes.Length);
            source.AddSamples(bytes, 0, bytes.Length);
        }
    }

    private sealed class VoiceLimiter(ISampleProvider source) : ISampleProvider
    {
        public WaveFormat WaveFormat => source.WaveFormat;
        public int Read(float[] buffer, int offset, int count)
        {
            var read = source.Read(buffer, offset, count);
            for (var i = offset; i < offset + read; i++) buffer[i] = Math.Clamp(buffer[i] * .8f, -.95f, .95f);
            return read;
        }
    }

    private void StopCapture()
    {
        var previous = input; input = null; frameOffset = 0;
        if (previous is null) return;
        previous.DataAvailable -= Capture;
        // StopRecording/Dispose join the capture thread: do not hold its callback's lock.
        System.Threading.ThreadPool.QueueUserWorkItem(_ => { try { previous.StopRecording(); } catch (Exception) { }
            try { previous.Dispose(); } catch (Exception) { } });
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true; room.VoiceAudioReceived -= Receive;
            StopCapture(); output?.Stop(); output?.Dispose(); output = null; sources.Clear();
        }
    }
}

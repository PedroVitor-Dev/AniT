using System.Buffers.Binary;
using System.Buffers;
using System.Text.Json;

namespace AniT.Theater;

internal sealed record RoomPacket(string Type, int Version = PacketChannel.ProtocolVersion, string? Token = null, MediaIdentity? Media = null, bool Ready = false, TimelineState? State = null, long Clock = 0, long Echo = 0, string? Error = null, bool Sharing = false, string? Name = null, byte[]? Data = null, long Offset = 0, TheaterMode Mode = TheaterMode.PeerSync, int Count = 0, long Request = 0, long PreparationAt = 0, EpisodeTransferProgress? Progress = null, StreamBufferStatus? Buffer = null, StreamConnectionInfo? Connection = null, Guid ParticipantId = default, Guid HostId = default, string? DisplayName = null, bool MicBlocked = false, string? VoiceToken = null, TheaterParticipant[]? Roster = null);

internal sealed class PacketChannel(Stream stream) : IDisposable
{
    internal const int ProtocolVersion = 10;
    private const int MaxPacketBytes = 8192;
    internal const int StreamChunkBytes = 32 * 1024;
    private const int BinaryMetadataBytes = 17;
    private readonly SemaphoreSlim writer = new(1, 1);

    public async Task SendAsync(RoomPacket packet, CancellationToken cancellationToken)
    {
        if (packet.Type == "voice-frame")
        {
            if (packet.Version != ProtocolVersion || packet.Data is not { Length: >= 1 and <= 512 } || packet.ParticipantId == Guid.Empty || packet.Offset <= 0 || packet.Clock <= 0 || packet.Request < 0) throw new InvalidDataException("Quadro de voz inválido.");
            const int metadata = 41;
            var voice = new byte[4 + metadata + packet.Data.Length];
            BinaryPrimitives.WriteInt32BigEndian(voice, -(metadata + packet.Data.Length)); voice[4] = 2;
            packet.ParticipantId.TryWriteBytes(voice.AsSpan(5, 16));
            BinaryPrimitives.WriteInt64BigEndian(voice.AsSpan(21), packet.Offset);
            BinaryPrimitives.WriteInt64BigEndian(voice.AsSpan(29), packet.Clock);
            BinaryPrimitives.WriteInt64BigEndian(voice.AsSpan(37), packet.Request);
            packet.Data.CopyTo(voice, 45);
            await writer.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { await stream.WriteAsync(voice, cancellationToken).ConfigureAwait(false); }
            finally { writer.Release(); }
            return;
        }
        if (packet.Type == "stream-chunk")
        {
            if (packet.Version != ProtocolVersion || packet.Data is null) throw new InvalidDataException("Trecho binário inválido.");
            await SendStreamChunkAsync(packet.Request, packet.Offset, packet.Data, cancellationToken).ConfigureAwait(false);
            return;
        }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(packet);
        if (bytes.Length > MaxPacketBytes) throw new InvalidDataException("Mensagem muito grande.");
        var frame = new byte[4 + bytes.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, bytes.Length);
        bytes.CopyTo(frame, 4);
        await writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // One write per frame avoids separate tiny TLS records for each header.
            await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        }
        finally { writer.Release(); }
    }

    internal async Task SendStreamChunkAsync(long request, long offset, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        if (request <= 0 || offset < 0 || data.Length is < 1 or > StreamChunkBytes) throw new InvalidDataException("Trecho binário inválido.");
        var size = 4 + BinaryMetadataBytes + data.Length;
        var frame = ArrayPool<byte>.Shared.Rent(size);
        try
        {
            BinaryPrimitives.WriteInt32BigEndian(frame, -(BinaryMetadataBytes + data.Length));
            frame[4] = 1;
            BinaryPrimitives.WriteInt64BigEndian(frame.AsSpan(5), request);
            BinaryPrimitives.WriteInt64BigEndian(frame.AsSpan(13), offset);
            data.Span.CopyTo(frame.AsSpan(21));
            await writer.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { await stream.WriteAsync(frame.AsMemory(0, size), cancellationToken).ConfigureAwait(false); }
            finally { writer.Release(); }
        }
        finally { ArrayPool<byte>.Shared.Return(frame); }
    }

    public async Task<RoomPacket> ReadAsync(CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var size = BinaryPrimitives.ReadInt32BigEndian(header);
        if (size < 0)
        {
            if (size > -(BinaryMetadataBytes + 1) || size < -(BinaryMetadataBytes + StreamChunkBytes)) throw new InvalidDataException("Tamanho binário inválido.");
            var binary = new byte[-size];
            await stream.ReadExactlyAsync(binary, cancellationToken).ConfigureAwait(false);
            if (binary[0] == 2)
            {
                if (binary.Length is < 42 or > 553) throw new InvalidDataException("Tamanho de voz inválido.");
                var id = new Guid(binary.AsSpan(1, 16));
                var sequence = BinaryPrimitives.ReadInt64BigEndian(binary.AsSpan(17));
                var timestamp = BinaryPrimitives.ReadInt64BigEndian(binary.AsSpan(25));
                var permission = BinaryPrimitives.ReadInt64BigEndian(binary.AsSpan(33));
                if (id == Guid.Empty || sequence <= 0 || timestamp <= 0 || permission < 0) throw new InvalidDataException("Metadados de voz inválidos.");
                return new("voice-frame", ParticipantId: id, Offset: sequence, Clock: timestamp, Request: permission, Data: binary[41..]);
            }
            var request = BinaryPrimitives.ReadInt64BigEndian(binary.AsSpan(1));
            var offset = BinaryPrimitives.ReadInt64BigEndian(binary.AsSpan(9));
            if (binary[0] != 1 || request <= 0 || offset < 0) throw new InvalidDataException("Trecho binário inválido.");
            return new("stream-chunk", Request: request, Offset: offset, Data: binary[BinaryMetadataBytes..]);
        }
        if (size is < 1 or > MaxPacketBytes) throw new InvalidDataException("Tamanho de mensagem inválido.");
        var bytes = new byte[size];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        var packet = JsonSerializer.Deserialize<RoomPacket>(bytes) ?? throw new InvalidDataException("Mensagem vazia.");
        if (packet.Version != ProtocolVersion) throw new InvalidDataException("Versão de sala incompatível. Atualize o AniT nos dois computadores.");
        return packet;
    }

    public void Dispose() => stream.Dispose();
}

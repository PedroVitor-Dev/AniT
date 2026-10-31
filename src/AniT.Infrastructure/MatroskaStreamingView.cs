namespace AniT.Infrastructure;

/// <summary>A same-length MKV view: optional font attachments and eager cue pointers become EBML Void.
/// Original downloaded bytes, media packets, track metadata and all byte offsets stay unchanged.</summary>
public sealed class MatroskaStreamingView : Stream
{
    readonly Stream source;
    readonly List<(long Offset, byte[] Header, bool Index)> patches;
    readonly List<(long Start, long End)> omitted = [];
    long position;
    volatile bool indexEnabled;
    public long CuesOffset { get; }
    public long FirstMediaOffset { get; }
    public long SkippedAttachmentBytes { get; }
    MatroskaStreamingView(Stream source, List<(long, byte[], bool)> patches, long firstMedia, long attachments, long cues = 0, List<(long Start, long End)>? omitted = null)
    { this.source = source; this.patches = patches; FirstMediaOffset = firstMedia; SkippedAttachmentBytes = attachments; CuesOffset = cues; this.omitted = omitted ?? []; }

    public static async Task<MatroskaStreamingView> CreateAsync(Stream source, CancellationToken token = default)
    {
        var patches = new List<(long, byte[], bool)>();
        var omitted = new List<(long Start, long End)>();
        long attachments = 0, firstMedia = 0, cues = 0;
        var ebml = await ReadHeaderAsync(source, 0, token);
        if (ebml is not { Id: 0x1A45DFA3 } || ebml.Value.Size > source.Length - ebml.Value.Bytes) {
            source.Position = 0; return new(source, patches, 0, 0);
        }
        var segmentAt = ebml.Value.Bytes + ebml.Value.Size;
        var segment = await ReadHeaderAsync(source, segmentAt, token);
        if (segment is not { Id: 0x18538067 }) { source.Position = 0; return new(source, patches, 0, 0); }
        long segmentData = segmentAt + segment.Value.Bytes;
        long at = segmentData; var elements = 0;
        while (at < source.Length && elements++ < 256) {
            var element = await ReadHeaderAsync(source, at, token);
            if (element is null) break;
            var (id, size, header) = element.Value;
            if (id == 0x1F43B675) { firstMedia = at; break; }
            if (size < 0 || size > source.Length - at - header) break;
            // Fetch only an actual, bounded SeekHead; never a fixed chunk that
            // accidentally includes font payload or video data.
            if (id == 0x114D9B74 && size <= 256 * 1024) {
                var buffer = new byte[checked(header + (int)size)];
                source.Position = at; await source.ReadExactlyAsync(buffer, token);
                var end = buffer.Length; var entry = header;
                while (entry < end && Header(buffer, entry, out var entryId, out var entrySize, out var entryHeader)) {
                    if (entrySize < 0 || entrySize > end - entry - entryHeader) break;
                    var next = entry + entryHeader + (int)entrySize;
                    if (entryId == 0x4DBB) {
                        var field = entry + entryHeader; uint target = 0; ulong seekPosition = 0;
                        while (field < next && Header(buffer, field, out var fieldId, out var fieldSize, out var fieldHeader)) {
                            if (fieldSize < 0 || fieldSize > next - field - fieldHeader) break;
                            if (fieldId == 0x53AB && fieldSize <= 4)
                                for (var i = 0; i < fieldSize; i++) target = (target << 8) | buffer[field + fieldHeader + i];
                            if (fieldId == 0x53AC && fieldSize <= 8)
                                for (var i = 0; i < fieldSize; i++) seekPosition = (seekPosition << 8) | buffer[field + fieldHeader + i];
                            field += fieldHeader + (int)fieldSize;
                        }
                        if (target is 0x1941A469 or 0x1C53BB6B or 0x1254C367)
                            patches.Add((at + entry, VoidHeader(entryHeader, entrySize), target == 0x1C53BB6B));
                        if (target == 0x1C53BB6B && seekPosition > 0 && seekPosition < (ulong)(source.Length - segmentData))
                            cues = segmentData + (long)seekPosition;
                    }
                    entry = next;
                }
            }
            if (id == 0x1941A469) {
                patches.Add((at, VoidHeader(header, size), false)); attachments += size;
                omitted.Add((at + header, at + header + size));
            }
            if (id == 0x1C53BB6B) { cues = at; patches.Add((at, VoidHeader(header, size), true)); }
            at += header + size;
            if (patches.Count > 128) break;
        }
        source.Position = 0;
        return new(source, patches, firstMedia, attachments, cues, omitted);
    }

    static async Task<(uint Id, long Size, int Bytes)?> ReadHeaderAsync(Stream source, long offset, CancellationToken token)
    {
        if (offset < 0 || offset >= source.Length) return null;
        source.Position = offset; var bytes = new byte[12];
        await source.ReadExactlyAsync(bytes.AsMemory(0, 1), token);
        var ids = 1;
        while (ids <= 4 && (bytes[0] & (0x80 >> (ids - 1))) == 0) ids++;
        if (ids > 4 || source.Length - offset < ids + 1) return null;
        await source.ReadExactlyAsync(bytes.AsMemory(1, ids), token);
        var sizes = 1;
        while (sizes <= 8 && (bytes[ids] & (0x80 >> (sizes - 1))) == 0) sizes++;
        if (sizes > 8 || source.Length - offset < ids + sizes) return null;
        if (sizes > 1) await source.ReadExactlyAsync(bytes.AsMemory(ids + 1, sizes - 1), token);
        return Header(bytes, 0, out var id, out var size, out var header) ? (id, size, header) : null;
    }

    public async Task<bool> PrepareIndexAsync(CancellationToken token)
    {
        var saved = Position;
        try {
            return await PrepareIndexAsync(async (offset, bytes, cancellation) => {
                source.Position = offset; await source.ReadExactlyAsync(bytes, cancellation);
            }, token);
        } finally { Position = saved; }
    }
    internal async Task<bool> PrepareIndexAsync(Func<long, Memory<byte>, CancellationToken, Task> read, CancellationToken token)
    {
        if (indexEnabled) return true;
        if (CuesOffset <= 0 || source.Length - CuesOffset < 16) return false;
        var header = new byte[16]; await read(CuesOffset, header, token);
        if (!Header(header, 0, out var id, out var length, out var size) || id != 0x1C53BB6B
            || length > 16 * 1024 * 1024 || length <= 0 || length > source.Length - CuesOffset - size) return false;
        var remaining = length + size; var offset = CuesOffset; var buffer = new byte[64 * 1024];
        while (remaining > 0) {
            var count = (int)Math.Min(remaining, buffer.Length);
            await read(offset, buffer.AsMemory(0, count), token); remaining -= count; offset += count;
        }
        token.ThrowIfCancellationRequested(); indexEnabled = true; return true;
    }

    internal bool IsRangeAvailable(long offset, int count, Func<long, int, bool> physical)
    {
        if (offset < 0 || count < 0 || offset > Length - count) return false;
        var end = offset + count;
        foreach (var skip in omitted) {
            if (skip.End <= offset) continue;
            if (skip.Start >= end) break;
            if (skip.Start > offset && !physical(offset, (int)(skip.Start - offset))) return false;
            offset = Math.Min(end, skip.End);
        }
        return offset >= end || physical(offset, (int)(end - offset));
    }

    static byte[] VoidHeader(int header, long size)
    {
        var bytes = header - 1; if (bytes is < 1 or > 8 || (ulong)size >= (1UL << (7 * bytes)) - 1) throw new InvalidDataException("Cabeçalho EBML inválido.");
        var result = new byte[header]; result[0] = 0xEC; var value = (ulong)size | (1UL << (7 * bytes));
        for (var i = header - 1; i > 0; i--) { result[i] = (byte)value; value >>= 8; }
        return result;
    }
    static bool Header(byte[] data, int at, out uint id, out long size, out int header)
    {
        id = 0; size = 0; header = 0; if (at < 0 || at >= data.Length || data[at] == 0) return false;
        var ids = 1; while (ids <= 4 && (data[at] & (0x80 >> (ids - 1))) == 0) ids++; if (ids > 4 || at + ids >= data.Length) return false;
        for (var i = 0; i < ids; i++) id = (id << 8) | data[at + i]; var start = at + ids; if (data[start] == 0) return false;
        var sizes = 1; while (sizes <= 8 && (data[start] & (0x80 >> (sizes - 1))) == 0) sizes++; if (sizes > 8 || start + sizes > data.Length) return false;
        size = data[start] & ((0x80 >> (sizes - 1)) - 1); for (var i = 1; i < sizes; i++) size = (size << 8) | data[start + i]; header = ids + sizes; return true;
    }
    internal async ValueTask<int> ReadIndexBytesAsync(Memory<byte> buffer, CancellationToken token)
    {
        source.Position = Position;
        var count = await source.ReadAsync(buffer, token);
        Position += count; return count;
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var start = Position; var count = (int)Math.Min(buffer.Length, Math.Max(0, Length - start));
        if (count == 0) return 0;
        var synthetic = false;
        foreach (var skip in omitted) {
            if (skip.End <= start) continue;
            if (skip.Start <= start) { count = (int)Math.Min(count, skip.End - start); synthetic = true; }
            else count = (int)Math.Min(count, skip.Start - start);
            break;
        }
        if (synthetic) buffer.Span[..count].Clear();
        else { source.Position = start; count = await source.ReadAsync(buffer[..count], token); }
        Position = start + count;
        foreach (var (offset, bytes, index) in patches)
        {
            if (index && indexEnabled) continue;
            var from = Math.Max(start, offset); var end = Math.Min(start + count, offset + bytes.Length);
            if (end > from) bytes.AsMemory((int)(from - offset), (int)(end - from)).CopyTo(buffer[(int)(from - start)..]);
        }
        return count;
    }
    public override int Read(byte[] bytes, int offset, int count) => ReadAsync(bytes.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override long Length => source.Length; public override long Position {
        get => position;
        set {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            position = value;
            var skip = omitted.FirstOrDefault(range => value >= range.Start && value < range.End);
            source.Position = skip.End > 0 ? skip.End : value;
        }
    }
    public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false;
    public override long Seek(long offset, SeekOrigin origin) {
        Position = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => checked(Position + offset),
            SeekOrigin.End => checked(Length + offset), _ => throw new ArgumentOutOfRangeException(nameof(origin)) };
        return Position;
    }
    public override void Flush() => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) source.Dispose(); base.Dispose(disposing); }
}

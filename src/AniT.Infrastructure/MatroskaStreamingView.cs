namespace AniT.Infrastructure;

/// <summary>A same-length MKV view: optional font attachments and eager cue pointers become EBML Void.
/// Original downloaded bytes, media packets, track metadata and all byte offsets stay unchanged.</summary>
public sealed class MatroskaStreamingView : Stream
{
    readonly Stream source;
    readonly List<(long Offset, byte[] Header, bool Index)> patches;
    volatile bool indexEnabled;
    public long CuesOffset { get; }
    public long FirstMediaOffset { get; }
    public long SkippedAttachmentBytes { get; }
    MatroskaStreamingView(Stream source, List<(long, byte[], bool)> patches, long firstMedia, long attachments, long cues = 0)
    { this.source = source; this.patches = patches; FirstMediaOffset = firstMedia; SkippedAttachmentBytes = attachments; CuesOffset = cues; }

    public static async Task<MatroskaStreamingView> CreateAsync(Stream source, CancellationToken token = default)
    {
        var buffer = new byte[(int)Math.Min(256 * 1024, source.Length)]; source.Position = 0; await source.ReadExactlyAsync(buffer, token);
        var patches = new List<(long, byte[], bool)>(); long attachments = 0, firstMedia = 0, cues = 0;
        if (!Header(buffer, 0, out var id, out var size, out var header) || id != 0x1A45DFA3) { source.Position = 0; return new(source, patches, 0, 0); }
        if (size > buffer.Length - header) { source.Position = 0; return new(source, patches, 0, 0); }
        var segment = (int)(header + size);
        if (!Header(buffer, segment, out id, out _, out header) || id != 0x18538067) { source.Position = 0; return new(source, patches, 0, 0); }
        long segmentData = segment + header; long position = segmentData; int elements = 0;
        while (position < source.Length && elements++ < 256)
        {
            byte[] data; int at;
            if (position < buffer.Length - 16) { data = buffer; at = (int)position; }
            else { data = new byte[(int)Math.Min(256, source.Length - position)]; source.Position = position; await source.ReadExactlyAsync(data, token); at = 0; }
            if (!Header(data, at, out id, out size, out header)) break;
            if (id == 0x1F43B675) { firstMedia = position; break; }
            if (size < 0 || size > source.Length - position - header) break;
            if (id == 0x114D9B74 && position + header + size <= buffer.Length)
            {
                var end = (int)(position + header + size); var entry = (int)position + header;
                while (entry < end && Header(buffer, entry, out var entryId, out var entrySize, out var entryHeader))
                {
                    var next = (long)entry + entryHeader + entrySize; if (next > end) break;
                    if (entryId == 0x4DBB)
                    {
                        var field = entry + entryHeader; uint target = 0; long seekPosition = 0;
                        while (field < next && Header(buffer, field, out var fieldId, out var fieldSize, out var fieldHeader))
                        {
                            if (fieldSize < 0 || field + fieldHeader + fieldSize > next) break;
                            if (fieldId == 0x53AB && fieldSize <= 4)
                                for (var i = 0; i < fieldSize; i++) target = (target << 8) | buffer[field + fieldHeader + i];
                            if (fieldId == 0x53AC && fieldSize <= 8)
                                for (var i = 0; i < fieldSize; i++) seekPosition = (seekPosition << 8) | buffer[field + fieldHeader + i];
                            field = checked((int)(field + fieldHeader + fieldSize));
                        }
                        if (target is 0x1941A469 or 0x1C53BB6B or 0x1254C367)
                            patches.Add((entry, VoidHeader(entryHeader, entrySize), target == 0x1C53BB6B));
                        if (target == 0x1C53BB6B && seekPosition > 0 && seekPosition < source.Length - segmentData) cues = segmentData + seekPosition;
                    }
                    entry = (int)next;
                }
            }
            if (id == 0x1941A469) { patches.Add((position, VoidHeader(header, size), false)); attachments += size; }
            position += header + size;
            // Only inspect bounded top-level metadata; never scan video packets to find a header.
            if (patches.Count > 128) break;
        }
        source.Position = 0; return new(source, patches, firstMedia, attachments, cues);
    }
    public async Task<bool> PrepareIndexAsync(CancellationToken token)
    {
        if (indexEnabled) return true;
        if (CuesOffset <= 0) return false;
        source.Position = CuesOffset; var header = new byte[16]; await source.ReadExactlyAsync(header, token);
        if (!Header(header, 0, out var id, out var length, out var size) || id != 0x1C53BB6B || length > 16 * 1024 * 1024 || length <= 0) return false;
        var remaining = length + size; source.Position = CuesOffset; var buffer = new byte[64 * 1024];
        while (remaining > 0) { var count = (int)Math.Min(remaining, buffer.Length); await source.ReadExactlyAsync(buffer.AsMemory(0, count), token); remaining -= count; }
        indexEnabled = true; return true;
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
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        var start = Position; var count = await source.ReadAsync(buffer, token);
        foreach (var (offset, bytes, index) in patches)
        {
            if (index && indexEnabled) continue;
            var from = Math.Max(start, offset); var end = Math.Min(start + count, offset + bytes.Length);
            if (end > from) bytes.AsMemory((int)(from - offset), (int)(end - from)).CopyTo(buffer[(int)(from - start)..]);
        }
        return count;
    }
    public override int Read(byte[] bytes, int offset, int count) => ReadAsync(bytes.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override long Length => source.Length; public override long Position { get => source.Position; set => source.Position = value; }
    public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false;
    public override long Seek(long offset, SeekOrigin origin) => source.Seek(offset, origin);
    public override void Flush() => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) source.Dispose(); base.Dispose(disposing); }
}

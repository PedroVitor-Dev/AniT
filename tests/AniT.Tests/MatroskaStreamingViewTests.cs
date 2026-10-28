using AniT.Infrastructure;
namespace AniT.Tests;

public sealed class MatroskaStreamingViewTests
{
    static byte[] Element(uint id, byte[] payload, int width = 2)
    {
        var name = new List<byte>(); do { name.Insert(0, (byte)id); id >>= 8; } while (id != 0);
        var size = new byte[width]; var value = (ulong)(uint)payload.Length | (1UL << (7 * width)); for (var i = width - 1; i >= 0; i--) { size[i] = (byte)value; value >>= 8; }
        return [.. name, .. size, .. payload];
    }
    static byte[] Seek(uint id, long offset)
    {
        var target = new byte[4]; for (var i = 3; i >= 0; i--) { target[i] = (byte)id; id >>= 8; }
        var position = new byte[8]; for (var i = 7; i >= 0; i--) { position[i] = (byte)offset; offset >>= 8; }
        return Element(0x4DBB, [.. Element(0x53AB, target), .. Element(0x53AC, position)]);
    }
    static (byte[] Bytes, int Attachment, int Cluster, int Cues, int CueEntry) Fixture()
    {
        var header = Element(0x1A45DFA3, []); var placeholder = Element(0x114D9B74, [.. Seek(0x1941A469, 0), .. Seek(0x1C53BB6B, 0), .. Seek(0x1254C367, 0)]);
        var attachments = Element(0x1941A469, new byte[1024 * 1024], 4); var cluster = Element(0x1F43B675, Enumerable.Range(0, 2048).Select(i => (byte)i).ToArray()); var cues = Element(0x1C53BB6B, new byte[32]);
        var at = placeholder.Length; var media = at + attachments.Length; var index = media + cluster.Length;
        var seek = Element(0x114D9B74, [.. Seek(0x1941A469, at), .. Seek(0x1C53BB6B, index), .. Seek(0x1254C367, index + cues.Length)]);
        byte[] data = [.. seek, .. attachments, .. cluster, .. cues, .. Element(0x1254C367, new byte[16])]; var start = header.Length + 12;
        return ([.. header, .. Element(0x18538067, data, 8)], start + at, start + media, start + index, start + 6 + Seek(0x1941A469, at).Length);
    }
    [Fact]
    public async Task ViewSkipsAttachmentsWithoutChangingOffsetsOrStoredBytesAndRestoresIndexOnDemand()
    {
        var fixture = Fixture(); using var original = new MemoryStream(fixture.Bytes.ToArray());
        await using var view = await MatroskaStreamingView.CreateAsync(original);
        Assert.Equal(fixture.Bytes.Length, view.Length); Assert.Equal(fixture.Cluster, view.FirstMediaOffset); Assert.Equal(fixture.Cues, view.CuesOffset); Assert.Equal(1024 * 1024, view.SkippedAttachmentBytes);
        var served = new byte[fixture.Bytes.Length]; await view.ReadExactlyAsync(served);
        Assert.Equal(0xEC, served[fixture.Attachment]); Assert.Equal(0xEC, served[fixture.CueEntry]);
        Assert.Equal(fixture.Bytes[fixture.Cluster..], served[fixture.Cluster..]);
        Assert.Equal(fixture.Bytes, original.ToArray());
        Assert.True(await view.PrepareIndexAsync(CancellationToken.None));
        view.Position = fixture.CueEntry; var restored = new byte[4]; await view.ReadExactlyAsync(restored);
        Assert.Equal(fixture.Bytes[fixture.CueEntry..(fixture.CueEntry + 4)], restored);
        view.Position = fixture.Attachment; Assert.Equal(0xEC, view.ReadByte());
    }
    [Fact]
    public async Task HeaderPatchesAlsoApplyToRangesStartingInsideTheHeader()
    {
        var fixture = Fixture(); using var original = new MemoryStream(fixture.Bytes); await using var view = await MatroskaStreamingView.CreateAsync(original);
        view.Position = fixture.Attachment; var whole = new byte[8]; await view.ReadExactlyAsync(whole);
        view.Position = fixture.Attachment + 2; var range = new byte[4]; await view.ReadExactlyAsync(range); Assert.Equal(whole[2..6], range);
    }
    [Fact]
    public async Task NonMatroskaDataPassesThroughUnchanged()
    {
        var data = new byte[64]; new Random(7).NextBytes(data); using var original = new MemoryStream(data); await using var view = await MatroskaStreamingView.CreateAsync(original);
        var served = new byte[data.Length]; await view.ReadExactlyAsync(served); Assert.Equal(data, served); Assert.Equal(0, view.FirstMediaOffset); Assert.False(await view.PrepareIndexAsync(CancellationToken.None));
    }
}

using System.IO;
using System.Text;

internal static class SyntheticTheaterMedia
{
    // Small uncompressed AVI following the RIFF layout; all frames are generated here, with no personal media.
    public static void WriteAvi(string path, int frames = 120)
    {
        const int width = 320, height = 180, fps = 10, frameSize = width * height * 3;
        using var file = File.Create(path); using var writer = new BinaryWriter(file);
        void Four(string value) => writer.Write(Encoding.ASCII.GetBytes(value));
        long Begin(string name) { Four(name); var mark = file.Position; writer.Write(0); return mark; }
        void End(long mark) { var end = file.Position; file.Position = mark; writer.Write((int)(end - mark - 4)); file.Position = end; }
        var riff = Begin("RIFF"); Four("AVI "); var hdrl = Begin("LIST"); Four("hdrl");
        var main = Begin("avih"); foreach (var value in new[] { 1000000 / fps, frameSize * fps, 0, 0x10, frames, 0, 1, frameSize, width, height, 0, 0, 0, 0 }) writer.Write(value); End(main);
        var strl = Begin("LIST"); Four("strl"); var stream = Begin("strh"); Four("vids"); Four("DIB "); writer.Write(0); writer.Write((short)0); writer.Write((short)0);
        foreach (var value in new[] { 0, 1, fps, 0, frames, frameSize, -1, 0 }) writer.Write(value);
        writer.Write((short)0); writer.Write((short)0); writer.Write((short)width); writer.Write((short)height); End(stream);
        var format = Begin("strf"); writer.Write(40); writer.Write(width); writer.Write(height); writer.Write((short)1); writer.Write((short)24);
        foreach (var value in new[] { 0, frameSize, 0, 0, 0, 0 }) writer.Write(value); End(format); End(strl); End(hdrl);
        var movi = Begin("LIST"); Four("movi"); var offsets = new List<int>();
        var pixels = new byte[frameSize];
        for (var frame = 0; frame < frames; frame++)
        {
            offsets.Add((int)(file.Position - movi - 4)); var chunk = Begin("00db");
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            {
                var index = (y * width + x) * 3; var stripe = Math.Abs(x - frame * 3 % width) < 12;
                pixels[index] = (byte)(stripe ? 255 : 60 + x / 3); pixels[index + 1] = (byte)(stripe ? 255 : 25 + y / 2); pixels[index + 2] = (byte)(stripe ? 255 : 20 + frame % 60);
            }
            writer.Write(pixels); End(chunk);
        }
        End(movi); var indexChunk = Begin("idx1"); foreach (var offset in offsets) { Four("00db"); writer.Write(0x10); writer.Write(offset); writer.Write(frameSize); }
        End(indexChunk); End(riff);
    }
}

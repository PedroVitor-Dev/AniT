using System.Media;
using System.IO;
using AniT.Core.Achievements;

namespace AniT.App;

internal static class AchievementSoundService
{
    public static void Play(AchievementRarity rarity, double volume)
    {
        var settings = AchievementSettingsStore.Load();
        if (!settings.PlaySound || volume <= 0) return;

        try
        {
            var notes = rarity switch
            {
                AchievementRarity.SupremeLegendary => new[] { 523d, 659d, 784d, 1047d },
                AchievementRarity.Legendary => new[] { 392d, 523d, 659d },
                AchievementRarity.Secret or AchievementRarity.Epic => new[] { 330d, 494d, 659d },
                AchievementRarity.Rare => new[] { 440d, 554d, 659d },
                _ => new[] { 440d, 660d }
            };
            using var stream = BuildWave(notes, Math.Clamp(volume, 0, 1));
            using var player = new SoundPlayer(stream);
            player.PlaySync();
        }
        catch
        {
            // Achievement feedback must never interrupt playback or navigation.
        }
    }

    private static MemoryStream BuildWave(IReadOnlyList<double> frequencies, double volume)
    {
        const int sampleRate = 22050;
        const short channels = 1;
        const short bits = 16;
        const double noteSeconds = 0.11;
        var samplesPerNote = (int)(sampleRate * noteSeconds);
        var totalSamples = samplesPerNote * frequencies.Count;
        var dataSize = totalSamples * sizeof(short);
        var stream = new MemoryStream(44 + dataSize);
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataSize);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channels * bits / 8);
            writer.Write((short)(channels * bits / 8));
            writer.Write(bits);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(dataSize);

            foreach (var frequency in frequencies)
            {
                for (var index = 0; index < samplesPerNote; index++)
                {
                    var fade = 1d - index / (double)samplesPerNote;
                    var sample = Math.Sin(2 * Math.PI * frequency * index / sampleRate);
                    writer.Write((short)(sample * short.MaxValue * 0.22 * volume * fade));
                }
            }
        }
        stream.Position = 0;
        return stream;
    }
}

using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace AniT.Infrastructure;

public interface IFileFingerprintService
{
    Task<string> ComputeQuickFingerprintAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>
/// Hashes the file size plus bounded samples from the beginning, middle and end.
/// Work stays constant for multi-gigabyte video files while still recognizing moves.
/// </summary>
public sealed class QuickFileFingerprintService : IFileFingerprintService
{
    private const int SampleSize = 64 * 1024;

    public async Task<string> ComputeQuickFingerprintAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            SampleSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(stream.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        var offsets = new[]
        {
            0L,
            Math.Max(0, stream.Length / 2 - SampleSize / 2),
            Math.Max(0, stream.Length - SampleSize)
        }.Distinct().ToArray();
        var buffer = ArrayPool<byte>.Shared.Rent(SampleSize);
        try
        {
            foreach (var offset in offsets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stream.Position = offset;
                var remaining = (int)Math.Min(SampleSize, stream.Length - offset);
                var readTotal = 0;
                while (readTotal < remaining)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(readTotal, remaining - readTotal), cancellationToken);
                    if (read == 0) break;
                    readTotal += read;
                }
                hash.AppendData(buffer, 0, readTotal);
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

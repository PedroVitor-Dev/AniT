using System.Security.Cryptography;

namespace AniT.Theater;

public sealed record MediaIdentity(long Length, string Sha256)
{
    public bool IsValid => Length > 0 && Sha256 is { Length: 64 } && Sha256.All(Uri.IsHexDigit);
    public bool Matches(MediaIdentity? other) => other is { IsValid: true } && IsValid && Length == other.Length && string.Equals(Sha256, other.Sha256, StringComparison.OrdinalIgnoreCase);

    public static async Task<MediaIdentity> FromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var before = new FileInfo(path);
        var length = before.Length;
        var modified = before.LastWriteTimeUtc;
        if (length == 0) throw new InvalidDataException("O arquivo está vazio.");
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false);
        var after = new FileInfo(path);
        if (after.Length != length || after.LastWriteTimeUtc != modified) throw new IOException("O arquivo mudou durante a verificação. Selecione-o novamente.");
        return new(length, Convert.ToHexString(hash));
    }
}

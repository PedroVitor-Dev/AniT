using System.Security.Cryptography;

namespace AniT.Core;

public sealed record ProfilePinCredential(string Hash, string Salt, int Iterations);

public static class ProfilePinSecurity
{
    public const int DefaultIterations = 160_000;
    private const int HashLength = 32;

    public static bool IsValidFormat(string? pin) =>
        pin is { Length: >= 4 and <= 6 } && pin.All(char.IsAsciiDigit);

    public static ProfilePinCredential Create(string pin)
    {
        if (!IsValidFormat(pin)) throw new ArgumentException("O PIN deve ter de 4 a 6 números.", nameof(pin));
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, DefaultIterations, HashAlgorithmName.SHA256, HashLength);
        return new ProfilePinCredential(Convert.ToBase64String(hash), Convert.ToBase64String(salt), DefaultIterations);
    }

    public static bool Verify(string? pin, string? encodedHash, string? encodedSalt, int iterations)
    {
        if (!IsValidFormat(pin) || string.IsNullOrWhiteSpace(encodedHash) || string.IsNullOrWhiteSpace(encodedSalt) || iterations <= 0) return false;
        try
        {
            var salt = Convert.FromBase64String(encodedSalt);
            var expected = Convert.FromBase64String(encodedHash);
            if (expected.Length is < 16 or > 64 || salt.Length is < 8 or > 64) return false;
            var actual = Rfc2898DeriveBytes.Pbkdf2(pin!, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

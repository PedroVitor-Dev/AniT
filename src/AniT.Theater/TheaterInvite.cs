using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace AniT.Theater;

// This is an invitation secret, not a discoverable central room ID. Never write it to logs.
public sealed record TheaterInvite(IPAddress Address, int Port, string Token, string CertificateHash)
{
    public string Encode() => $"anit-theater://{Address}:{Port}/{Token}/{CertificateHash}";

    public static TheaterInvite Parse(string value)
    {
        if (value.Length > 512 || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "anit-theater" || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || !IPAddress.TryParse(uri.Host, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork || ip.Equals(IPAddress.Any) || uri.Port is < 1 or > 65535)
            throw new FormatException("Convite inválido. Cole o convite completo gerado pelo host.");
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || parts[0].Length != 64 || parts[1].Length != 64 || !parts.All(p => p.All(Uri.IsHexDigit)))
            throw new FormatException("Convite inválido. Cole o convite completo gerado pelo host.");
        return new(ip, uri.Port, parts[0], parts[1]);
    }

    internal static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    internal bool AcceptsToken(string? candidate) => candidate is { Length: 64 } && CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(Token), System.Text.Encoding.ASCII.GetBytes(candidate));
}

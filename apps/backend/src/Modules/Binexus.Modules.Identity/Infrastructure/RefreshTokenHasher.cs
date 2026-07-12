using System.Security.Cryptography;
using System.Text;

namespace Binexus.Modules.Identity.Infrastructure;

/// <summary>
/// Opaque refresh tokens: 256-bit RandomNumberGenerator entropy, Base64Url without padding.
/// Persist only SHA-256 of the raw token. SHA-256 is appropriate because the token is already
/// high-entropy random data (not a human password); a slow KDF would add latency without benefit.
/// </summary>
public static class RefreshTokenHasher
{
    public const int TokenEntropyBytes = 32;

    public static string Generate()
    {
        Span<byte> bytes = stackalloc byte[TokenEntropyBytes];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    public static string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    public static bool FixedTimeEqualsHex(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

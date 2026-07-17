using System.Security.Cryptography;
using System.Text;

namespace Binexus.Platform.Branching.Crypto;

/// <summary>
/// High-entropy (256-bit) opaque secret used for the pairing status token and the pairing receipt.
/// The raw value is delivered once to the Device; only the SHA-256 hash is persisted. Never logged.
/// This is NOT the permanent device credential and NOT a user JWT.
/// </summary>
public static class PairingSecret
{
    public const int EntropyBytes = 32;

    public static string Generate()
    {
        Span<byte> secret = stackalloc byte[EntropyBytes];
        RandomNumberGenerator.Fill(secret);
        return Base64Url.Encode(secret);
    }

    public static string Hash(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();
    }

    public static bool FixedTimeEqualsHash(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
}

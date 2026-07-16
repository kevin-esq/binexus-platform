using System.Security.Cryptography;
using System.Text;

namespace Binexus.Platform.Branching.Crypto;

public static class InstallationToken
{
    public const int EntropyBytes = 32;

    public static string Generate()
    {
        Span<byte> token = stackalloc byte[EntropyBytes];
        RandomNumberGenerator.Fill(token);
        return Base64Url.Encode(token);
    }

    public static string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }

    public static bool FixedTimeEqualsHash(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
}

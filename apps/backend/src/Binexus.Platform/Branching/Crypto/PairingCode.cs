using System.Security.Cryptography;
using System.Text;

namespace Binexus.Platform.Branching.Crypto;

/// <summary>
/// Human pairing code shown in-store. 8 decimal digits (~23.3 bits). Acceptable only because the
/// ceremony also requires a challenge, ECDSA proof-of-possession, admin fingerprint approval,
/// persisted lockout, a short TTL and LAN-local exposure. Never stored or logged raw.
/// </summary>
public static class PairingCode
{
    public const int DigitCount = 8;
    private const int RejectionThreshold = 250; // largest multiple of 10 below 256, avoids modulo bias

    public static string Generate()
    {
        Span<char> digits = stackalloc char[DigitCount];
        Span<byte> sample = stackalloc byte[1];
        for (var index = 0; index < DigitCount; index++)
        {
            byte value;
            do
            {
                RandomNumberGenerator.Fill(sample);
                value = sample[0];
            }
            while (value >= RejectionThreshold);

            digits[index] = (char)('0' + (value % 10));
        }

        return $"{new string(digits[..4])}-{new string(digits[4..])}";
    }

    public static string Normalize(string pairingCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pairingCode);
        var compact = new string(pairingCode
            .Where(static character => character is not '-' and not ' ')
            .ToArray());

        if (compact.Length != DigitCount || compact.Any(static character => character is < '0' or > '9'))
        {
            throw new FormatException("Pairing code must be exactly 8 decimal digits.");
        }

        return compact;
    }

    public static string Hash(string pairingCode, string pepper)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pepper);
        var normalized = Normalize(pairingCode);
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(pepper), Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool FixedTimeEqualsHash(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
}

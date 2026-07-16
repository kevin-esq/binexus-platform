using System.Security.Cryptography;
using System.Text;

namespace Binexus.Platform.Branching.Crypto;

public static class BranchActivationCode
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int SegmentLength = 5;
    private const int PayloadLength = SegmentLength * 2;

    public static string Generate()
    {
        Span<char> payload = stackalloc char[PayloadLength];
        Span<byte> random = stackalloc byte[1];
        for (var index = 0; index < payload.Length; index++)
        {
            byte sample;
            do
            {
                RandomNumberGenerator.Fill(random);
                sample = random[0];
            }
            while (sample >= 224);

            payload[index] = Alphabet[sample % Alphabet.Length];
        }

        return $"BNX-{new string(payload[..SegmentLength])}-{new string(payload[SegmentLength..])}";
    }

    public static string Normalize(string activationCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activationCode);
        var compact = new string(activationCode
            .Where(static character => character is not '-' and not ' ')
            .ToArray())
            .ToUpperInvariant();

        if (compact.StartsWith("BNX", StringComparison.Ordinal))
        {
            compact = compact[3..];
        }

        if (compact.Length != PayloadLength || compact.Any(character => !Alphabet.Contains(character)))
        {
            throw new FormatException("Activation code must use the Crockford Base32 alphabet.");
        }

        return compact;
    }

    public static string Hash(string activationCode, string pepper)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pepper);
        var normalized = Normalize(activationCode);
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(pepper), Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool FixedTimeEqualsHash(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
}

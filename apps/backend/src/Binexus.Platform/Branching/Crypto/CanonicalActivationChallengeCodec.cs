using System.Buffers.Binary;
using System.Text;

namespace Binexus.Platform.Branching.Crypto;

public sealed record CanonicalActivationChallenge(
    Guid ChallengeId,
    Guid BranchInstanceId,
    string PublicKeyFingerprint,
    string InstallationTokenHash,
    string Nonce,
    DateTimeOffset ExpiresAtUtc);

public static class CanonicalActivationChallengeCodec
{
    public static byte[] Encode(CanonicalActivationChallenge challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        var fields = new[]
        {
            ActivationCryptoFormats.ChallengeVersion,
            challenge.ChallengeId.ToString("D"),
            challenge.BranchInstanceId.ToString("D"),
            challenge.PublicKeyFingerprint,
            challenge.InstallationTokenHash,
            challenge.Nonce,
            challenge.ExpiresAtUtc.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        };

        using var stream = new MemoryStream();
        Span<byte> length = stackalloc byte[sizeof(ushort)];
        foreach (var field in fields)
        {
            var bytes = Encoding.UTF8.GetBytes(field);
            if (bytes.Length > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(challenge), "A challenge field exceeds 65535 bytes.");
            }

            BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)bytes.Length);
            stream.Write(length);
            stream.Write(bytes);
        }

        return stream.ToArray();
    }

    public static CanonicalActivationChallenge Decode(ReadOnlySpan<byte> payload)
    {
        var fields = new string[7];
        var offset = 0;
        for (var index = 0; index < fields.Length; index++)
        {
            if (payload.Length - offset < sizeof(ushort))
            {
                throw new FormatException("Challenge payload is truncated.");
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(offset, sizeof(ushort)));
            offset += sizeof(ushort);
            if (payload.Length - offset < length)
            {
                throw new FormatException("Challenge payload field is truncated.");
            }

            fields[index] = Encoding.UTF8.GetString(payload.Slice(offset, length));
            offset += length;
        }

        if (offset != payload.Length || fields[0] != ActivationCryptoFormats.ChallengeVersion
            || !Guid.TryParseExact(fields[1], "D", out var challengeId)
            || !Guid.TryParseExact(fields[2], "D", out var branchInstanceId)
            || !DateTimeOffset.TryParseExact(
                fields[6],
                "O",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var expiresAtUtc))
        {
            throw new FormatException("Challenge payload is invalid.");
        }

        return new CanonicalActivationChallenge(
            challengeId,
            branchInstanceId,
            fields[3],
            fields[4],
            fields[5],
            expiresAtUtc.ToUniversalTime());
    }
}

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Binexus.Platform.Branching.DeviceAuth;

public static class CanonicalDeviceAuthChallengeCodec
{
    public static byte[] Encode(CanonicalDeviceAuthChallenge challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        return EncodeFields(
        [
            DeviceAuthCryptoFormats.ChallengeVersion,
            challenge.ChallengeId.ToString("D"),
            challenge.Nonce,
            challenge.DeviceId.ToString("D"),
            challenge.BranchInstanceId.ToString("D"),
            DeviceAuthCryptoFormats.Audience,
            challenge.CredentialHash,
            challenge.PublicKeyFingerprint,
            FormatTimestamp(challenge.ExpiresAtUtc),
        ]);
    }

    private static byte[] EncodeFields(string[] fields)
    {
        using var stream = new MemoryStream();
        Span<byte> length = stackalloc byte[sizeof(ushort)];
        foreach (var field in fields)
        {
            var bytes = Encoding.UTF8.GetBytes(field);
            if (bytes.Length > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(fields), "A challenge field exceeds 65535 bytes.");
            }

            BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)bytes.Length);
            stream.Write(length);
            stream.Write(bytes);
        }

        return stream.ToArray();
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Binexus.Platform.Branching.Crypto;

/// <summary>Signed payload for the exchange (proof-of-possession) challenge.</summary>
public sealed record CanonicalDevicePairingExchangeChallenge(
    Guid ChallengeId,
    Guid BranchInstanceId,
    Guid PairingSessionId,
    Guid DeviceId,
    string PublicKeyFingerprint,
    string CredentialHash,
    string Nonce,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Signed payload for a receipt reissue challenge. Proves the Device still holds the private key without
/// knowing the current receipt — used to mint Receipt B after a lost poll or API restart.
/// </summary>
public sealed record CanonicalDevicePairingReceiptReissueChallenge(
    Guid ReissueChallengeId,
    Guid PairingRequestId,
    Guid BranchInstanceId,
    Guid DeviceId,
    string PublicKeyFingerprint,
    string CredentialHash,
    string Nonce,
    DateTimeOffset ExpiresAtUtc);

/// <summary>Signed payload for the post-approval confirmation challenge. Binds the terminal + receipt hash.</summary>
public sealed record CanonicalDevicePairingConfirmChallenge(
    Guid ConfirmationChallengeId,
    Guid PairingRequestId,
    Guid BranchInstanceId,
    Guid DeviceId,
    Guid TerminalId,
    string PublicKeyFingerprint,
    string CredentialHash,
    string PairingReceiptHash,
    string Nonce,
    DateTimeOffset ExpiresAtUtc);

public static class CanonicalDevicePairingChallengeCodec
{
    public static byte[] EncodeExchange(CanonicalDevicePairingExchangeChallenge challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        return Encode(
        [
            DevicePairingCryptoFormats.ExchangeChallengeVersion,
            challenge.ChallengeId.ToString("D"),
            challenge.BranchInstanceId.ToString("D"),
            challenge.PairingSessionId.ToString("D"),
            challenge.DeviceId.ToString("D"),
            challenge.PublicKeyFingerprint,
            challenge.CredentialHash,
            challenge.Nonce,
            FormatTimestamp(challenge.ExpiresAtUtc),
        ]);
    }

    public static byte[] EncodeReceiptReissue(CanonicalDevicePairingReceiptReissueChallenge challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        return Encode(
        [
            DevicePairingCryptoFormats.ReceiptReissueChallengeVersion,
            challenge.ReissueChallengeId.ToString("D"),
            challenge.PairingRequestId.ToString("D"),
            challenge.BranchInstanceId.ToString("D"),
            challenge.DeviceId.ToString("D"),
            challenge.PublicKeyFingerprint,
            challenge.CredentialHash,
            challenge.Nonce,
            FormatTimestamp(challenge.ExpiresAtUtc),
        ]);
    }

    public static byte[] EncodeConfirmation(CanonicalDevicePairingConfirmChallenge challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        return Encode(
        [
            DevicePairingCryptoFormats.ConfirmationChallengeVersion,
            challenge.ConfirmationChallengeId.ToString("D"),
            challenge.PairingRequestId.ToString("D"),
            challenge.BranchInstanceId.ToString("D"),
            challenge.DeviceId.ToString("D"),
            challenge.TerminalId.ToString("D"),
            challenge.PublicKeyFingerprint,
            challenge.CredentialHash,
            challenge.PairingReceiptHash,
            challenge.Nonce,
            FormatTimestamp(challenge.ExpiresAtUtc),
        ]);
    }

    private static byte[] Encode(string[] fields)
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

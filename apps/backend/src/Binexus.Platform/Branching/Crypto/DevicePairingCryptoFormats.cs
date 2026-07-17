namespace Binexus.Platform.Branching.Crypto;

/// <summary>
/// Stable wire-format identifiers for Branch device/terminal pairing proof-of-possession.
/// Reuses the PR 3 activation approach (ECDSA P-256 + SHA-256) with distinct challenge versions.
/// </summary>
public static class DevicePairingCryptoFormats
{
    public const string Algorithm = "ECDSA_P256_SHA256";
    public const string PublicKeyFormat = "Base64Url(SubjectPublicKeyInfo DER)";
    public const string SignatureFormat = "Base64Url(IEEE P1363)";
    public const string FingerprintFormat = "lowercase hex SHA-256 of SPKI DER bytes";
    public const string CanonicalPayload = "length-prefixed UTF-8 binary";
    public const string ExchangeChallengeVersion = "binexus-device-pairing-exchange-v1";
    public const string ConfirmationChallengeVersion = "binexus-device-pairing-confirm-v1";
    public const string ReceiptReissueChallengeVersion = "binexus-device-pairing-receipt-reissue-v1";
}

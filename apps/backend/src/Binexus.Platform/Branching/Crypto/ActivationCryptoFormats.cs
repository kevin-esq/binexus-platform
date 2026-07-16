namespace Binexus.Platform.Branching.Crypto;

/// <summary>
/// Stable wire-format identifiers for Branch Server activation proof-of-possession.
/// </summary>
public static class ActivationCryptoFormats
{
    public const string Algorithm = "ECDSA_P256_SHA256";
    public const string PublicKeyFormat = "Base64Url(SubjectPublicKeyInfo DER)";
    public const string SignatureFormat = "Base64Url(IEEE P1363)";
    public const string FingerprintFormat = "lowercase hex SHA-256 of SPKI DER bytes";
    public const string CanonicalChallengePayload = "length-prefixed UTF-8 binary";
    public const string ChallengeVersion = "binexus-branch-activation-v1";
}

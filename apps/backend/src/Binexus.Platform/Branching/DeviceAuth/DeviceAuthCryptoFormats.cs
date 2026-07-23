namespace Binexus.Platform.Branching.DeviceAuth;

public static class DeviceAuthCryptoFormats
{
    public const string ChallengeVersion = "binexus-device-auth-challenge-v1";
    public const string Audience = "binexus-branch-device-auth";
    public const string TokenAudience = "binexus-branch-device";
    public const string TokenType = "binexus-device-access";
    public const string Algorithm = "ECDSA_P256_SHA256";
    public const string DeviceAuthorizationHeader = "X-Binexus-Device-Authorization";
    public const string AuthenticationScheme = "DeviceAccessToken";
    public const string DeviceAndUserPolicy = "BranchDeviceAndUser";
    public const string DeviceOnlyPolicy = "BranchDeviceOnly";

    /// <summary>HttpContext.Items key set by device authentication when DAT validation fails.</summary>
    public const string FailureCodeItemKey = "binexus.deviceAuth.failureCode";
}

/// <summary>Fields for DAT issuance PoP. Server reconstructs from DB + challenge — never trust client hash/fingerprint.</summary>
public sealed record CanonicalDeviceAuthChallenge(
    Guid ChallengeId,
    Guid BranchInstanceId,
    Guid DeviceId,
    string PublicKeyFingerprint,
    string CredentialHash,
    string Nonce,
    DateTimeOffset ExpiresAtUtc);

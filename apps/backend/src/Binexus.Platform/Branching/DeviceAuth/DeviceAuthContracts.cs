namespace Binexus.Platform.Branching.DeviceAuth;

public static class DeviceAuthErrorCodes
{
    public const string DeviceAuthRequired = "DEVICE_AUTH_REQUIRED";
    public const string DeviceTokenInvalid = "DEVICE_TOKEN_INVALID";
    public const string DeviceTokenExpired = "DEVICE_TOKEN_EXPIRED";
    public const string DeviceRevoked = "DEVICE_REVOKED";
    public const string DeviceNotActive = "DEVICE_NOT_ACTIVE";
    public const string DeviceBranchMismatch = "DEVICE_BRANCH_MISMATCH";
    public const string DeviceTerminalMissing = "DEVICE_TERMINAL_MISSING";
    public const string DeviceTerminalDisabled = "DEVICE_TERMINAL_DISABLED";
    public const string DeviceBindingInvalid = "DEVICE_BINDING_INVALID";
    public const string DeviceProofInvalid = "DEVICE_PROOF_INVALID";
    public const string DeviceChallengeExpired = "DEVICE_CHALLENGE_EXPIRED";
    public const string DeviceChallengeReplayed = "DEVICE_CHALLENGE_REPLAYED";
    public const string DeviceStatusUnavailable = "DEVICE_STATUS_UNAVAILABLE";
    public const string UserAuthRequired = "USER_AUTH_REQUIRED";
    public const string UserBranchMismatch = "USER_BRANCH_MISMATCH";
}

public sealed class DeviceAuthException : Exception
{
    public DeviceAuthException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed record DeviceAuthChallengeResponse(
    Guid ChallengeId,
    string Nonce,
    Guid BranchInstanceId,
    DateTimeOffset ExpiresAtUtc,
    string ProtocolVersion);

public sealed record DeviceAuthTokenResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAtUtc,
    Guid DeviceId,
    Guid TerminalId,
    Guid BranchInstanceId);

public sealed record DeviceAuthMeResponse(
    Guid DeviceId,
    string Status,
    Guid TerminalId,
    Guid BranchInstanceId,
    Guid TenantId,
    Guid BranchId);

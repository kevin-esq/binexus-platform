namespace Binexus.Platform.Branching.Pairing;

public static class DevicePairingErrorCodes
{
    /// <summary>Uniform anonymous failure for the machine ceremony (bad code, signature, key, hash, reuse).</summary>
    public const string PairingInvalid = "PAIRING_INVALID";
    public const string BranchNotActive = "BRANCH_NOT_ACTIVE";
    public const string PairingLocked = "PAIRING_LOCKED";
    public const string PairingRequestNotFound = "PAIRING_REQUEST_NOT_FOUND";
    /// <summary>Admin action on a request in a non-actionable state (already terminal / expired).</summary>
    public const string PairingConflict = "PAIRING_CONFLICT";
    public const string DeviceNotFound = "DEVICE_NOT_FOUND";
    public const string Forbidden = "FORBIDDEN";
}

public sealed class DevicePairingException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record CreatePairingSessionResult(
    Guid PairingSessionId,
    string PairingCode,
    DateTimeOffset ExpiresAtUtc);

public sealed record CreateExchangeChallengeResult(
    Guid ChallengeId,
    Guid BranchInstanceId,
    string Nonce,
    DateTimeOffset ExpiresAtUtc);

public sealed record PairingExchangeResult(
    Guid PairingRequestId,
    string DeviceFingerprintShort,
    string Status,
    string PairingStatusToken,
    DateTimeOffset ExpiresAtUtc);

public sealed record PairingRequestView(
    Guid PairingRequestId,
    Guid DeviceId,
    string DeviceFingerprintShort,
    string RequestedTerminalName,
    string Status,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    Guid? TerminalId,
    DateTimeOffset? ApprovedAtUtc,
    DateTimeOffset? RejectedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record ApprovePairingRequestResult(
    Guid PairingRequestId,
    Guid DeviceId,
    Guid TerminalId,
    Guid ConfirmationChallengeId,
    string Status);

public sealed record RejectPairingRequestResult(Guid PairingRequestId, string Status);

public sealed record PairingStatusResult(
    Guid PairingRequestId,
    string Status,
    Guid BranchInstanceId,
    Guid? TerminalId,
    Guid? ConfirmationChallengeId,
    string? ConfirmationNonce,
    DateTimeOffset? ConfirmationExpiresAtUtc,
    string? PairingReceipt);

public sealed record CreateReceiptReissueChallengeResult(
    Guid ChallengeId,
    Guid BranchInstanceId,
    string Nonce,
    DateTimeOffset ExpiresAtUtc);

public sealed record ReissuePairingReceiptResult(
    Guid PairingRequestId,
    Guid BranchInstanceId,
    Guid TerminalId,
    string PairingReceipt,
    Guid ConfirmationChallengeId,
    string ConfirmationNonce,
    DateTimeOffset ConfirmationExpiresAtUtc);

public sealed record PairingConfirmResult(
    Guid PairingRequestId,
    Guid DeviceId,
    Guid TerminalId,
    string Status,
    bool AlreadyActive);

public sealed record RevokeDeviceResult(
    Guid DeviceId,
    Guid? TerminalId,
    string DeviceStatus,
    bool AlreadyRevoked);

public sealed record PairedDeviceView(
    Guid DeviceId,
    string PublicKeyFingerprint,
    string DeviceFingerprintShort,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PairedAtUtc,
    DateTimeOffset? RevokedAtUtc);

public sealed record BranchTerminalView(
    Guid TerminalId,
    Guid DeviceId,
    string Name,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ActivatedAtUtc);

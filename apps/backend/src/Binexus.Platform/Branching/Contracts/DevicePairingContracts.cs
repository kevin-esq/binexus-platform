namespace Binexus.Platform.Branching.Contracts;

// Admin surface -----------------------------------------------------------------------------------

public sealed record CreatePairingSessionResponse(
    Guid PairingSessionId,
    string PairingCode,
    DateTimeOffset ExpiresAtUtc);

public sealed record PairingRequestResponse(
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

public sealed record ApprovePairingRequestResponse(
    Guid PairingRequestId,
    Guid DeviceId,
    Guid TerminalId,
    Guid ConfirmationChallengeId,
    string Status);

public sealed record RejectPairingRequestResponse(Guid PairingRequestId, string Status);

public sealed record RevokeDeviceResponse(Guid DeviceId, Guid? TerminalId, string DeviceStatus);

public sealed record PairedDeviceResponse(
    Guid DeviceId,
    string PublicKeyFingerprint,
    string DeviceFingerprintShort,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PairedAtUtc,
    DateTimeOffset? RevokedAtUtc);

public sealed record BranchTerminalResponse(
    Guid TerminalId,
    Guid DeviceId,
    string Name,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ActivatedAtUtc);

// Machine surface ---------------------------------------------------------------------------------

public sealed record CreateExchangeChallengeRequest(
    Guid PairingSessionId,
    string PairingCode,
    Guid DeviceId,
    string PublicKey,
    string CredentialHash);

public sealed record CreateExchangeChallengeResponse(
    Guid ChallengeId,
    Guid BranchInstanceId,
    string Nonce,
    DateTimeOffset ExpiresAtUtc);

public sealed record PairingExchangeRequest(
    Guid PairingSessionId,
    string PairingCode,
    Guid DeviceId,
    string PublicKey,
    Guid ChallengeId,
    string Signature,
    string CredentialHash,
    string TerminalName);

public sealed record PairingExchangeResponse(
    Guid PairingRequestId,
    string DeviceFingerprintShort,
    string Status,
    string PairingStatusToken,
    DateTimeOffset ExpiresAtUtc);

public sealed record PairingStatusRequest(string PairingStatusToken);

public sealed record PairingStatusResponse(
    Guid PairingRequestId,
    string Status,
    Guid BranchInstanceId,
    Guid? TerminalId,
    Guid? ConfirmationChallengeId,
    string? ConfirmationNonce,
    DateTimeOffset? ConfirmationExpiresAtUtc,
    string? PairingReceipt);

public sealed record CreateReceiptReissueChallengeRequest(string PairingStatusToken);

public sealed record CreateReceiptReissueChallengeResponse(
    Guid ChallengeId,
    Guid BranchInstanceId,
    string Nonce,
    DateTimeOffset ExpiresAtUtc);

public sealed record ReissuePairingReceiptRequest(
    string PairingStatusToken,
    Guid ReissueChallengeId,
    string Signature);

public sealed record ReissuePairingReceiptResponse(
    Guid PairingRequestId,
    Guid BranchInstanceId,
    Guid TerminalId,
    string PairingReceipt,
    Guid ConfirmationChallengeId,
    string ConfirmationNonce,
    DateTimeOffset ConfirmationExpiresAtUtc);

public sealed record PairingConfirmRequest(
    Guid PairingRequestId,
    Guid ConfirmationChallengeId,
    string Signature,
    string PairingReceipt,
    string PairingStatusToken);

public sealed record PairingConfirmResponse(
    Guid PairingRequestId,
    Guid DeviceId,
    Guid TerminalId,
    string Status,
    bool AlreadyActive);

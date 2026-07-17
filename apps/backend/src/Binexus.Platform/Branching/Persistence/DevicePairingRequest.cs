namespace Binexus.Platform.Branching.Persistence;

/// <summary>
/// A Device's request to pair, awaiting explicit admin approval. Kept separate from
/// <see cref="BranchDevice"/>: proof-of-possession alone must not create a Device/Terminal.
/// Holds a snapshot of the requested material and the hash of the polling status token.
/// </summary>
public sealed class DevicePairingRequest
{
    public const string PendingApprovalStatus = "PendingApproval";
    public const string ApprovedStatus = "Approved";
    public const string RejectedStatus = "Rejected";
    public const string ExpiredStatus = "Expired";
    public const string CompletedStatus = "Completed";

    private DevicePairingRequest()
    {
    }

    public static DevicePairingRequest CreatePending(
        Guid id,
        Guid pairingSessionId,
        Guid branchInstanceId,
        Guid deviceId,
        string publicKey,
        string publicKeyFingerprint,
        string credentialHash,
        string requestedTerminalName,
        string requestedTerminalNameNormalized,
        string statusTokenHash,
        DateTimeOffset statusTokenExpiresAtUtc,
        DateTimeOffset requestedAtUtc,
        DateTimeOffset expiresAtUtc) =>
        new()
        {
            Id = id,
            PairingSessionId = pairingSessionId,
            BranchInstanceId = branchInstanceId,
            DeviceId = deviceId,
            PublicKey = publicKey,
            PublicKeyFingerprint = publicKeyFingerprint,
            CredentialHash = credentialHash,
            RequestedTerminalName = requestedTerminalName,
            RequestedTerminalNameNormalized = requestedTerminalNameNormalized,
            StatusTokenHash = statusTokenHash,
            StatusTokenExpiresAtUtc = statusTokenExpiresAtUtc,
            Status = PendingApprovalStatus,
            RequestedAtUtc = requestedAtUtc,
            ExpiresAtUtc = expiresAtUtc,
        };

    public Guid Id { get; private set; }
    public Guid PairingSessionId { get; private set; }
    public Guid BranchInstanceId { get; private set; }
    public Guid DeviceId { get; private set; }
    public string PublicKey { get; private set; } = string.Empty;
    public string PublicKeyFingerprint { get; private set; } = string.Empty;
    public string CredentialHash { get; private set; } = string.Empty;
    public string RequestedTerminalName { get; private set; } = string.Empty;
    public string RequestedTerminalNameNormalized { get; private set; } = string.Empty;
    public string Status { get; private set; } = PendingApprovalStatus;
    public string StatusTokenHash { get; private set; } = string.Empty;
    public DateTimeOffset StatusTokenExpiresAtUtc { get; private set; }
    public DateTimeOffset RequestedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public Guid? TerminalId { get; private set; }
    public string? PairingReceiptHash { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? RejectedAtUtc { get; private set; }
    public Guid? RejectedByUserId { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public uint Version { get; private set; }

    public void RotateStatusToken(string statusTokenHash, DateTimeOffset expiresAtUtc)
    {
        StatusTokenHash = statusTokenHash;
        StatusTokenExpiresAtUtc = expiresAtUtc;
    }

    public void MarkApproved(
        Guid terminalId,
        string pairingReceiptHash,
        Guid approvedByUserId,
        DateTimeOffset approvedAtUtc)
    {
        Status = ApprovedStatus;
        TerminalId = terminalId;
        PairingReceiptHash = pairingReceiptHash;
        ApprovedByUserId = approvedByUserId;
        ApprovedAtUtc = approvedAtUtc;
    }

    /// <summary>Replaces the persisted receipt hash when Receipt B is minted after PoP reissue.</summary>
    public void RotatePairingReceipt(string pairingReceiptHash) => PairingReceiptHash = pairingReceiptHash;

    public void MarkRejected(Guid rejectedByUserId, DateTimeOffset rejectedAtUtc)
    {
        Status = RejectedStatus;
        RejectedByUserId = rejectedByUserId;
        RejectedAtUtc = rejectedAtUtc;
    }

    public void MarkExpired() => Status = ExpiredStatus;

    public void MarkCompleted(DateTimeOffset completedAtUtc)
    {
        Status = CompletedStatus;
        CompletedAtUtc = completedAtUtc;
    }
}

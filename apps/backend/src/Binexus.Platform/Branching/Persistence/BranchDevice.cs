namespace Binexus.Platform.Branching.Persistence;

/// <summary>
/// Permanent local Device identity, created only after admin approval. The Branch stores the
/// public key, its fingerprint and the credential hash — never the raw device credential.
/// The <c>DeviceId</c> is minted by the client and adopted verbatim (it is the primary key).
/// </summary>
public sealed class BranchDevice
{
    public const string PendingConfirmationStatus = "PendingConfirmation";
    public const string ActiveStatus = "Active";
    public const string RevokedStatus = "Revoked";

    private BranchDevice()
    {
    }

    public static BranchDevice CreatePendingConfirmation(
        Guid deviceId,
        Guid branchInstanceId,
        string publicKey,
        string publicKeyFingerprint,
        string credentialHash,
        Guid pairingRequestId,
        DateTimeOffset createdAtUtc) =>
        new()
        {
            Id = deviceId,
            BranchInstanceId = branchInstanceId,
            PublicKey = publicKey,
            PublicKeyFingerprint = publicKeyFingerprint,
            CredentialHash = credentialHash,
            PairingRequestId = pairingRequestId,
            Status = PendingConfirmationStatus,
            CreatedAtUtc = createdAtUtc,
        };

    public Guid Id { get; private set; }
    public Guid BranchInstanceId { get; private set; }
    public string PublicKey { get; private set; } = string.Empty;
    public string PublicKeyFingerprint { get; private set; } = string.Empty;
    public string CredentialHash { get; private set; } = string.Empty;
    public string Status { get; private set; } = PendingConfirmationStatus;
    public Guid PairingRequestId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? PairedAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public Guid? RevokedByUserId { get; private set; }
    public uint Version { get; private set; }

    public void MarkActive(DateTimeOffset pairedAtUtc)
    {
        Status = ActiveStatus;
        PairedAtUtc = pairedAtUtc;
    }

    public void Revoke(Guid revokedByUserId, DateTimeOffset revokedAtUtc)
    {
        Status = RevokedStatus;
        RevokedByUserId = revokedByUserId;
        RevokedAtUtc = revokedAtUtc;
    }
}

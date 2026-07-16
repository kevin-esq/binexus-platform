namespace Binexus.Platform.Branching.Persistence;

public sealed class CloudBranchInstance
{
    public const string ActivatingStatus = "Activating";
    public const string ActiveStatus = "Active";

    private CloudBranchInstance()
    {
    }

    public static CloudBranchInstance CreateActivating(
        Guid branchInstanceId,
        Guid tenantId,
        Guid branchId,
        string installationTokenHash,
        string publicKey,
        string publicKeyFingerprint,
        Guid activationId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset activatingUntilUtc) =>
        new()
        {
            BranchInstanceId = branchInstanceId,
            TenantId = tenantId,
            BranchId = branchId,
            Status = ActivatingStatus,
            InstallationTokenHash = installationTokenHash,
            PublicKey = publicKey,
            PublicKeyFingerprint = publicKeyFingerprint,
            ActivationId = activationId,
            ActivatingUntilUtc = activatingUntilUtc,
            CreatedAtUtc = createdAtUtc,
        };

    public Guid BranchInstanceId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public string Status { get; private set; } = ActivatingStatus;
    public string InstallationTokenHash { get; private set; } = string.Empty;
    public string PublicKey { get; private set; } = string.Empty;
    public string PublicKeyFingerprint { get; private set; } = string.Empty;
    public Guid ActivationId { get; private set; }
    public DateTimeOffset? ActivatingUntilUtc { get; private set; }
    public DateTimeOffset? ActivatedAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public uint Version { get; private set; }

    public void RefreshActivating(
        Guid activationId,
        string installationTokenHash,
        string publicKey,
        string publicKeyFingerprint,
        DateTimeOffset activatingUntilUtc)
    {
        ActivationId = activationId;
        InstallationTokenHash = installationTokenHash;
        PublicKey = publicKey;
        PublicKeyFingerprint = publicKeyFingerprint;
        ActivatingUntilUtc = activatingUntilUtc;
        Status = ActivatingStatus;
        ActivatedAtUtc = null;
    }

    public void MarkActive(DateTimeOffset activatedAtUtc)
    {
        Status = ActiveStatus;
        ActivatedAtUtc = activatedAtUtc;
        ActivatingUntilUtc = null;
    }

    public void ExpireActivating()
    {
        if (Status == ActivatingStatus)
        {
            // Soft-remove by deleting the row; callers remove after this mark.
            ActivatingUntilUtc = DateTimeOffset.MinValue;
        }
    }
}

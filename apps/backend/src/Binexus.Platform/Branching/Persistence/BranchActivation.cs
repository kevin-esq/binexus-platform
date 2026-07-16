namespace Binexus.Platform.Branching.Persistence;

public sealed class BranchActivation
{
    public const string OpenStatus = "Open";
    public const string ReservedStatus = "Reserved";
    public const string ConsumedStatus = "Consumed";
    public const string ExpiredStatus = "Expired";

    private BranchActivation()
    {
    }

    public static BranchActivation CreateOpen(
        Guid id,
        Guid tenantId,
        Guid branchId,
        string codeHash,
        DateTimeOffset expiresAtUtc,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            BranchId = branchId,
            CodeHash = codeHash,
            Status = OpenStatus,
            ExpiresAtUtc = expiresAtUtc,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = createdAtUtc,
        };

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public string CodeHash { get; private set; } = string.Empty;
    public string Status { get; private set; } = OpenStatus;
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ReservedUntilUtc { get; private set; }
    public Guid? AdoptedBranchInstanceId { get; private set; }
    public string? PublicKeyFingerprint { get; private set; }
    public string? InstallationTokenHash { get; private set; }
    public string? ActivationReceiptHash { get; private set; }
    public int FailedAttemptCount { get; private set; }
    public DateTimeOffset? LockedUntilUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset? ReservedAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public uint Version { get; private set; }

    public void MarkExpired()
    {
        if (Status is OpenStatus or ReservedStatus)
        {
            Status = ExpiredStatus;
        }
    }

    public void MarkReserved(
        Guid branchInstanceId,
        string publicKeyFingerprint,
        string installationTokenHash,
        string activationReceiptHash,
        DateTimeOffset reservedAtUtc,
        DateTimeOffset reservedUntilUtc)
    {
        Status = ReservedStatus;
        AdoptedBranchInstanceId = branchInstanceId;
        PublicKeyFingerprint = publicKeyFingerprint;
        InstallationTokenHash = installationTokenHash;
        ActivationReceiptHash = activationReceiptHash;
        ReservedAtUtc = reservedAtUtc;
        ReservedUntilUtc = reservedUntilUtc;
        FailedAttemptCount = 0;
        LockedUntilUtc = null;
    }

    public void RotateReceipt(string activationReceiptHash)
    {
        if (Status != ReservedStatus)
        {
            throw new InvalidOperationException("Only Reserved activations can rotate receipts.");
        }

        ActivationReceiptHash = activationReceiptHash;
    }

    public void MarkConsumed(DateTimeOffset consumedAtUtc)
    {
        Status = ConsumedStatus;
        ConsumedAtUtc = consumedAtUtc;
    }

    public void RecordFailedAttempt(int maxFailedAttempts, DateTimeOffset nowUtc, TimeSpan lockDuration)
    {
        FailedAttemptCount++;
        if (FailedAttemptCount >= maxFailedAttempts)
        {
            LockedUntilUtc = nowUtc.Add(lockDuration);
        }
    }

    public bool IsLocked(DateTimeOffset nowUtc) =>
        LockedUntilUtc is { } until && until > nowUtc;
}

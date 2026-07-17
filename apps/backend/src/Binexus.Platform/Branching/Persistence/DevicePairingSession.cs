namespace Binexus.Platform.Branching.Persistence;

/// <summary>
/// Admin-authorized, short-lived pairing ceremony window. Holds only the HMAC of the human code.
/// Single-use: consumed when the first pairing request is created from it.
/// </summary>
public sealed class DevicePairingSession
{
    public const string OpenStatus = "Open";
    public const string ConsumedStatus = "Consumed";
    public const string ExpiredStatus = "Expired";

    private DevicePairingSession()
    {
    }

    public static DevicePairingSession CreateOpen(
        Guid id,
        Guid branchInstanceId,
        string codeHash,
        Guid createdByUserId,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc) =>
        new()
        {
            Id = id,
            BranchInstanceId = branchInstanceId,
            CodeHash = codeHash,
            Status = OpenStatus,
            CreatedByUserId = createdByUserId,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = createdAtUtc,
        };

    public Guid Id { get; private set; }
    public Guid BranchInstanceId { get; private set; }
    public string CodeHash { get; private set; } = string.Empty;
    public string Status { get; private set; } = OpenStatus;
    public Guid CreatedByUserId { get; private set; }
    public int FailedAttemptCount { get; private set; }
    public DateTimeOffset? LockedUntilUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public uint Version { get; private set; }

    public bool IsLocked(DateTimeOffset nowUtc) => LockedUntilUtc is { } until && until > nowUtc;

    public void RecordFailedAttempt(int maxFailedAttempts, DateTimeOffset nowUtc, TimeSpan lockoutDuration)
    {
        FailedAttemptCount++;
        if (FailedAttemptCount >= maxFailedAttempts)
        {
            LockedUntilUtc = nowUtc.Add(lockoutDuration);
        }
    }

    public void MarkConsumed(DateTimeOffset consumedAtUtc)
    {
        Status = ConsumedStatus;
        ConsumedAtUtc = consumedAtUtc;
    }

    public void MarkExpired()
    {
        if (Status == OpenStatus)
        {
            Status = ExpiredStatus;
        }
    }
}

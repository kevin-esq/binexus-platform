namespace Binexus.Platform.Branching.Persistence;

/// <summary>
/// Local Branch Server installation identity (singleton row). Not tenant-scoped business data.
/// </summary>
public sealed class BranchInstance
{
    public const string LocalSingletonKey = "local";

    public const string ReadyForActivationStatus = "ReadyForActivation";
    public const string ActiveStatus = "Active";

    /// <summary>
    /// Unique index on <c>singleton_key</c>. Expected concurrent race target for Ensure.
    /// </summary>
    public const string SingletonKeyUniqueIndexName = "ix_branch_instances_singleton_key";

    private BranchInstance()
    {
    }

    public static BranchInstance CreateLocal(Guid id, DateTimeOffset createdAtUtc) =>
        new()
        {
            Id = id,
            SingletonKey = LocalSingletonKey,
            Status = ReadyForActivationStatus,
            CreatedAtUtc = createdAtUtc,
        };

    public Guid Id { get; private set; }

    public string SingletonKey { get; private set; } = LocalSingletonKey;

    public string Status { get; private set; } = ReadyForActivationStatus;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid? TenantId { get; private set; }

    public Guid? BranchId { get; private set; }

    public DateTimeOffset? ActivatedAtUtc { get; private set; }

    public Guid? CloudActivationId { get; private set; }

    public void Activate(Guid tenantId, Guid branchId, Guid cloudActivationId, DateTimeOffset activatedAtUtc)
    {
        if (Status != ReadyForActivationStatus)
        {
            throw new InvalidOperationException("Branch instance is already active.");
        }

        Status = ActiveStatus;
        TenantId = tenantId;
        BranchId = branchId;
        CloudActivationId = cloudActivationId;
        ActivatedAtUtc = activatedAtUtc;
    }

    /// <summary>PostgreSQL <c>xmin</c> concurrency token (reserved for future writers).</summary>
    public uint Version { get; private set; }
}

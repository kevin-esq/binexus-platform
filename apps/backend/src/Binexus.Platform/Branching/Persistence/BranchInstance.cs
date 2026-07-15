namespace Binexus.Platform.Branching.Persistence;

/// <summary>
/// Local Branch Server installation identity (singleton row). Not tenant-scoped business data.
/// </summary>
public sealed class BranchInstance
{
    public const string LocalSingletonKey = "local";

    public const string ReadyForActivationStatus = "ReadyForActivation";

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

    /// <summary>PostgreSQL <c>xmin</c> concurrency token (reserved for future writers).</summary>
    public uint Version { get; private set; }
}

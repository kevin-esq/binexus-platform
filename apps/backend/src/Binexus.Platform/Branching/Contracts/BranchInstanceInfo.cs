namespace Binexus.Platform.Branching.Contracts;

/// <summary>Immutable Branch Server installation identity for operational surfaces.</summary>
public sealed record BranchInstanceInfo(
    Guid Id,
    BranchServerStatus Status,
    Guid? TenantId = null,
    Guid? BranchId = null);

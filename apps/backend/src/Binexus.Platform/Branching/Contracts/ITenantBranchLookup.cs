namespace Binexus.Platform.Branching.Contracts;

/// <summary>
/// Cross-context port that establishes whether a Branch belongs to a Tenant.
/// </summary>
public interface ITenantBranchLookup
{
    Task<bool> ExistsForTenantAsync(Guid tenantId, Guid branchId, CancellationToken cancellationToken = default);
}

using Binexus.Modules.Identity.Domain;
using Binexus.Platform.Branching.Contracts;
using Binexus.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Binexus.Modules.Identity.Infrastructure;

internal sealed class EfTenantBranchLookup(BinexusDbContext dbContext) : ITenantBranchLookup
{
    public Task<bool> ExistsForTenantAsync(
        Guid tenantId,
        Guid branchId,
        CancellationToken cancellationToken = default) =>
        dbContext.Set<Branch>()
            .IgnoreQueryFilters()
            .AnyAsync(x => x.TenantId == tenantId && x.Id == branchId, cancellationToken);
}

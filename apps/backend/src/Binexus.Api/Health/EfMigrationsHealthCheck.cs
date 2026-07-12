using Binexus.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Binexus.Api.Health;

/// <summary>
/// Ready when Postgres has no pending EF migrations. MinIO is intentionally excluded from readiness.
/// </summary>
public sealed class EfMigrationsHealthCheck(BinexusDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var pending = await dbContext.Database.GetPendingMigrationsAsync(cancellationToken);
        var pendingList = pending.ToList();
        if (pendingList.Count == 0)
        {
            return HealthCheckResult.Healthy("EF migrations applied.");
        }

        return HealthCheckResult.Unhealthy(
            $"Pending EF migrations: {string.Join(", ", pendingList)}");
    }
}

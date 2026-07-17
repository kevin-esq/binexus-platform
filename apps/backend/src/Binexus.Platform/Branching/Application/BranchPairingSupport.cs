using Binexus.Platform.Branching.Contracts;
using Binexus.Platform.Branching.Pairing;
using Binexus.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Binexus.Platform.Branching.Application;

/// <summary>
/// Shared pairing preconditions. The authoritative clock for expiry, lockout and concurrency is
/// PostgreSQL <c>NOW()</c> (see <see cref="GetDatabaseNowAsync"/>); <see cref="TimeProvider"/> is used
/// only for timestamps written to rows, matching the Outbox policy.
/// </summary>
internal static class BranchPairingSupport
{
    private static readonly string[] AdminRoles = ["ADMIN", "SUPER_ADMIN"];

    public static Guid RequireActiveBranch(BranchInstanceInfo instance)
    {
        if (instance.Status != BranchServerStatus.Active)
        {
            throw new DevicePairingException(
                DevicePairingErrorCodes.BranchNotActive,
                "Branch instance is not Active.");
        }

        return instance.Id;
    }

    public static void RequireAdmin(string role)
    {
        if (!AdminRoles.Contains(role, StringComparer.Ordinal))
        {
            throw new DevicePairingException(
                DevicePairingErrorCodes.Forbidden,
                "ADMIN or SUPER_ADMIN role is required.");
        }
    }

    public static void RequireCoherentTenantBranch(BranchInstanceInfo instance, Guid tenantId, Guid branchId)
    {
        if (instance.TenantId != tenantId || instance.BranchId != branchId)
        {
            throw new DevicePairingException(
                DevicePairingErrorCodes.Forbidden,
                "Caller tenant/branch does not match the active Branch instance.");
        }
    }

    public static async Task<DateTimeOffset> GetDatabaseNowAsync(
        BinexusDbContext db,
        CancellationToken cancellationToken) =>
        await db.Database
            .SqlQuery<DateTimeOffset>($"SELECT NOW() AS \"Value\"")
            .SingleAsync(cancellationToken);

    public static DevicePairingException Invalid() =>
        new(DevicePairingErrorCodes.PairingInvalid, "Pairing request is invalid.");
}

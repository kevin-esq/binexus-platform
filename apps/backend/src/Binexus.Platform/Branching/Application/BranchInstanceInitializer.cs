using Binexus.Platform.Branching.Contracts;
using Binexus.Platform.Branching.Persistence;
using Binexus.Platform.Ids;
using Binexus.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Binexus.Platform.Branching.Application;

/// <summary>
/// Idempotent singleton ensure: SELECT → INSERT → on singleton unique race SELECT again. No UPDATE.
/// </summary>
public sealed class BranchInstanceInitializer(
    BinexusDbContext db,
    IIdGenerator idGenerator,
    TimeProvider timeProvider,
    BranchInstanceMemoryStore memoryStore) : IBranchInstanceInitializer
{
    public async Task<BranchInstanceInfo> EnsureAsync(CancellationToken cancellationToken = default)
    {
        var existing = await db.BranchInstances
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.SingletonKey == BranchInstance.LocalSingletonKey,
                cancellationToken);

        if (existing is not null)
        {
            return memoryStore.Publish(ToInfo(existing));
        }

        var candidateId = idGenerator.NewId();
        var createdAt = timeProvider.GetUtcNow();
        db.BranchInstances.Add(BranchInstance.CreateLocal(candidateId, createdAt));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return memoryStore.Publish(new BranchInstanceInfo(candidateId, BranchServerStatus.ReadyForActivation));
        }
        catch (DbUpdateException ex) when (BranchInstancePostgresErrors.IsExpectedSingletonRace(ex))
        {
            db.ChangeTracker.Clear();
            var winner = await db.BranchInstances
                .AsNoTracking()
                .SingleAsync(
                    x => x.SingletonKey == BranchInstance.LocalSingletonKey,
                    cancellationToken);
            return memoryStore.Publish(ToInfo(winner));
        }
    }

    private static BranchInstanceInfo ToInfo(BranchInstance entity) =>
        new(entity.Id, ParseStatus(entity.Status));

    private static BranchServerStatus ParseStatus(string status) =>
        status == BranchInstance.ReadyForActivationStatus
            ? BranchServerStatus.ReadyForActivation
            : throw new InvalidOperationException($"Unsupported BranchInstance status '{status}'.");
}

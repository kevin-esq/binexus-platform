using Binexus.IntegrationTests.Infrastructure;
using Binexus.Modules.Identity.Domain;
using Binexus.Modules.Inventory.Application;
using Binexus.Modules.Inventory.Contracts;
using Binexus.Modules.Inventory.Domain;
using Binexus.Platform.Ids;
using Binexus.Platform.Persistence;
using Binexus.Platform.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Binexus.IntegrationTests.Inventory;

public sealed class InventoryConcurrencyTests(PostgresTestFixture postgres) : IClassFixture<PostgresTestFixture>
{
    [Fact]
    public async Task Concurrent_reserves_for_last_unit_allow_one_winner()
    {
        var (tenantId, branchId, _) = await BranchesAsync();
        var productId = $"sku-cres-{Guid.NewGuid():N}";
        await SeedStockAsync(tenantId, branchId, productId, 1);

        async Task<bool> AttemptAsync()
        {
            using var scope = postgres.CreateScope();
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            tenant.SetContext(new TenantContext(tenantId, Guid.NewGuid(), RoleNames.Admin, branchId, "race"));
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var api = scope.ServiceProvider.GetRequiredService<IInventoryReservationApi>();
            try
            {
                return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    await using var tx = await db.Database.BeginTransactionAsync();
                    var result = await api.TryReserveForOrderAsync(
                        new InventoryReserveForOrderRequest(
                            tenantId,
                            Guid.NewGuid(),
                            [new InventoryReservationLine(branchId, Guid.NewGuid(), productId, 1)]),
                        CancellationToken.None);
                    if (!result.Succeeded)
                    {
                        await tx.RollbackAsync();
                        return false;
                    }

                    await db.SaveChangesAsync();
                    await tx.CommitAsync();
                    return true;
                });
            }
            catch (DbUpdateException)
            {
                return false;
            }
            catch (InventoryDomainException ex) when (ex.Code == InventoryError.ConcurrencyConflict)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(AttemptAsync(), AttemptAsync());
        results.Count(x => x).Should().Be(1);
    }

    [Fact]
    public async Task Receive_and_cancel_conflict_on_same_pending_transfer()
    {
        var (tenantId, source, destination) = await BranchesAsync();
        var productId = $"sku-rc-{Guid.NewGuid():N}";
        await SeedStockAsync(tenantId, source, productId, 5);

        Guid transferId;
        using (var scope = postgres.CreateScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            tenant.SetContext(new TenantContext(tenantId, Guid.NewGuid(), RoleNames.Admin, source, "setup"));
            var service = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            var created = await service.CreateTransferAsync(
                new CreateStockTransferRequest(source, destination, productId, 2, "race"),
                CancellationToken.None);
            created.IsSuccess.Should().BeTrue();
            transferId = created.Value!.Id;
        }

        async Task<string> AttemptReceiveAsync()
        {
            using var scope = postgres.CreateScope();
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            tenant.SetContext(new TenantContext(tenantId, Guid.NewGuid(), RoleNames.Admin, source, "recv"));
            var service = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            var result = await service.ReceiveTransferAsync(transferId, CancellationToken.None);
            return result.IsSuccess ? "received" : result.Error!.Code;
        }

        async Task<string> AttemptCancelAsync()
        {
            using var scope = postgres.CreateScope();
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            tenant.SetContext(new TenantContext(tenantId, Guid.NewGuid(), RoleNames.Admin, source, "cancel"));
            var service = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            var result = await service.CancelTransferAsync(transferId, CancellationToken.None);
            return result.IsSuccess ? "cancelled" : result.Error!.Code;
        }

        var outcomes = await Task.WhenAll(AttemptReceiveAsync(), AttemptCancelAsync());
        outcomes.Count(x => x == "received" || x == "cancelled").Should().Be(1);
        using var verify = postgres.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var transfer = await db.Set<StockTransfer>().IgnoreQueryFilters().SingleAsync(x => x.Id == transferId);
        transfer.Status.Should().BeOneOf(StockTransferStatus.Received, StockTransferStatus.Cancelled);
    }

    private async Task<(Guid TenantId, Guid Source, Guid Destination)> BranchesAsync()
    {
        using var scope = postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var tenant = await db.Set<Tenant>().IgnoreQueryFilters().SingleAsync(x => x.Slug == "acme");
        var branches = await db.Set<Branch>().IgnoreQueryFilters().Where(x => x.TenantId == tenant.Id).ToListAsync();
        var source = branches.Single(x => x.Name == "Main");
        var destination = branches.FirstOrDefault(x => x.Name == "Secondary");
        if (destination is null)
        {
            destination = new Branch(ids.NewId(), tenant.Id, "Secondary");
            db.Add(destination);
            await db.SaveChangesAsync();
        }

        return (tenant.Id, source.Id, destination.Id);
    }

    private async Task SeedStockAsync(Guid tenantId, Guid branchId, string productId, int onHand)
    {
        using var scope = postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        db.Add(new StockItem(ids.NewId(), tenantId, branchId, productId, onHand, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }
}

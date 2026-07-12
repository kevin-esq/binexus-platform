using Binexus.IntegrationTests.Infrastructure;
using Binexus.Modules.Identity.Domain;
using Binexus.Modules.Inventory.Contracts;
using Binexus.Modules.Inventory.Domain;
using Binexus.Platform.Ids;
using Binexus.Platform.Messaging;
using Binexus.Platform.Persistence;
using Binexus.Platform.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Binexus.IntegrationTests.Inventory;

public sealed class SharedTransactionTests(PostgresTestFixture postgres) : IClassFixture<PostgresTestFixture>
{
    [Fact]
    public async Task Caller_and_inventory_roll_back_together_when_commit_is_skipped()
    {
        var (tenantId, branchId) = await SeedAsync();
        var productId = $"sku-tx-rb-{Guid.NewGuid():N}";
        await SeedStockAsync(tenantId, branchId, productId, 5);
        var orderId = Guid.NewGuid();

        using (var scope = postgres.CreateScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            tenant.SetContext(new TenantContext(tenantId, Guid.NewGuid(), RoleNames.Admin, branchId, "tx"));
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var api = scope.ServiceProvider.GetRequiredService<IInventoryReservationApi>();
            var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();

            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                db.Add(new OutboxMessage
                {
                    Id = ids.NewId(),
                    TenantId = tenantId,
                    EventName = "CALLER_MARKER",
                    PayloadJson = """{"ok":true}""",
                    OccurredAtUtc = DateTimeOffset.UtcNow,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                });
                var result = await api.TryReserveForOrderAsync(
                    new InventoryReserveForOrderRequest(
                        tenantId,
                        orderId,
                        [new InventoryReservationLine(branchId, Guid.NewGuid(), productId, 2)]),
                    CancellationToken.None);
                result.Succeeded.Should().BeTrue();
                await transaction.RollbackAsync();
            });
        }

        using var verify = postgres.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<BinexusDbContext>();
        (await verifyDb.Set<StockReservation>().IgnoreQueryFilters().CountAsync(x => x.OrderId == orderId)).Should().Be(0);
        (await verifyDb.OutboxMessages.CountAsync(x => x.EventName == "CALLER_MARKER" && x.TenantId == tenantId)).Should().Be(0);
        (await verifyDb.OutboxMessages.CountAsync(x => x.EventName == "INVENTORY_RESERVED" && x.TenantId == tenantId)).Should().Be(0);
        var item = await verifyDb.Set<StockItem>().IgnoreQueryFilters().SingleAsync(x => x.TenantId == tenantId && x.ProductId == productId);
        item.Reserved.Should().Be(0);
    }

    [Fact]
    public async Task Caller_and_inventory_commit_together_with_outbox()
    {
        var (tenantId, branchId) = await SeedAsync();
        var productId = $"sku-tx-ok-{Guid.NewGuid():N}";
        await SeedStockAsync(tenantId, branchId, productId, 5);
        var orderId = Guid.NewGuid();

        using (var scope = postgres.CreateScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            tenant.SetContext(new TenantContext(tenantId, Guid.NewGuid(), RoleNames.Admin, branchId, "tx"));
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var api = scope.ServiceProvider.GetRequiredService<IInventoryReservationApi>();
            var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();

            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                db.Add(new OutboxMessage
                {
                    Id = ids.NewId(),
                    TenantId = tenantId,
                    EventName = "CALLER_MARKER",
                    PayloadJson = """{"ok":true}""",
                    OccurredAtUtc = DateTimeOffset.UtcNow,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                });
                var result = await api.TryReserveForOrderAsync(
                    new InventoryReserveForOrderRequest(
                        tenantId,
                        orderId,
                        [new InventoryReservationLine(branchId, Guid.NewGuid(), productId, 2)]),
                    CancellationToken.None);
                result.Succeeded.Should().BeTrue();
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            });
        }

        using var verify = postgres.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<BinexusDbContext>();
        (await verifyDb.Set<StockReservation>().IgnoreQueryFilters().CountAsync(x => x.OrderId == orderId)).Should().Be(1);
        (await verifyDb.OutboxMessages.CountAsync(x => x.EventName == "CALLER_MARKER" && x.TenantId == tenantId)).Should().Be(1);
        (await verifyDb.OutboxMessages.CountAsync(x => x.EventName == "INVENTORY_RESERVED" && x.TenantId == tenantId)).Should().Be(1);
        var item = await verifyDb.Set<StockItem>().IgnoreQueryFilters().SingleAsync(x => x.TenantId == tenantId && x.ProductId == productId);
        item.Reserved.Should().Be(2);
    }

    private async Task<(Guid TenantId, Guid BranchId)> SeedAsync()
    {
        using var scope = postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var tenant = await db.Set<Tenant>().IgnoreQueryFilters().SingleAsync(x => x.Slug == "acme");
        var branch = await db.Set<Branch>().IgnoreQueryFilters().SingleAsync(x => x.TenantId == tenant.Id && x.Name == "Main");
        return (tenant.Id, branch.Id);
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

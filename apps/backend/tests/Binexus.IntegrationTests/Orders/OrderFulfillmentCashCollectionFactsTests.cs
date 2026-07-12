using Binexus.IntegrationTests.Infrastructure;
using Binexus.Modules.Identity.Application;
using Binexus.Modules.Identity.Domain;
using Binexus.Modules.Orders.Contracts;
using Binexus.Modules.Orders.Domain;
using Binexus.Platform.Ids;
using Binexus.Platform.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Binexus.IntegrationTests.Orders;

[Collection("postgres")]
public sealed class OrderFulfillmentCashCollectionFactsTests(PostgresTestFixture postgres) : IClassFixture<PostgresTestFixture>
{
    [Fact]
    public async Task Cash_collection_facts_are_tenant_scoped_and_limited_to_requested_order_ids()
    {
        var first = await SeedOrderAsync("CASH");
        var second = await SeedOrderAsync("CARD");
        var foreign = await SeedOtherTenantOrderAsync();
        var missing = Guid.CreateVersion7();

        using var scope = postgres.CreateScope();
        var fulfillment = scope.ServiceProvider.GetRequiredService<IOrderFulfillmentApi>();
        var result = await fulfillment.GetCashCollectionFactsAsync(first.TenantId, [first.OrderId, foreign, missing], CancellationToken.None);

        result.Facts.Should().ContainSingle(fact => fact.OrderId == first.OrderId && fact.PaymentMethod == "CASH");
        result.Facts.Should().NotContain(fact => fact.OrderId == second.OrderId || fact.OrderId == foreign);
        result.MissingOrderIds.Should().BeEquivalentTo([foreign, missing]);
    }

    private async Task<(Guid TenantId, Guid OrderId)> SeedOrderAsync(string paymentMethod)
    {
        using var scope = postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var tenant = await db.Set<Tenant>().IgnoreQueryFilters().SingleAsync(x => x.Slug == "acme");
        var branch = await db.Set<Branch>().IgnoreQueryFilters().SingleAsync(x => x.TenantId == tenant.Id && x.Name == "Main");
        var user = await db.Set<User>().IgnoreQueryFilters().SingleAsync(x => x.TenantId == tenant.Id && x.NormalizedEmail == EmailNormalizer.Normalize("admin@acme.test"));
        var orderId = ids.NewId();
        db.Add(new Order(orderId, tenant.Id, branch.Id, $"cash-facts-{Guid.NewGuid():N}", "USD", paymentMethod, user.Id, ids.NewId(),
            [new OrderLine(ids.NewId(), orderId, "sku", "Widget", 1, 100)], DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        return (tenant.Id, orderId);
    }

    private async Task<Guid> SeedOtherTenantOrderAsync()
    {
        using var scope = postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var tenant = new Tenant(ids.NewId(), $"cash-facts-{Guid.NewGuid():N}", "Other", DateTimeOffset.UtcNow);
        var branch = new Branch(ids.NewId(), tenant.Id, "Main");
        var user = new User(ids.NewId(), tenant.Id, $"{Guid.NewGuid():N}@cash-facts.test", $"{Guid.NewGuid():N}", "hash", RoleNames.Admin, branch.Id);
        var orderId = ids.NewId();
        db.AddRange(tenant, branch, user, new Order(orderId, tenant.Id, branch.Id, "foreign", "USD", "CASH", user.Id, ids.NewId(),
            [new OrderLine(ids.NewId(), orderId, "sku", "Widget", 1, 100)], DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        return orderId;
    }
}

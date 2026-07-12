using Binexus.IntegrationTests.Infrastructure;
using Binexus.Modules.Identity.Application;
using Binexus.Modules.Identity.Domain;
using Binexus.Modules.Logistics.Application;
using Binexus.Modules.Logistics.Domain;
using Binexus.Modules.Orders.Contracts;
using Binexus.Modules.Orders.Domain;
using Binexus.Platform.Dispatching;
using Binexus.Platform.Ids;
using Binexus.Platform.Persistence;
using Binexus.Platform.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Binexus.IntegrationTests.Logistics;

[Collection("postgres")]
public sealed class LogisticsRollbackTests(PostgresTestFixture postgres) : IClassFixture<PostgresTestFixture>
{
    [Fact]
    public async Task Confirm_delivery_rolls_back_stop_proof_order_and_outbox_when_orders_fails()
    {
        await postgres.ResetOutboxAsync();
        var seed = await SeedDispatchedStopAsync();

        using (var scope = postgres.CreateScope(services =>
        {
            services.RemoveAll<IOrderFulfillmentApi>();
            services.AddScoped<IOrderFulfillmentApi, FailingOrderFulfillmentApi>();
        }))
        {
            SetTenant(scope, seed);
            var dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            var result = await dispatcher.DispatchAsync(new ConfirmDeliveryCommand(
                seed.StopId,
                new ConfirmDeliveryRequest(new DeliveryProofRequest(null, null, "receiver", null, null, null)),
                $"rollback-{Guid.NewGuid():N}",
                null));
            result.IsFailure.Should().BeTrue();
        }

        using var verify = postgres.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var stop = await db.Set<DeliveryRouteStop>().IgnoreQueryFilters().SingleAsync(x => x.Id == seed.StopId);
        var order = await db.Set<Order>().IgnoreQueryFilters().SingleAsync(x => x.Id == seed.OrderId);
        stop.Status.Should().Be(DeliveryRouteStopStatus.Planned);
        order.State.Should().Be(OrderState.OutForDelivery);
        (await db.Set<DeliveryProof>().IgnoreQueryFilters().CountAsync(x => x.DeliveryRouteStopId == seed.StopId)).Should().Be(0);
        (await db.OutboxMessages.CountAsync(x => x.TenantId == seed.TenantId && x.EventName == "DELIVERY_CONFIRMED")).Should().Be(0);
    }

    private async Task<Seed> SeedDispatchedStopAsync()
    {
        using var scope = postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var tenant = await db.Set<Tenant>().IgnoreQueryFilters().SingleAsync(x => x.Slug == "acme");
        var branch = await db.Set<Branch>().IgnoreQueryFilters().SingleAsync(x => x.TenantId == tenant.Id && x.Name == "Main");
        var user = await db.Set<User>().IgnoreQueryFilters().SingleAsync(x => x.TenantId == tenant.Id && x.NormalizedEmail == EmailNormalizer.Normalize("admin@acme.test"));
        var now = DateTimeOffset.UtcNow;
        var orderId = ids.NewId();
        var order = new Order(orderId, tenant.Id, branch.Id, $"rollback-{Guid.NewGuid():N}", "USD", "CARD", user.Id, ids.NewId(),
            [new OrderLine(ids.NewId(), orderId, "sku", "Widget", 1, 100)], now);
        db.Add(order);
        db.Add(order.Approve(ids.NewId(), user.Id, null, now));
        db.Add(order.MoveToPicking(ids.NewId(), user.Id, null, now));
        db.Add(order.MarkReadyForDeliveryRoute(ids.NewId(), user.Id, null, now));
        db.Add(order.MarkOutForDelivery(ids.NewId(), user.Id, null, now));
        var routeId = ids.NewId();
        var route = new DeliveryRoute(routeId, tenant.Id, branch.Id, null, now, $"route-{Guid.NewGuid():N}");
        var stop = new DeliveryRouteStop(ids.NewId(), tenant.Id, branch.Id, routeId, orderId, 1, now);
        route.AddStop(stop, now, $"assign-{Guid.NewGuid():N}");
        route.Dispatch(user.Id, now, $"dispatch-{Guid.NewGuid():N}");
        db.AddRange(route, stop);
        await db.SaveChangesAsync();
        return new(tenant.Id, branch.Id, user.Id, orderId, stop.Id);
    }

    private static void SetTenant(IServiceScope scope, Seed seed) =>
        scope.ServiceProvider.GetRequiredService<ICurrentTenant>()
            .SetContext(new TenantContext(seed.TenantId, seed.UserId, RoleNames.Admin, seed.BranchId, "logistics-rollback"));

    private sealed record Seed(Guid TenantId, Guid BranchId, Guid UserId, Guid OrderId, Guid StopId);

    private sealed class FailingOrderFulfillmentApi : IOrderFulfillmentApi
    {
        public Task<OrderFulfillmentResult> MoveToPickingAsync(OrderFulfillmentRequest request, CancellationToken ct) => throw new InvalidOperationException("boom");
        public Task<OrderFulfillmentResult> MarkReadyForDeliveryRouteAsync(OrderFulfillmentRequest request, CancellationToken ct) => throw new InvalidOperationException("boom");
        public Task<OrderFulfillmentResult> MarkOutForDeliveryAsync(OrderFulfillmentRequest request, CancellationToken ct) => throw new InvalidOperationException("boom");
        public Task<OrderFulfillmentResult> MarkOutForDeliveryAsync(OrderFulfillmentBatchRequest request, CancellationToken ct) => throw new InvalidOperationException("boom");
        public Task<OrderFulfillmentResult> MarkDeliveredAsync(OrderFulfillmentRequest request, CancellationToken ct) =>
            Task.FromResult(new OrderFulfillmentResult(OrderFulfillmentOutcome.NoLongerApplicable, "ORDER_REJECTED", "boom"));
        public Task<OrderFulfillmentResult> MarkDeliveryAttemptFailedAsync(OrderFulfillmentRequest request, CancellationToken ct) => throw new InvalidOperationException("boom");
        public Task<OrderFulfillmentResult> SettleCodOrdersAsync(SettleCodOrdersRequest request, CancellationToken ct) => throw new InvalidOperationException("boom");
        public Task<CashCollectionFactsResult> GetCashCollectionFactsAsync(Guid tenantId, IReadOnlyList<Guid> orderIds, CancellationToken ct) => throw new InvalidOperationException("boom");
    }
}

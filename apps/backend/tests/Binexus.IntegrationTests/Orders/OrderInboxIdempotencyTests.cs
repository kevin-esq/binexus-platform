using System.Text.Json;
using Binexus.IntegrationTests.Infrastructure;
using Binexus.IntegrationTests.Outbox;
using Binexus.Modules.Identity.Domain;
using Binexus.Modules.Orders.Application;
using Binexus.Modules.Orders.Domain;
using Binexus.Platform.Dispatching;
using Binexus.Platform.Ids;
using Binexus.Platform.Messaging;
using Binexus.Platform.Persistence;
using Binexus.Platform.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Binexus.IntegrationTests.Orders;

[Collection("postgres")]
public sealed class OrderInboxIdempotencyTests(PostgresTestFixture fixture) : IClassFixture<PostgresTestFixture>
{
    private const string HandlerKey = "orders.lifecycle";
    private static readonly Guid FixedActorId = Guid.Parse("01970000-0000-7000-8000-000000000001");

    public static TheoryData<string, OrderState, OrderState> DuplicateLifecycleCases => new()
    {
        { "PICKING_COMPLETED", OrderState.Picking, OrderState.ReadyForDeliveryRoute },
        { "DELIVERY_CONFIRMED", OrderState.OutForDelivery, OrderState.Delivered },
        { "DELIVERY_FAILED", OrderState.OutForDelivery, OrderState.DeliveryAttemptFailed },
        { "DELIVERY_ROUTE_LIQUIDATED", OrderState.Delivered, OrderState.Settled },
    };

    [Theory]
    [MemberData(nameof(DuplicateLifecycleCases))]
    public async Task Duplicate_event_delivery_does_not_mutate_order_twice(
        string eventName,
        OrderState fromState,
        OrderState expectedState)
    {
        await fixture.ResetOutboxAsync();
        var (tenantId, branchId) = await AcmeBranchAsync();
        var orderId = await SeedOrderInStateAsync(tenantId, branchId, fromState);
        var registry = new ConfigurableEventHandlerRegistry();
        registry.SetHandlers(eventName, HandlerKey);
        var probe = new OrdersLifecycleProcessingProbe();
        var messageId = await SeedMessageAsync(tenantId, eventName, orderId);

        var firstRun = await RunProcessorAsync("orders-worker-1", registry, probe);
        var secondRun = await RunProcessorAsync("orders-worker-2", registry, probe);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var order = await db.Set<Order>().IgnoreQueryFilters().Include(x => x.Transitions).SingleAsync(x => x.Id == orderId);
        var delivery = await db.EventHandlerDeliveries.AsNoTracking().SingleAsync(x =>
            x.TenantId == tenantId && x.EventId == messageId && x.HandlerKey == HandlerKey);

        firstRun.Should().BeGreaterThanOrEqualTo(1);
        secondRun.Should().Be(0);
        probe.ProcessCount.Should().Be(1);
        delivery.Status.Should().Be(EventHandlerDeliveryStatus.Processed);
        order.State.Should().Be(expectedState);
        order.Transitions.Count(x => x.FromState == fromState && x.ToState == expectedState).Should().Be(1);
        (await OrderLifecycleOutboxCountAsync(db, tenantId, orderId, expectedState)).Should().BeLessThanOrEqualTo(1);
    }

    private async Task<int> RunProcessorAsync(
        string workerId,
        ConfigurableEventHandlerRegistry registry,
        OrdersLifecycleProcessingProbe probe)
    {
        using var scope = fixture.CreateScope(services => ConfigureServices(services, registry, probe));
        var outboxProcessor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
        return await outboxProcessor.ProcessBatchAsync(workerId, CancellationToken.None);
    }

    private static void ConfigureServices(
        IServiceCollection services,
        ConfigurableEventHandlerRegistry registry,
        OrdersLifecycleProcessingProbe probe)
    {
        services.Replace(ServiceDescriptor.Singleton<IEventHandlerRegistry>(registry));
        services.RemoveAll<IIntegrationEventProcessor>();
        services.AddSingleton(probe);
        services.AddScoped<IIntegrationEventProcessor, OrdersLifecycleEventProcessor>();
    }

    private async Task<Guid> SeedMessageAsync(Guid tenantId, string eventName, Guid orderId)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var id = ids.NewId();
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = id,
            TenantId = tenantId,
            EventName = eventName,
            PayloadJson = JsonSerializer.Serialize(new { orderId }),
            OccurredAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Status = OutboxMessageStatus.Pending,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedOrderInStateAsync(Guid tenantId, Guid branchId, OrderState state)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var orderId = ids.NewId();
        var now = DateTimeOffset.UtcNow;
        var order = new Order(
            orderId,
            tenantId,
            branchId,
            $"cust-inbox-{orderId:N}",
            "USD",
            "CASH",
            FixedActorId,
            ids.NewId(),
            [new OrderLine(ids.NewId(), orderId, $"sku-inbox-{orderId:N}", "Widget", 1, 100)],
            now);
        db.Add(order);

        foreach (var transition in TransitionsTo(state, order, ids, now))
        {
            db.Add(transition);
        }

        await db.SaveChangesAsync();
        return orderId;
    }

    private static IEnumerable<OrderTransition> TransitionsTo(
        OrderState state,
        Order order,
        IIdGenerator ids,
        DateTimeOffset now)
    {
        if (state == OrderState.Draft)
        {
            yield break;
        }

        yield return order.Approve(ids.NewId(), FixedActorId, null, now);
        if (state == OrderState.Approved)
        {
            yield break;
        }

        yield return order.MoveToPicking(ids.NewId(), FixedActorId, null, now);
        if (state == OrderState.Picking)
        {
            yield break;
        }

        yield return order.MarkReadyForDeliveryRoute(ids.NewId(), FixedActorId, null, now);
        if (state == OrderState.ReadyForDeliveryRoute)
        {
            yield break;
        }

        yield return order.MarkOutForDelivery(ids.NewId(), FixedActorId, null, now);
        if (state == OrderState.OutForDelivery)
        {
            yield break;
        }

        yield return order.MarkDelivered(ids.NewId(), FixedActorId, null, now);
        if (state == OrderState.Delivered)
        {
            yield break;
        }

        throw new InvalidOperationException($"Unsupported inbox seed state {state}.");
    }

    private async Task<(Guid TenantId, Guid BranchId)> AcmeBranchAsync()
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var tenant = await db.Set<Tenant>().IgnoreQueryFilters().SingleAsync(x => x.Slug == "acme");
        var branch = await db.Set<Branch>().IgnoreQueryFilters().SingleAsync(x => x.TenantId == tenant.Id && x.Name == "Main");
        return (tenant.Id, branch.Id);
    }

    private static async Task<int> OrderLifecycleOutboxCountAsync(
        BinexusDbContext db,
        Guid tenantId,
        Guid orderId,
        OrderState state)
    {
        var eventName = state switch
        {
            OrderState.ReadyForDeliveryRoute => "ORDER_READY_FOR_DELIVERY_ROUTE",
            OrderState.Delivered => "ORDER_DELIVERED",
            OrderState.Settled => "ORDER_SETTLED",
            _ => null,
        };
        if (eventName is null)
        {
            return 0;
        }

        var payloads = await db.OutboxMessages
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EventName == eventName)
            .Select(x => x.PayloadJson)
            .ToListAsync();
        return payloads.Count(x => x.Contains(orderId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    private sealed class OrdersLifecycleProcessingProbe
    {
        public int ProcessCount { get; private set; }

        public void Increment() => ProcessCount++;
    }

    private sealed class OrdersLifecycleEventProcessor(
        ICommandDispatcher dispatcher,
        ICurrentTenant currentTenant,
        OrdersLifecycleProcessingProbe probe) : IIntegrationEventProcessor
    {
        public string HandlerKey => OrderInboxIdempotencyTests.HandlerKey;

        public string EventName => "*";

        public async Task<IntegrationProcessOutcome> ProcessAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            var orderId = JsonDocument.Parse(message.PayloadJson).RootElement.GetProperty("orderId").GetGuid();
            currentTenant.SetContext(new TenantContext(
                message.TenantId,
                FixedActorId,
                RoleNames.Admin,
                BranchId: null,
                RequestId: message.Id.ToString()));

            var result = message.EventName switch
            {
                "PICKING_COMPLETED" => await dispatcher.DispatchAsync(
                    new MarkOrderReadyForDeliveryRouteCommand(orderId, FixedActorId, "picking completed", message.Id.ToString()),
                    cancellationToken),
                "DELIVERY_CONFIRMED" => await dispatcher.DispatchAsync(
                    new MarkOrderDeliveredCommand(orderId, FixedActorId, "delivery confirmed", message.Id.ToString()),
                    cancellationToken),
                "DELIVERY_FAILED" => await dispatcher.DispatchAsync(
                    new MarkOrderDeliveryAttemptFailedCommand(orderId, FixedActorId, "delivery failed", message.Id.ToString()),
                    cancellationToken),
                "DELIVERY_ROUTE_LIQUIDATED" => await dispatcher.DispatchAsync(
                    new SettleOrderCommand(orderId, FixedActorId, "route liquidated", message.Id.ToString()),
                    cancellationToken),
                _ => throw new InvalidOperationException($"Unsupported event {message.EventName}."),
            };

            if (result.IsFailure)
            {
                throw new InvalidOperationException(result.Error!.Code);
            }

            probe.Increment();
            return IntegrationProcessOutcome.Processed;
        }
    }
}

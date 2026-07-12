using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Binexus.Modules.Inventory.Contracts;
using Binexus.Modules.Orders.Application;
using Binexus.Modules.Orders.Contracts;
using Binexus.Modules.Orders.Domain;
using Binexus.Platform.Ids;
using Binexus.Platform.Messaging;
using Binexus.Platform.Persistence;
using Binexus.Platform.Tenancy;
using Binexus.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using static Binexus.Modules.Orders.Infrastructure.OrdersCommandSupport;

namespace Binexus.Modules.Orders.Infrastructure;

#pragma warning disable CA1725

public sealed class OrdersQueryService(BinexusDbContext db, ICurrentTenant currentTenant) : IOrdersQueryService
{
    public Task<Result<ListOrdersResult>> ListAsync(ListOrdersQuery query, CancellationToken ct) =>
        Capture(() => ListCoreAsync(query, ct));

    public Task<Result<OrderDetail>> GetAsync(Guid orderId, CancellationToken ct) =>
        Capture(() => GetCoreAsync(orderId, ct));

    public Task<Result<OrderDetail?>> FindByOperationKeyAsync(string operationKey, CancellationToken ct) =>
        Capture(() => FindByOperationKeyCoreAsync(operationKey, ct));

    private async Task<ListOrdersResult> ListCoreAsync(ListOrdersQuery query, CancellationToken ct)
    {
        var tenantId = RequireTenant();
        var limit = Math.Clamp(query.Limit ?? 50, 1, 100);
        var source = db.Set<Order>().AsNoTracking().Where(x => x.TenantId == tenantId);
        if (Guid.TryParse(query.Cursor, out var cursorId))
        {
            var cursor = await db.Set<Order>().AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == cursorId, ct)
                ?? throw new OrdersDomainException(OrdersError.InvalidCursor, "Invalid cursor.");
            source = source.Where(x => x.CreatedAtUtc < cursor.CreatedAtUtc || (x.CreatedAtUtc == cursor.CreatedAtUtc && x.Id.CompareTo(cursor.Id) < 0));
        }
        else if (!string.IsNullOrWhiteSpace(query.Cursor)) throw new OrdersDomainException(OrdersError.InvalidCursor, "Invalid cursor.");

        var rows = await source.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Take(limit + 1).ToListAsync(ct);
        var items = rows.Take(limit).Select(ToSummary).ToArray();
        return new ListOrdersResult(items, rows.Count > limit ? items[^1].Id.ToString() : null);
    }

    private async Task<OrderDetail> GetCoreAsync(Guid orderId, CancellationToken ct)
    {
        var tenantId = RequireTenant();
        var order = await db.Set<Order>().AsNoTracking().Include(x => x.Lines).Include(x => x.Transitions)
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == orderId, ct)
            ?? throw new OrdersDomainException(OrdersError.OrderNotFound, "Order not found.");
        return ToDetail(order);
    }

    private async Task<OrderDetail?> FindByOperationKeyCoreAsync(string operationKey, CancellationToken ct)
    {
        var tenantId = RequireTenant();
        var order = await db.Set<Order>().AsNoTracking().Include(x => x.Lines).Include(x => x.Transitions)
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.OperationKey == operationKey, ct);
        return order is null ? null : ToDetail(order);
    }

    private static async Task<Result<T>> Capture<T>(Func<Task<T>> action)
    {
        try
        {
            return ResultFactory.Ok(await action());
        }
        catch (OrdersDomainException ex)
        {
            return ResultFactory.Fail<T>(OrdersErrorMapping.ToDomainError(ex));
        }
    }

    private Guid RequireTenant() => currentTenant.Current?.TenantId ?? throw new OrdersDomainException("FORBIDDEN", "Tenant context is required.");
    internal static OrderSummary ToSummary(Order x) => new(x.Id, x.BranchId, x.CustomerId, ToApi(x.State), x.PaymentMethod, x.TotalCents, x.Currency, x.CreatedAtUtc, x.Lines.Count);
    internal static OrderDetail ToDetail(Order x) => new(
        x.Id, x.BranchId, x.CustomerId, ToApi(x.State), x.PaymentMethod, x.TotalCents, x.Currency, x.CreatedAtUtc, x.UpdatedAtUtc, x.CreatedByUserId,
        x.Lines.Select(l => new OrderLineSummary(l.Id, l.ProductId, l.ProductName, l.Quantity, l.UnitPriceCents, l.LineTotalCents)).ToArray(),
        x.Transitions.OrderBy(t => t.OccurredAtUtc).Select(t => new OrderTransitionSummary(t.Id, t.FromState is null ? null : ToApi(t.FromState.Value), ToApi(t.ToState), t.Reason, t.OccurredAtUtc, t.ByUserId)).ToArray());
    internal static string ToApi(OrderState state) => state switch
    {
        OrderState.Draft => "DRAFT",
        OrderState.Approved => "APPROVED",
        OrderState.Picking => "PICKING",
        OrderState.ReadyForDeliveryRoute => "READY_FOR_DELIVERY_ROUTE",
        OrderState.OutForDelivery => "OUT_FOR_DELIVERY",
        OrderState.DeliveryAttemptFailed => "DELIVERY_ATTEMPT_FAILED",
        OrderState.Delivered => "DELIVERED",
        OrderState.Settled => "SETTLED",
        OrderState.Cancelled => "CANCELLED",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };
}

public sealed class OrderFulfillmentService(BinexusDbContext db, IIdGenerator ids, TimeProvider clock) : IOrderFulfillmentApi
{
    public Task<OrderFulfillmentResult> MoveToPickingAsync(OrderFulfillmentRequest request, CancellationToken ct) =>
        ChangeAsync(request, OrderState.Picking, "ORDER_PICKING_STARTED", ct);

    public Task<OrderFulfillmentResult> MarkReadyForDeliveryRouteAsync(OrderFulfillmentRequest request, CancellationToken ct) =>
        ChangeAsync(request, OrderState.ReadyForDeliveryRoute, "ORDER_READY_FOR_DELIVERY_ROUTE", ct);

    public Task<OrderFulfillmentResult> MarkOutForDeliveryAsync(OrderFulfillmentRequest request, CancellationToken ct) =>
        ChangeAsync(request, OrderState.OutForDelivery, eventName: null, ct);

    public Task<OrderFulfillmentResult> MarkDeliveredAsync(OrderFulfillmentRequest request, CancellationToken ct) =>
        ChangeAsync(request, OrderState.Delivered, "ORDER_DELIVERED", ct);

    public Task<OrderFulfillmentResult> MarkDeliveryAttemptFailedAsync(OrderFulfillmentRequest request, CancellationToken ct) =>
        ChangeAsync(request, OrderState.DeliveryAttemptFailed, eventName: null, ct);

    public async Task<OrderFulfillmentResult> MarkOutForDeliveryAsync(OrderFulfillmentBatchRequest request, CancellationToken ct)
    {
        if (request.OrderIds.Count == 0)
        {
            return new(OrderFulfillmentOutcome.NoLongerApplicable, OrdersError.InvalidOrder, "At least one order is required.");
        }

        foreach (var orderId in request.OrderIds.Distinct())
        {
            var result = await MarkOutForDeliveryAsync(new OrderFulfillmentRequest(
                request.TenantId,
                orderId,
                request.ActorId,
                request.CorrelationId,
                request.CausationId,
                request.Reason,
                request.Source), ct);
            if (result.Outcome is OrderFulfillmentOutcome.NotFound
                or OrderFulfillmentOutcome.NoLongerApplicable
                or OrderFulfillmentOutcome.ConcurrencyConflict)
            {
                return result;
            }
        }

        return new(OrderFulfillmentOutcome.Success);
    }

    public async Task<OrderFulfillmentResult> SettleCodOrdersAsync(SettleCodOrdersRequest request, CancellationToken ct)
    {
        foreach (var orderId in request.OrderIds.Distinct())
        {
            var order = await LoadAsync(request.TenantId, orderId, ct);
            if (order is null)
            {
                return new(OrderFulfillmentOutcome.NotFound, OrdersError.OrderNotFound, "Order not found.");
            }

            if (!string.Equals(order.PaymentMethod, "CASH", StringComparison.Ordinal))
            {
                return new(OrderFulfillmentOutcome.NoLongerApplicable, OrdersError.InvalidTransition, "Only cash orders can be settled through route liquidation.");
            }

            if (order.State == OrderState.Settled)
            {
                continue;
            }

            if (!Order.CanTransition(order.State, OrderState.Settled))
            {
                return new(OrderFulfillmentOutcome.NoLongerApplicable, OrdersError.InvalidTransition, $"Cannot move order from {order.State} to {OrderState.Settled}.");
            }

            db.Add(order.Settle(
                ids.NewId(),
                request.ActorId,
                request.Reason,
                clock.GetUtcNow(),
                ToCorrelationString(request.CorrelationId)));
            Record(db, ids, request.TenantId, "ORDER_SETTLED", new { orderId = order.Id, branchId = order.BranchId }, ToCorrelationString(request.CorrelationId), clock);
        }

        return new(OrderFulfillmentOutcome.Success);
    }

    public async Task<CashCollectionFactsResult> GetCashCollectionFactsAsync(Guid tenantId, IReadOnlyList<Guid> orderIds, CancellationToken ct)
    {
        var distinctIds = orderIds.Distinct().ToArray();
        var rows = await db.Set<Order>()
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && distinctIds.Contains(x.Id))
            .Select(x => new CashCollectionFact(x.Id, x.PaymentMethod, x.TotalCents, x.Currency, OrdersQueryService.ToApi(x.State)))
            .ToListAsync(ct);
        var found = rows.Select(x => x.OrderId).ToHashSet();
        return new CashCollectionFactsResult(rows, distinctIds.Where(id => !found.Contains(id)).ToArray());
    }

    private async Task<OrderFulfillmentResult> ChangeAsync(
        OrderFulfillmentRequest request,
        OrderState target,
        string? eventName,
        CancellationToken ct)
    {
        var order = await LoadAsync(request.TenantId, request.OrderId, ct);
        if (order is null)
        {
            return new(OrderFulfillmentOutcome.NotFound, OrdersError.OrderNotFound, "Order not found.");
        }

        if (order.State == target)
        {
            return new(OrderFulfillmentOutcome.AlreadyApplied);
        }

        if (!Order.CanTransition(order.State, target))
        {
            return new(OrderFulfillmentOutcome.NoLongerApplicable, OrdersError.InvalidTransition, $"Cannot move order from {order.State} to {target}.");
        }

        db.Add(CreateTransition(order, target, request));
        if (eventName is not null)
        {
            Record(db, ids, request.TenantId, eventName, new { orderId = order.Id, branchId = order.BranchId }, ToCorrelationString(request.CorrelationId), clock);
        }

        return new(OrderFulfillmentOutcome.Success);
    }

    private Task<Order?> LoadAsync(Guid tenantId, Guid orderId, CancellationToken ct) =>
        db.Set<Order>()
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == orderId, ct);

    private OrderTransition CreateTransition(Order order, OrderState target, OrderFulfillmentRequest request)
    {
        var now = clock.GetUtcNow();
        return target switch
        {
            OrderState.Picking => order.MoveToPicking(ids.NewId(), request.ActorId, request.Reason, now, ToCorrelationString(request.CorrelationId), ToGuidString(request.CausationId), request.Source),
            OrderState.ReadyForDeliveryRoute => order.MarkReadyForDeliveryRoute(ids.NewId(), request.ActorId, request.Reason, now, ToCorrelationString(request.CorrelationId), ToGuidString(request.CausationId), request.Source),
            OrderState.OutForDelivery => order.MarkOutForDelivery(ids.NewId(), request.ActorId, request.Reason, now, ToCorrelationString(request.CorrelationId)),
            OrderState.Delivered => order.MarkDelivered(ids.NewId(), request.ActorId, request.Reason, now, ToCorrelationString(request.CorrelationId)),
            OrderState.DeliveryAttemptFailed => order.MarkDeliveryAttemptFailed(ids.NewId(), request.ActorId, request.Reason, now, ToCorrelationString(request.CorrelationId)),
            _ => throw new InvalidOperationException($"Unsupported fulfillment target {target}."),
        };
    }

    private static string? ToCorrelationString(Guid? correlationId) =>
        correlationId?.ToString("D");

    private static string ToGuidString(Guid id) => id.ToString("D");
}

public sealed class CreateOrderHandler(BinexusDbContext db, ICurrentTenant tenant, IIdGenerator ids, TimeProvider clock) : Binexus.Platform.Dispatching.ICommandHandler<CreateOrderCommand>
{
    public Task<Result> HandleAsync(CreateOrderCommand command, CancellationToken ct) => Capture(async () =>
    {
        var context = Require(tenant);
        if (command.OperationKey is not null)
        {
            var existing = await db.Set<Order>().Include(x => x.Lines)
                .SingleOrDefaultAsync(x => x.TenantId == context.TenantId && x.OperationKey == command.OperationKey, ct);
            if (existing is not null)
            {
                if (!MatchesCreatePayload(existing, command.Request))
                {
                    return Result.Failure(DomainError.Conflict(
                        OrdersError.IdempotencyKeyReused,
                        "Idempotency-Key was already used with a different payload."));
                }

                return Result.Success();
            }
        }

        var branchId = command.Request.BranchId ?? context.BranchId ?? throw new OrdersDomainException(OrdersError.InvalidOrder, "branchId is required.");
        var lines = command.Request.Lines.Select(x => new OrderLine(ids.NewId(), command.OrderId, x.ProductId, x.ProductName, x.Quantity, x.UnitPriceCents)).ToArray();
        var order = new Order(
            command.OrderId,
            context.TenantId,
            branchId,
            command.Request.CustomerId,
            command.Request.Currency,
            command.Request.PaymentMethod,
            Actor(context),
            ids.NewId(),
            lines,
            clock.GetUtcNow(),
            command.OperationKey,
            command.CorrelationId);
        db.Add(order);
        Record(db, ids, context.TenantId, "ORDER_CREATED", new { orderId = order.Id, branchId = order.BranchId, totalCents = order.TotalCents, currency = order.Currency }, command.CorrelationId, clock);
        return Result.Success();
    });
}

public sealed class ApproveOrderHandler(BinexusDbContext db, ICurrentTenant tenant, IInventoryReservationApi inventory, IIdGenerator ids, TimeProvider clock) : Binexus.Platform.Dispatching.ICommandHandler<ApproveOrderCommand>
{
    public Task<Result> HandleAsync(ApproveOrderCommand command, CancellationToken ct) => Capture(async () =>
    {
        var context = Require(tenant);
        var order = await Load(db, context.TenantId, command.OrderId, ct);
        if (await TryIdempotentTransitionAsync(db, context.TenantId, command.OrderId, command.OperationKey, OrderState.Approved, payloadFingerprint: null, ct) is { } repeated)
        {
            return repeated;
        }

        if (order.State != OrderState.Draft)
        {
            return Result.Failure(DomainError.Conflict(OrdersError.InvalidTransition, "Only draft orders can be approved."));
        }

        var reservation = await inventory.TryReserveForOrderAsync(
            new(context.TenantId, order.Id, order.Lines.Select(x => new InventoryReservationLine(order.BranchId, x.Id, x.ProductId, x.Quantity)).ToArray(), command.CorrelationId),
            ct);
        if (!reservation.Succeeded)
        {
            return Result.Failure(DomainError.Conflict(OrdersError.InsufficientStock, "Insufficient stock."));
        }

        var actorId = Actor(context);
        db.Add(order.Approve(ids.NewId(), actorId, null, clock.GetUtcNow(), command.OperationKey, command.CorrelationId));
        var eventId = ids.NewId();
        Record(db, ids, context.TenantId, "ORDER_APPROVED", new
        {
            tenantId = context.TenantId,
            orderId = order.Id,
            branchId = order.BranchId,
            eventId,
            actorId,
            lines = order.Lines.Select(line => new
            {
                orderLineId = line.Id,
                productId = line.ProductId,
                quantity = line.Quantity,
            }),
        }, command.CorrelationId, clock, eventId);
        return Result.Success();
    });
}

public sealed class CancelOrderHandler(BinexusDbContext db, ICurrentTenant tenant, IInventoryReservationApi inventory, IIdGenerator ids, TimeProvider clock) : Binexus.Platform.Dispatching.ICommandHandler<CancelOrderCommand>
{
    public Task<Result> HandleAsync(CancelOrderCommand command, CancellationToken ct) => Capture(async () =>
    {
        var context = Require(tenant);
        var order = await Load(db, context.TenantId, command.OrderId, ct);
        var fingerprint = Fingerprint(command.Reason);
        if (await TryIdempotentTransitionAsync(db, context.TenantId, command.OrderId, command.OperationKey, OrderState.Cancelled, fingerprint, ct) is { } repeated)
        {
            return repeated;
        }

        if (order.State is not (OrderState.Draft or OrderState.Approved or OrderState.DeliveryAttemptFailed))
        {
            return Result.Failure(DomainError.Conflict(OrdersError.InvalidTransition, "Order cannot be cancelled in its current state."));
        }

        // Nest parity: ORDER_CANCELLED releases any ACTIVE reservations (including DELIVERY_ATTEMPT_FAILED).
        // Business risk: physical stock may already have left the branch; documented in orders-checkpoint.
        if (order.State != OrderState.Draft)
        {
            await inventory.ReleaseForOrderAsync(new(context.TenantId, order.Id, command.CorrelationId), ct);
        }

        db.Add(order.Cancel(ids.NewId(), Actor(context), command.Reason, clock.GetUtcNow(), command.OperationKey, command.CorrelationId));
        Record(db, ids, context.TenantId, "ORDER_CANCELLED", new { orderId = order.Id, branchId = order.BranchId }, command.CorrelationId, clock);
        return Result.Success();
    });
}

public sealed class RequeueFailedDeliveryOrderHandler(BinexusDbContext db, ICurrentTenant tenant, IIdGenerator ids, TimeProvider clock) : Binexus.Platform.Dispatching.ICommandHandler<RequeueFailedDeliveryOrderCommand>
{
    public Task<Result> HandleAsync(RequeueFailedDeliveryOrderCommand command, CancellationToken ct) => Capture(async () =>
    {
        var context = Require(tenant);
        var order = await Load(db, context.TenantId, command.OrderId, ct);
        var fingerprint = Fingerprint(command.Reason);
        if (await TryIdempotentTransitionAsync(db, context.TenantId, command.OrderId, command.OperationKey, OrderState.ReadyForDeliveryRoute, fingerprint, ct) is { } repeated)
        {
            return repeated;
        }

        db.Add(order.RequeueForDelivery(ids.NewId(), Actor(context), command.Reason, clock.GetUtcNow(), command.OperationKey, command.CorrelationId));
        Record(db, ids, context.TenantId, "ORDER_READY_FOR_DELIVERY_ROUTE", new { orderId = order.Id, branchId = order.BranchId }, command.CorrelationId, clock);
        return Result.Success();
    });
}

public sealed class OrderLifecycleHandlers(BinexusDbContext db, ICurrentTenant tenant, IIdGenerator ids, TimeProvider clock) :
    Binexus.Platform.Dispatching.ICommandHandler<MoveOrderToPickingCommand>,
    Binexus.Platform.Dispatching.ICommandHandler<MarkOrderReadyForDeliveryRouteCommand>,
    Binexus.Platform.Dispatching.ICommandHandler<MarkOrderOutForDeliveryCommand>,
    Binexus.Platform.Dispatching.ICommandHandler<MarkOrderDeliveredCommand>,
    Binexus.Platform.Dispatching.ICommandHandler<MarkOrderDeliveryAttemptFailedCommand>,
    Binexus.Platform.Dispatching.ICommandHandler<SettleOrderCommand>
{
    public Task<Result> HandleAsync(MoveOrderToPickingCommand c, CancellationToken ct) => Change(c.OrderId, c.ActorId, c.Reason, c.CorrelationId, OrderState.Picking, "ORDER_PICKING_STARTED", ct);
    public Task<Result> HandleAsync(MarkOrderReadyForDeliveryRouteCommand c, CancellationToken ct) => Change(c.OrderId, c.ActorId, c.Reason, c.CorrelationId, OrderState.ReadyForDeliveryRoute, "ORDER_READY_FOR_DELIVERY_ROUTE", ct);
    public Task<Result> HandleAsync(MarkOrderOutForDeliveryCommand c, CancellationToken ct) => Change(c.OrderId, c.ActorId, c.Reason, c.CorrelationId, OrderState.OutForDelivery, null, ct);
    public Task<Result> HandleAsync(MarkOrderDeliveredCommand c, CancellationToken ct) => Change(c.OrderId, c.ActorId, c.Reason, c.CorrelationId, OrderState.Delivered, "ORDER_DELIVERED", ct);
    public Task<Result> HandleAsync(MarkOrderDeliveryAttemptFailedCommand c, CancellationToken ct) => Change(c.OrderId, c.ActorId, c.Reason, c.CorrelationId, OrderState.DeliveryAttemptFailed, null, ct);
    public Task<Result> HandleAsync(SettleOrderCommand c, CancellationToken ct) => Change(c.OrderId, c.ActorId, c.Reason, c.CorrelationId, OrderState.Settled, "ORDER_SETTLED", ct);

    private Task<Result> Change(Guid id, Guid actor, string? reason, string? correlationId, OrderState target, string? eventName, CancellationToken ct) => Capture(async () =>
    {
        var context = Require(tenant);
        var order = await Load(db, context.TenantId, id, ct);
        if (order.State == target)
        {
            return Result.Success();
        }

        var transitionId = ids.NewId();
        OrderTransition transition = target switch
        {
            OrderState.Picking => order.MoveToPicking(transitionId, actor, reason, clock.GetUtcNow(), correlationId),
            OrderState.ReadyForDeliveryRoute => order.MarkReadyForDeliveryRoute(transitionId, actor, reason, clock.GetUtcNow(), correlationId),
            OrderState.OutForDelivery => order.MarkOutForDelivery(transitionId, actor, reason, clock.GetUtcNow(), correlationId),
            OrderState.Delivered => order.MarkDelivered(transitionId, actor, reason, clock.GetUtcNow(), correlationId),
            OrderState.DeliveryAttemptFailed => order.MarkDeliveryAttemptFailed(transitionId, actor, reason, clock.GetUtcNow(), correlationId),
            OrderState.Settled => order.Settle(transitionId, actor, reason, clock.GetUtcNow(), correlationId),
            _ => throw new InvalidOperationException($"Unsupported lifecycle target {target}."),
        };
        db.Add(transition);
        if (eventName is not null)
        {
            Record(db, ids, context.TenantId, eventName, new { orderId = order.Id, branchId = order.BranchId }, correlationId, clock);
        }

        return Result.Success();
    });
}

internal static class OrdersCommandSupport
{
    internal static async Task<Result> Capture(Func<Task<Result>> action)
    {
        try
        {
            return await action();
        }
        catch (OrdersDomainException ex)
        {
            return Result.Failure(OrdersErrorMapping.ToDomainError(ex));
        }
    }

    internal static TenantContext Require(ICurrentTenant tenant) => tenant.Current ?? throw new OrdersDomainException("FORBIDDEN", "Tenant context is required.");
    internal static Guid Actor(TenantContext context) => context.UserId ?? throw new OrdersDomainException("FORBIDDEN", "User context is required.");
    internal static async Task<Order> Load(BinexusDbContext db, Guid tenantId, Guid orderId, CancellationToken ct) =>
        await db.Set<Order>().Include(x => x.Lines).SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == orderId, ct)
        ?? throw new OrdersDomainException(OrdersError.OrderNotFound, "Order not found.");

    internal static async Task<Result?> TryIdempotentTransitionAsync(
        BinexusDbContext db,
        Guid tenantId,
        Guid orderId,
        string? key,
        OrderState expectedToState,
        string? payloadFingerprint,
        CancellationToken ct)
    {
        if (key is null) return null;

        var existing = await db.Set<OrderTransition>()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.OperationKey == key, ct);
        if (existing is null) return null;

        if (existing.OrderId != orderId || existing.ToState != expectedToState)
        {
            return Result.Failure(DomainError.Conflict(
                OrdersError.IdempotencyKeyReused,
                "Idempotency-Key was already used for a different operation."));
        }

        if (payloadFingerprint is not null && Fingerprint(existing.Reason) != payloadFingerprint)
        {
            return Result.Failure(DomainError.Conflict(
                OrdersError.IdempotencyKeyReused,
                "Idempotency-Key was already used with a different payload."));
        }

        return Result.Success();
    }

    internal static bool MatchesCreatePayload(Order existing, CreateOrderRequest request)
    {
        var branchId = request.BranchId ?? existing.BranchId;
        if (existing.BranchId != branchId
            || !string.Equals(existing.CustomerId, request.CustomerId.Trim(), StringComparison.Ordinal)
            || !string.Equals(existing.Currency, request.Currency.Trim().ToUpperInvariant(), StringComparison.Ordinal)
            || !string.Equals(existing.PaymentMethod, request.PaymentMethod.Trim().ToUpperInvariant(), StringComparison.Ordinal)
            || existing.Lines.Count != request.Lines.Count)
        {
            return false;
        }

        var existingLines = existing.Lines.OrderBy(x => x.ProductId).ThenBy(x => x.Quantity).ToArray();
        var requestLines = request.Lines.OrderBy(x => x.ProductId).ThenBy(x => x.Quantity).ToArray();
        for (var i = 0; i < existingLines.Length; i++)
        {
            if (!string.Equals(existingLines[i].ProductId, requestLines[i].ProductId.Trim(), StringComparison.Ordinal)
                || !string.Equals(existingLines[i].ProductName, requestLines[i].ProductName.Trim(), StringComparison.Ordinal)
                || existingLines[i].Quantity != requestLines[i].Quantity
                || existingLines[i].UnitPriceCents != requestLines[i].UnitPriceCents)
            {
                return false;
            }
        }

        return true;
    }

    internal static string Fingerprint(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash);
    }

    internal static void Record(BinexusDbContext db, IIdGenerator ids, Guid tenantId, string name, object payload, string? correlationId, TimeProvider clock, Guid? messageId = null)
    {
        var now = clock.GetUtcNow();
        db.Add(new OutboxMessage
        {
            Id = messageId ?? ids.NewId(),
            TenantId = tenantId,
            EventName = name,
            PayloadJson = JsonSerializer.Serialize(payload),
            OccurredAtUtc = now,
            CreatedAtUtc = now,
            CorrelationId = correlationId,
        });
    }
}

using System.Text;
using System.Text.Json;
using Binexus.Modules.Orders.Contracts;
using Binexus.Modules.Warehouse.Application;
using Binexus.Modules.Warehouse.Domain;
using Binexus.Platform.Dispatching;
using Binexus.Platform.Ids;
using Binexus.Platform.Messaging;
using Binexus.Platform.Persistence;
using Binexus.Platform.Tenancy;
using Binexus.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using static Binexus.Modules.Warehouse.Infrastructure.WarehouseCommandSupport;

namespace Binexus.Modules.Warehouse.Infrastructure;

public sealed class WarehouseQueryService(BinexusDbContext db, ICurrentTenant currentTenant) : IWarehouseQueryService
{
    public Task<Result<ListPickingTasksResult>> ListAsync(ListPickingTasksQuery query, CancellationToken ct) =>
        Capture(() => ListCoreAsync(query, ct));

    public Task<Result<PickingTaskSummary>> GetAsync(Guid pickingTaskId, CancellationToken ct) =>
        Capture(() => GetCoreAsync(pickingTaskId, ct));

    private async Task<ListPickingTasksResult> ListCoreAsync(ListPickingTasksQuery query, CancellationToken ct)
    {
        var tenantId = RequireTenant();
        var limit = Math.Clamp(query.Limit ?? 50, 1, 100);
        var source = db.Set<PickingTask>()
            .AsNoTracking()
            .Include(x => x.Lines)
            .Where(x => x.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            source = source.Where(x => x.Status == WarehousePersistedEnums.ParsePickingTaskStatus(query.Status.Trim().ToUpperInvariant()));
        }

        if (query.BranchId is { } branchId)
        {
            source = source.Where(x => x.BranchId == branchId);
        }

        if (Guid.TryParse(query.Cursor, out var cursorId))
        {
            var cursor = await db.Set<PickingTask>()
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == cursorId, ct)
                ?? throw new WarehouseDomainException(WarehouseError.InvalidCursor, "Invalid cursor.");
            source = source.Where(x => x.CreatedAtUtc < cursor.CreatedAtUtc || (x.CreatedAtUtc == cursor.CreatedAtUtc && x.Id.CompareTo(cursor.Id) < 0));
        }
        else if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            throw new WarehouseDomainException(WarehouseError.InvalidCursor, "Invalid cursor.");
        }

        var rows = await source
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(limit + 1)
            .ToListAsync(ct);
        var items = rows.Take(limit).Select(ToSummary).ToArray();
        return new ListPickingTasksResult(items, rows.Count > limit ? items[^1].Id.ToString() : null);
    }

    private async Task<PickingTaskSummary> GetCoreAsync(Guid pickingTaskId, CancellationToken ct)
    {
        var tenantId = RequireTenant();
        var task = await db.Set<PickingTask>()
            .AsNoTracking()
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == pickingTaskId, ct)
            ?? throw new WarehouseDomainException(WarehouseError.PickingTaskNotFound, "Picking task not found.");
        return ToSummary(task);
    }

    private static async Task<Result<T>> Capture<T>(Func<Task<T>> action)
    {
        try
        {
            return ResultFactory.Ok(await action());
        }
        catch (WarehouseDomainException ex)
        {
            return ResultFactory.Fail<T>(WarehouseErrorMapping.ToDomainError(ex));
        }
    }

    private Guid RequireTenant() => currentTenant.Current?.TenantId ?? throw new WarehouseDomainException("FORBIDDEN", "Tenant context is required.");

    internal static PickingTaskSummary ToSummary(PickingTask task) => new(
        task.Id,
        task.BranchId,
        task.OrderId,
        WarehousePersistedEnums.ToApi(task.Status),
        task.CreatedAtUtc,
        task.UpdatedAtUtc,
        task.CompletedAtUtc,
        task.CompletedByUserId,
        task.Lines.OrderBy(x => x.OrderLineId).Select(x => new PickingLineSummary(x.Id, x.OrderLineId, x.ProductId, x.Quantity, x.PickedQuantity)).ToArray());
}

public sealed class CompletePickingTaskHandler(
    BinexusDbContext db,
    ICurrentTenant currentTenant,
    IOrderFulfillmentApi orders,
    IIdGenerator ids,
    TimeProvider clock) : ICommandHandler<CompletePickingTaskCommand>
{
    public Task<Result> HandleAsync(CompletePickingTaskCommand command, CancellationToken cancellationToken) => Capture(async () =>
    {
        var context = Require(currentTenant);
        var completionOperationKey = CompletionOperationKey(context.TenantId, command.OperationKey);
        var completedWithKey = await db.Set<PickingTask>()
            .SingleOrDefaultAsync(x => x.TenantId == context.TenantId && x.CompletionOperationKey == completionOperationKey, cancellationToken);
        if (completedWithKey is not null)
        {
            if (completedWithKey.Id == command.PickingTaskId && completedWithKey.Status == PickingTaskStatus.Completed)
            {
                return Result.Success();
            }

            throw new WarehouseDomainException(WarehouseError.PickingTaskNotPending, "Idempotency-Key was already used for a different picking task.");
        }

        var task = await db.Set<PickingTask>()
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.TenantId == context.TenantId && x.Id == command.PickingTaskId, cancellationToken)
            ?? throw new WarehouseDomainException(WarehouseError.PickingTaskNotFound, "Picking task not found.");

        if (task.Status == PickingTaskStatus.Completed)
        {
            throw new WarehouseDomainException(WarehouseError.PickingTaskNotPending, "Picking task is not pending.");
        }

        var actorId = context.UserId ?? throw new WarehouseDomainException("FORBIDDEN", "User context is required.");
        task.Complete(actorId, clock.GetUtcNow(), completionOperationKey);
        var readyResult = await orders.MarkReadyForDeliveryRouteAsync(
            new OrderFulfillmentRequest(
                context.TenantId,
                task.OrderId,
                actorId,
                ParseCorrelationId(command.CorrelationId),
                ids.NewId(),
                "picking completed",
                "warehouse.complete-picking"),
            cancellationToken);
        if (readyResult.Outcome is OrderFulfillmentOutcome.NotFound or OrderFulfillmentOutcome.NoLongerApplicable)
        {
            return Result.Failure(DomainError.Conflict(
                readyResult.Code ?? WarehouseError.PickingTaskNotPending,
                readyResult.Message ?? "Order cannot be marked ready for delivery route."));
        }

        if (readyResult.Outcome == OrderFulfillmentOutcome.ConcurrencyConflict)
        {
            return Result.Failure(DomainError.Conflict(
                readyResult.Code ?? "ORDER_CONCURRENCY_CONFLICT",
                readyResult.Message ?? "Order changed while completing picking."));
        }

        Record(db, ids, context.TenantId, "PICKING_COMPLETED", new { pickingTaskId = task.Id, orderId = task.OrderId, branchId = task.BranchId }, command.CorrelationId, clock);
        return Result.Success();
    });

    private static async Task<Result> Capture(Func<Task<Result>> action)
    {
        try
        {
            return await action();
        }
        catch (WarehouseDomainException ex)
        {
            return Result.Failure(WarehouseErrorMapping.ToDomainError(ex));
        }
    }

    private static TenantContext Require(ICurrentTenant currentTenant) =>
        currentTenant.Current ?? throw new WarehouseDomainException("FORBIDDEN", "Tenant context is required.");

    private static string CompletionOperationKey(Guid tenantId, string key) =>
        $"warehouse-complete:{tenantId:D}:{key}";

    private static Guid? ParseCorrelationId(string? value) =>
        Guid.TryParse(value, out var correlationId) ? correlationId : null;
}

public sealed class OrderApprovedWarehouseProcessor(
    BinexusDbContext db,
    IOrderFulfillmentApi orders,
    IIdGenerator ids,
    TimeProvider clock) : IIntegrationEventProcessor
{
    public const string Key = "warehouse.order-approved";

    public string HandlerKey => Key;

    public string EventName => "ORDER_APPROVED";

    public async Task<IntegrationProcessOutcome> ProcessAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var payload = Parse(message);
        var existing = await db.Set<PickingTask>()
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.TenantId == message.TenantId && x.OrderId == payload.OrderId, cancellationToken);
        if (existing is not null)
        {
            VerifyExistingTask(existing, payload);
        }

        var moveResult = await orders.MoveToPickingAsync(
            new OrderFulfillmentRequest(
                message.TenantId,
                payload.OrderId,
                payload.ActorId,
                ParseCorrelationId(message.CorrelationId),
                message.Id,
                "order approved",
                Key),
            cancellationToken);
        if (moveResult.Outcome is OrderFulfillmentOutcome.NotFound or OrderFulfillmentOutcome.NoLongerApplicable)
        {
            throw new IgnoredHandlerException(
                moveResult.Code ?? "warehouse.order_approved_ignored",
                moveResult.Message ?? "ORDER_APPROVED no longer applies.");
        }

        if (moveResult.Outcome == OrderFulfillmentOutcome.ConcurrencyConflict)
        {
            throw new InvalidOperationException(moveResult.Message ?? "Order fulfillment concurrency conflict.");
        }

        if (existing is null)
        {
            var taskId = ids.NewId();
            var lines = payload.Lines
                .Select(line => new PickingLine(ids.NewId(), message.TenantId, taskId, line.OrderLineId, line.ProductId, line.Quantity))
                .ToArray();
            db.Add(new PickingTask(taskId, message.TenantId, payload.BranchId, payload.OrderId, message.Id, lines, clock.GetUtcNow()));
        }

        return IntegrationProcessOutcome.Processed;
    }

    private static OrderApprovedPayload Parse(OutboxMessage message)
    {
        if (Encoding.UTF8.GetByteCount(message.PayloadJson) > 64 * 1024)
        {
            throw new PermanentHandlerException("warehouse.invalid_order_approved", "ORDER_APPROVED payload exceeds 64KB.");
        }

        try
        {
            using var document = JsonDocument.Parse(message.PayloadJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw InvalidPayload("ORDER_APPROVED payload must be an object.");
            }

            var tenantId = RequiredGuid(root, "tenantId");
            if (tenantId != message.TenantId)
            {
                throw InvalidPayload("ORDER_APPROVED tenantId must match the envelope tenant.");
            }

            var eventId = RequiredGuid(root, "eventId");
            if (eventId != message.Id)
            {
                throw InvalidPayload("ORDER_APPROVED eventId must match the outbox message id.");
            }

            var lines = root.GetProperty("lines")
                .EnumerateArray()
                .Select(line => new OrderApprovedLine(
                    RequiredGuid(line, "orderLineId"),
                    RequiredString(line, "productId"),
                    RequiredInt(line, "quantity")))
                .ToArray();

            if (lines.Length == 0)
            {
                throw new PermanentHandlerException("warehouse.invalid_order_approved", "ORDER_APPROVED payload requires at least one line.");
            }

            if (lines.Length > 200)
            {
                throw InvalidPayload("ORDER_APPROVED payload cannot contain more than 200 lines.");
            }

            if (lines.Select(x => x.OrderLineId).Distinct().Count() != lines.Length)
            {
                throw InvalidPayload("ORDER_APPROVED payload requires unique orderLineIds.");
            }

            if (lines.Any(x => x.Quantity <= 0))
            {
                throw InvalidPayload("ORDER_APPROVED quantities must be positive.");
            }

            if (lines.Any(x => string.IsNullOrWhiteSpace(x.ProductId) || x.ProductId.Length > 256))
            {
                throw InvalidPayload("ORDER_APPROVED productId values must be 1-256 characters.");
            }

            return new OrderApprovedPayload(
                tenantId,
                RequiredGuid(root, "orderId"),
                RequiredGuid(root, "branchId"),
                OptionalGuid(root, "actorId"),
                eventId,
                lines);
        }
        catch (KeyNotFoundException ex)
        {
            throw new PermanentHandlerException("warehouse.invalid_order_approved", ex.Message);
        }
        catch (JsonException ex)
        {
            throw new PermanentHandlerException("warehouse.invalid_order_approved", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw new PermanentHandlerException("warehouse.invalid_order_approved", ex.Message);
        }
    }

    private static void VerifyExistingTask(PickingTask task, OrderApprovedPayload payload)
    {
        if (task.BranchId != payload.BranchId)
        {
            throw new PermanentHandlerException("warehouse.order_approved_branch_mismatch", "Existing picking task branch does not match ORDER_APPROVED payload.");
        }

        var existingLines = task.Lines.OrderBy(x => x.OrderLineId).ToArray();
        var payloadLines = payload.Lines.OrderBy(x => x.OrderLineId).ToArray();
        if (existingLines.Length != payloadLines.Length)
        {
            throw new PermanentHandlerException("warehouse.order_approved_lines_mismatch", "Existing picking task lines do not match ORDER_APPROVED payload.");
        }

        for (var i = 0; i < existingLines.Length; i++)
        {
            if (existingLines[i].OrderLineId != payloadLines[i].OrderLineId
                || !string.Equals(existingLines[i].ProductId, payloadLines[i].ProductId, StringComparison.Ordinal)
                || existingLines[i].Quantity != payloadLines[i].Quantity)
            {
                throw new PermanentHandlerException("warehouse.order_approved_lines_mismatch", "Existing picking task lines do not match ORDER_APPROVED payload.");
            }
        }
    }

    private static Guid RequiredGuid(JsonElement element, string propertyName)
    {
        var value = element.GetProperty(propertyName).GetGuid();
        return value == Guid.Empty ? throw InvalidPayload($"{propertyName} must be a non-empty UUID.") : value;
    }

    private static Guid? OptionalGuid(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var guid = value.GetGuid();
        return guid == Guid.Empty ? throw InvalidPayload($"{propertyName} must be a non-empty UUID when present.") : guid;
    }

    private static string RequiredString(JsonElement element, string propertyName) =>
        element.GetProperty(propertyName).GetString() ?? throw InvalidPayload($"{propertyName} is required.");

    private static int RequiredInt(JsonElement element, string propertyName) =>
        element.GetProperty(propertyName).GetInt32();

    private static PermanentHandlerException InvalidPayload(string message) =>
        new("warehouse.invalid_order_approved", message);

    private static Guid? ParseCorrelationId(string? value) =>
        Guid.TryParse(value, out var correlationId) ? correlationId : null;

    private sealed record OrderApprovedPayload(Guid TenantId, Guid OrderId, Guid BranchId, Guid? ActorId, Guid EventId, IReadOnlyList<OrderApprovedLine> Lines);

    private sealed record OrderApprovedLine(Guid OrderLineId, string ProductId, int Quantity);
}

internal static class WarehouseCommandSupport
{
    internal static void Record(BinexusDbContext db, IIdGenerator ids, Guid tenantId, string name, object payload, string? correlationId, TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        db.Add(new OutboxMessage
        {
            Id = ids.NewId(),
            TenantId = tenantId,
            EventName = name,
            PayloadJson = JsonSerializer.Serialize(payload),
            OccurredAtUtc = now,
            CreatedAtUtc = now,
            CorrelationId = correlationId,
        });
    }
}

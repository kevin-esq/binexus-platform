using Binexus.Modules.Inventory.Application;
using Binexus.Modules.Inventory.Domain;
using Binexus.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Binexus.Modules.Inventory.Infrastructure;

public sealed class InventoryStockService(InventoryPersistence store) : IInventoryService
{
    public Task<Result<ListStockItemsResult>> ListStockAsync(ListStockItemsQuery query, CancellationToken ct) =>
        Capture(() => ListStockCoreAsync(query, ct));

    private async Task<ListStockItemsResult> ListStockCoreAsync(ListStockItemsQuery query, CancellationToken ct)
    {
        var tenantId = store.RequireTenantId();
        var limit = Math.Clamp(query.Limit ?? 50, 1, 100);
        var q = store.Db.Set<StockItem>().AsNoTracking()
            .Where(x => x.TenantId == tenantId);
        if (query.BranchId is Guid branchId)
        {
            q = q.Where(x => x.BranchId == branchId);
        }

        if (!string.IsNullOrWhiteSpace(query.ProductId))
        {
            q = q.Where(x => x.ProductId == query.ProductId);
        }

        if (Guid.TryParse(query.Cursor, out var cursorId))
        {
            var cursor = await store.Db.Set<StockItem>().AsNoTracking()
                .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == cursorId, ct);
            if (cursor is null)
            {
                throw new InventoryDomainException(InventoryError.InvalidCursor, "Invalid cursor.");
            }

            q = q.Where(x =>
                x.CreatedAtUtc > cursor.CreatedAtUtc
                || (x.CreatedAtUtc == cursor.CreatedAtUtc && x.Id.CompareTo(cursor.Id) > 0));
        }
        else if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            throw new InventoryDomainException(InventoryError.InvalidCursor, "Invalid cursor.");
        }

        var rows = await q.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Take(limit + 1)
            .ToListAsync(ct);
        var items = rows.Take(limit).Select(ToSummary).ToArray();
        return new(items, rows.Count > limit ? items[^1].Id.ToString() : null);
    }

    public Task<Result<AdjustStockResult>> AdjustAsync(AdjustStockRequest request, CancellationToken ct) =>
        Capture(() => AdjustCoreAsync(request, ct));

    private async Task<AdjustStockResult> AdjustCoreAsync(AdjustStockRequest request, CancellationToken ct)
    {
        InventoryPersistence.ValidateProductId(request.ProductId);
        InventoryPersistence.ValidateReason(request.Reason, required: true);
        if (request.Delta == 0)
        {
            throw new InventoryDomainException(InventoryError.InvalidAdjustment, "delta must be a non-zero integer.");
        }

        var tenantId = store.RequireTenantId();
        if (!string.IsNullOrWhiteSpace(request.OperationKey))
        {
            var prior = await store.Db.Set<StockMovement>()
                .SingleOrDefaultAsync(
                    x => x.TenantId == tenantId && x.OperationKey == request.OperationKey,
                    ct);
            if (prior is not null)
            {
                if (prior.BranchId != request.BranchId
                    || prior.ProductId != request.ProductId
                    || prior.Quantity != request.Delta
                    || prior.Type != StockMovementType.Adjustment)
                {
                    throw new InventoryDomainException(
                        InventoryError.IdempotencyKeyConflict,
                        "Idempotency key was already used with a different adjustment.");
                }

                var existingItem = await store.RequireItemAsync(tenantId, prior.BranchId, prior.ProductId, ct);
                return new(ToSummary(existingItem), prior.Id);
            }
        }

        var now = store.Clock.GetUtcNow();
        var item = await store.Db.Set<StockItem>()
            .SingleOrDefaultAsync(
                x => x.TenantId == tenantId && x.BranchId == request.BranchId && x.ProductId == request.ProductId,
                ct);
        if (item is null)
        {
            if (request.Delta < 0)
            {
                throw new InventoryDomainException(
                    InventoryError.InvalidAdjustment,
                    "No stock item for product at branch.");
            }

            item = new StockItem(store.Ids.NewId(), tenantId, request.BranchId, request.ProductId, request.Delta, now);
            store.Db.Add(item);
        }
        else
        {
            item.Adjust(request.Delta, now);
        }

        var movement = new StockMovement(
            store.Ids.NewId(),
            tenantId,
            request.BranchId,
            request.ProductId,
            request.Delta,
            StockMovementType.Adjustment,
            request.OperationKey,
            now);
        store.Db.Add(movement);
        store.RecordEvent(
            tenantId,
            "STOCK_ADJUSTED",
            new
            {
                stockItemId = item.Id,
                branchId = request.BranchId,
                productId = request.ProductId,
                delta = request.Delta,
                movementId = movement.Id,
            },
            correlationId: null);
        await store.PersistAsync(ct);
        return new(ToSummary(item), movement.Id);
    }

    public Task<Result<StockTransferSummary>> CreateTransferAsync(CreateStockTransferRequest request, CancellationToken ct) =>
        Capture(() => CreateTransferCoreAsync(request, ct));

    private async Task<StockTransferSummary> CreateTransferCoreAsync(CreateStockTransferRequest request, CancellationToken ct)
    {
        InventoryPersistence.ValidateProductId(request.ProductId);
        if (!string.IsNullOrWhiteSpace(request.Reason))
        {
            InventoryPersistence.ValidateReason(request.Reason, required: false);
        }

        var tenantId = store.RequireTenantId();
        if (!string.IsNullOrWhiteSpace(request.OperationKey))
        {
            var prior = await store.Db.Set<StockTransfer>()
                .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.OperationKey == request.OperationKey, ct);
            if (prior is not null)
            {
                if (prior.SourceBranchId != request.SourceBranchId
                    || prior.DestinationBranchId != request.DestinationBranchId
                    || prior.ProductId != request.ProductId
                    || prior.Quantity != request.Quantity)
                {
                    throw new InventoryDomainException(
                        InventoryError.IdempotencyKeyConflict,
                        "Idempotency key was already used with a different transfer payload.");
                }

                return ToSummary(prior);
            }
        }

        var now = store.Clock.GetUtcNow();
        var source = await store.RequireItemAsync(tenantId, request.SourceBranchId, request.ProductId, ct);
        source.Reserve(request.Quantity, now);
        var transfer = new StockTransfer(
            store.Ids.NewId(),
            tenantId,
            request.SourceBranchId,
            request.DestinationBranchId,
            request.ProductId,
            request.Quantity,
            string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
            now);
        if (!string.IsNullOrWhiteSpace(request.OperationKey))
        {
            transfer.AssignOperationKey(request.OperationKey);
        }

        store.Db.Add(transfer);
        store.RecordEvent(
            tenantId,
            "STOCK_TRANSFER_CREATED",
            new
            {
                transferId = transfer.Id,
                sourceBranchId = transfer.SourceBranchId,
                destinationBranchId = transfer.DestinationBranchId,
                productId = transfer.ProductId,
                quantity = transfer.Quantity,
            },
            correlationId: null);
        await store.PersistAsync(ct);
        return ToSummary(transfer);
    }

    public Task<Result<ListStockTransfersResult>> ListTransfersAsync(ListStockTransfersQuery query, CancellationToken ct) =>
        Capture(() => ListTransfersCoreAsync(query, ct));

    private async Task<ListStockTransfersResult> ListTransfersCoreAsync(ListStockTransfersQuery query, CancellationToken ct)
    {
        var tenantId = store.RequireTenantId();
        var limit = Math.Clamp(query.Limit ?? 50, 1, 100);
        var q = store.Db.Set<StockTransfer>().AsNoTracking().Where(x => x.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(query.Status)
            && Enum.TryParse<StockTransferStatus>(query.Status, ignoreCase: true, out var status))
        {
            q = q.Where(x => x.Status == status);
        }

        if (Guid.TryParse(query.Cursor, out var cursorId))
        {
            var cursor = await store.Db.Set<StockTransfer>().AsNoTracking()
                .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == cursorId, ct);
            if (cursor is null)
            {
                throw new InventoryDomainException(InventoryError.InvalidCursor, "Invalid cursor.");
            }

            q = q.Where(x =>
                x.CreatedAtUtc < cursor.CreatedAtUtc
                || (x.CreatedAtUtc == cursor.CreatedAtUtc && x.Id.CompareTo(cursor.Id) < 0));
        }
        else if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            throw new InventoryDomainException(InventoryError.InvalidCursor, "Invalid cursor.");
        }

        var rows = await q.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Take(limit + 1)
            .ToListAsync(ct);
        var items = rows.Take(limit).Select(ToSummary).ToArray();
        return new(items, rows.Count > limit ? items[^1].Id.ToString() : null);
    }

    public Task<Result<ReceiveStockTransferResult>> ReceiveTransferAsync(Guid transferId, CancellationToken ct) =>
        Capture(() => ReceiveTransferCoreAsync(transferId, ct));

    private async Task<ReceiveStockTransferResult> ReceiveTransferCoreAsync(Guid transferId, CancellationToken ct)
    {
        var tenantId = store.RequireTenantId();
        var transfer = await store.Db.Set<StockTransfer>()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == transferId, ct)
            ?? throw new InventoryDomainException(InventoryError.TransferNotFound, "Transfer not found.");

        if (transfer.Status == StockTransferStatus.Received)
        {
            var outKey = InventoryPersistence.TransferOutKey(transfer.Id);
            var inKey = InventoryPersistence.TransferInKey(transfer.Id);
            var outgoing = await store.Db.Set<StockMovement>()
                .SingleAsync(x => x.TenantId == tenantId && x.OperationKey == outKey, ct);
            var incoming = await store.Db.Set<StockMovement>()
                .SingleAsync(x => x.TenantId == tenantId && x.OperationKey == inKey, ct);
            return new(ToSummary(transfer), outgoing.Id, incoming.Id);
        }

        var now = store.Clock.GetUtcNow();
        var source = await store.RequireItemAsync(transfer.TenantId, transfer.SourceBranchId, transfer.ProductId, ct);
        var destination = await store.Db.Set<StockItem>()
            .SingleOrDefaultAsync(
                x => x.TenantId == transfer.TenantId
                    && x.BranchId == transfer.DestinationBranchId
                    && x.ProductId == transfer.ProductId,
                ct);
        if (destination is null)
        {
            destination = new StockItem(
                store.Ids.NewId(),
                transfer.TenantId,
                transfer.DestinationBranchId,
                transfer.ProductId,
                0,
                now);
            store.Db.Add(destination);
        }

        source.ReceiveTransferOut(transfer.Quantity, now);
        destination.Adjust(transfer.Quantity, now);
        transfer.Receive(now);

        var outgoingMovement = new StockMovement(
            store.Ids.NewId(),
            transfer.TenantId,
            source.BranchId,
            source.ProductId,
            -transfer.Quantity,
            StockMovementType.TransferOut,
            InventoryPersistence.TransferOutKey(transfer.Id),
            now);
        var incomingMovement = new StockMovement(
            store.Ids.NewId(),
            transfer.TenantId,
            destination.BranchId,
            destination.ProductId,
            transfer.Quantity,
            StockMovementType.TransferIn,
            InventoryPersistence.TransferInKey(transfer.Id),
            now);
        store.Db.AddRange(outgoingMovement, incomingMovement);
        store.RecordEvent(
            tenantId,
            "STOCK_TRANSFER_RECEIVED",
            new
            {
                transferId = transfer.Id,
                sourceBranchId = transfer.SourceBranchId,
                destinationBranchId = transfer.DestinationBranchId,
                productId = transfer.ProductId,
                quantity = transfer.Quantity,
            },
            correlationId: null);
        await store.PersistAsync(ct);
        return new(ToSummary(transfer), outgoingMovement.Id, incomingMovement.Id);
    }

    public Task<Result<StockTransferSummary>> CancelTransferAsync(Guid transferId, CancellationToken ct) =>
        Capture(() => CancelTransferCoreAsync(transferId, ct));

    private async Task<StockTransferSummary> CancelTransferCoreAsync(Guid transferId, CancellationToken ct)
    {
        var tenantId = store.RequireTenantId();
        var transfer = await store.Db.Set<StockTransfer>()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == transferId, ct)
            ?? throw new InventoryDomainException(InventoryError.TransferNotFound, "Transfer not found.");

        if (transfer.Status == StockTransferStatus.Cancelled)
        {
            return ToSummary(transfer);
        }

        var now = store.Clock.GetUtcNow();
        var source = await store.RequireItemAsync(transfer.TenantId, transfer.SourceBranchId, transfer.ProductId, ct);
        source.Release(transfer.Quantity, now);
        transfer.Cancel(now);
        store.RecordEvent(
            tenantId,
            "STOCK_TRANSFER_CANCELLED",
            new
            {
                transferId = transfer.Id,
                sourceBranchId = transfer.SourceBranchId,
                destinationBranchId = transfer.DestinationBranchId,
                productId = transfer.ProductId,
                quantity = transfer.Quantity,
            },
            correlationId: null);
        await store.PersistAsync(ct);
        return ToSummary(transfer);
    }

    private static async Task<Result<T>> Capture<T>(Func<Task<T>> action)
    {
        try
        {
            return ResultFactory.Ok(await action());
        }
        catch (InventoryDomainException ex)
        {
            return ResultFactory.Fail<T>(InventoryErrorMapping.ToDomainError(ex));
        }
    }

    private static StockItemSummary ToSummary(StockItem x) =>
        new(x.Id, x.BranchId, x.ProductId, x.OnHand, x.Reserved, x.Available, x.CreatedAtUtc, x.UpdatedAtUtc);

    private static StockTransferSummary ToSummary(StockTransfer x) =>
        new(
            x.Id,
            x.SourceBranchId,
            x.DestinationBranchId,
            x.ProductId,
            x.Quantity,
            InventoryPersistedEnums.ToApi(x.Status),
            x.Reason,
            x.CreatedAtUtc,
            x.UpdatedAtUtc,
            x.ReceivedAtUtc,
            x.CancelledAtUtc);
}

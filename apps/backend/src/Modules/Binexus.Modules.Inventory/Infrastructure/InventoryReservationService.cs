using Binexus.Modules.Inventory.Contracts;
using Binexus.Modules.Inventory.Domain;
using Microsoft.EntityFrameworkCore;

namespace Binexus.Modules.Inventory.Infrastructure;

public sealed class InventoryReservationService(InventoryPersistence store) : IInventoryReservationApi
{
    public async Task<InventoryReservationResult> TryReserveForOrderAsync(
        InventoryReserveForOrderRequest request,
        CancellationToken ct)
    {
        store.EnsureTenantMatches(request.TenantId);
        if (request.Lines.Count == 0)
        {
            return new(false, InventoryError.ValidationQuantity);
        }

        var existing = await store.Db.Set<StockReservation>()
            .Where(x => x.TenantId == request.TenantId && x.OrderId == request.OrderId)
            .ToListAsync(ct);
        if (existing.Count > 0)
        {
            if (existing.Count == request.Lines.Count
                && existing.All(x => x.Status == StockReservationStatus.Active)
                && request.Lines.All(line =>
                    existing.Any(e =>
                        e.OrderLineId == line.OrderLineId
                        && e.ProductId == line.ProductId
                        && e.Quantity == line.Quantity)))
            {
                return new(true, null);
            }

            return new(false, InventoryError.InsufficientStock);
        }

        var now = store.Clock.GetUtcNow();
        var stockByKey = new Dictionary<(Guid BranchId, string ProductId), StockItem>();
        var failures = new List<object>();

        foreach (var line in request.Lines)
        {
            InventoryPersistence.ValidateProductId(line.ProductId);
            if (line.Quantity <= 0)
            {
                failures.Add(new { orderLineId = line.OrderLineId, productId = line.ProductId, requested = line.Quantity, available = 0 });
                continue;
            }

            var key = (line.BranchId, line.ProductId);
            if (!stockByKey.TryGetValue(key, out var item))
            {
                item = await store.Db.Set<StockItem>().SingleOrDefaultAsync(
                    x => x.TenantId == request.TenantId
                        && x.BranchId == line.BranchId
                        && x.ProductId == line.ProductId,
                    ct);
                if (item is null)
                {
                    failures.Add(new { orderLineId = line.OrderLineId, productId = line.ProductId, requested = line.Quantity, available = 0 });
                    continue;
                }

                stockByKey[key] = item;
            }

            if (item.Available < line.Quantity)
            {
                failures.Add(new
                {
                    orderLineId = line.OrderLineId,
                    productId = line.ProductId,
                    requested = line.Quantity,
                    available = item.Available,
                });
            }
        }

        if (failures.Count > 0)
        {
            return new(false, InventoryError.InsufficientStock);
        }

        foreach (var line in request.Lines)
        {
            var item = stockByKey[(line.BranchId, line.ProductId)];
            item.Reserve(line.Quantity, now);
            store.Db.Add(new StockReservation(
                store.Ids.NewId(),
                request.TenantId,
                line.BranchId,
                request.OrderId,
                line.OrderLineId,
                line.ProductId,
                line.Quantity,
                StockReservationStatus.Active,
                now));
            store.Db.Add(new StockMovement(
                store.Ids.NewId(),
                request.TenantId,
                line.BranchId,
                line.ProductId,
                line.Quantity,
                StockMovementType.Reserve,
                InventoryPersistence.OrderReserveKey(request.OrderId, line.OrderLineId),
                now));
        }

        store.RecordEvent(
            request.TenantId,
            "INVENTORY_RESERVED",
            new { orderId = request.OrderId, branchId = request.Lines[0].BranchId, lineCount = request.Lines.Count },
            request.CorrelationId);
        return new(true, null);
    }

    public async Task ReleaseForOrderAsync(InventoryReleaseForOrderRequest request, CancellationToken ct)
    {
        store.EnsureTenantMatches(request.TenantId);
        var reservations = await store.Db.Set<StockReservation>()
            .Where(x =>
                x.TenantId == request.TenantId
                && x.OrderId == request.OrderId
                && x.Status == StockReservationStatus.Active)
            .ToListAsync(ct);
        if (reservations.Count == 0)
        {
            return;
        }

        var now = store.Clock.GetUtcNow();
        foreach (var reservation in reservations)
        {
            var item = await store.RequireItemAsync(request.TenantId, reservation.BranchId, reservation.ProductId, ct);
            item.Release(reservation.Quantity, now);
            reservation.Release(now);
            store.Db.Add(new StockMovement(
                store.Ids.NewId(),
                request.TenantId,
                reservation.BranchId,
                reservation.ProductId,
                -reservation.Quantity,
                StockMovementType.Release,
                $"release:{reservation.Id}",
                now));
        }

        store.RecordEvent(
            request.TenantId,
            "INVENTORY_RELEASED",
            new { orderId = request.OrderId, branchId = reservations[0].BranchId, lineCount = reservations.Count },
            request.CorrelationId);
    }
}

using Binexus.Modules.Inventory.Contracts;
using Binexus.Modules.Inventory.Domain;
using Microsoft.EntityFrameworkCore;

namespace Binexus.Modules.Inventory.Infrastructure;

public sealed class InventorySaleService(InventoryPersistence store) : IInventorySaleApi
{
    public async Task<InventorySaleDecrementResult> DecrementForSaleAsync(
        InventorySaleDecrementRequest request,
        CancellationToken ct)
    {
        store.EnsureTenantMatches(request.TenantId);
        if (request.Lines.Count == 0)
        {
            return new(false, InventoryError.ValidationQuantity);
        }

        var lineKeys = request.Lines.Select(l => InventoryPersistence.SaleLineKey(request.SaleId, l.SaleLineId)).ToArray();
        var existingCount = await store.Db.Set<StockMovement>()
            .CountAsync(x => x.TenantId == request.TenantId && lineKeys.Contains(x.OperationKey!), ct);
        if (existingCount == request.Lines.Count)
        {
            return new(true, null);
        }

        if (existingCount > 0)
        {
            return new(false, InventoryError.InvalidAdjustment);
        }

        try
        {
            var items = new List<(InventorySaleLine Line, StockItem Item)>();
            foreach (var line in request.Lines)
            {
                InventoryPersistence.ValidateProductId(line.ProductId);
                if (line.Quantity <= 0)
                {
                    return new(false, InventoryError.ValidationQuantity);
                }

                var item = await store.RequireItemAsync(request.TenantId, line.BranchId, line.ProductId, ct);
                if (item.Available < line.Quantity)
                {
                    return new(false, InventoryError.InsufficientStock);
                }

                items.Add((line, item));
            }

            var now = store.Clock.GetUtcNow();
            foreach (var (line, item) in items)
            {
                item.Sell(line.Quantity, now);
                store.Db.Add(new StockMovement(
                    store.Ids.NewId(),
                    request.TenantId,
                    line.BranchId,
                    line.ProductId,
                    -line.Quantity,
                    StockMovementType.Sale,
                    InventoryPersistence.SaleLineKey(request.SaleId, line.SaleLineId),
                    now));
            }

            store.RecordEvent(
                request.TenantId,
                "STOCK_SOLD",
                new
                {
                    saleId = request.SaleId,
                    tenantId = request.TenantId,
                    branchId = request.Lines[0].BranchId,
                    lineCount = request.Lines.Count,
                },
                correlationId: null);
            return new(true, null);
        }
        catch (InventoryDomainException ex)
        {
            return new(false, ex.Code);
        }
    }
}

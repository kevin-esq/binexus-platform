using System.Text.Json;
using Binexus.Modules.Inventory.Domain;
using Binexus.Platform.Ids;
using Binexus.Platform.Messaging;
using Binexus.Platform.Persistence;
using Binexus.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Binexus.Modules.Inventory.Infrastructure;

public sealed class InventoryPersistence(
    BinexusDbContext db,
    ICurrentTenant currentTenant,
    IIdGenerator ids,
    TimeProvider clock)
{
    internal const int MinReasonLength = 3;
    internal const int MaxReasonLength = 200;
    internal const int MaxProductIdLength = 256;

    internal BinexusDbContext Db => db;
    internal IIdGenerator Ids => ids;
    internal TimeProvider Clock => clock;

    internal Guid RequireTenantId() =>
        currentTenant.Current?.TenantId
        ?? throw new InventoryDomainException("FORBIDDEN", "Tenant context is required.");

    /// <summary>
    /// Cross-module APIs must run under an authenticated tenant that matches the request.
    /// </summary>
    internal void EnsureTenantMatches(Guid tenantId)
    {
        var current = currentTenant.Current?.TenantId
            ?? throw new InventoryDomainException("FORBIDDEN", "Tenant context is required.");
        if (current != tenantId)
        {
            throw new InventoryDomainException("FORBIDDEN", "Tenant mismatch.");
        }
    }

    internal async Task<StockItem> RequireItemAsync(
        Guid tenantId,
        Guid branchId,
        string productId,
        CancellationToken ct) =>
        await db.Set<StockItem>().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.BranchId == branchId && x.ProductId == productId,
            ct)
        ?? throw new InventoryDomainException(InventoryError.InsufficientStock, "Insufficient stock.");

    internal async Task PersistAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InventoryDomainException(
                InventoryError.ConcurrencyConflict,
                "Inventory was updated concurrently. Retry the operation.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new InventoryDomainException(
                InventoryError.IdempotencyKeyConflict,
                "An idempotency key was already used for this operation.");
        }
        catch (DbUpdateException)
        {
            throw new InventoryDomainException(
                InventoryError.ConcurrencyConflict,
                "Inventory could not be persisted because it changed concurrently.");
        }
    }

    internal void RecordEvent(Guid tenantId, string name, object payload, string? correlationId)
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

    internal static void ValidateProductId(string productId)
    {
        if (string.IsNullOrWhiteSpace(productId) || productId.Length > MaxProductIdLength)
        {
            throw new InventoryDomainException(InventoryError.InvalidAdjustment, "productId is invalid.");
        }
    }

    internal static void ValidateReason(string reason, bool required)
    {
        var trimmed = reason?.Trim() ?? string.Empty;
        if (required && trimmed.Length < MinReasonLength)
        {
            throw new InventoryDomainException(
                InventoryError.InvalidAdjustment,
                $"reason must be at least {MinReasonLength} characters.");
        }

        if (trimmed.Length > MaxReasonLength)
        {
            throw new InventoryDomainException(
                InventoryError.InvalidAdjustment,
                $"reason must be at most {MaxReasonLength} characters.");
        }
    }

    internal static string TransferOutKey(Guid transferId) => $"transfer:{transferId}:out";
    internal static string TransferInKey(Guid transferId) => $"transfer:{transferId}:in";
    internal static string OrderReserveKey(Guid orderId, Guid orderLineId) => $"order:{orderId}:{orderLineId}";
    internal static string SaleLineKey(Guid saleId, Guid saleLineId) => $"sale:{saleId}:{saleLineId}";
}

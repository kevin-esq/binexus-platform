# ADR-0014: Sync inventory reservation and authenticated tenant context

## Status

Accepted — 2026-07-11.

## Decision

`IInventoryReservationApi` and `IInventorySaleApi` only stage aggregate changes, movements, and outbox messages on the caller's scoped `BinexusDbContext`. The caller owns the single `SaveChangesAsync` and transaction commit. A synchronous stock shortage returns `INSUFFICIENT_STOCK` without persisting `FAILED` reservations or `INVENTORY_RESERVATION_FAILED`; that event schema remains for historical Nest asynchronous compatibility.

`AuthenticatedTenantMiddleware` establishes the tenant from validated JWT claims in every environment after authentication. `DevelopmentTenantOverrideMiddleware` is restricted to Development and Testing for the tenant probe and never supersedes JWT context.

Inventory enum values retain C# names but persist and serialize as uppercase contract strings through explicit EF converters.

## Consequences

- Cross-module commands can atomically combine inventory and their own writes/outbox messages.
- HTTP inventory commands persist through their private service boundary and map concurrency/idempotency conflicts to stable domain codes.
- Transfer records are unitary: one `ProductId` and `Quantity`, without transfer lines.

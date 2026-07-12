# CHECKPOINT INVENTORY

**Fecha:** 2026-07-12  
**Estado:** **Cierre formal** tras ajustes de tenant middleware, TX compartida, eventos sync y enums. Orders autorizado a continuación.

Auditoría Nest: [`inventory-nest-audit.md`](./inventory-nest-audit.md)  
ADR: [`docs/adr/0014-inventory-sync-reservation-and-tenant-middleware.md`](../adr/0014-inventory-sync-reservation-and-tenant-middleware.md)

---

## Tenant middleware corregido

| Middleware                            | Ámbito                                                                            |
| ------------------------------------- | --------------------------------------------------------------------------------- |
| `AuthenticatedTenantMiddleware`       | **Todos** los ambientes; claims JWT validados → `ICurrentTenant`; limpia al final |
| `DevelopmentTenantOverrideMiddleware` | Solo Development/Testing; headers probe; **nunca** sobrescribe JWT                |

Workers (`OutboxProcessor`): `SetContext` por mensaje + `Clear` en `finally` — sin reutilizar contexto entre eventos.

---

## INVENTORY_RESERVATION_FAILED

**Decisión:** dormant / deprecated para approve síncrono.

```text
reserva falla → Result InsufficientStock → rollback caller → sin outbox de fallo → Orders 409 + DRAFT
```

No se abren TX secundarias para publicar el fallo. Schema conservado por compatibilidad Nest histórica.  
`INVENTORY_RESERVED` sí se stagea en outbox dentro de la TX exitosa.

---

## Transacción compartida / SaveChanges

- HTTP Inventory: `PersistAsync` propio (es el caller); mutaciones stagean outbox (`STOCK_ADJUSTED`, `STOCK_TRANSFER_*`, `STOCK_SOLD`) en la misma TX.
- `IInventoryService` / queries Orders devuelven `Result<T>`; el edge HTTP mapea `DomainError` → Problem Details (sin catch de excepciones de negocio).
- Implementación partida: `InventoryStockService`, `InventoryReservationService`, `InventorySaleService` + `InventoryPersistence`.
- Un solo `SaveChanges` del pipeline/caller; outbox en el mismo commit.
- Pruebas: rollback conjunto / commit conjunto (`SharedTransactionTests`).

---

## Transferencias (unitarias)

Una transferencia = **un** `ProductId` + `Quantity` (sin tabla de líneas). Campos: ProductId, Quantity, SourceBranchId, DestinationBranchId, Status (+ OperationKey opcional).

```text
create  → Reserved += en origen
receive → Reserved/OnHand origen −; OnHand destino +; 1× TRANSFER_OUT + 1× TRANSFER_IN (destino se crea si falta)
cancel  → Reserved −; OnHand intacto; sin movimientos IN/OUT
```

---

## Idempotency-Key

```text
HTTP Idempotency-Key → interno {op}:{tenantId}:{key}
```

| Endpoint         | Comportamiento                                                                                 |
| ---------------- | ---------------------------------------------------------------------------------------------- |
| adjust           | Header preferido; body `operationKey` alias; conflicto de payload → `IDEMPOTENCY_KEY_CONFLICT` |
| transfer create  | Header → `stock_transfers.operation_key` unique                                                |
| receive / cancel | Header validado; idempotencia natural por estado/id                                            |

---

## Enums persistidos

Contrato DB UPPERCASE vía `InventoryPersistedEnums` (converters EF explícitos): `PENDING`, `ACTIVE`, `RESERVE`, etc. API usa el mismo contrato (sin `ToUpperInvariant` disperso).

---

## Concurrencia

`xmin` → `409 INVENTORY_CONCURRENCY_CONFLICT`. Pruebas: ventas concurrentes, reservas del último unitario, receive vs cancel.

---

## Append-only StockMovement

Solo insert; architecture test: sin setters/métodos públicos mutadores.

---

## Feature

`INVENTORY` no bloquea endpoints (paridad).

---

## Migraciones

- `Inventory_Stock`, `Inventory_StockForeignKeys`, `Inventory_TransferOperationKey`
- `dotnet ef migrations has-pending-model-changes` → sin pendientes

---

## Pruebas / verificación

```text
dotnet restore → OK
dotnet build -c Release → 0 warnings / 0 errors
dotnet test  -c Release → 105/105 passed, 0 failed, 0 skipped
  Unit 32 + Architecture 9 + Integration 64
dotnet list package --vulnerable --include-transitive → limpio
OpenAPI/SDK regenerados
dotnet ef migrations has-pending-model-changes → sin pendientes
```

---

## Riesgos pendientes

1. Receive/cancel no persisten Idempotency-Key aparte del estado del recurso.
2. Branch ACL de usuario ausente (paridad Nest).
3. Catalog / Sales / Warehouse fuera de alcance.

**Inventory cerrado formalmente. Orders autorizado en el mismo PR.**

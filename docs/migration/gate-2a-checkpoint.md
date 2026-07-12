# Gate 2A — Checkpoint migración .NET

**Estado:** Pendiente aprobación usuario  
**Siguiente:** Gate 2B — primera migración EF (Platform outbox/inbox) + worker claim logic + Testcontainers migrate

---

## Estrategia EF — recomendación

### Opción A — Esquema físico completo upfront

Crear las 26 tablas Prisma-equivalentes en la primera migración.

| Pros                                                 | Contras                                 |
| ---------------------------------------------------- | --------------------------------------- |
| FK integrity definida una vez                        | Diff enorme, difícil de revisar         |
| Tests de integración contra schema completo temprano | Configuraciones EF vacías / incorrectas |
|                                                      | Dominio falso en módulos no migrados    |

### Opción B — Migraciones por etapa (RECOMENDADA)

| Orden | Migración              | Contenido                                         |
| ----- | ---------------------- | ------------------------------------------------- |
| 1     | `Platform_OutboxInbox` | outbox_messages, event_handler_deliveries         |
| 2     | `Identity`             | Tenant, User, Branch, RefreshToken, TenantFeature |
| 3     | `Inventory`            | StockItem, Reservation, Movement, Transfer        |
| 4     | `Orders`               | Order, lines, transitions                         |
| 5     | `Warehouse`            | Picking                                           |
| 6     | `Logistics`            | Routes, stops, proof, liquidation                 |
| 7     | `Sales`                | SalesSession, Sale, lines, PaymentCapture         |

| Pros                                    | Contras                                       |
| --------------------------------------- | --------------------------------------------- |
| Diffs revisables en el único PR         | Orden de FK entre módulos requiere disciplina |
| Config EF alineada con dominio migrado  | Seeds parciales por etapa                     |
| Base limpia sin deuda de modelos vacíos |                                               |
| Cada gate prueba su slice               |                                               |

**Decisión Gate 2B:** Opción B. Primera migración = **solo Platform (outbox/inbox)**.

Separación explícita:

- **Modelo físico:** EF configurations en `Binexus.Platform.Persistence` / `Modules.*.Infrastructure`
- **Modelo de dominio:** agregados en `Modules.*.Domain`, conectados en la etapa del módulo

---

## Clasificación de comandos

| Marker                     | Transacción DB  | Outbox              | Ejemplos futuros                       |
| -------------------------- | --------------- | ------------------- | -------------------------------------- |
| `ITransactionalCommand`    | Sí (dispatcher) | Opcional en handler | ApproveOrder, CreateSale, CloseSession |
| `INonTransactionalCommand` | No              | No                  | Presigned URL, validaciones puras      |
| `IIdempotentCommand`       | Según base      | Según base          | CreateSale con idempotency key         |
| `IQuery<T>`                | No              | No                  | ListOrders, GetSession                 |

El dispatcher abre TX **solo** para `ITransactionalCommand`. Queries nunca.

---

## Árbol de proyectos

```text
backend/
  Directory.Build.props
  global.json
  Binexus.sln
  src/
    Binexus.Api/
    Binexus.Workers/
    Binexus.Platform/
    Binexus.SharedKernel/
    Modules/
      Binexus.Modules.Identity/
  tests/
    Binexus.UnitTests/
    Binexus.IntegrationTests/
    Binexus.ArchitectureTests/
  scripts/
    export-openapi.ps1
```

---

## Referencias entre proyectos

```text
SharedKernel          (sin dependencias internas)
Platform              → SharedKernel
Modules.Identity      → SharedKernel
Api                   → Platform, SharedKernel, Modules.Identity
Workers               → Platform
UnitTests             → Platform, SharedKernel
ArchitectureTests     → Platform, SharedKernel, Modules.Identity
IntegrationTests      → Api
```

**Prohibido:** Platform → Modules, SharedKernel → cualquier otro, Domain → Infrastructure.

---

## Paquetes NuGet (justificación)

| Paquete                          | Uso                     |
| -------------------------------- | ----------------------- |
| EF Core 10 + Npgsql              | Persistencia PostgreSQL |
| Serilog                          | Logging estructurado    |
| OpenTelemetry                    | Trazas/métricas base    |
| AspNetCore.HealthChecks.NpgSql   | Readiness               |
| Microsoft.AspNetCore.OpenApi     | OpenAPI 3.1 nativo      |
| NetArchTest.Rules                | Architecture tests      |
| FluentAssertions + xUnit         | Tests                   |
| Testcontainers.PostgreSql        | Integración (Gate 2B)   |
| Microsoft.AspNetCore.Mvc.Testing | WebApplicationFactory   |

Sin MediatR, sin AutoMapper, sin FluentValidation en Gate 2A (se añade por slice).

---

## Seguridad base (Gate 2A)

| Control                                | Estado                        |
| -------------------------------------- | ----------------------------- |
| HTTPS + HSTS (no-Development)          | ✓                             |
| Forwarded headers (proxies confiables) | ✓ configurado                 |
| CORS allowlist + credentials           | ✓                             |
| Max request body                       | ✓ Kestrel limit               |
| Rate limiting policy `auth`            | ✓ preparado                   |
| Problem Details sin stack en prod      | ✓                             |
| Options validate on start              | ✓ Database, Cors              |
| CancellationToken en handlers          | ✓                             |
| Secretos en config/env                 | ✓ no en repo                  |
| Auth completa                          | Pendiente etapa Identity      |
| Tenant probe                           | Solo Development/Test headers |

---

## OpenAPI → SDK

Script: `backend/scripts/export-openapi.ps1`

Gate 2B: CI exporta `openapi.json` desde `/openapi/v1.json` y ejecuta generación en `packages/sdk`.

Gate 2A: endpoint OpenAPI mapeado en Development (`MapOpenApi`).

---

## Pruebas ejecutadas (Gate 2A)

```
dotnet build   → 0 errores, 0 warnings
dotnet test    → 9/9 passed
  UnitTests:           3 (UUID v7, dispatcher non-TX)
  ArchitectureTests:   4 (namespace rules)
  IntegrationTests:    2 (health, tenant-probe)
```

Testcontainers + migrate: **Gate 2B**.

---

## Riesgos y decisiones pendientes

| Item                                   | Estado                                                   |
| -------------------------------------- | -------------------------------------------------------- |
| Primera migración EF                   | Pendiente aprobación 2B                                  |
| Worker claim/delivery logic            | Esqueleto; implementación 2B                             |
| Testcontainers + migrate               | Gate 2B                                                  |
| `Guid.CreateVersion7` requiere .NET 9+ | OK (.NET 10)                                             |
| NetArchTest en net10                   | Verificar en build                                       |
| Tabla `AuditLog`                       | Migración Platform o Identity — decidir en Identity gate |

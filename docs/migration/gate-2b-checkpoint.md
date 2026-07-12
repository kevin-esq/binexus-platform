# CHECKPOINT GATE 2B

**Fecha:** 2026-07-10  
**Estado:** Listo para revisión — **no avanzar a Identity** hasta aprobación explícita.

---

## 1. Versiones finales de paquetes (principales)

| Paquete                                      | Versión                    | Proyecto                      |
| -------------------------------------------- | -------------------------- | ----------------------------- |
| `Microsoft.EntityFrameworkCore`              | 10.0.9                     | Platform, Api (Design), tests |
| `Npgsql.EntityFrameworkCore.PostgreSQL`      | 10.0.3                     | Platform                      |
| `EFCore.NamingConventions`                   | 10.0.0                     | Platform                      |
| `Microsoft.AspNetCore.OpenApi`               | 10.0.9                     | Api                           |
| `Microsoft.Extensions.ApiDescription.Server` | 10.0.9                     | Api                           |
| `Microsoft.OpenApi`                          | 2.3.0 (override explícito) | Api                           |
| `Testcontainers.PostgreSql`                  | 4.5.0                      | IntegrationTests              |
| `dotnet-ef` (tool)                           | 10.0.9                     | `.config/dotnet-tools.json`   |

**Eliminado:** OpenTelemetry.\* (opción D — no indispensable para Gate 2).

---

## 2. Auditoría de vulnerabilidades

Comando CI:

```bash
dotnet package list --file backend/Binexus.slnx --vulnerable --include-transitive
```

### Resueltos

| Paquete              | Acción                                                  |
| -------------------- | ------------------------------------------------------- |
| OpenTelemetry 1.12.0 | Eliminado — GHSA-g94r-2vxg-569j (Moderate) ya no aplica |

### Excepción documentada (única)

| Campo             | Valor                                                                           |
| ----------------- | ------------------------------------------------------------------------------- |
| Paquete           | `Microsoft.OpenApi` 2.3.0                                                       |
| Severidad         | High                                                                            |
| Advisory          | GHSA-v5pm-xwqc-g5wc                                                             |
| Introducido por   | `Microsoft.AspNetCore.OpenApi` 10.0.9                                           |
| Versión corregida | 3.8.0 — **incompatible** con source generator ASP.NET Core 10.0.9               |
| Ruta en runtime   | Solo generamos contratos propios; no parseamos OpenAPI externos                 |
| Retiro            | Cuando `Microsoft.AspNetCore.OpenApi` dependa de OpenApi ≥ 3.8 sin romper build |
| Supresión         | `NuGetAuditSuppress` único GHSA en `Directory.Build.props`                      |
| Documentación     | `docs/migration/nuget-audit-exceptions.md`                                      |

**No** se usa `NoWarn NU1902/NU1903` ni `NuGetAudit=false`.

CI falla ante High/Critical no aprobados (excluye GHSA documentado vía `jq`).

---

## 3. Supresiones de analizadores

| Regla                        | Resolución                                                            |
| ---------------------------- | --------------------------------------------------------------------- |
| CA1848                       | `[LoggerMessage]` en `PlatformLog`, `OutboxProcessorLog`, `WorkerLog` |
| CA1000                       | Fábricas estáticas movidas a `ResultFactory`                          |
| CA1707                       | Solo proyectos de test (`NoWarn` en `*.Tests.csproj`)                 |
| CA1707/CA1861 migraciones EF | `.editorconfig` local en `Persistence/Migrations/`                    |

---

## 4. Migración creada

| Campo      | Valor                                                    |
| ---------- | -------------------------------------------------------- |
| Nombre     | `Platform_OutboxInbox`                                   |
| Id         | `20260710104015_Platform_OutboxInbox`                    |
| Ubicación  | `backend/src/Binexus.Platform/Persistence/Migrations/`   |
| Estrategia | Opción B — incremental por módulo; primera solo Platform |

### Tablas incluidas (solo Gate 2B)

- `outbox_messages`
- `event_handler_deliveries`

Sin tablas especulativas ni módulos vacíos.

### Constraints relevantes

- `UNIQUE (tenant_id, event_id, handler_key)` en deliveries
- Índices en `(status, locked_until_utc)` y `(status, next_attempt_at_utc)`

### Pruebas de migración

- `MigrationTests.Migrate_on_empty_database_creates_outbox_tables`
- `MigrationTests.Migrate_down_and_up_roundtrip_preserves_schema`

### EF model drift

```bash
dotnet ef migrations has-pending-model-changes \
  --project src/Binexus.Platform/Binexus.Platform.csproj \
  --startup-project src/Binexus.Api/Binexus.Api.csproj
```

Resultado: **verde** — sin cambios pendientes.

---

## 5. SQL generado (resumen)

Ver `20260710104015_Platform_OutboxInbox.cs` — snake_case vía `EFCore.NamingConventions`:

```sql
CREATE TABLE outbox_messages (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  event_name varchar(128) NOT NULL,
  payload_json jsonb NOT NULL,
  schema_version integer NOT NULL,
  occurred_at_utc timestamptz NOT NULL,
  status varchar(32) NOT NULL,
  applicable_handler_keys jsonb,
  attempt_count integer NOT NULL,
  next_attempt_at_utc timestamptz,
  locked_until_utc timestamptz,
  locked_by varchar(128),
  last_error_code varchar(64),
  last_error_message varchar(512),
  correlation_id varchar(128),
  causation_id varchar(128),
  created_at_utc timestamptz NOT NULL,
  initialized_at_utc timestamptz,
  completed_at_utc timestamptz
);

CREATE TABLE event_handler_deliveries (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  event_id uuid NOT NULL REFERENCES outbox_messages(id) ON DELETE CASCADE,
  handler_key varchar(128) NOT NULL,
  status varchar(32) NOT NULL,
  attempt_count integer NOT NULL,
  next_attempt_at_utc timestamptz,
  locked_until_utc timestamptz,
  locked_by varchar(128),
  last_error_code varchar(64),
  last_error_message varchar(512),
  created_at_utc timestamptz NOT NULL,
  processed_at_utc timestamptz,
  UNIQUE (tenant_id, event_id, handler_key)
);
```

---

## 6. Algoritmo claim/delivery (definitivo)

Documentación completa: `docs/migration/gate-2a-outbox-inbox.md`  
Implementación: `backend/src/Binexus.Platform/Messaging/OutboxProcessor.cs`

### Primer claim (transacción única PostgreSQL)

```text
BEGIN
→ SELECT id FROM outbox_messages
    WHERE status IN ('Pending','Processing','FailedTransient')
      AND (locked_until_utc IS NULL OR locked_until_utc < now)
      AND (next_attempt_at_utc IS NULL OR next_attempt_at_utc <= now)
    ORDER BY occurred_at_utc
    FOR UPDATE SKIP LOCKED LIMIT 1
→ Si InitializedAtUtc IS NULL:
    → snapshot ApplicableHandlerKeys + InitializedAtUtc
    → si [] → Completed + COMMIT
    → si handlers → INSERT event_handler_deliveries (Pending)
→ Status = Processing, lock worker, AttemptCount++
→ COMMIT
```

### Dispatch por handler

```text
BEGIN (por delivery)
→ SELECT delivery FOR UPDATE
→ si Processed/FailedPermanent → skip
→ Processing + ejecutar handler (atomicidad local PostgreSQL)
→ Processed | FailedTransient | FailedPermanent
→ FinalizeOutboxStatus
→ COMMIT
→ ReleaseOutboxClaim (libera lock outbox si no terminal)
```

### Garantía real

```text
at-least-once delivery + handlers idempotentes + atomicidad local cuando sea posible
```

---

## 7. Estados definitivos

### OutboxMessage

| Estado                  | Significado                                 |
| ----------------------- | ------------------------------------------- |
| `Pending`               | Nunca reclamado o lock expirado             |
| `Processing`            | Dispatch en curso                           |
| `Completed`             | Todos los handlers OK o sin handlers        |
| `CompletedWithFailures` | Sin trabajo pendiente; ≥1 `FailedPermanent` |
| `FailedTransient`       | Error transitorio al **claim** (DB)         |
| `FailedPermanent`       | Payload inválido / imposible inicializar    |

`AttemptCount` (outbox) = solo claim/reclaim. Reintentos de negocio en `EventHandlerDelivery.AttemptCount`.

### Eventos sin handlers

`ApplicableHandlerKeys = []` → `Completed` inmediato (no queda en `Pending`).

### Política adoptada

**Opción A aprobada:** `CompletedWithFailures` cuando un handler falla permanentemente.

---

## 8. Pruebas de concurrencia (PostgreSQL Testcontainers)

Archivo: `backend/tests/Binexus.IntegrationTests/Outbox/OutboxProcessorTests.cs`

| #   | Escenario                        | Test                                                                    |
| --- | -------------------------------- | ----------------------------------------------------------------------- |
| 1   | Dos workers reclaman lote        | `Two_workers_process_concurrent_batch_without_duplicate_initialization` |
| 2   | Solo uno inicializa cada evento  | (mismo + `Two_workers_do_not_duplicate_deliveries`)                     |
| 3   | Deliveries no duplicadas         | `Two_workers_do_not_duplicate_deliveries`                               |
| 4   | Lock expirado recuperable        | `Expired_outbox_lock_can_be_reclaimed`                                  |
| 5   | Handler procesado no re-ejecuta  | `Processed_handler_is_not_executed_again`                               |
| 6   | Handler transitorio reintenta    | `Transient_handler_failure_is_retried`                                  |
| 7   | Handler permanente visible       | `Permanent_handler_failure_yields_completed_with_failures`              |
| 8   | Evento sin handlers termina      | `Event_without_handlers_completes_immediately`                          |
| 9   | Tenant scope desde envelope      | `Tenant_scope_is_reconstructed_from_envelope`                           |
| 10  | CancellationToken detiene worker | `Cancellation_token_stops_worker_cleanly`                               |

**Requisito:** Docker en ejecución (Testcontainers). En CI Ubuntu el daemon está disponible.

**Local (sin Docker):** 10 tests de outbox/migración omitidos; 10 tests restantes pasan.

---

## 9. OpenAPI build-time

| Campo     | Valor                                                                            |
| --------- | -------------------------------------------------------------------------------- |
| Mecanismo | `Microsoft.Extensions.ApiDescription.Server` + `OpenApiGenerateDocumentsOnBuild` |
| Artefacto | `artifacts/openapi/binexus-v1.json`                                              |
| Versión   | OpenAPI **3.1.1**                                                                |
| Runtime   | `MapOpenApi()` solo en Development                                               |

Pipeline:

```text
dotnet build → binexus-v1.json → openapi-typescript → packages/sdk/src/generated/schema.d.ts → git diff
```

Script: `backend/scripts/generate-sdk.ps1`

---

## 10. SDK TypeScript generado

- **Salida:** `packages/sdk/src/generated/schema.d.ts`
- **Herramienta:** `openapi-typescript` 7.13.0 (vía `@binexus/sdk`)
- **CI:** genera y verifica `git diff --exit-code`

---

## 11. Seguridad Gate 2B

### `/internal/tenant-probe`

- Registrado solo en Development/Testing (`Program.cs`)
- `ExcludeFromDescription()` — fuera de OpenAPI público
- Test producción: `TenantProbeProductionTests` → **404**
- Producción: tenant vía identidad autenticada (futuro Identity), no header libre

### Forwarded headers

- `UseForwardedHeaders` antes de HTTPS redirection (`UseBinexusSecurityDefaults`)
- `ForwardLimit = 2`
- `KnownProxies` + `KnownIPNetworks` desde config (`Security:TrustedProxies`, `Security:TrustedNetworks`)
- No se limpian listas para aceptar cualquier origen

### UUID v7

- Producción: `Guid.CreateVersion7(timeProvider.GetUtcNow())`
- Tests: `SequentialUuidV7IdGenerator` — v7 válidos, variante RFC, múltiples IDs mismo timestamp

---

## 12. Build y test

### Build

```bash
cd backend && dotnet build --configuration Release
```

**Resultado:** 0 errores.

### Test (sin Docker — subset)

```bash
dotnet test --configuration Release --filter "FullyQualifiedName!~Outbox&FullyQualifiedName!~Migration"
```

| Proyecto                       | Pasando |
| ------------------------------ | ------- |
| Binexus.UnitTests              | 3/3     |
| Binexus.ArchitectureTests      | 4/4     |
| Binexus.IntegrationTests (API) | 3/3     |

### Test (completo — requiere Docker)

```bash
dotnet test --configuration Release
```

**Total esperado:** 21 tests (10 adicionales outbox/migración con Testcontainers).

---

## 13. CI backend job

Archivo: `.github/workflows/ci.yml` → job `backend`

1. `pnpm install` (SDK generation)
2. `dotnet package list --vulnerable --include-transitive` + fail High/Critical
3. `dotnet build --configuration Release`
4. `dotnet ef migrations has-pending-model-changes`
5. `dotnet test`
6. Verificar `artifacts/openapi/binexus-v1.json`
7. `generate-sdk.ps1` + diff SDK

---

## 14. Riesgos restantes

| Riesgo                                                                  | Mitigación / siguiente paso                                         |
| ----------------------------------------------------------------------- | ------------------------------------------------------------------- |
| `Microsoft.OpenApi` 2.3.0 High                                          | Excepción documentada; revisar cada release AspNetCore.OpenApi      |
| EF version binding Npgsql 10.0.3 → Relational 10.0.4 vs Platform 10.0.9 | Warning MSB3277 en tests; alinear cuando Npgsql publique pin 10.0.9 |
| Handlers con servicios externos                                         | State machine + idempotency key (documentado, no implementado aún)  |
| Replay manual de handlers nuevos                                        | Operación admin futura                                              |
| Integration tests locales                                               | Requieren Docker Desktop activo                                     |
| Identity no migrado                                                     | Tenant en prod sin JWT hasta Gate posterior                         |

---

## 15. Archivos clave tocados en Gate 2B

```
backend/src/Binexus.Platform/Messaging/OutboxProcessor.cs
backend/src/Binexus.Platform/Persistence/Migrations/20260710104015_Platform_OutboxInbox.cs
backend/tests/Binexus.IntegrationTests/Outbox/OutboxProcessorTests.cs
backend/tests/Binexus.IntegrationTests/Infrastructure/PostgresTestFixture.cs
backend/scripts/generate-sdk.ps1
artifacts/openapi/binexus-v1.json
packages/sdk/src/generated/schema.d.ts
docs/migration/gate-2a-outbox-inbox.md
docs/migration/nuget-audit-exceptions.md
.github/workflows/ci.yml
```

---

**Siguiente paso tras tu aprobación:** Gate 3 / Identity — **no iniciado**.

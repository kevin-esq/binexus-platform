# CHECKPOINT GATE 2B — FINAL

**Fecha:** 2026-07-10  
**Estado:** Bloqueantes cerrados — listo para aprobación explícita. **No avanzar a Identity** hasta sign-off.

---

## 1. Tests reales (Docker / Testcontainers)

**Comando:**

```bash
dotnet test backend/Binexus.slnx --configuration Release --no-restore --logger "console;verbosity=normal"
```

**Entorno:** Docker Desktop disponible; Testcontainers `postgres:16-alpine`.

| Métrica                       | Valor                                             |
| ----------------------------- | ------------------------------------------------- |
| Tests descubiertos (solución) | **30** (Unit 6 + Architecture 4 + Integration 20) |
| Ejecutados                    | **30**                                            |
| Passed                        | **30**                                            |
| Failed                        | **0**                                             |
| Skipped                       | **0**                                             |
| Duración total                | ~33 s                                             |
| PostgreSQL imagen             | `postgres:16-alpine` (Testcontainers)             |

**Concurrencia Outbox (`OutboxProcessorTests`):** 9 escenarios — **3 ejecuciones consecutivas verdes** (9/9 cada una, ~1–2 s).

| Run | Passed | Failed | Skipped |
| --- | ------ | ------ | ------- |
| 1   | 9      | 0      | 0       |
| 2   | 9      | 0      | 0       |
| 3   | 9      | 0      | 0       |

Tests adicionales de concurrencia / atomicidad: `HandlerAtomicityTests` (5), incl. locks, snapshot, estados mixtos, rollback de handler.

**Sin skips por Docker:** fixture falla si Docker no está disponible (no hay `Skip.If`).

---

## 2. Matriz final de paquetes EF / Npgsql

**Estrategia:** alinear EF Core a **10.0.9** (soportado por Npgsql EF 10.0.3).

| Paquete                                      | Versión directa | Transitiva resuelta | Introducido por      | Versión final | Motivo                  |
| -------------------------------------------- | --------------- | ------------------- | -------------------- | ------------- | ----------------------- |
| `Microsoft.EntityFrameworkCore`              | 10.0.9          | 10.0.9              | Platform, tests      | **10.0.9**    | Alineación explícita    |
| `Microsoft.EntityFrameworkCore.Relational`   | 10.0.9          | 10.0.9              | Platform, Api Design | **10.0.9**    | Elimina mismatch 10.0.4 |
| `Microsoft.EntityFrameworkCore.Design`       | 10.0.9          | —                   | Api                  | **10.0.9**    | Migraciones             |
| `Npgsql.EntityFrameworkCore.PostgreSQL`      | 10.0.3          | —                   | Platform             | **10.0.3**    | Provider oficial PG     |
| `Npgsql`                                     | —               | 10.0.3              | Npgsql EF            | **10.0.3**    | Transitive              |
| `Microsoft.EntityFrameworkCore.Abstractions` | —               | 10.0.9              | EF Core              | **10.0.9**    | Transitive              |
| `Microsoft.EntityFrameworkCore.Analyzers`    | —               | 10.0.9              | EF Core              | **10.0.9**    | Transitive              |

**MSB3277:** aparecía al compilar tests cuando `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 arrastraba `Microsoft.EntityFrameworkCore.Relational` **10.0.4** mientras Platform referenciaba EF **10.0.9**. Corregido con referencias directas 10.0.9 en Platform/Api/tests — **no** se forzó transitiva sin validar provider.

| Comando                                                                  | Warnings |
| ------------------------------------------------------------------------ | -------- |
| `dotnet restore backend/Binexus.slnx --force-evaluate`                   | **0**    |
| `dotnet build backend/Binexus.slnx -c Release` (`TreatWarningsAsErrors`) | **0**    |
| `dotnet test backend/Binexus.slnx -c Release --no-build`                 | **0**    |

---

## 3. NuGet Audit

```bash
dotnet list backend/Binexus.slnx package --vulnerable --include-transitive --format json
```

**Resultado:** sin vulnerabilidades High/Critical en ningún proyecto.

**Excepciones:** `docs/migration/nuget-audit-exceptions.json` → `[]` (vacío).

CI (`backend` job): emite JSON legible por máquina, falla en High/Critical, valida `reviewBy` + `removalCondition` si hay excepciones.

---

## 4. Microsoft.OpenApi — decisión final

**Opción A adoptada:** `Microsoft.OpenApi` **2.7.5** (parche GHSA-v5pm-xwqc-g5wc) + `Microsoft.AspNetCore.OpenApi` 10.0.9.

Detalle reproducible: `docs/migration/openapi-vulnerability-spike.md`.

| Opción                   | Resultado                                                                  |
| ------------------------ | -------------------------------------------------------------------------- |
| A — OpenApi ≥ 2.7.5      | **OK** — build, generator, audit limpio                                    |
| B — OpenApi ≥ 3.5.4      | **FAIL** — generator CS0200 (`IOpenApiMediaType.Example` read-only en 3.x) |
| C — Aislar generación    | DLL patched en publish; sin parsing externo                                |
| D — Desactivar generator | No necesario                                                               |

**Supresión global eliminada** de `backend/Directory.Build.props`.

---

## 5. Artefacto publicado

```bash
dotnet publish backend/src/Binexus.Api -c Release -o artifacts/publish/api
```

| Archivo                            | Versión / nota                        |
| ---------------------------------- | ------------------------------------- |
| `Microsoft.OpenApi.dll`            | **2.7.5.0** (486 752 bytes) — patched |
| `Microsoft.AspNetCore.OpenApi.dll` | 10.0.9                                |

No se parsean documentos OpenAPI de terceros en runtime.

---

## 6. SQL y esquema verificado

**Script idempotente:** `artifacts/migration/platform-outbox-inbox-idempotent.sql`

```bash
dotnet ef migrations script \
  --project backend/src/Binexus.Platform \
  --startup-project backend/src/Binexus.Api \
  --idempotent
```

**Verificado en PostgreSQL** (`SchemaCatalogTests` + migración aplicada):

| Contrato                                    | Confirmado                                                          |
| ------------------------------------------- | ------------------------------------------------------------------- |
| PKs `uuid`                                  | `outbox_messages.id`, `event_handler_deliveries.id`                 |
| `timestamptz`                               | `occurred_at_utc`, `created_at_utc`, locks, retries                 |
| `jsonb`                                     | `payload_json`, `applicable_handler_keys`                           |
| UNIQUE `(tenant_id, event_id, handler_key)` | `ix_event_handler_deliveries_tenant_id_event_id_handler_key`        |
| Índices claim                               | `(status, locked_until_utc)`, `(status, next_attempt_at_utc)`       |
| Longitudes máx.                             | `event_name` 128, `handler_key` 128, `last_error_message` 512, etc. |
| FK delete                                   | `ON DELETE CASCADE` (`confdeltype = c`)                             |
| Estados                                     | `varchar(32)` — validados en aplicación + tests                     |

---

## 7. Modelo Outbox / Delivery (código + pruebas)

| Regla                            | Implementación                                                                                                                            |
| -------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| Snapshot `ApplicableHandlerKeys` | Una vez en primer claim; `EventHandlerRegistryValidator.NormalizeHandlerKeys` (orden determinista, sin duplicados)                        |
| Snapshot + deliveries            | Misma transacción en `TryClaimNextMessageAsync`                                                                                           |
| Handler keys duplicadas          | Rechazo al arranque (`ValidateProcessorKeys`)                                                                                             |
| Estados terminales               | `Completed` si todas `Processed`; `CompletedWithFailures` solo sin Pending/Processing/FailedTransient; nunca terminal con retry pendiente |
| Lock expiry SQL                  | **`NOW()`** PostgreSQL (autoritativo para reclaim)                                                                                        |
| Scheduling app                   | `TimeProvider` para `locked_until_utc` / backoff escritos                                                                                 |
| Reclaim con lock vigente         | `FOR UPDATE` + `locked_until_utc < NOW()` — test `Delivery_with_active_lock_is_not_reclaimed`                                             |
| Atomicidad handler               | SAVEPOINT `handler_effects`; rollback en fallo; tests EF en `HandlerAtomicityTests`                                                       |

---

## 8. Pruebas nuevas / actualizadas

| Archivo                                                     | Propósito                                                         |
| ----------------------------------------------------------- | ----------------------------------------------------------------- |
| `Outbox/OutboxProcessorTests.cs`                            | 9 escenarios concurrencia / retry / reclaim                       |
| `Outbox/HandlerAtomicityTests.cs`                           | Atomicidad, estados mixtos, snapshot, lock                        |
| `Outbox/AtomicProbeProcessor.cs`                            | Side-effect EF rastreado                                          |
| `Persistence/SchemaCatalogTests.cs`                         | Catálogo PG (`information_schema`, `pg_indexes`, `pg_constraint`) |
| `Persistence/MigrationTests.cs`                             | Up/down migración                                                 |
| `UnitTests/Messaging/EventHandlerRegistryValidatorTests.cs` | Normalización registry                                            |
| `Api/TenantProbeProductionTests.cs`                         | 404 en Production                                                 |

---

## 9. OpenAPI / SDK reproducible

| Check                               | Resultado                                                            |
| ----------------------------------- | -------------------------------------------------------------------- |
| `artifacts/openapi/binexus-v1.json` | Sin `/internal/tenant-probe`, localhost, paths absolutos, timestamps |
| `generate-sdk.ps1` × 2              | Hash idéntico (`schema.d.ts`)                                        |
| Encabezado SDK                      | `GENERATED FILE — DO NOT EDIT`                                       |

---

## 10. Build / restore / test (resumen)

```
dotnet restore backend/Binexus.slnx --force-evaluate  → 0 warnings
dotnet build   backend/Binexus.slnx -c Release        → 0 warnings, 0 errors
dotnet test    backend/Binexus.slnx -c Release        → 30/30 passed, 0 skipped
```

---

## 11. Riesgos aceptados explícitamente

| Riesgo                                             | Mitigación                                                      | Revisión                                            |
| -------------------------------------------------- | --------------------------------------------------------------- | --------------------------------------------------- |
| `Microsoft.OpenApi.dll` en publish (2.7.5 patched) | No ingestion OpenAPI externa; audit CI                          | Cuando ASP.NET Core alinee 3.x sin romper generator |
| Reloj dual PG `NOW()` vs `TimeProvider`            | Documentado en `OutboxProcessor`; SQL usa PG para reclaim       | Revisar si se necesita `clock_timestamp()`          |
| Índices claim no parciales (`WHERE …`)             | Índices compuestos status+lock; suficiente para volumen Gate 2B | Optimizar si hay contención medida                  |

---

## 12. Pendiente de aprobación

Gate 2B cumple criterios del checklist. **Identity permanece bloqueado** hasta sign-off explícito.

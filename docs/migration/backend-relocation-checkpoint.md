# CHECKPOINT BACKEND RELOCATION — FINAL

**Status:** CLOSED  
**Date:** 2026-07-12  
**Scope:** Structural move only — Nest already deleted; no domain/API/event changes.

---

## Ubicación

|                     | Path                           |
| ------------------- | ------------------------------ |
| **Anterior**        | `backend/` (raíz del monorepo) |
| **Final**           | `apps/backend/`                |
| **Raíz `backend/`** | **No existe**                  |

## Árbol final de `apps/backend`

```text
apps/backend/
  Binexus.slnx
  Directory.Build.props
  global.json
  README.md
  .config/
  db/
  scripts/
  spike/
  src/
    Binexus.Api/
    Binexus.Workers/
    Binexus.Platform/
    Binexus.Platform.Features.Contracts/
    Binexus.SharedKernel/
    Modules/
  tests/
  tools/
```

No se creó `contracts/` vacío (no existía en el árbol movido; docs que lo citan siguen apuntando a la ruta objetivo futura / schemas en `docs/events/schemas`).

## Cantidad de archivos movidos

Árbol completo de la solución .NET (sin `bin`/`obj`/`.vs`): ~cientos de fuentes + tooling. Movimiento filesystem (el árbol .NET no estaba tracked como `backend/` en este working tree).

## Rutas / scripts / Docker / CI actualizados

- `package.json` → `apps/backend/...` (`dev:backend`, `build:backend`, `test:backend`, smoke, migrate, seed)
- `Dockerfile.api` / `Dockerfile.workers` → `COPY apps/backend/...`, `WORKDIR /src/apps/backend`
- `.dockerignore` → **ya no excluye** `apps/backend` (antes excluía Nest)
- `.github/workflows/ci.yml` → `working-directory: apps/backend`, scripts/artifacts paths
- CODEOWNERS, PR/issue templates
- Scripts: ROOT `../../..` desde `apps/backend/scripts/`
- OpenAPI out dir: `../../../../artifacts/openapi` desde `Binexus.Api.csproj`
- Test MinIO cors: +1 nivel hacia repo root

## Skills/rules modificados (justificación)

| Archivo                                           | Por qué                                                |
| ------------------------------------------------- | ------------------------------------------------------ |
| `.cursor/rules/skill-auto-router.mdc`             | Glob activo `backend/**/*.cs` → `apps/backend/**/*.cs` |
| `.cursor/rules/dotnet-backend.mdc`                | Home de solución                                       |
| `.cursor/rules/semantic-naming.mdc`               | Globs Modules                                          |
| `.cursor/rules/typescript-testing.md`             | Rutas `backend/tests`                                  |
| `.cursor/skills/dotnet-clean-code/SKILL.md`       | `dotnet format` / props                                |
| `.cursor/skills/dotnet-modular-monolith/SKILL.md` | Layout home                                            |
| `.cursor/skills/learn-codebase/SKILL.md`          | Links Modules/Platform                                 |
| `.cursor/skills/webapp-testing/SKILL.md`          | Descripción API home                                   |
| `.cursor/skills/semantic-naming/SKILL.md`         | Descripción Modules                                    |

## OpenAPI / SDK / SQL

- OpenAPI regenerado en `artifacts/openapi/binexus-v1.json` (hash estable entre regen)
- SDK regenerado vía `apps/backend/scripts/generate-sdk.ps1`
- SQL idempotente: `apps/backend/db/binexus-idempotent.sql`
- **0 migraciones EF nuevas**; `has-pending-model-changes` → no pending

## Búsqueda rutas obsoletas

| Clase                    | Ejemplos                                                                                |
| ------------------------ | --------------------------------------------------------------------------------------- |
| HISTORICAL_DOCUMENTATION | Gate 5–7 checkpoints, nest-audits (`backend/scripts/...` en el momento de la migración) |
| FALSE_POSITIVE           | `apps/backend/...` (ruta nueva)                                                         |
| ACTIVE_PATH_BUG          | Ninguno ejecutable restante tras el ajuste                                              |

## Verificación

| Check                                                       | Result                                                 |
| ----------------------------------------------------------- | ------------------------------------------------------ |
| `pnpm install --frozen-lockfile`                            | PASS                                                   |
| `dotnet restore/build apps/backend/Binexus.slnx -c Release` | PASS, **0 warnings**                                   |
| `dotnet test`                                               | **237** passed (29+58+150), 0 failed, 0 skipped        |
| NuGet High/Critical                                         | **0**                                                  |
| has-pending-model-changes                                   | No pending                                             |
| SDK test + typecheck                                        | 9/9 + PASS                                             |
| Web typecheck / lint / build                                | PASS                                                   |
| Compose services                                            | `postgres migrate minio minio-bucket-init api workers` |
| `pnpm docker:smoke:win` (puertos aislados)                  | **GATE6 COMPOSE SMOKE PASS**                           |
| Nest / Redis                                                | **0**                                                  |

## Riesgos residuales

- Docs `docs/events/README.md` citan `apps/backend/contracts/events` aunque esa carpeta aún no existe en el árbol (ya era thus under root `backend/`); no se inventó carpeta vacía.
- Checkpoints Gate 5–7 conservan paths `backend/` históricos a propósito.
- CI de GitHub no re-ejecutado en este agente; gates locales verdes.
- `.vs` bajo `apps/backend` es local — no commitear.

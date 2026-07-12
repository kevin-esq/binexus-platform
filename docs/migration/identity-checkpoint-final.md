# CHECKPOINT IDENTITY — FINAL

**Fecha:** 2026-07-11  
**Estado:** **CERRADO formalmente** (ajustes de enumeración + catálogo de roles). Inventory autorizado a continuación.

Auditoría Nest: [`identity-nest-audit.md`](./identity-nest-audit.md)

---

## Seed por ambiente y pruebas

| Ambiente           | Seed demo                                                                                | Contraseña                                                                         |
| ------------------ | ---------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------- |
| Testing            | Registrado + ejecutable                                                                  | Default del placeholder conocido **solo en Testing**                               |
| Development        | Registrado; password **obligatoria** vía `IdentitySeed:AdminPassword` (user-secrets/env) | Si se usa el placeholder conocido → **warning sanitizado** (sin loguear el secret) |
| Production/Staging | **No se registra** `DevelopmentIdentitySeeder`                                           | Arranque **falla** si `IdentitySeed:AdminPassword` es el placeholder conocido      |

- No aparece en `appsettings*.json`, Dockerfiles, compose, OpenAPI ni artefacto publish.
- Pruebas: seed Test + idempotencia; Production sin seeder; Production rechaza placeholder; publish JSON sin placeholder.

```powershell
dotnet user-secrets set "Jwt:SigningKey" "<>=32 bytes>" --project backend/src/Binexus.Api
dotnet user-secrets set "IdentitySeed:AdminPassword" "<dev-only>" --project backend/src/Binexus.Api
```

---

## Benchmark Argon2 (herramienta, no CI)

```text
dotnet run --project backend/tools/Argon2Benchmark -c Release
```

Resultados medidos (máquina de desarrollo, 2026-07-11):

| Concurrency | wall    | p50     | p95     | Peak WS  |
| ----------- | ------- | ------- | ------- | -------- |
| 1           | 342 ms  | 340 ms  | 340 ms  | ~302 MB  |
| 5           | 1395 ms | 1365 ms | 1395 ms | ~702 MB  |
| 10          | 3388 ms | 2814 ms | 3387 ms | ~1226 MB |
| 20          | 9098 ms | 7763 ms | 9067 ms | ~1437 MB |

- CancellationToken → `OperationCanceledException` (ok)
- Hash malformado → `false`
- **Límite de concurrencia Argon2 en runtime: 2** (SemaphoreSlim) tras observar presión de memoria ≥ ~1 GB a 10+ verifies
- Parámetros **no** reducidos: argon2id m=65536 t=3 p=4
- Password UTF-8: **1–1024 bytes** antes de Argon2

---

## Protección parámetros Argon2 hostiles

Al verificar hashes almacenados se parsea el PHC y se rechaza si:

- variante ≠ `argon2id`
- `m > 262144` (256 MiB) o `t > 10` o `p > 8`
- formato corrupto

`NeedsRehash` si parámetros ≠ locked target.

---

## Isopoh — versión y riesgo

| Campo                   | Valor                                                             |
| ----------------------- | ----------------------------------------------------------------- |
| Paquete                 | `Isopoh.Cryptography.Argon2` **2.0.0**                            |
| Upstream                | https://github.com/macleman/Isopoh                                |
| NuGet audit             | limpio (sin vulnerabilidades en solución)                         |
| Encapsulación           | Solo `Infrastructure.Argon2PasswordHasher` vía `IPasswordHasher`  |
| Alternativa evaluada    | Konscious (sin PHC Node-compatible out-of-box)                    |
| Criterio de sustitución | Advisory High/Critical, incompatibilidad PHC, o abandono upstream |
| Revisión                | Cada Gate / cuando NuGet Audit falle                              |

---

## Compatibilidad Node / .NET

- Hash Nest (`argon2@0.41.x`) → verify .NET: **probado**
- Hash .NET → verify .NET: **probado**
- Password incorrecto / corrupto / variante `argon2i` / params hostiles: **probado**

---

## Refresh JWT → opaco (`BREAKING_INTERNAL_FORMAT / HTTP_COMPATIBLE`)

Búsqueda en `apps/web`, `apps/desktop`, `packages/sdk`, tests:

- Ningún consumidor decodifica refresh, lee claims, exige 3 segmentos JWT, ni calcula exp desde el token.
- Web/SDK tratan `refreshToken` como string opaco en body/localStorage.
- Contrato HTTP: `{ accessToken, refreshToken }` / `{ refreshToken }` sin cambio de nombres.
- Prueba contractual: login → refresh → logout sin imponer formato JWT.

---

## Refresh concurrente y política reuse

- Mecanismo real: `ExecuteUpdate` condicional (`UsedAtUtc`/`RevokedAtUtc` null) + TX.
- Exactamente un `200`; el otro `401` + `REFRESH_TOKEN_REUSED`.
- El perdedor revoca la **familia**; puede invalidar también el token del ganador (sesión legítima cerrada en retry de red).
- UX: usuario debe volver a login; código público `REFRESH_TOKEN_REUSED`; telemetría `RefreshReuseDetected`.
- Ventana de gracia futura: **documentada, no implementada**.

---

## Modelo refresh token

- RNG 32 bytes (256-bit), Base64Url sin padding
- Persistencia: SHA-256 hex (apropiado: token ya es alta entropía)
- Unique index `TokenHash`
- Raw token nunca en DB ni logs
- FamilyId / Parent / ReplacedBy / Used / Revoked / Reason

---

## Normalización de email (intencional vs Nest exact-match)

```text
trim → Unicode FormKC → ToUpperInvariant
unique (TenantId, NormalizedEmail)
```

No se aplican reglas Gmail (no se eliminan puntos).  
Mismo email en otro tenant: permitido. Duplicado normalizado en mismo tenant: rechazado.

---

## Middleware y rate limiter

Orden efectivo:

```text
ForwardedHeaders (+ HTTPS fuera de Dev/Test)
Serilog
ExceptionHandler (Problem Details)
Routing
Cors
RateLimiter
Authentication
DevelopmentTenantMiddleware
Authorization
MapEndpoints
```

**Policy `auth`** (login + refresh):

| Campo     | Valor                                          |
| --------- | ---------------------------------------------- |
| Algoritmo | Fixed window                                   |
| Límite    | 30                                             |
| Ventana   | 1 minuto                                       |
| Cola      | 0                                              |
| Partition | `{RemoteIp}:{Path}` (IP vía ForwardedHeaders)  |
| 429       | Problem Details `RATE_LIMITED` + `Retry-After` |

No se usa email/tenantSlug como única llave.

---

## Validación JWT

ASP.NET JwtBearer valida: firma HS256, issuer, audience, lifetime/exp, signed tokens. Claims incluyen `jti`. Clock skew 30s. Alg no se acepta desde el token.

---

## Estados usuario/tenant / roles (cierre 2026-07-11)

| Caso                                                      | Respuesta pública                                                  |
| --------------------------------------------------------- | ------------------------------------------------------------------ |
| Login: tenant/user/password inválidos **o** user disabled | `401 INVALID_CREDENTIALS` (uniforme)                               |
| Login: rol desconocido                                    | `401 INVALID_CREDENTIALS` + log `UnknownRole`                      |
| Refresh: user disabled / rol desconocido                  | `401 INVALID_REFRESH_TOKEN`                                        |
| GET /auth/me tras disable (JWT aún válido)                | `403 ACCOUNT_UNAVAILABLE`                                          |
| Tenant disabled                                           | **No soportado** (sin campo)                                       |
| Branch otro tenant                                        | No se emite `branchId`                                             |
| Catálogo roles                                            | `SUPER_ADMIN\|ADMIN\|CASHIER\|WAREHOUSE\|DRIVER` + `ck_users_role` |

Motivos internos de login (`LoginFailedReason`): `TenantNotFound`, `UserNotFound`, `InvalidPassword`, `UserDisabled`, `UnknownRole` — **nunca** al cliente.

---

## Artefacto / secretos

- Sin SigningKey en repo `appsettings.json`
- Tests: llave efímera / explícita de test
- Production: ValidateOnStart longitud ≥ 32
- OpenAPI document generation: llave efímera solo en host GetDocument
- Publish JSON audit: sin placeholder de password demo

---

## OpenAPI / SDK

- Regenerado; `refresh()` + logout con body
- `git diff` schema reproducible (clean en verificación)

---

## NuGet audit

Sin vulnerabilidades High/Critical transitivas. Excepciones: `[]`.

---

## Restore / build / test (cierre 2026-07-11)

```text
dotnet restore → OK
dotnet build -c Release → 0 warnings / 0 errors (Jwt__SigningKey en env para OpenAPI gen)
dotnet test  -c Release → 74/74 passed, 0 failed, 0 skipped
  Unit 18 + Architecture 4 + Integration 52
dotnet list package --vulnerable --include-transitive → limpio
OpenAPI regenerado → artifacts/openapi/binexus-v1.json
SDK regenerado → packages/sdk/src/generated/schema.d.ts
```

---

## Riesgos aceptados

1. Reuse concurrente puede cerrar sesión legítima (familia completa).
2. Access token válido tras logout hasta `exp` (sin blacklist).
3. Isopoh 2.0.0 — dependencia encapsulada; revisar en cada audit.
4. Development exige secrets locales (sin fallback de signing key en código).
5. Argon2 concurrency capped at 2 — login latency under burst; mitigated by rate limit.

**Identity cerrado. Inventory autorizado en el mismo PR.**

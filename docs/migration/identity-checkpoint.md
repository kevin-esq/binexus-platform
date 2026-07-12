# CHECKPOINT IDENTITY

**Fecha:** 2026-07-11  
**Estado:** Identity migrado; listo para aprobación. **Inventory / Orders no iniciados.**

Auditoría Nest previa: [`identity-nest-audit.md`](./identity-nest-audit.md)

---

## CHECKPOINT IDENTITY

### Auditoría de comportamiento Nest

| Área       | Nest                                         | .NET                                             |
| ---------- | -------------------------------------------- | ------------------------------------------------ |
| Login      | slug + email + password → tokens             | Equivalente + `INVALID_CREDENTIALS` + rate limit |
| Refresh    | JWT refresh + SHA-256 store; rotación simple | **Opaque RNG** + family + reuse detection        |
| Logout     | Revoca refresh; 204                          | Igual; sin blacklist access                      |
| `/me`      | user/tenant/branch                           | Misma forma                                      |
| Argon2     | argon2id m=65536,t=3,p=4                     | Isopoh; cross-verify Nest hash                   |
| Claims     | sub,tenantId,role,branchId                   | + iss,aud,jti; validación completa               |
| Cookies    | No (body + localStorage)                     | Conservado                                       |
| Tests Nest | Ninguno                                      | 51 tests .NET                                    |

### Estructura Identity

```text
Binexus.Modules.Identity/
  Domain/          Tenant, User, Branch, RefreshToken, RoleNames
  Application/     IAuthService, EmailNormalizer, AuthErrorCodes, DTOs
  Infrastructure/  Argon2, JWT, AuthService, EF configs, seed, logs
  Features/Auth/   MapIdentityEndpoints (/auth/*)
```

Platform: `IDbContextModelContributor` — sin referencia a Identity.

### Migración EF y SQL

- Id: `20260711023047_Identity_TenantsUsersBranchesRefresh`
- Tablas: `tenants`, `branches`, `users`, `refresh_tokens`
- **No** `tenant_features` en esta etapa
- Delete: Tenant→User/Branch `RESTRICT`; User→RefreshToken `CASCADE`; Branch→User.BranchId `SET NULL`

### Modelo

| Entidad      | Notas                                                                                  |
| ------------ | -------------------------------------------------------------------------------------- |
| Tenant       | slug unique, name, CreatedAtUtc                                                        |
| Branch       | TenantId, name                                                                         |
| User         | NormalizedEmail unique/tenant, PasswordHash PHC, Role, BranchId?, IsSystem, IsDisabled |
| RefreshToken | TokenHash SHA-256, FamilyId, Parent/ReplacedBy, Used/Revoked, reason                   |

IDs: UUIDv7 aplicación.

### Argon2

- Biblioteca: **Isopoh.Cryptography.Argon2** 2.0.0
- Parámetros: argon2id v19, m=65536, t=3, p=4
- Compatibilidad: hash Nest `ChangeMe123!` verificado en unit test
- NeedsRehash si prefijo de parámetros difiere → rehash en login exitoso

### JWT

| Campo      | Valor                                                  |
| ---------- | ------------------------------------------------------ |
| Alg        | HS256 (explícito; no alg from token)                   |
| Claims     | sub, tenantId, role, branchId, iss, aud, iat, exp, jti |
| Access TTL | 15m (configurable)                                     |
| Clock skew | 30s                                                    |
| SigningKey | env/user-secrets; min 32 bytes; ValidateOnStart        |

### Refresh rotation / reuse

1. Hash SHA-256 del token opaco (32 bytes Base64Url)
2. TX: claim condicional (`UsedAtUtc`/`RevokedAtUtc` null) → marcar used → emitir nuevo con Parent/Family
3. Reuse → revocar familia → `REFRESH_TOKEN_REUSED`
4. Concurrente: solo un update gana

### Contratos HTTP

| Método | Ruta            | Auth   | Rate limit | Status        |
| ------ | --------------- | ------ | ---------- | ------------- |
| POST   | `/auth/login`   | Public | `auth`     | 200 / 401     |
| POST   | `/auth/refresh` | Public | `auth`     | 200 / 401     |
| POST   | `/auth/logout`  | Bearer | —          | 204           |
| GET    | `/auth/me`      | Bearer | —          | 200 / 401/403 |

Body refresh/logout: `{ refreshToken }` (no cookies).

### OpenAPI y SDK

- OpenAPI regenerado sin rutas internas
- `BinexusClient.refresh()` añadido
- `logout()` envía refreshToken desde storage
- Header `GENERATED FILE — DO NOT EDIT` en schema

### Rate limiting

Policy `auth`: fixed window 30/min por IP (Gate 2). Solo login/refresh.

### Tenant isolation

- Login: tenant por **slug**
- Autenticados: tenant desde claims JWT (no header/body override en prod)
- Tests: cross-tenant refresh/me rechazados

### Pruebas

| Suite                                             | Resultado            |
| ------------------------------------------------- | -------------------- |
| Unit                                              | 12/12                |
| Architecture                                      | 4/4                  |
| Integration (Testcontainers `postgres:16-alpine`) | 35/35                |
| **Total**                                         | **51/51**, 0 skipped |

### NuGet audit

`dotnet list package --vulnerable --include-transitive` → **sin vulnerabilidades**. Excepciones: `[]`.

### Restore / build / test

```text
dotnet restore → OK
dotnet build -c Release → 0 warnings, 0 errors (TreatWarningsAsErrors)
dotnet test -c Release → 51/51
```

### Riesgos pendientes (aceptados / documentados)

| Riesgo                           | Mitigación                                        |
| -------------------------------- | ------------------------------------------------- |
| Refresh opaco ≠ Nest JWT refresh | Contrato HTTP igual; Nest sigue vivo hasta Gate 6 |
| Dev SigningKey fallback inseguro | Solo Development; prod falla sin key              |
| Access token válido post-logout  | Intencional (semántica Nest); sin blacklist       |
| Sin permissions system           | Roles exactos Nest; estructura lista para ampliar |
| Problem Details en auth          | `title`/`extensions.code` con códigos estables    |

### Prohibido / no hecho

Registro público, MFA, SSO, admin users, Inventory, Orders, API keys, password reset.

---

**Siguiente:** aprobación explícita de Identity antes de Inventory/Orders.

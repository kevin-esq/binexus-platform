# ADR-0006: Authentication — JWT (access + refresh rotation) + Argon2 + RBAC

| Field    | Value                          |
| -------- | ------------------------------ |
| Status   | Accepted — Amended by ADR-0015 |
| Date     | 2026-05-23                     |
| Deciders | Kevin Esquivel                 |
| Tags     | security, auth, identity, rbac |

> **Amended by ADR-0015:** JWT + refresh rotation + RBAC remain; Identity module is .NET (Argon2id / ASP.NET Core), not Nest.

## Context and problem statement

We need to authenticate users (web, desktop, future mobile) and authorize them by role within a tenant. The session model must work for SPAs, native desktop apps, and eventually offline scenarios where a local hub holds the session for hours without re-validating against the cloud.

We also need a password storage scheme that resists offline cracking attempts on a stolen database dump.

**Question:** what session model, password hash, and authorization scheme do we ship?

## Decision drivers

- **Multi-client** — web (Next.js), desktop (Tauri), mobile (Expo, future).
- **Stateless verification on the request path** — backend should validate a token without a DB round-trip.
- **Revocable sessions** — stolen refresh tokens must be killable.
- **Future offline-first** — the access token must be self-contained for a local hub to authorize requests without the cloud.
- **Password storage that survives a DB leak** — memory-hard hashing.
- **Roles, not free-form permissions** — Phase 0 doesn't need ABAC complexity.

## Considered options

### Session model

1. **Server-side sessions (cookie + session store)**.
2. **JWT access token only** (no refresh).
3. **JWT access token + refresh token with rotation** (chosen).
4. **OAuth2 with a third-party IdP** (Auth0, Keycloak).

### Password hashing

A. **bcrypt**.
B. **scrypt**.
C. **Argon2id** (chosen).

### Authorization

I. **RBAC** with a small `Role` enum (chosen for Phase 0).
II. **ABAC** (attribute-based, policy engine).
III. **ACL** per resource.

## Decision outcome

**Chosen options:**

- **Session model:** JWT access token (short-lived, ~15 min) + refresh token (long-lived, rotated on every use, hash stored server-side for revocation).
- **Password hashing:** Argon2id with sane parameters (memory ≥ 19 MiB, parallelism 1, iterations 2). Tunable via env.
- **Authorization:** RBAC via the `Role` enum (`SUPER_ADMIN`, `ADMIN`, `CASHIER`, `WAREHOUSE`, `DRIVER`), enforced by a `RolesGuard`.

JWTs carry `{ sub: userId, tenantId, role, branchId? }` — the same shape consumed by `TenantContextMiddleware` (ADR-0005).

### Positive consequences

- **Stateless request path.** The backend validates the access token with a key — no DB hit per request.
- **Revocable.** A compromised refresh token is killed by deleting its row.
- **Rotation defeats replay.** A leaked refresh token is invalidated the moment the rightful owner refreshes again (`reuse detection` flag fires → revoke all tokens for the user).
- **Argon2id** is the current best-practice password hash (winner of the Password Hashing Competition; memory-hard).
- **Roles are explicit and small.** Easy to reason about; easy to lint against.

### Negative consequences

- **JWT revocation of access tokens** between refresh windows is not possible without a deny list. We accept the ≤ 15 min window.
- **Argon2 is a native dependency** — requires `allowBuilds` whitelisting in pnpm 11 (handled).
- **RBAC won't fit** the day we need per-record permissions (e.g. "this user can edit _only_ orders for branch X"). We will then add a thin ABAC layer on top, not replace RBAC.

### Trade-offs accepted

- Access tokens up to 15 minutes of staleness in revocation. Mitigated for high-risk actions by re-prompting for password.
- We keep RBAC's coarseness now. Branch-scoping is the second axis (carried in `branchId`); per-resource ACLs are deferred until a real use case appears.

## Pros and cons of the options

### Session model

#### Server-side sessions

- **Good:** Trivial revocation.
- **Bad:** Backend hits a session store every request — bad for offline hubs.
- **Bad:** Sticky-session pain in multi-instance deploys without shared store.

#### JWT access only

- **Good:** Simplest. Stateless.
- **Bad:** Long-lived access tokens are dangerous to leak.
- **Bad:** No revocation mid-flight.

#### JWT access + refresh rotation _(chosen)_

- **Good:** Stateless request path + a revocation handle.
- **Good:** Rotation + reuse detection = strong story against token theft.
- **Bad:** More complex than session cookies.

#### OAuth2 with third-party IdP

- **Good:** Outsource the hard parts (MFA, password resets, OIDC).
- **Bad:** Adds a hard external dependency.
- **Bad:** Overkill for Phase 0 with a small set of internal roles.
- **Bad:** Multi-tenant routing through a third-party IdP is non-trivial.

### Password hashing

#### bcrypt

- **Good:** Battle-tested; everywhere.
- **Bad:** Capped at 72 bytes input, not memory-hard. Modern GPU/ASIC crackers are a concern.

#### scrypt

- **Good:** Memory-hard.
- **Bad:** Less first-class library support than Argon2 in Node.

#### Argon2id _(chosen)_

- **Good:** PHC winner; memory-hard; resistant to GPU/ASIC.
- **Good:** Solid Node binding (`argon2`).
- **Bad:** Native dependency (build script whitelist required).

### Authorization

#### RBAC _(chosen)_

- **Good:** Five roles cover Phase 0 cleanly.
- **Good:** Trivial to enforce with a guard.
- **Bad:** Doesn't express per-record rules.

#### ABAC

- **Good:** Expressive — "user can edit if `record.branchId === user.branchId AND record.status === DRAFT`".
- **Bad:** Policy engines (OPA, Casbin) add operational weight.
- **Bad:** Premature for Phase 0.

#### ACL

- **Good:** Most flexible.
- **Bad:** Performance and UX nightmare without an explicit model.

## Validation

This decision is working if:

- A leaked refresh token is invalidated within one refresh cycle (rotation + reuse detection works).
- Password hashes withstand a DB dump well enough that the realistic threat is phishing, not cracking.
- New endpoints get a `@Roles(...)` decorator and the guard does the rest — no ad-hoc role checks inside handlers.

It is failing if:

- We catch ourselves embedding role logic inside business code (`if (user.role !== 'ADMIN') ...`).
- Refresh token rotation produces user-visible logouts under normal usage.
- We start needing per-record permissions and bolt them on with `if`-statements in handlers (signal to add a thin ABAC layer).

## More information

- [Argon2 PHC](https://github.com/P-H-C/phc-winner-argon2)
- [OWASP — Authentication cheat sheet](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html)
- [OWASP — JSON Web Token cheat sheet](https://cheatsheetseries.owasp.org/cheatsheets/JSON_Web_Token_for_Java_Cheat_Sheet.html)
- [RFC 6749 — OAuth 2.0](https://datatracker.ietf.org/doc/html/rfc6749) (refresh-token semantics)
- Related: ADR-0005 (multi-tenant — JWT carries `tenantId`), ADR-0008 (observability — auth events become structured logs).

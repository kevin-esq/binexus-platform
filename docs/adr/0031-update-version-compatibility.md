# ADR-0031: Update and version compatibility

| Field    | Value          |
| -------- | -------------- |
| Status   | Proposed       |
| Date     | 2026-07-12     |
| Deciders | Kevin Esquivel |

## Context

Tauri, Branch Runtime, Postgres migrations, and sync protocol evolve on different clocks. One stale cashier must not brick the sucursal.

## Decision

Differentiate:

```text
Tauri (Branch Client) update
Branch Runtime update
PostgreSQL migration
Sync protocol update
```

### Compatibility matrix (design)

| Channel               | Rule                                                                                               |
| --------------------- | -------------------------------------------------------------------------------------------------- |
| Branch API version    | Semver; clients send version header                                                                |
| Desktop version       | Branch enforces `MinDesktopVersion`; newer desktop may require `MinApiVersion`                     |
| Sync protocol version | Negotiated; older protocol supported during a documented window                                    |
| Supported events      | Consumer ignores unknown optional fields; rejects breaking unknown required types into dead-letter |
| Compatibility window  | At least one prior minor desktop against current Branch API for LAN clients                        |

A single outdated Branch Client is rejected or limited; other clients and Branch Server continue.

Auto-update implementation is out of scope for this ADR.

## Consequences

### Positive

- Controlled rollout.
- Isolates stale clients.

### Negative / Trade-offs

- Support matrix to maintain.
- Forced upgrade messaging in Tauri.

## Alternatives considered

1. **Always require lockstep updates of all cashiers** - Rejected as brittle.
2. **No min version checks** - Rejected.
3. **Auto-update mandatory in v1** - Deferred.

## Decision outcome

Proposed. Await checkpoint approval before Accepted.

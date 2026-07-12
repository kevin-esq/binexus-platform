# ADR-0024: Offline-first - internet vs Branch Server LAN

| Field    | Value          |
| -------- | -------------- |
| Status   | Proposed       |
| Date     | 2026-07-12     |
| Deciders | Kevin Esquivel |

## Context

ADR-0003 says offline-first. Operators conflate "no internet" with "cashier cannot reach Branch Server". Promising per-terminal confirmed sales without the Principal oversells v1.

## Decision

### Internet down

```text
Internet to Cloud is down
→ the sucursal continues normal in-person operations on Branch Server
```

Sales, sessions, stock movements, and local routes commit on Branch Server PostgreSQL. Sync drains later.

### LAN to Branch Server down

```text
Branch Client cannot reach Branch Server
→ the terminal cannot confirm new authoritative operations
```

v1 does **not** promise that each terminal sells in isolation with its own DB or confirmed sales queue.

### What Tauri may keep locally

- Unconfirmed cart
- Configuration and server profile
- Visual session / UI state
- Cached read models
- A request whose response was lost (client may retry with same `commandId`)

### What counts as confirmed

A sale (or other authoritative command) is confirmed only when Branch Server and local PostgreSQL commit.

### Explicit non-goal

Degraded terminal mode with local authoritative commits requires a future ADR.

### Labels

| Phrase                          | Meaning                                  |
| ------------------------------- | ---------------------------------------- |
| Offline de internet             | Cloud unreachable; Branch Server up      |
| Offline del Branch Server (LAN) | Branch Client cannot reach Branch Server |

## Consequences

### Positive

- Honest product promise.
- Keeps single authority (ADR-0017).

### Negative / Trade-offs

- Cashier downtime if Principal fails.
- Support must teach both offline kinds.

## Alternatives considered

1. **Per-terminal SQLite authority** - Rejected for v1.
2. **Cloud write fallback when Principal down** - Rejected for offline-first.
3. **Silent local confirm then merge** - Rejected.

## Decision outcome

Proposed. Await checkpoint approval before Accepted. Amends interpretation of ADR-0003 for Branch Runtime.

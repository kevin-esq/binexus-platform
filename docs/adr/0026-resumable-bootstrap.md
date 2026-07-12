# ADR-0026: Resumable Branch bootstrap

| Field    | Value          |
| -------- | -------------- |
| Status   | Proposed       |
| Date     | 2026-07-12     |
| Deciders | Kevin Esquivel |

## Context

Activation must not be one giant download. Networks drop. Catalog scale is unknown; Catalog module is not fully built. UX must stay generic.

## Decision

Bootstrap is phased and resumable:

```text
Activate
→ obtain identity
→ persist credentials
→ start bootstrap
→ download manifests
→ download batches
→ verify checksum/version
→ save checkpoint
→ continue after interrupt
→ mark Branch Ready
```

### Phases (generic UX copy)

1. Descargando configuración
2. Descargando catálogo publicado
3. Aplicando módulos
4. Finalizando sucursal

Do not promise specific SKU counts or vendors in architecture docs.

### Rules

- Each verified batch advances a bootstrap checkpoint.
- Re-download is idempotent by batch id/checksum.
- Cancel leaves instance `Bootstrapping` until resumed or deactivated.
- Tauri on Principal shows phase + percent from Branch health API.
- If subscription/entitlement is revoked during bootstrap, Branch stops at a safe phase and surfaces `EntitlementBlocked`; it does not mark Ready.
- Partial operation: only explicitly listed prerequisites enable each capability (e.g. auth before POS). Ready means prerequisites for declared modules are met.

Secondary Branch Clients do not run Cloud bootstrap. They pair and pull needed cache from Branch Server.

## Consequences

### Positive

- Survives flaky internet during opening a branch.
- Honest UX without fake catalog promises.

### Negative / Trade-offs

- Longer time-to-Ready on first install.
- Need clear prerequisite matrix per module later.

## Alternatives considered

1. **Single monolithic download request** - Rejected.
2. **Ready before catalog** - Rejected for POS.
3. **Hard-coded 15k product SLA** - Rejected until Catalog exists.

## Decision outcome

Proposed. Await checkpoint approval before Accepted.

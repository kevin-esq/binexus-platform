# ADR-0019: Branch Server activation with Cloud

| Field    | Value          |
| -------- | -------------- |
| Status   | Proposed       |
| Date     | 2026-07-12     |
| Deciders | Kevin Esquivel |

## Context

Binding a Principal host to a tenant sucursal is a different trust relationship from attaching a cashier terminal to that Principal. One handshake must not serve both.

## Decision

**Branch Server activation** links:

```text
Branch Server
↔
Tenant / Branch in Cloud
```

Web Admin generates a short-lived, single-use activation code (or replace token) bound to `(TenantId, BranchId)`.

### Result of successful activation

- Existing local `BranchInstanceId` (cloud-adopted; not reminted)
- Permanent Branch↔Cloud credential (sync + management)
- Installation identity (DeviceId for the Principal host)
- Initial configuration pointer
- Entitlements snapshot reference
- Bootstrap checkpoint cursor at start
- Local status becomes Active (future); TenantId / BranchId bound

### Flow (design)

```text
Branch Server first boot mints BranchInstanceId (UUIDv7) locally
→ Web Admin issues activation code
→ Branch Installer / Wizard submits code + BranchInstanceId to Cloud
→ Cloud validates entitlement and Active-instance rules (ADR-0017)
→ Cloud adopts BranchInstanceId (or rejects on conflict / Replace required)
→ Cloud returns credentials + TenantId/BranchId binding
→ Branch persists secrets in OS store
→ Branch starts resumable bootstrap (ADR-0026)
→ Branch marks Ready when prerequisites met
```

Activation codes do not remain on disk after use. Cloud must not silently substitute a different `BranchInstanceId`.

This flow does **not** issue Tauri device credentials for secondary cashiers.

## Consequences

### Positive

- Cloud remains source of tenant ownership.
- Replace and second-server rules have a single gate.

### Negative / Trade-offs

- First activation needs internet to Cloud.
- Support must distinguish activation failures from later pairing failures.

## Alternatives considered

1. **Same code for Branch activation and Tauri pairing** - Rejected.
2. **Offline forever without Cloud activation** - Rejected for v1 tenant control.
3. **Manual UUID paste without Cloud validation** - Rejected.

## Decision outcome

Proposed. Await checkpoint approval before Accepted.

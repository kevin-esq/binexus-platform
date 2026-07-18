# ADR-0020: Branch Client pairing with Branch Server

| Field    | Value          |
| -------- | -------------- |
| Status   | Proposed       |
| Date     | 2026-07-12     |
| Deciders | Kevin Esquivel |

## Context

Secondary cashiers must trust a specific Branch Server on a hostile or shared Wi-Fi. mDNS finds candidates; it does not authorize.

## Decision

**Branch Client pairing** links:

```text
Tauri (Branch Client)
↔
Branch Server
```

This flow is separate from Branch Server activation (ADR-0019).

### Pairing must validate

- Legitimate Branch Server (fingerprint / cert identity)
- Sucursal / BranchInstance match expected tenant branch
- Compatible API and client versions (ADR-0031)
- New `DeviceId` for the client machine
- Optional `TerminalId` assignment (or deferred selection)
- Short-lived local approval code or Principal operator approval
- Issue of device credential (cert or keyed credential)
- Revocation path on Branch (and sync of revocation to Cloud when online)

### Happy path (design)

```text
Discover candidates (ADR-0021)
→ Operator selects server (name, InstanceId fragment, version, fingerprint)
→ Verify TLS / pinned identity
→ Submit pairing approval artifact to Branch Server
→ Branch creates Device record + credential
→ Client stores device credential in OS/keychain store
→ Client authenticates API calls with device credential + user token
```

Cloud may optionally assist approval when online. Pairing must also work when the Principal can approve locally after the Branch Server is already activated.

mDNS / DNS-SD only returns candidates. It never proves trust.

## Consequences

### Positive

- Unknown LAN hosts cannot use user JWT alone.
- Revocation targets Device without deleting Terminal roles.

### Negative / Trade-offs

- Pairing UX when multiple servers appear.
- Local approval UX when Cloud is down (post-activation only).

## Alternatives considered

1. **Reuse Cloud activation code for cashiers** - Rejected.
2. **Trust mDNS hostname alone** - Rejected.
3. **User JWT without device credential** - Rejected (ADR-0023).

## Decision outcome

Proposed. PR5 implements the Branch Client pairing shell (Tauri + secure store + ceremony) on Windows MSVC; status remains Proposed until operational TLS, mDNS discovery, and POS surfaces land.

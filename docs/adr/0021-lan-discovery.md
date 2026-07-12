# ADR-0021: LAN discovery candidates and fallbacks

| Field    | Value          |
| -------- | -------------- |
| Status   | Proposed       |
| Date     | 2026-07-12     |
| Deciders | Kevin Esquivel |

## Context

Cashiers need to find Branch Server without typing IPs when possible. Hostnames like `binexus-server.local` collide, spoof, and fail on many routers. Discovery must not imply identity.

## Decision

Discovery returns **candidates**, not trusted servers.

Each candidate includes at least:

- Branch Server display name
- Partial `BranchInstanceId`
- Reachable address (`IP:port` or resolved host)
- Server version
- Verifiable fingerprint / public identity material

When more than one candidate appears, the operator must choose.

### Order

| Order | Mechanism                      | Role                                            |
| ----- | ------------------------------ | ----------------------------------------------- |
| 1     | mDNS / DNS-SD                  | Convenience on open LANs                        |
| 2     | QR from Principal              | Transfers endpoint + fingerprint without typing |
| 3     | Manual `IP:port` or hostname   | Always available fallback                       |
| 4     | Optional Cloud-assisted lookup | When client reaches Cloud but not multicast     |

`binexus-server.local` may appear as a display hint. It is never the trust anchor. TLS fingerprint / device-bound server identity is the trust anchor after selection (ADR-0023).

## Consequences

### Positive

- Works on locked-down LANs via manual entry.
- Reduces spoofing via hostname-only trust.

### Negative / Trade-offs

- Operator choice when two Principals advertise.
- mDNS still noisy on bad Wi-Fi.

## Alternatives considered

1. **Require static DNS only** - Rejected for SMB installs.
2. **Trust `.local` name as identity** - Rejected.
3. **Cloud-only discovery** - Rejected when internet is down.

## Decision outcome

Proposed. Await checkpoint approval before Accepted.

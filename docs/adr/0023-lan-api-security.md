# ADR-0023: LAN API security for Branch Server

| Field    | Value          |
| -------- | -------------- |
| Status   | Proposed       |
| Date     | 2026-07-12     |
| Deciders | Kevin Esquivel |

## Context

`HTTP local + user JWT` is not enough. Any host on the Wi-Fi can attempt API calls. Public CA names do not fit `.local` or changing LAN IPs.

## Decision

Initial security stack for Branch Client → Branch Server:

```text
TLS local
+ Branch Server identity (certificate / fingerprint)
+ device credential (paired DeviceId)
+ user access token
```

### Concrete posture (v1 design)

| Control              | Decision                                                                                          |
| -------------------- | ------------------------------------------------------------------------------------------------- |
| Listen address       | Bind only to configured interfaces (default private LAN), not `0.0.0.0` unless operator opts in   |
| Firewall             | Installer opens Branch API port on Private profile only                                           |
| Transport            | TLS required for paired clients                                                                   |
| Server identity      | Branch Installer generates a local server certificate; clients pin fingerprint learned at pairing |
| Device auth          | Mutual proof via device credential issued at pairing (client cert or signed device token)         |
| User auth            | Branch-signed user access token after local login (synced password hashes)                        |
| Unknown LAN host     | Rejected without valid device credential                                                          |
| Rotation             | Device credentials and server cert rotatable; old material revocable                              |
| Revocation           | Branch stores revoked DeviceIds; sync mirrors revocation to Cloud when online                     |
| IP / hostname change | Clients reconnect by pinned fingerprint + rediscovery/manual address; hostname is not trust       |
| Min client version   | Branch rejects clients below `MinDesktopVersion`                                                  |

### Establishing trust without public CA

1. Operator selects discovery candidate and sees fingerprint.
2. Pairing ceremony confirms fingerprint (QR compare or Principal display).
3. Client stores fingerprint + device credential in secure store.
4. Later TLS sessions must match pinned identity.

HTTP may exist only for Installer loopback diagnostics before activation. Paired Branch Clients do not use plaintext HTTP for business APIs.

Local user authentication uses synced credentials and BranchInstance-specific signing keys. Pairing is not a user session.

## Consequences

### Positive

- Blocks casual LAN clients without device pairing.
- Survives IP changes when fingerprint is pinned.

### Negative / Trade-offs

- Certificate UX and rotation support cost.
- First-time pairing requires careful fingerprint confirmation.

## Alternatives considered

1. **HTTP + user JWT only** - Rejected.
2. **Public CA for `.local`** - Rejected as impractical.
3. **IP allowlists alone** - Rejected (DHCP, BYOD).
4. **mTLS via enterprise PKI only** - Deferred; too heavy for SMB v1.

## Decision outcome

Proposed. Await checkpoint approval before Accepted.

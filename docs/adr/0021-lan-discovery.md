# ADR-0021: LAN discovery for Branch Server

| Field    | Value                                       |
| -------- | ------------------------------------------- |
| Status   | Proposed                                    |
| Date     | 2026-07-12                                  |
| Deciders | Kevin Esquivel                              |
| Tags     | architecture, branch, discovery, tauri, lan |

## Context and problem statement

ADR-0018 places one Branch Server inside each sucursal. Tauri terminals need to find that server on the local network before they can pair, authenticate, and call the Branch API over HTTP. Branch networks can be unmanaged, locked down, or segmented by corporate WiFi settings. Discovery must be convenient when the LAN supports it and reliable when it does not.

**Question:** how should Tauri terminals discover and connect to the Branch Server on a local network?

## Decision drivers

- First-run setup should be fast for a normal small-branch LAN.
- Discovery must not assume multicast works.
- Corporate WiFi, guest networks, VLANs, and AP isolation can block mDNS.
- Every connection path must still route through Branch API over HTTP.
- Fallbacks must support nontechnical operators and support staff.
- The discovery model must identify the Branch Server, not a database or a Tauri peer.

## Considered options

1. **mDNS/DNS-SD primary with required fallbacks** - Branch Server advertises `_binexus._tcp`; Tauri falls back to QR payload, manual `IP:port`, or local DNS.
2. **mDNS only** - rely on Bonjour/Zeroconf style discovery for every branch.
3. **Manual IP only** - operators enter the Branch Server address during setup.
4. **Cloud rendezvous for local discovery** - Cloud tells each terminal how to reach the Branch Server.

## Decision outcome

**Chosen option:** _mDNS/DNS-SD primary with required fallbacks_, because it gives a good setup path on friendly LANs without making multicast a hard dependency.

The Branch Server advertises a DNS-SD service named `_binexus._tcp` on the LAN. Tauri terminals use that advertisement as the primary discovery path. Bonjour and Zeroconf refer to the same family of local discovery techniques and may appear in platform documentation or dependencies.

Fallbacks are mandatory:

- QR code with a signed or structured connection payload.
- Manual `IP:port` entry.
- Optional local DNS name where a branch or IT provider can configure one.

The system must never assume mDNS works. Network isolation, firewall rules, and corporate WiFi can block multicast even when HTTP between the terminal and Branch Server works.

### Positive consequences

- Small branches can pair terminals without typing network details.
- Support can recover setup through QR or manual connection paths.
- Corporate environments can use local DNS when multicast is blocked.
- Discovery stays focused on the Branch Server HTTP endpoint.

### Negative consequences

- Setup UI and support docs must cover multiple connection paths.
- QR payloads and manual addresses need validation and clear error handling.
- mDNS behavior varies by operating system, network profile, and firewall policy.

### Trade-offs accepted

- Binexus accepts more setup paths to avoid brittle discovery.
- mDNS improves the happy path but does not become a correctness assumption.
- Cloud may help distribute pairing information in future flows, but Cloud must not participate in an in-person sale path.

## Pros and cons of the options

### Option 1 - mDNS/DNS-SD primary with required fallbacks

- **Good:** Gives automatic discovery where LAN multicast works.
- **Good:** Keeps setup possible on locked-down networks.
- **Good:** Matches common Bonjour and Zeroconf behavior on desktop platforms.
- **Bad:** Requires testing across Windows network profiles and firewall settings.
- **Bad:** More paths mean more setup UX and support documentation.

### Option 2 - mDNS only

- **Good:** Clean user experience on simple networks.
- **Good:** No address entry or QR flow required.
- **Bad:** Fails on common corporate WiFi and AP isolation setups.
- **Bad:** Support cannot recover without changing network configuration.
- **Bad:** Treats best-effort discovery as mandatory infrastructure.

### Option 3 - Manual IP only

- **Good:** Works when HTTP routing works and the address is known.
- **Good:** Avoids multicast dependencies.
- **Bad:** Creates avoidable setup friction.
- **Bad:** IP addresses can change unless the branch configures DHCP reservations or static addressing.
- **Bad:** Nontechnical operators may enter the wrong address or port.

### Option 4 - Cloud rendezvous for local discovery

- **Good:** Can work across complex networks if Branch Server reports its address to Cloud.
- **Good:** Central support can see pairing state.
- **Bad:** Cloud becomes part of local setup and may be unavailable during branch installation.
- **Bad:** Does not solve local reachability from terminal to Branch Server.
- **Bad:** Risks normalizing Cloud involvement in local operations.

## Validation

This decision is working if:

- A Branch Server advertises `_binexus._tcp` on networks that support DNS-SD.
- A Tauri terminal can connect through QR payload and manual `IP:port` when mDNS fails.
- Setup diagnostics can explain whether discovery, HTTP reachability, or pairing failed.
- Branch documentation tells support staff how to configure optional local DNS.
- No Tauri flow discovers or connects directly to PostgreSQL.

Re-evaluate this decision if:

- Field testing shows mDNS fails often enough that QR or manual entry should become primary.
- Windows service hosting or firewall rules make DNS-SD support unreliable even on simple LANs.
- A managed appliance or installer can provision local DNS more reliably than multicast discovery.

## More information

- Related ADRs: [ADR-0003](0003-offline-first-design.md), [ADR-0016](0016-runtime-modes-cloud-vs-branch.md), [ADR-0018](0018-branch-server.md), [ADR-0019](0019-device-identity.md)
- Related docs: [`docs/architecture/dotnet-backend.md`](../architecture/dotnet-backend.md)
- External: [RFC 6762 - Multicast DNS](https://www.rfc-editor.org/rfc/rfc6762), [RFC 6763 - DNS-Based Service Discovery](https://www.rfc-editor.org/rfc/rfc6763)

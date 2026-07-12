# ADR-0024: Branch local HTTP API

| Field    | Value                                     |
| -------- | ----------------------------------------- |
| Status   | Proposed                                  |
| Date     | 2026-07-12                                |
| Deciders | Kevin Esquivel                            |
| Tags     | branch, http, lan, openapi, security, pos |

## Context and problem statement

Secondary cashier terminals need a stable protocol to talk to the Branch backend on the LAN. The protocol must work with Tauri, web tooling, diagnostics, and future SDK clients without exposing PostgreSQL or forcing a Cloud round trip during in-person work.

The Branch API should resemble the Cloud API for the operational subset whenever possible. Shared modules should produce the same commands, validation, and OpenAPI contracts, while composition roots choose Cloud or Branch runtime services.

**Question:** what local protocol do POS and operations terminals use first, and how does the branch API expose it safely on the LAN?

## Decision drivers

- **Terminal simplicity** - Tauri clients need a common, debuggable protocol.
- **Shared surface** - Cloud and Branch should share the operational OpenAPI subset where domain behavior matches.
- **Local authority** - Terminals use Branch for POS when Branch is present.
- **LAN security** - Discovery must not imply trust; local traffic should prefer HTTPS.
- **Firewall containment** - Branch API should accept LAN traffic only by default.
- **Future transport flexibility** - gRPC may help sync or internal service calls later, but terminals need HTTP first.

## Considered options

1. **HTTP on LAN, preferably HTTPS with local CA or pinned certificates** - POS terminals call the Branch API over HTTP semantics and verify local server identity.
2. **gRPC first for terminals** - POS clients use gRPC for all branch operations.
3. **Direct PostgreSQL from terminals** - Tauri clients connect to the branch database.
4. **Cloud POS path even when Branch is present** - Terminals call Cloud for sales and use Branch as cache.

## Decision outcome

**Chosen option:** _HTTP on LAN, preferably HTTPS with local CA or pinned certificates_, because HTTP keeps the terminal protocol simple and lets Branch expose the same operational API shape as Cloud where the shared modules support it.

The Branch API exposes the operational subset of the Cloud OpenAPI surface where possible. Differences must come from runtime capability, not from duplicated domain modules. `AddCloudRuntime` and `AddBranchRuntime` select Cloud-specific or Branch-specific integrations at composition root level.

### Local address convention

Branch discovery prefers a stable local hostname:

- Primary: `https://binexus.local:<port>` through mDNS when available.
- Fallback: `https://<principal-lan-ip>:<port>` shown by the Principal installer or support screen.
- Development may use localhost conventions, but production terminals must treat discovery as untrusted until handshake verifies the server.

The final port number is a product configuration decision. The architecture requires a documented default, one Branch API listener per Principal, and installer control over firewall rules.

### Security and firewall posture

- Prefer HTTPS with a branch local CA or pinned server certificate.
- Bind the Branch API to LAN interfaces needed for cashier terminals, not to public interfaces.
- Configure Windows Firewall to allow the chosen port on private LAN profiles only by default.
- Reject unauthenticated operational requests even on the local subnet.
- Keep local certificate material in the OS secret store on the Principal.

### gRPC scope

gRPC remains optional for future sync, internal worker communication, or high-volume service-to-service paths. POS terminals use HTTP first. A future gRPC endpoint must not become the only way to complete a sale.

### Positive consequences

- Tauri POS clients can use a familiar request model and generated SDKs.
- Branch and Cloud can share OpenAPI contracts for operational commands.
- LAN firewall rules become explicit installation artifacts.
- HTTPS or certificate pinning prevents blind trust in `binexus.local`.
- Support can diagnose local API behavior with standard HTTP tools.

### Negative consequences

- Local HTTPS and certificate lifecycle add installation work.
- mDNS can fail on locked-down networks, so IP fallback needs good UX.
- HTTP may carry more overhead than gRPC for high-volume internal streams.

### Trade-offs accepted

- Binexus accepts certificate setup complexity to avoid cleartext credential flow on LAN.
- Binexus accepts HTTP first for terminal compatibility and reserves gRPC for later specialized paths.
- Binexus accepts a hostname convention with fallback because customer LANs vary.

## Pros and cons of the options

### Option 1 - HTTP on LAN with HTTPS or pinned certificates

- **Good:** Works naturally with Tauri, browsers, SDK generation, and OpenAPI.
- **Good:** Preserves the same operational surface as Cloud where modules are shared.
- **Good:** Allows standard auth, logging, and diagnostics.
- **Bad:** Requires careful certificate and firewall setup.
- **Bad:** mDNS support varies by network.

### Option 2 - gRPC first for terminals

- **Good:** Efficient binary protocol and strong contract tooling.
- **Bad:** Adds client complexity for Tauri and browser-adjacent tooling.
- **Bad:** Makes support and manual diagnostics harder.
- **Bad:** Premature for POS request patterns.

### Option 3 - Direct PostgreSQL from terminals

- **Good:** Avoids an API hop.
- **Bad:** Violates the rule that terminals never write PostgreSQL directly.
- **Bad:** Leaks database credentials to cashier machines.
- **Bad:** Bypasses domain validation, auth, audit, and outbox behavior.

### Option 4 - Cloud POS path even when Branch is present

- **Good:** One public API endpoint for all clients.
- **Bad:** Cloud outage stops in-person sales.
- **Bad:** Violates Branch operational authority.
- **Bad:** Turns Branch into a cache instead of the sale authority.

## Validation

This decision is working if:

- A Secondary Cashier can discover the Principal, verify its certificate, authenticate locally, and complete POS requests over HTTP.
- The Branch OpenAPI surface matches the Cloud operational subset where domain behavior is shared.
- Windows Firewall defaults allow LAN terminals and reject public network exposure.
- A Cloud outage does not change the POS terminal request path.
- No terminal receives PostgreSQL credentials.

It is failing if:

- POS terminals call Cloud for `CreateSale` while a Branch runtime is present.
- Operators disable TLS because pairing or login fails on LAN.
- Branch-only controllers fork business behavior from Cloud instead of sharing modules.
- The API binds broadly without firewall restriction.

## More information

- Related ADRs: [ADR-0002](0002-modular-monolith-architecture.md), [ADR-0003](0003-offline-first-design.md), [ADR-0022](0022-pairing-and-handshake.md), [ADR-0023](0023-branch-installation.md), [ADR-0025](0025-local-authentication.md)

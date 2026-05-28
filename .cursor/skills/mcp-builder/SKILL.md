---
name: mcp-builder
description: Build and ship Binexus's own MCP (Model Context Protocol) server(s) so customers, partners, and internal agents can drive the platform from any MCP-compatible client (Cursor, Claude Desktop, Codex, Windsurf, etc.). Use when designing the public Binexus MCP surface (tools + resources), when adding a new MCP tool/resource for an existing context, when writing the tool descriptor JSON, when packaging the MCP server for distribution, or when adding auth + multi-tenant scoping to MCP calls. Pairs with `mcp-server-patterns` (existing skill) and the architecture of `apps/backend`.
---

# mcp-builder (Binexus)

Build Binexus's outward-facing MCP server(s) — the thing customers will install in their AI assistant to drive their tenant from chat. Adapted from Anthropic's `mcp-builder` (see [`skills/skills-mainb/skills/mcp-builder/SKILL.md`](../../../skills/skills-mainb/skills/mcp-builder/SKILL.md)).

This skill is forward-looking — the Binexus MCP doesn't exist yet. It is the design contract for when we ship it.

## When to invoke

- Designing the first version of `@binexus/mcp` (the npm-distributable MCP server).
- Adding a new MCP `tool` (e.g. `binexus_create_order`, `binexus_dispatch_route`).
- Adding a new MCP `resource` (e.g. `binexus://tenant/<id>/orders`).
- Wiring auth (API token vs OAuth) to MCP calls.
- Publishing a new version to npm.

## Two distinct MCPs we may ship

| MCP                     | Purpose                                                                                  | Audience                       |
| ----------------------- | ---------------------------------------------------------------------------------------- | ------------------------------ |
| `@binexus/mcp-public`   | The customer-facing MCP. Wraps the same SDK that `apps/web` uses, scoped by API token.   | Tenants + their AI assistants  |
| `@binexus/mcp-internal` | Operator-only MCP. Adds back-office tools (cross-tenant queries, tier changes, refunds). | Binexus founder + ops engineer |

Public ships first (when F7 stabilizes). Internal can wait.

## Tool design (per Anthropic mcp-builder principles)

Each MCP tool is one verb on one noun. Atomic, idempotent, well-named.

### Naming

Pattern: `binexus_<verb>_<noun>`:

- `binexus_create_order`
- `binexus_list_orders`
- `binexus_get_order`
- `binexus_dispatch_delivery_route`
- `binexus_confirm_delivery_stop`
- `binexus_get_branch`
- `binexus_list_branches`

Mirror the SDK method names — `client.createOrder()` ↔ `binexus_create_order`. This is the same vocabulary `apps/web` uses; no parallel taxonomy.

### Parameters

- Use the existing Zod schemas from `packages/events/src/schemas/` where applicable.
- Required parameters first, then optional.
- Never accept `tenantId` as a parameter — the tenant is in the auth context. Accepting it as a param invites cross-tenant attacks.
- Date-times: ISO 8601 strings.
- IDs: branded types from `packages/types`.

### Return values

- The same shape the SDK returns. Consistency over "MCP-friendly" reformatting.
- Error: JSON-RPC error with code + message + a `recoverable` hint.
- Idempotency tokens: every write tool accepts an optional `idempotencyKey`, identical to the SDK pattern.

### Tool descriptor JSON

Each tool gets a JSON file under `packages/mcp/tools/<tool>.json`. Example skeleton:

```json
{
  "name": "binexus_create_order",
  "description": "Create a new order for the authenticated tenant. Idempotent when idempotencyKey is provided.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "branchId": { "type": "string", "description": "Branch ID where the order originates" },
      "lines": {
        "type": "array",
        "items": {
          "type": "object",
          "properties": {
            "sku": { "type": "string" },
            "quantity": { "type": "integer" }
          },
          "required": ["sku", "quantity"]
        }
      },
      "customerId": { "type": "string" },
      "idempotencyKey": { "type": "string" }
    },
    "required": ["branchId", "lines"]
  }
}
```

These descriptors auto-generate from Zod schemas via a build step — never hand-maintain both.

## Resources

Resources expose read-only views as URIs. Pattern: `binexus://<context>/<id>` or `binexus://<context>/<id>/<sub>`.

Examples:

- `binexus://tenant/me` — current tenant info.
- `binexus://orders/<orderId>` — single order.
- `binexus://orders/<orderId>/events` — event timeline for the order.
- `binexus://logistics/routes/<routeId>` — route state.
- `binexus://docs/architecture` — public docs.

Resources are paginated when the result is a list. MIME types: `application/json` for data, `text/markdown` for docs.

## Auth

The MCP server runs as a stdio process (Anthropic's MCP default transport) and authenticates via a Binexus API token stored in the user's MCP config.

Each tenant generates one or more **MCP tokens** in `/settings/tokens`. The token:

- Is tenant-scoped (`tenantId` baked in).
- Has a role (`viewer`, `dispatcher`, `admin`) — same RBAC as the panel.
- Has an optional expiry.
- Is revocable.

Token format (proposal):

```
bnx_mcp_<env>_<rand32>
bnx_mcp_live_a1b2c3...
```

The MCP server reads the token from env (`BINEXUS_TOKEN`) on startup, never as a tool parameter.

## Multi-tenant scoping

- Server resolves `tenantId` from the token on startup. ONCE.
- Every tool call carries the resolved `tenantId` to the backend.
- Backend's existing `TenantContextService.run()` wraps every call. No new code path; the MCP is just a different transport over the same SDK.

This guarantees zero cross-tenant data leakage by construction.

## Packaging + distribution

### npm package

```
packages/mcp/                          # source
  package.json (name: @binexus/mcp)
  src/index.ts (MCP server entry)
  src/tools/
  src/resources/
  tools/<tool>.json (per-tool descriptor)
  resources/<resource>.json
```

Published as `@binexus/mcp` on npm (public). Installed by users via:

```bash
npm install -g @binexus/mcp
```

### User config (Cursor example)

```json
// .cursor/mcp.json (per-machine, gitignored)
{
  "mcpServers": {
    "binexus": {
      "command": "npx",
      "args": ["-y", "@binexus/mcp"],
      "env": {
        "BINEXUS_TOKEN": "bnx_mcp_live_..."
      }
    }
  }
}
```

Same shape for Claude Desktop, Windsurf, Codex, etc.

### Versioning

- Semver.
- Major bumps only when a tool changes its required input shape or removes a tool.
- Tool additions are minor.
- Deprecate tools by leaving them in for 2 major versions, marked `deprecated: true` in the descriptor, with a `replacementTool` hint.

## Rate limiting

- Per-token rate limit, NOT per-tenant. Allows tenants to issue multiple tokens (CI, agent, human) and bill them separately later.
- Default: 60 calls/minute, 5,000/day. Configurable per tier.
- 429 with `Retry-After` header on cap.

## Observability

Every MCP call writes an `AuditEvent`:

- `MCP_TOOL_CALLED` — tool name, token ID, latency, input hash (NOT input body), status code.
- `MCP_RESOURCE_READ` — same for resource fetches.

Audit lets tenants review what their agents did under their account.

## Anti-patterns

- Adding a new MCP tool without a corresponding SDK method. Skip — make the SDK method first, the MCP tool wraps it.
- Accepting `tenantId` as a tool parameter. Cross-tenant breakage waiting to happen.
- Returning Prisma rows directly. Use the same DTOs the SDK returns.
- Catching errors and swallowing them. JSON-RPC errors are how MCP communicates failure — let them propagate cleanly.
- Logging full input bodies (PII risk). Hash the body, log the hash.
- Distributing the MCP via a private CDN. npm is the contract.
- Updating Binexus core domain code from `packages/mcp`. The MCP package depends on `@binexus/sdk`, not on backend internals.

## Pre-PR checklist

- [ ] New tool maps 1:1 to an existing SDK method.
- [ ] Zod schema reused, not duplicated.
- [ ] Tenant-scoped, no `tenantId` parameter.
- [ ] Tool descriptor JSON in `packages/mcp/tools/`.
- [ ] Rate-limit + audit entries.
- [ ] Documentation entry in `docs/mcp/<tool>.md`.
- [ ] Backwards compatibility check on the existing schema — no removed fields without major bump.

## Reference

- Upstream skill: [`skills/skills-mainb/skills/mcp-builder/SKILL.md`](../../../skills/skills-mainb/skills/mcp-builder/SKILL.md)
- [`.cursor/skills/mcp-server-patterns/SKILL.md`](../mcp-server-patterns/SKILL.md) — existing MCP patterns skill
- MCP spec: https://modelcontextprotocol.io/
- [`packages/sdk`](../../../packages/sdk) — the SDK Binexus's MCP wraps
- [`apps/backend/src/common/tenant/tenant-context.service.ts`](../../../apps/backend/src/common/tenant/tenant-context.service.ts) — tenant scoping
- [`docs/architecture/multi-tenant.md`](../../../docs/architecture/multi-tenant.md)

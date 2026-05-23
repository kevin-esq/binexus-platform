# Identity bounded context

Status: **active** (Phase 0).

Domain reference: [`docs/domains/identity.md`](../../../../../docs/domains/identity.md).

Identity owns tenants, branches, users, refresh tokens, JWT auth, and RBAC. It is the only context currently implemented beyond placeholder state.

Current structure:

```txt
identity/
├── identity.module.ts
└── auth/
    ├── auth.controller.ts
    └── auth.service.ts
```

Future command hardening will move auth use cases behind explicit command handlers where it adds clarity.

# Branch wizard UX (architecture)

First-run UX for Branch Server activation and Branch Client pairing. Implementation is out of scope.

Related: [ADR-0019](../adr/0019-branch-server-activation.md), [ADR-0020](../adr/0020-branch-client-pairing.md), [ADR-0021](../adr/0021-lan-discovery.md), [ADR-0022](../adr/0022-branch-installer.md), [ADR-0026](../adr/0026-resumable-bootstrap.md).

## Entry choice

```text
1. Instalar / activar Branch Server   → Installer + Cloud activation + bootstrap
2. Emparejar esta caja (Branch Client) → Discovery + pairing
3. Solo Cloud (navegador)              → Web Admin; exit desktop setup
```

Do not label options as a single "modo local".

## Flow A — Branch Server

```text
Elevate / launch Binexus Branch Installer
→ Installer progress (Postgres, services, firewall)
→ Enter Cloud activation code from Web Admin
→ Persist BranchInstance credentials
→ Resumable bootstrap phases:
   Descargando configuración
   Descargando catálogo publicado
   Aplicando módulos
   Finalizando sucursal
→ Branch Ready
→ Optional: open Tauri as first client on same machine
```

Installer owns provisioning. Tauri shows structured progress only.

## Flow B — Branch Client

```text
Discover candidates (name, InstanceId fragment, address, version, fingerprint)
→ Operator selects when multiple
→ Confirm fingerprint
→ Local pairing approval
→ Store device credential
→ User login
→ Select Terminal
→ Ready for LAN ops
```

Manual `IP:port` always available. mDNS never skips fingerprint confirmation.

## Failure copy (design)

| Case                              | Message intent                                   |
| --------------------------------- | ------------------------------------------------ |
| Internet down during activation   | Activation needs Cloud; retry when online        |
| Internet down after Ready         | Continue selling; sync later                     |
| Branch Server unreachable         | Cannot confirm operations; check Principal / LAN |
| Entitlement blocked mid-bootstrap | Stop; contact admin                              |

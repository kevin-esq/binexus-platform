# Binexus Mobile

Status: **placeholder — not under active development**.

Decision recorded in Phase 0: mobile is **not** a Phase 0 deliverable, not even a scaffold. The bottleneck for Binexus right now is core operational workflows (Orders, Inventory, Sales), not a phone app.

We will revisit this once:

1. Orders, Inventory and one POS flow are stable in the web/desktop app.
2. There is a concrete tenant use case that needs mobile (e.g. driver app for routes).

When that happens, the stack will likely be:

- **Framework**: Expo SDK (latest stable at that time)
- **State / data**: shared via `@binexus/sdk`
- **Auth**: same JWT flow as web/desktop
- **First module**: probably the driver delivery app (routes + proof of delivery)

Until then, do NOT scaffold an Expo project here. An empty placeholder is intentional.

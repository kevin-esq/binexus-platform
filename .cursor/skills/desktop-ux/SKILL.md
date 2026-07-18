---
name: desktop-ux
description: Desktop UX patterns for Tauri apps — windowing, tray, shortcuts, native dialogs, offline indicators, POS/kiosk considerations. Use when designing desktop UI chrome, multi-window flows, tray apps, or operator-facing Branch Client UX.
---

# desktop-ux

## Principles

- Feel native: system dialogs, menus, and shortcuts via Tauri plugins — not reinvented web modals for OS tasks
- One primary window for POS/operator flows; secondary windows only with clear jobs
- Always show connection/sync state for offline-capable apps
- Prefer keyboard-friendly operator flows (POS)

## Always

- Respect platform conventions (close vs quit; tray quit on Windows)
- Confirm destructive actions
- Keep privileged prompts in OS dialogs where appropriate
- Design for intermittent network (queue visible)

## Never

- Trap users without quit path from tray-only apps
- Hide pairing/recovery errors behind generic toasts without codes
- Overwhelm first viewport with dashboard chrome (see frontend design rules for web)

## Multi-window

Separate capabilities per window role. Do not reuse admin permissions on customer-facing windows.

## Binexus operator panel

Desktop shell hosts operator UI; align with existing web operator patterns when embedding views, but hardware/pairing feedback must surface clearly from Rust events.

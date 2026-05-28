---
name: messaging
description: Real-time customer + driver messaging across SMS, push notifications, transactional WhatsApp, and email. Use when wiring delivery notifications to end-customers ("Tu pedido va en camino"), when adding driver-app push (route dispatched, stop assigned), when notifying tenants of platform events (invoice issued, cap reached), when adding two-factor codes, or when picking a SMS / WhatsApp provider (Twilio, MessageBird, Vonage). Pairs with `lifecycle` (sequences) and `react-native` (driver-side push).
---

# messaging (Binexus)

Operational messaging across SMS, push, WhatsApp, and transactional email. Different from [`lifecycle`](../lifecycle/SKILL.md) (which is marketing sequences); this skill is real-time, event-driven, and customer-or-driver-facing.

Adapted from [`skills/marketingskills-main/skills/sms/SKILL.md`](../../../skills/marketingskills-main/skills/sms/SKILL.md) plus operational push-notification patterns common in TMS.

## When to invoke

- A new domain event needs to trigger an outbound message.
- Wiring push notifications in the driver app (when `apps/mobile` exists).
- Adding "Your order is out for delivery" SMS / WhatsApp to the end customer.
- Adding 2FA / verification codes.
- Picking the messaging provider (Twilio vs MessageBird vs Vonage — TBD).
- Reviewing cost per message (LATAM SMS costs vary wildly per country).

## Channels Binexus uses

| Channel             | Used for                                                | Cost order         |
| ------------------- | ------------------------------------------------------- | ------------------ |
| Push (mobile)       | Driver: route dispatched, stop assigned, return-to-base | ≈ free             |
| Push (web)          | Tenant operator: high-priority panel events             | ≈ free             |
| In-app banner       | Tenant operator while in panel                          | ≈ free             |
| Transactional email | Receipts, invoices, password reset, 2FA backup          | ≈ $0.0001/email    |
| SMS                 | End-customer: "Tu pedido va en camino" + tracking link  | $0.005-0.03/SMS MX |
| WhatsApp Business   | End-customer: richer than SMS, lower delivery latency   | $0.01-0.08/msg MX  |
| Voice (IVR)         | Driver dispatch when push fails — fallback, rare        | $0.10/call MX      |

## Driver app push (F4+ when mobile ships)

Map every domain event the driver cares about to a push notification:

| Domain event                  | Driver push                                                              |
| ----------------------------- | ------------------------------------------------------------------------ |
| `DELIVERY_ROUTE_DISPATCHED`   | "Tienes una ruta nueva: {N} paradas, salida {time}" → opens route detail |
| `DELIVERY_ROUTE_STOP_CHANGED` | "Cambio en tu ruta: parada {sequence} actualizada"                       |
| `DELIVERY_ROUTE_CANCELLED`    | "Tu ruta de {time} fue cancelada"                                        |
| `MESSAGE_FROM_DISPATCHER`     | Direct text from dispatcher to driver                                    |
| (optional) `DELIVERY_FAILED`  | When the driver app reported a failed delivery, confirm receipt          |

Deep links: `binexus://route/<routeId>` opens the route screen. Wire in `apps/mobile/app/_layout.tsx`.

`expo-notifications` is the channel. JWT-scoped — never broadcast.

## End-customer SMS / WhatsApp

When F4 adds the "notify customer" flag on a route, send:

| Event                                  | Channel preference | Body (ES MX, ≤ 100 chars)                                                  |
| -------------------------------------- | ------------------ | -------------------------------------------------------------------------- |
| `DELIVERY_ROUTE_DISPATCHED` (per stop) | WhatsApp > SMS     | "Hola {firstName}, tu pedido sale hoy. Sigue la ruta: {trackUrl}"          |
| Stop is 10 min out                     | WhatsApp > SMS     | "Tu repartidor llega en ~10 min."                                          |
| `DELIVERY_CONFIRMED`                   | WhatsApp > SMS     | "Pedido entregado a las {time}. ¿Algún problema? Responde a este mensaje." |
| `DELIVERY_FAILED`                      | WhatsApp > SMS     | "No pudimos entregar tu pedido. Te contactamos en breve."                  |

Rules:

- Customer phone in E.164 format only (`+52...`). Reject anything else.
- Truncate to 100 chars to avoid multi-segment SMS billing.
- The track URL is a per-stop signed short link (`bnx.us/<token>`, valid 24 h).
- Sender ID: alphanumeric where allowed ("Binexus"), per-country sender-ID rules vary. Document per geo.
- Opt-out: every message has a reply-`STOP` mechanism. Honour for 365 days.
- Customer's locale comes from the tenant; never auto-detect.

## Tenant-operator messages

When something happens to the tenant's account that they need to see _now_:

- Cap reached (soft / hard) — in-product banner + email; SMS only if the tenant opted in to "billing critical" channel.
- Invoice issued — email.
- Payment failed — email + in-product modal on next login.
- Security event (new device login) — email always; SMS if tenant enabled "security critical".

Never SMS a tenant for non-critical reasons. They didn't sign up for that.

## 2FA / verification codes

- 6-digit numeric codes.
- TTL 5 min.
- One channel: SMS or WhatsApp (tenant-admin chooses during setup).
- Replays: never reuse a code. New action = new code.
- Rate limit: max 3 codes per phone per hour.
- Provider failover: if SMS fails after 2 attempts, fall back to email.

## Provider selection (decision pending)

| Provider        | Pros                                       | Cons                                             | LATAM coverage |
| --------------- | ------------------------------------------ | ------------------------------------------------ | -------------- |
| Twilio          | Best docs, biggest ecosystem, WhatsApp BSP | Most expensive in LATAM; sender-ID friction      | Excellent      |
| MessageBird     | Strong LATAM presence, competitive pricing | Smaller ecosystem; some MX deliverability issues | Excellent      |
| Vonage          | Good prices                                | Documentation thinner; smaller community         | Good           |
| Direct carriers | Cheapest                                   | Per-country setup; compliance burden             | Variable       |

Decision: start with **Twilio** for the velocity, switch later if cost dominates. Bake the provider abstraction into the code so the switch costs hours, not weeks.

## Outbox + retry (consistent with the domain bus)

Outbound messages MUST go through an outbox + retry pattern, identical to the [`event-system`](../../../docs/architecture/event-system.md) outbox:

1. Domain handler writes a `MessageOutbox` row in the same transaction as the state change.
2. A separate `MessageDispatcher` polls outbox, sends, marks `sentAt` on success.
3. Retries with exponential backoff on transient failures (5xx, network).
4. After 5 failures, move to `dead_letter` and emit `MESSAGE_DELIVERY_FAILED` for ops alerting.
5. Each message records `providerId` for auditability.

NEVER send synchronously from a command handler. The command must succeed even if Twilio is down.

## Privacy + compliance

- Customer phone numbers are PII. Same protections as email — encrypted at rest, never in logs.
- LATAM SMS regulation: respect quiet hours (08:00-21:00 local time of the recipient).
- WhatsApp Business: only Meta-approved templates for non-conversational outbound. The 24-hour customer-initiated window allows free-form replies.
- Audit trail: every send is an `AuditEvent` with provider ID, target type, recipient hash, message slug (NOT body).
- Tenant gets a "send a copy of customer notifications to me" option in F4+.

## Anti-patterns

- Sending SMS from a domain handler synchronously. Outbox or nothing.
- Storing the full SMS body in audit logs (compliance violation if it contains PII).
- "Spamming" delivery updates ("Your order is 8 minutes out", then 6, then 4 — pick one or two checkpoints).
- Sending in MXN-formatted strings to an Argentine customer.
- Reusing the same WhatsApp template across tenants without tenant branding.
- Forgetting to wire `STOP` honoring.

## Pre-PR checklist

- [ ] All sends go through `MessageOutbox`.
- [ ] Recipient phone normalized to E.164.
- [ ] Body ≤ channel limit; templated per locale.
- [ ] Audit logged without the body text.
- [ ] Quiet hours respected.
- [ ] STOP / opt-out tested.
- [ ] If driver-side push: deep link tested on iOS + Android.

## Reference

- [`skills/marketingskills-main/skills/sms/SKILL.md`](../../../skills/marketingskills-main/skills/sms/SKILL.md)
- [`.cursor/skills/react-native/SKILL.md`](../react-native/SKILL.md) — driver-side push wiring
- [`.cursor/skills/lifecycle/SKILL.md`](../lifecycle/SKILL.md) — marketing sequences (different surface)
- [`docs/architecture/event-system.md`](../../../docs/architecture/event-system.md) — outbox pattern reused here
- [`.cursor/rules/common-security.md`](../../rules/common-security.md)
- [`.cursor/rules/typescript-security.md`](../../rules/typescript-security.md)

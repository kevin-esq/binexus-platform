---
name: react-native
description: Performance and platform guidance for the future Binexus mobile app(s) — primarily the driver/courier app, optionally a tenant-admin companion. Use when scaffolding `apps/mobile` (Expo SDK 53+), building any RN screen, optimizing list / scroll performance, integrating native modules (camera, geolocation, push, MinIO uploads), or sharing types/SDK between the monorepo and the mobile app. The mobile apps do NOT exist yet — this skill is the bootstrap reference + day-to-day rules once they ship.
---

# react-native (Binexus)

Future-state guidance for the Binexus mobile surface. There is no `apps/mobile/` yet. When the **driver app** ships (Phase F4+ — the natural home for `DELIVERY_CONFIRMED` capture with photo + signature + GPS), this skill is the contract.

Adapted from Vercel's `vercel-react-native-skills`. Full rule list: [`skills/agent-skills-main/skills/react-native-skills/SKILL.md`](../../../skills/agent-skills-main/skills/react-native-skills/SKILL.md).

## Why the driver app exists

The current web flow `apps/web/src/app/logistics/page.tsx` collects proof via `window.prompt()` — usable for dispatchers, unusable for drivers in the field. The driver app exists to:

1. Receive dispatched routes (consume the `DELIVERY_ROUTE_DISPATCHED` event via the SDK or polling).
2. Show stops in sequence with map + ETA.
3. Capture proof of delivery at each stop: photo, signature, recipient name, GPS, optional notes.
4. Call `client.confirmDelivery(stopId, { proof: {...} })`.
5. Upload photo / signature to MinIO via a presigned URL the backend issues.
6. Work offline: cache the route, queue confirmations, sync when online.

Once the driver app is live, F4 Logistics' Proof Base finally gets the input device it was designed for. The data model in `DeliveryProof` already supports it (see `apps/backend/prisma/schema.prisma`).

## Bootstrap (when starting `apps/mobile/`)

### 1. Scaffold

```bash
pnpm dlx create-expo-app apps/mobile --template default
```

Expo SDK 53+ for New Architecture support, React Native 0.76+. Use TypeScript template.

### 2. Wire to the monorepo

Add to root `pnpm-workspace.yaml`:

```yaml
packages:
  - 'apps/*'
  - 'packages/*'
```

Already there — confirm `apps/mobile` matches `apps/*`.

In `apps/mobile/package.json`:

```json
"dependencies": {
  "@binexus/sdk": "workspace:*",
  "@binexus/types": "workspace:*",
  "@binexus/events": "workspace:*"
}
```

The SDK was designed to be runtime-agnostic. If it imports any Node-only API, surface that to the backend team and add a `react-native` field in the SDK's `package.json` to remap. Do NOT vendor a forked SDK.

### 3. Metro config

Metro needs to resolve workspace packages. Add `metro.config.js`:

```js
const { getDefaultConfig } = require('expo/metro-config');
const path = require('path');
const workspaceRoot = path.resolve(__dirname, '../..');
const projectRoot = __dirname;

const config = getDefaultConfig(projectRoot);
config.watchFolders = [workspaceRoot];
config.resolver.nodeModulesPaths = [
  path.resolve(projectRoot, 'node_modules'),
  path.resolve(workspaceRoot, 'node_modules'),
];
config.resolver.disableHierarchicalLookup = true;
module.exports = config;
```

This is the standard pnpm + Expo monorepo wiring.

### 4. Native modules used day one

- `expo-camera` — proof photos.
- `expo-location` — GPS at confirm time.
- `react-native-signature-canvas` or equivalent — signature capture.
- `expo-notifications` — push when a route is dispatched.
- `@react-native-async-storage/async-storage` — offline queue persistence.
- `react-native-mmkv` for the offline queue when async-storage is too slow.

Do NOT pull in `react-native-image-picker` and `expo-image-picker` at the same time. Pick the Expo flavour to stay inside the managed workflow.

## Rules of the road

### 1. List performance (CRITICAL)

The route stops list and the order list need this from day one — drivers will scroll long days.

- Use `FlashList` (`@shopify/flash-list`) for any list >20 items. Never `FlatList` for the stops list.
- Memoize list item components with `React.memo`.
- Extract callbacks outside `renderItem`. Inline arrows are a re-render trap.
- No inline `style={{}}` objects on list items — define the style outside the component.
- Use `getItemType` for heterogeneous lists (stop vs section header).

### 2. Animation (HIGH)

- Use `react-native-reanimated` for any animation that interacts with gestures, scroll, or layout.
- Animate ONLY `transform` and `opacity` on the worklet thread. `width`, `height`, `top`, `left` are forbidden for animation.
- Use `useDerivedValue` for computed animations. Never `useState` for animated values.
- Prefer `Pressable` + `useSharedValue` for press feedback. Avoid `TouchableOpacity` for new code.

### 3. Navigation (HIGH)

- `expo-router` (file-based) — matches the rest of the monorepo's mental model.
- Group: `(driver)`, `(auth)`, `(modal)` for layout segments.
- Deep links: support `binexus://route/<routeId>` so push notifications open the right stop.

### 4. Offline-first

The driver app MUST work in airplane mode. Network is gravy.

- Persist active route + stops in MMKV on dispatch event.
- Confirm action writes to a local queue (MMKV list).
- Background sync flushes the queue when network returns. Each `confirmDelivery` call is idempotent on the backend (see `ConfirmDeliveryHandler` — already idempotent), so retries are safe.
- Surface a "pendientes de sincronizar" badge if the queue has items.

### 5. State

- Local screen state: `useState` / `useReducer`.
- Cross-screen state: Zustand store. NOT Redux. Already the project default for any future global state.
- Server state: `@tanstack/react-query` with the SDK as the fetcher. Persisted query cache to AsyncStorage / MMKV.

### 6. Images

- Use `expo-image` (`<Image>` from `expo-image`), not React Native's built-in `Image`.
- Always set `contentFit` and `cachePolicy="memory-disk"`.
- Compress proof photos to ≤1MB before upload. The backend MinIO endpoint should reject larger.

### 7. Multi-tenant in the driver app

- JWT lives in `expo-secure-store`, NEVER AsyncStorage.
- On login, store `tenantId` from the JWT claim into MMKV (non-secure cache for fast reads).
- Every SDK call carries the JWT automatically. No "tenant switcher" in the driver app — drivers belong to one tenant.

### 8. Monorepo discipline

- Shared types come from `@binexus/types`. Never duplicate `OrderState`, `DeliveryRouteStopStatus`, etc.
- Event names come from `@binexus/events`. The driver app SUBSCRIBES (poll/SSE/WS) but does not PRODUCE events directly — it sends commands via the SDK.
- Shared UI primitives that are RN-compatible go into `packages/ui-mobile` (new package, not `packages/ui` which is web).

## Anti-patterns

- A single `packages/ui` that imports `react-dom`. Keep web and mobile UI separate.
- A custom HTTP client in the driver app. Use `@binexus/sdk`.
- Polling the backend every 5 seconds instead of consuming push / SSE.
- Storing the JWT in MMKV / AsyncStorage. Use SecureStore.
- Adding `react-native-firebase` for push when `expo-notifications` already covers Binexus's needs.

## Pre-PR checklist

- [ ] Lists use FlashList.
- [ ] Animations on transform/opacity only.
- [ ] SDK from `@binexus/sdk`, not vendored.
- [ ] JWT in SecureStore, never elsewhere.
- [ ] Offline queue tested in airplane mode.
- [ ] Proof photos ≤1MB.
- [ ] iOS + Android both run locally (`pnpm --filter @binexus/mobile ios`, `... android`).

## Reference

- Full Vercel rule list: [`skills/agent-skills-main/skills/react-native-skills/SKILL.md`](../../../skills/agent-skills-main/skills/react-native-skills/SKILL.md)
- [`apps/backend/prisma/schema.prisma`](../../../apps/backend/prisma/schema.prisma) — `DeliveryProof` model fields the app needs to populate.
- [`apps/backend/src/contexts/logistics/application/commands/confirm-delivery.command.ts`](../../../apps/backend/src/contexts/logistics/application/commands/confirm-delivery.command.ts) — proof persistence path the app calls into.
- [`docs/architecture/event-system.md`](../../../docs/architecture/event-system.md) — `DELIVERY_ROUTE_DISPATCHED` is the dispatch trigger.

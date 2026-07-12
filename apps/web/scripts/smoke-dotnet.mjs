#!/usr/bin/env node
/**
 * Gate 5 full smoke against .NET API (SMOKE_REQUIRE=1 for CI / gate5 stack).
 *
 * Covers: auth refresh/logout, inventory adjust, orders→warehouse picking,
 * logistics proof upload, optional cash liquidation, POS session sale.
 *
 * Credentials default to IdentitySeedDefaults (acme / admin@acme.test / ChangeMe123!).
 * Override: SMOKE_API_URL, SMOKE_TENANT, SMOKE_EMAIL, SMOKE_PASSWORD.
 * Exit 0 with SKIP when API is down unless SMOKE_REQUIRE=1.
 */

const apiUrl = (process.env.SMOKE_API_URL ?? process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5102').replace(
  /\/+$/,
  '',
);
const tenantSlug = process.env.SMOKE_TENANT ?? 'acme';
const email = process.env.SMOKE_EMAIL ?? 'admin@acme.test';
const password = process.env.SMOKE_PASSWORD ?? 'ChangeMe123!';
const requireApi = process.env.SMOKE_REQUIRE === '1';
const outboxTimeoutMs = Number(process.env.SMOKE_OUTBOX_TIMEOUT_MS ?? 90_000);
const pollMs = Number(process.env.SMOKE_POLL_MS ?? 1500);

/** @type {Map<string, string>} */
const idempotencyKeys = new Map();

function keyFor(operation) {
  if (!idempotencyKeys.has(operation)) {
    idempotencyKeys.set(operation, crypto.randomUUID());
  }
  return idempotencyKeys.get(operation);
}

async function sleep(ms) {
  await new Promise((r) => setTimeout(r, ms));
}

/**
 * @param {string} method
 * @param {string} path
 * @param {{ body?: unknown, token?: string, idempotency?: string | false, expectStatuses?: number[] }} [opts]
 */
async function api(method, path, opts = {}) {
  const headers = {};
  const methodUpper = method.toUpperCase();
  const isMutating = methodUpper !== 'GET' && methodUpper !== 'HEAD';

  if (opts.token) headers.authorization = `Bearer ${opts.token}`;
  if (isMutating && opts.idempotency !== false) {
    headers['Idempotency-Key'] =
      typeof opts.idempotency === 'string' ? opts.idempotency : crypto.randomUUID();
  }
  /** @type {RequestInit} */
  const init = { method, headers };
  if (opts.body !== undefined) {
    headers['content-type'] = 'application/json';
    init.body = JSON.stringify(opts.body);
  }

  const response = await fetch(`${apiUrl}${path}`, init);
  const text = await response.text();
  let json = null;
  if (text) {
    try {
      json = JSON.parse(text);
    } catch {
      json = text;
    }
  }

  const expected = opts.expectStatuses ?? [200, 201, 204];
  if (!expected.includes(response.status)) {
    const detail = typeof json === 'object' && json ? JSON.stringify(json) : text;
    throw new Error(`${method} ${path} → ${response.status} ${detail}`);
  }
  return { status: response.status, json, headers: response.headers };
}

function fail(message) {
  console.error(`SMOKE FAIL: ${message}`);
  process.exit(1);
}

async function waitForPicking(token, orderId, deadline) {
  while (Date.now() < deadline) {
    const order = await api('GET', `/orders/${orderId}`, { token });
    const state = order.json?.state;
    if (state === 'PICKING' || state === 'READY_FOR_DELIVERY_ROUTE') {
      return state;
    }
    const picking = await api('GET', '/warehouse/picking-tasks?status=PENDING&limit=50', { token });
    const match = (picking.json?.items ?? []).find((t) => t.orderId === orderId);
    if (match) return 'PICKING';
    await sleep(pollMs);
  }
  fail(`outbox/picking timeout for order ${orderId} after ${outboxTimeoutMs}ms (is Binexus.Workers running?)`);
}

async function main() {
  let health;
  try {
    health = await fetch(`${apiUrl}/health`, { signal: AbortSignal.timeout(3000) });
  } catch (err) {
    const reason = err instanceof Error ? err.message : String(err);
    if (requireApi) {
      fail(`API unreachable at ${apiUrl} (${reason})`);
    }
    console.log(`SMOKE SKIP: API not running at ${apiUrl} (${reason})`);
    process.exit(0);
  }

  if (!health.ok) fail(`GET /health → ${health.status}`);
  console.log(`OK health ${apiUrl}`);

  // --- Auth ---
  const login = await api('POST', '/auth/login', {
    body: { tenantSlug, email, password },
    idempotency: keyFor('login'),
  });
  let accessToken = login.json.accessToken;
  let refreshToken = login.json.refreshToken;
  if (!accessToken || !refreshToken) fail('login missing tokens');
  console.log('OK login');

  const me1 = await api('GET', '/auth/me', { token: accessToken });
  const branchId = me1.json?.branch?.id;
  if (!branchId) fail('me missing branch.id');
  console.log('OK me');

  const refreshed = await api('POST', '/auth/refresh', {
    body: { refreshToken },
    idempotency: keyFor('refresh'),
  });
  accessToken = refreshed.json.accessToken;
  refreshToken = refreshed.json.refreshToken;
  if (!accessToken || !refreshToken) fail('refresh missing tokens');
  console.log('OK refresh');

  await api('GET', '/auth/me', { token: accessToken });
  console.log('OK me (new access)');

  const refreshAfterLogout = refreshToken;
  await api('POST', '/auth/logout', {
    body: { refreshToken },
    token: accessToken,
    idempotency: keyFor('logout'),
    expectStatuses: [204],
  });
  console.log('OK logout');

  await api('POST', '/auth/refresh', {
    body: { refreshToken: refreshAfterLogout },
    idempotency: keyFor('refresh-rejected'),
    expectStatuses: [401],
  });
  console.log('OK old refresh rejected');

  // Re-login for domain flows
  const login2 = await api('POST', '/auth/login', {
    body: { tenantSlug, email, password },
    idempotency: keyFor('login-2'),
  });
  accessToken = login2.json.accessToken;
  refreshToken = login2.json.refreshToken;

  const suffix = crypto.randomUUID().slice(0, 8);
  const productId = `smoke-sku-${suffix}`;

  // --- Inventory ---
  const adjust = await api('POST', '/inventory/stock/adjust', {
    token: accessToken,
    idempotency: keyFor(`adjust-${productId}`),
    body: { branchId, productId, delta: 20, reason: 'gate5 smoke seed' },
  });
  if (adjust.json?.stockItem?.onHand !== 20) fail(`adjust onHand expected 20 got ${adjust.json?.stockItem?.onHand}`);
  console.log('OK adjust stock');

  const stockList = await api('GET', `/inventory/stock?branchId=${branchId}&productId=${productId}`, {
    token: accessToken,
  });
  const stockItem = (stockList.json?.items ?? []).find((x) => x.productId === productId);
  if (!stockItem || stockItem.onHand !== 20) fail('list stock qty mismatch');
  console.log('OK list stock');

  // --- Orders + Warehouse ---
  const created = await api('POST', '/orders', {
    token: accessToken,
    idempotency: keyFor(`order-${suffix}`),
    body: {
      customerId: `smoke-customer-${suffix}`,
      currency: 'USD',
      paymentMethod: 'CASH',
      lines: [
        {
          productId,
          productName: 'Smoke Widget',
          quantity: 2,
          unitPriceCents: 1500,
        },
      ],
    },
    expectStatuses: [200, 201],
  });
  const orderId = created.json?.id;
  if (!orderId) fail('create order missing id');
  console.log('OK create order');

  const approved = await api('POST', `/orders/${orderId}/approve`, {
    token: accessToken,
    idempotency: keyFor(`approve-${orderId}`),
  });
  if (approved.json?.state !== 'APPROVED') fail(`approve state ${approved.json?.state}`);
  console.log('OK approve order');

  const pickingState = await waitForPicking(accessToken, orderId, Date.now() + outboxTimeoutMs);
  console.log(`OK outbox → ${pickingState}`);

  const pending = await api('GET', '/warehouse/picking-tasks?status=PENDING&limit=50', {
    token: accessToken,
  });
  const task = (pending.json?.items ?? []).find((t) => t.orderId === orderId);
  if (!task) fail('no PENDING picking task for order');
  console.log('OK list picking PENDING');

  await api('POST', `/warehouse/picking-tasks/${task.id}/complete`, {
    token: accessToken,
    idempotency: keyFor(`complete-${task.id}`),
  });
  const afterPick = await api('GET', `/orders/${orderId}`, { token: accessToken });
  if (afterPick.json?.state !== 'READY_FOR_DELIVERY_ROUTE') {
    fail(`order after pick expected READY_FOR_DELIVERY_ROUTE got ${afterPick.json?.state}`);
  }
  console.log('OK picking complete → READY_FOR_DELIVERY_ROUTE');

  // Wait for logistics candidate from PICKING_COMPLETED / ORDER_READY outbox
  const candidateDeadline = Date.now() + outboxTimeoutMs;
  let candidate = null;
  while (Date.now() < candidateDeadline) {
    const candidates = await api('GET', '/logistics/delivery-route-candidates?status=READY&limit=50', {
      token: accessToken,
    });
    candidate = (candidates.json?.items ?? []).find((c) => c.orderId === orderId);
    if (candidate) break;
    await sleep(pollMs);
  }
  if (!candidate) fail('no READY delivery candidate (workers processing ORDER_READY?)');
  console.log('OK logistics candidates READY');

  const route = await api('POST', '/logistics/delivery-routes', {
    token: accessToken,
    idempotency: keyFor(`route-${suffix}`),
    body: { branchId },
  });
  const routeId = route.json?.id;
  if (!routeId) fail('create route missing id');
  console.log('OK create route');

  await api('POST', `/logistics/delivery-routes/${routeId}/assign-orders`, {
    token: accessToken,
    idempotency: keyFor(`assign-${routeId}`),
    body: { orderIds: [orderId] },
  });
  console.log('OK assign');

  const me = await api('GET', '/auth/me', { token: accessToken });
  const driverUserId = me.json?.user?.id;
  await api('POST', `/logistics/delivery-routes/${routeId}/dispatch`, {
    token: accessToken,
    idempotency: keyFor(`dispatch-${routeId}`),
    body: { driverUserId },
  });
  console.log('OK dispatch');

  const stops = await api('GET', `/logistics/delivery-routes/${routeId}/stops`, { token: accessToken });
  const stop = (stops.json?.items ?? [])[0];
  if (!stop) fail('no stops after dispatch');

  const proofBytes = new Uint8Array(128).fill(7);
  const proof = await api('POST', `/logistics/delivery-route-stops/${stop.id}/proof-uploads`, {
    token: accessToken,
    idempotency: keyFor(`proof-${stop.id}`),
    body: { kind: 'PHOTO', contentType: 'image/jpeg', sizeBytes: proofBytes.length },
  });
  const uploadUrl = proof.json?.uploadUrl;
  const objectKey = proof.json?.objectKey;
  if (!uploadUrl || !objectKey) fail('proof upload missing uploadUrl/objectKey');

  // Never log uploadUrl (presigned query contains signature).
  let uploadHost = 'unknown';
  try {
    uploadHost = new URL(uploadUrl).host;
  } catch {
    fail('proof uploadUrl is not a valid URL');
  }

  const putRes = await fetch(uploadUrl, {
    method: 'PUT',
    headers: { 'content-type': 'image/jpeg' },
    body: proofBytes,
  });
  if (putRes.status !== 204 && putRes.status !== 200) {
    fail(`proof PUT → ${putRes.status} (host=${uploadHost}; body omitted)`);
  }
  console.log(`OK proof-upload PUT (host=${uploadHost})`);
  if (process.env.Logistics__Storage__Provider === 'MinIO' || process.env.SMOKE_EXPECT_MINIO === '1') {
    const publicEp =
      process.env.Logistics__Storage__PublicEndpoint ||
      process.env.SMOKE_MINIO_PUBLIC ||
      '';
    let expectedHost = '';
    try {
      if (publicEp) expectedHost = new URL(publicEp).host;
    } catch {
      /* ignore */
    }
    const looksLikeMinio =
      /minio/i.test(uploadHost) ||
      /:(9000|9100|9200)\b/.test(uploadHost) ||
      (expectedHost !== '' && uploadHost === expectedHost);
    if (!looksLikeMinio) {
      fail(
        `expected MinIO public host${expectedHost ? ` (${expectedHost})` : ''}, got host=${uploadHost}`,
      );
    }
  }

  await api('POST', `/logistics/delivery-route-stops/${stop.id}/confirm-delivery`, {
    token: accessToken,
    idempotency: keyFor(`confirm-${stop.id}`),
    body: { proof: { photoObjectKey: objectKey, signatureObjectKey: null } },
  });
  const deliveredOrder = await api('GET', `/orders/${orderId}`, { token: accessToken });
  if (deliveredOrder.json?.state !== 'DELIVERED' && deliveredOrder.json?.state !== 'SETTLED') {
    fail(`confirm expected DELIVERED got ${deliveredOrder.json?.state}`);
  }
  console.log('OK confirm delivery');

  // --- Liquidation (optional; Dev seed enables LIQUIDATION) ---
  try {
    const routes = await api('GET', `/logistics/delivery-routes?status=COMPLETED&limit=20`, {
      token: accessToken,
    });
    const completed = (routes.json?.items ?? []).find((r) => r.id === routeId);
    if (completed) {
      await api('POST', `/logistics/delivery-routes/${routeId}/liquidate`, {
        token: accessToken,
        idempotency: keyFor(`liquidate-${routeId}`),
        body: { declaredCents: 3000 },
      });
      console.log('OK liquidate');
    } else {
      console.log('SKIP liquidate (route not COMPLETED yet — COD may settle async)');
    }
  } catch (err) {
    const msg = err instanceof Error ? err.message : String(err);
    if (msg.includes('FEATURE_DISABLED') || msg.includes('403')) {
      console.log(`SKIP liquidate (${msg})`);
    } else {
      throw err;
    }
  }

  // --- POS ---
  const terminalId = `smoke-pos-${suffix}`;
  const opened = await api('POST', '/sales/sessions/open', {
    token: accessToken,
    idempotency: keyFor(`pos-open-${suffix}`),
    body: { terminalId, openingFloatCents: 0, currency: 'USD' },
  });
  const sessionId = opened.json?.session?.id;
  if (!sessionId) fail('POS open missing session');
  console.log('OK POS open');

  const posProduct = `smoke-pos-sku-${suffix}`;
  await api('POST', '/inventory/stock/adjust', {
    token: accessToken,
    idempotency: keyFor(`pos-adjust-${posProduct}`),
    body: { branchId, productId: posProduct, delta: 5, reason: 'pos smoke' },
  });

  const sale = await api('POST', `/sales/sessions/${sessionId}/sales`, {
    token: accessToken,
    idempotency: keyFor(`pos-sale-${suffix}`),
    body: {
      currency: 'USD',
      lines: [{ productId: posProduct, productName: 'POS Smoke', quantity: 1, unitPriceCents: 1000 }],
      payments: [
        { method: 'CASH', amountCents: 400 },
        { method: 'CARD', amountCents: 600 },
      ],
    },
  });
  if (!sale.json?.ticket?.id) fail('create sale missing ticket');
  console.log('OK POS sale split CASH+CARD');

  const sessionBeforeClose = await api('GET', `/sales/sessions/${sessionId}`, { token: accessToken });
  // Opening float 0 + CASH payment 400 = expected cash for arqueo.
  const declared = 400;
  await api('POST', `/sales/sessions/${sessionId}/close`, {
    token: accessToken,
    idempotency: keyFor(`pos-close-${suffix}`),
    body: { declaredClosingCents: declared },
  });
  const sessionAfterClose = await api('GET', `/sales/sessions/${sessionId}`, { token: accessToken });
  if (sessionAfterClose.json?.status !== 'CLOSED') {
    fail(`POS close expected CLOSED, got ${sessionAfterClose.json?.status}`);
  }
  const expected = sessionAfterClose.json?.expectedClosingCents;
  if (expected !== declared) {
    fail(`POS arqueo expected ${declared}, got ${expected}`);
  }
  console.log(
    `OK POS close (declared=${declared}, expected=${expected}, before=${sessionBeforeClose.json?.status}, after=${sessionAfterClose.json?.status})`,
  );

  console.log('SMOKE PASS');
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});

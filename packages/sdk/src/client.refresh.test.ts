import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { createBinexusClient, type TokenProvider } from './client';
import { BinexusApiError } from './errors';

function memoryTokens(initial?: { access?: string; refresh?: string }): TokenProvider & {
  access: string | null;
  refresh: string | null;
  cleared: number;
} {
  const state = {
    access: initial?.access ?? (null as string | null),
    refresh: initial?.refresh ?? (null as string | null),
    cleared: 0,
    getAccessToken() {
      return state.access;
    },
    getRefreshToken() {
      return state.refresh;
    },
    setTokens(accessToken: string, refreshToken: string) {
      state.access = accessToken;
      state.refresh = refreshToken;
    },
    clear() {
      state.access = null;
      state.refresh = null;
      state.cleared += 1;
    },
  };
  return state;
}

describe('BinexusClient single-flight refresh', () => {
  it('refreshes once for concurrent 401s and retries with new access token', async () => {
    const tokens = memoryTokens({ access: 'old-access', refresh: 'refresh-1' });
    let refreshCalls = 0;
    let meCalls = 0;
    const seenAuth: string[] = [];
    const seenKeys: string[] = [];

    const fetchMock: typeof fetch = async (input, init) => {
      const url = String(input);
      const auth = (init?.headers as Record<string, string> | undefined)?.authorization;
      const idem = (init?.headers as Record<string, string> | undefined)?.['Idempotency-Key'];

      if (url.endsWith('/auth/refresh')) {
        refreshCalls += 1;
        await new Promise((r) => setTimeout(r, 20));
        return new Response(
          JSON.stringify({ accessToken: 'new-access', refreshToken: 'refresh-2' }),
          { status: 200, headers: { 'content-type': 'application/json' } },
        );
      }

      if (url.includes('/orders')) {
        meCalls += 1;
        seenAuth.push(auth ?? '');
        if (idem) seenKeys.push(idem);
        if (auth === 'Bearer old-access') {
          return new Response(JSON.stringify({ detail: 'expired', code: 'UNAUTHORIZED' }), {
            status: 401,
            headers: { 'content-type': 'application/json' },
          });
        }
        return new Response(JSON.stringify({ items: [], nextCursor: null }), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        });
      }

      throw new Error(`unexpected url ${url}`);
    };

    const client = createBinexusClient({
      baseUrl: 'http://api.test',
      tokenProvider: tokens,
      fetch: fetchMock,
    });

    const [a, b] = await Promise.all([client.listOrders(), client.listOrders()]);
    assert.deepEqual(a.items, []);
    assert.deepEqual(b.items, []);
    assert.equal(refreshCalls, 1);
    assert.equal(meCalls, 4); // 2 × (fail + retry)
    assert.equal(tokens.access, 'new-access');
    assert.ok(
      seenAuth.every((h, i) => (i < 2 ? h === 'Bearer old-access' : h === 'Bearer new-access')),
    );
  });

  it('reuses the same Idempotency-Key across the 401 retry', async () => {
    const tokens = memoryTokens({ access: 'old-access', refresh: 'refresh-1' });
    const keys: string[] = [];

    const fetchMock: typeof fetch = async (input, init) => {
      const url = String(input);
      const headers = init?.headers as Record<string, string> | undefined;
      if (url.endsWith('/auth/refresh')) {
        return new Response(
          JSON.stringify({ accessToken: 'new-access', refreshToken: 'refresh-2' }),
          { status: 200, headers: { 'content-type': 'application/json' } },
        );
      }
      if (url.endsWith('/orders') && init?.method === 'POST') {
        keys.push(headers?.['Idempotency-Key'] ?? '');
        if (headers?.authorization === 'Bearer old-access') {
          return new Response(JSON.stringify({ code: 'UNAUTHORIZED' }), {
            status: 401,
            headers: { 'content-type': 'application/json' },
          });
        }
        return new Response(JSON.stringify({ id: 'order-1' }), {
          status: 201,
          headers: { 'content-type': 'application/json' },
        });
      }
      throw new Error(`unexpected ${url}`);
    };

    const client = createBinexusClient({
      baseUrl: 'http://api.test',
      tokenProvider: tokens,
      fetch: fetchMock,
    });

    await client.createOrder({
      customerId: 'c1',
      currency: 'USD',
      paymentMethod: 'CASH',
      lines: [{ productId: 'p1', productName: 'P', quantity: 1, unitPriceCents: 100 }],
    });

    assert.equal(keys.length, 2);
    assert.equal(keys[0], keys[1]);
    assert.ok(keys[0].length > 0);
  });

  it('does not refresh on 403 FEATURE_DISABLED', async () => {
    const tokens = memoryTokens({ access: 'access', refresh: 'refresh-1' });
    let refreshCalls = 0;

    const fetchMock: typeof fetch = async (input) => {
      const url = String(input);
      if (url.endsWith('/auth/refresh')) {
        refreshCalls += 1;
        return new Response('{}', { status: 500 });
      }
      return new Response(JSON.stringify({ detail: 'Feature off', code: 'FEATURE_DISABLED' }), {
        status: 403,
        headers: { 'content-type': 'application/json' },
      });
    };

    const client = createBinexusClient({
      baseUrl: 'http://api.test',
      tokenProvider: tokens,
      fetch: fetchMock,
    });

    await assert.rejects(
      () => client.getCurrentSalesSession({ terminalId: 't1' }),
      (err: unknown) => err instanceof BinexusApiError && err.code === 'FEATURE_DISABLED',
    );
    assert.equal(refreshCalls, 0);
    assert.equal(tokens.access, 'access');
  });

  it('clears tokens when refresh fails', async () => {
    const tokens = memoryTokens({ access: 'old-access', refresh: 'refresh-1' });

    const fetchMock: typeof fetch = async (input) => {
      const url = String(input);
      if (url.endsWith('/auth/refresh')) {
        return new Response(JSON.stringify({ detail: 'reuse', code: 'REFRESH_TOKEN_REUSED' }), {
          status: 401,
          headers: { 'content-type': 'application/json' },
        });
      }
      return new Response(JSON.stringify({ code: 'UNAUTHORIZED' }), {
        status: 401,
        headers: { 'content-type': 'application/json' },
      });
    };

    const client = createBinexusClient({
      baseUrl: 'http://api.test',
      tokenProvider: tokens,
      fetch: fetchMock,
    });

    await assert.rejects(() => client.me());
    assert.equal(tokens.access, null);
    assert.equal(tokens.refresh, null);
    assert.ok(tokens.cleared >= 1);
  });

  it('does not attempt refresh after logout cleared tokens', async () => {
    const tokens = memoryTokens({ access: 'access', refresh: 'refresh-1' });
    let refreshCalls = 0;

    const fetchMock: typeof fetch = async (input, init) => {
      const url = String(input);
      if (url.endsWith('/auth/logout')) {
        return new Response(null, { status: 204 });
      }
      if (url.endsWith('/auth/refresh')) {
        refreshCalls += 1;
        return new Response('{}', { status: 500 });
      }
      if (url.endsWith('/auth/me')) {
        return new Response(JSON.stringify({ code: 'UNAUTHORIZED' }), {
          status: 401,
          headers: { 'content-type': 'application/json' },
        });
      }
      throw new Error(`unexpected ${url} ${init?.method}`);
    };

    const client = createBinexusClient({
      baseUrl: 'http://api.test',
      tokenProvider: tokens,
      fetch: fetchMock,
    });

    await client.logout();
    assert.equal(tokens.access, null);
    await assert.rejects(() => client.me());
    assert.equal(refreshCalls, 0);
  });
});

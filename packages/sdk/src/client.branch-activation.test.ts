import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { createBinexusClient, type TokenProvider } from './client';
import { BinexusApiError } from './errors';

function memoryTokens(initial?: { access?: string; refresh?: string }): TokenProvider & {
  access: string | null;
  refresh: string | null;
} {
  const state = {
    access: initial?.access ?? (null as string | null),
    refresh: initial?.refresh ?? (null as string | null),
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
    },
  };
  return state;
}

describe('BinexusClient.createBranchActivation', () => {
  it('POSTs /cloud/branch-activations with branchId, auth, and Idempotency-Key', async () => {
    const tokens = memoryTokens({ access: 'access-token' });
    const seen: {
      method?: string;
      url?: string;
      auth?: string;
      idempotencyKey?: string;
      body?: unknown;
    } = {};

    const fetchMock: typeof fetch = async (input, init) => {
      seen.method = init?.method;
      seen.url = String(input);
      const headers = init?.headers as Record<string, string> | undefined;
      seen.auth = headers?.authorization;
      seen.idempotencyKey = headers?.['Idempotency-Key'];
      seen.body = init?.body ? JSON.parse(String(init.body)) : undefined;

      return new Response(
        JSON.stringify({
          activationId: 'act-1',
          activationCode: 'CODE-1',
          expiresAtUtc: '2026-07-16T20:00:00Z',
        }),
        { status: 201, headers: { 'content-type': 'application/json' } },
      );
    };

    const client = createBinexusClient({
      baseUrl: 'http://api.test',
      tokenProvider: tokens,
      fetch: fetchMock,
    });

    const result = await client.createBranchActivation('branch-42');

    assert.equal(seen.method, 'POST');
    assert.equal(seen.url, 'http://api.test/cloud/branch-activations');
    assert.deepEqual(seen.body, { branchId: 'branch-42' });
    assert.equal(seen.auth, 'Bearer access-token');
    assert.ok(typeof seen.idempotencyKey === 'string' && seen.idempotencyKey.length > 0);
    assert.deepEqual(result, {
      activationId: 'act-1',
      activationCode: 'CODE-1',
      expiresAtUtc: '2026-07-16T20:00:00Z',
    });
  });

  it('reuses the same Idempotency-Key across the 401 refresh retry', async () => {
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

      if (url.endsWith('/cloud/branch-activations') && init?.method === 'POST') {
        keys.push(headers?.['Idempotency-Key'] ?? '');
        if (headers?.authorization === 'Bearer old-access') {
          return new Response(JSON.stringify({ code: 'UNAUTHORIZED' }), {
            status: 401,
            headers: { 'content-type': 'application/json' },
          });
        }
        return new Response(
          JSON.stringify({
            activationId: 'act-2',
            activationCode: 'CODE-2',
            expiresAtUtc: '2026-07-16T21:00:00Z',
          }),
          { status: 201, headers: { 'content-type': 'application/json' } },
        );
      }

      throw new Error(`unexpected ${url}`);
    };

    const client = createBinexusClient({
      baseUrl: 'http://api.test',
      tokenProvider: tokens,
      fetch: fetchMock,
    });

    const result = await client.createBranchActivation('branch-99');
    assert.equal(result.activationId, 'act-2');
    assert.equal(keys.length, 2);
    assert.equal(keys[0], keys[1]);
    assert.ok(keys[0].length > 0);
    assert.equal(tokens.access, 'new-access');
  });

  it('maps Problem Details failures to BinexusApiError', async () => {
    const tokens = memoryTokens({ access: 'access-token' });

    const fetchMock: typeof fetch = async () =>
      new Response(
        JSON.stringify({
          type: 'https://httpstatuses.com/409',
          title: 'Conflict',
          status: 409,
          detail: 'Branch already has an open activation.',
          code: 'ACTIVATION_CONFLICT',
        }),
        { status: 409, headers: { 'content-type': 'application/problem+json' } },
      );

    const client = createBinexusClient({
      baseUrl: 'http://api.test',
      tokenProvider: tokens,
      fetch: fetchMock,
    });

    await assert.rejects(
      () => client.createBranchActivation('branch-1'),
      (err: unknown) =>
        err instanceof BinexusApiError &&
        err.status === 409 &&
        err.code === 'ACTIVATION_CONFLICT' &&
        err.message === 'Branch already has an open activation.',
    );
  });

  it('exposes no public wrappers for machine or Branch-local activation steps', () => {
    const client = createBinexusClient({ baseUrl: 'http://api.test' });
    const record = client as unknown as Record<string, unknown>;

    for (const name of [
      'createBranchActivationChallenge',
      'exchangeBranchActivation',
      'resumeBranchActivation',
      'confirmBranchActivation',
      'activateBranchInstance',
      'challengeBranchActivation',
      'exchangeActivation',
      'resumeActivation',
      'confirmActivation',
    ]) {
      assert.equal(typeof record[name], 'undefined', `unexpected public method: ${name}`);
    }

    assert.equal(typeof client.createBranchActivation, 'function');
  });
});

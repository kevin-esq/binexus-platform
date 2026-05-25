import type {
  ApproveOrderResult,
  AuthSession,
  CancelOrderResult,
  ListOrdersQuery,
  ListOrdersResult,
  OrderDetail,
  OrderId,
} from '@binexus/types';

import { BinexusApiError } from './errors';

export interface TokenProvider {
  getAccessToken(): string | null | Promise<string | null>;
  setTokens?(accessToken: string, refreshToken: string): void | Promise<void>;
  clear?(): void | Promise<void>;
}

export interface BinexusClientOptions {
  baseUrl: string;
  tokenProvider?: TokenProvider;
  fetch?: typeof fetch;
}

export interface LoginInput {
  email: string;
  password: string;
  tenantSlug: string;
}

export interface LoginResult {
  accessToken: string;
  refreshToken: string;
}

export interface CancelOrderInput {
  reason?: string;
}

const SLASH_CHAR_CODE = 47;

// Linear, regex-free trailing-slash stripping. Avoids the ReDoS surface (see
// CodeQL js/polynomial-redos) that a greedy anchored pattern like `/\/+$/`
// would expose when `baseUrl` is library input.
function stripTrailingSlashes(value: string): string {
  let end = value.length;
  while (end > 0 && value.charCodeAt(end - 1) === SLASH_CHAR_CODE) {
    end--;
  }
  return end === value.length ? value : value.slice(0, end);
}

export class BinexusClient {
  private readonly baseUrl: string;
  private readonly tokenProvider: TokenProvider | undefined;
  private readonly fetchImpl: typeof fetch;

  constructor(options: BinexusClientOptions) {
    this.baseUrl = stripTrailingSlashes(options.baseUrl);
    this.tokenProvider = options.tokenProvider;
    this.fetchImpl = options.fetch ?? fetch.bind(globalThis);
  }

  async login(input: LoginInput): Promise<LoginResult> {
    const result = await this.request<LoginResult>('POST', '/auth/login', {
      body: input,
      auth: false,
    });
    await this.tokenProvider?.setTokens?.(result.accessToken, result.refreshToken);
    return result;
  }

  async logout(): Promise<void> {
    await this.request<void>('POST', '/auth/logout', { auth: true }).catch(() => undefined);
    await this.tokenProvider?.clear?.();
  }

  async me(): Promise<AuthSession> {
    return this.request<AuthSession>('GET', '/auth/me', { auth: true });
  }

  async listOrders(query: ListOrdersQuery = {}): Promise<ListOrdersResult> {
    const params = new URLSearchParams();
    if (query.limit !== undefined) params.set('limit', String(query.limit));
    if (query.cursor) params.set('cursor', query.cursor);
    const qs = params.toString();
    const path = qs ? `/orders?${qs}` : '/orders';
    return this.request<ListOrdersResult>('GET', path, { auth: true });
  }

  async getOrder(id: OrderId | string): Promise<OrderDetail> {
    return this.request<OrderDetail>('GET', `/orders/${encodeURIComponent(id)}`, { auth: true });
  }

  async approveOrder(id: OrderId | string): Promise<ApproveOrderResult> {
    return this.request<ApproveOrderResult>('POST', `/orders/${encodeURIComponent(id)}/approve`, {
      auth: true,
    });
  }

  async cancelOrder(
    id: OrderId | string,
    input: CancelOrderInput = {},
  ): Promise<CancelOrderResult> {
    return this.request<CancelOrderResult>('POST', `/orders/${encodeURIComponent(id)}/cancel`, {
      body: input,
      auth: true,
    });
  }

  private async request<T>(
    method: string,
    path: string,
    opts: { body?: unknown; auth?: boolean } = {},
  ): Promise<T> {
    const headers: Record<string, string> = { 'content-type': 'application/json' };

    if (opts.auth && this.tokenProvider) {
      const token = await this.tokenProvider.getAccessToken();
      if (token) headers.authorization = `Bearer ${token}`;
    }

    const response = await this.fetchImpl(`${this.baseUrl}${path}`, {
      method,
      headers,
      body: opts.body !== undefined ? JSON.stringify(opts.body) : undefined,
    });

    if (!response.ok) {
      const errPayload = await response.json().catch(() => ({}) as Record<string, unknown>);
      const message =
        (typeof errPayload.message === 'string' ? errPayload.message : undefined) ??
        `Request failed: ${response.status}`;
      const code = typeof errPayload.code === 'string' ? errPayload.code : undefined;
      throw new BinexusApiError(message, response.status, code, errPayload);
    }

    if (response.status === 204) return undefined as T;
    return (await response.json()) as T;
  }
}

export function createBinexusClient(options: BinexusClientOptions): BinexusClient {
  return new BinexusClient(options);
}

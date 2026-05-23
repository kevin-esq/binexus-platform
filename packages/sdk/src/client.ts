import type { AuthSession } from '@binexus/types';

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

export class BinexusClient {
  private readonly baseUrl: string;
  private readonly tokenProvider: TokenProvider | undefined;
  private readonly fetchImpl: typeof fetch;

  constructor(options: BinexusClientOptions) {
    this.baseUrl = options.baseUrl.replace(/\/+$/, '');
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

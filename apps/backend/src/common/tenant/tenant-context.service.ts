import { AsyncLocalStorage } from 'node:async_hooks';

import { Injectable } from '@nestjs/common';

export interface TenantContext {
  tenantId: string;
  userId: string;
  role: string;
  branchId: string | null;
  requestId: string;
}

@Injectable()
export class TenantContextService {
  private readonly storage = new AsyncLocalStorage<TenantContext>();

  run<T>(ctx: TenantContext, fn: () => T): T {
    return this.storage.run(ctx, fn);
  }

  current(): TenantContext {
    const ctx = this.storage.getStore();
    if (!ctx) {
      throw new Error(
        'TenantContext not initialized. Did the request bypass TenantContextMiddleware?',
      );
    }
    return ctx;
  }

  tenantIdOrNull(): string | null {
    return this.storage.getStore()?.tenantId ?? null;
  }
}

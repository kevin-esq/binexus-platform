import { Injectable, type OnModuleDestroy, type OnModuleInit } from '@nestjs/common';
import { Prisma, PrismaClient } from '@prisma/client';

import { type TenantContextService } from '../tenant/tenant-context.service';

// Models that store data per-tenant and must be auto-filtered.
// Other foundation models (Tenant itself, RefreshToken) are explicitly excluded.
const TENANT_SCOPED_MODELS = new Set<string>([
  'User',
  'Branch',
  'OutboxEvent',
  'TenantFeature',
  'Order',
  'OrderLine',
  'OrderTransition',
]);

// Operations that should have tenantId enforced.
const SCOPED_READ_OPS = new Set<string>([
  'findFirst',
  'findFirstOrThrow',
  'findMany',
  'findUnique',
  'findUniqueOrThrow',
  'count',
  'aggregate',
  'groupBy',
]);
const SCOPED_WRITE_OPS = new Set<string>([
  'update',
  'updateMany',
  'delete',
  'deleteMany',
  'upsert',
]);

@Injectable()
export class PrismaService extends PrismaClient implements OnModuleInit, OnModuleDestroy {
  constructor(private readonly tenantContext: TenantContextService) {
    super({
      log: [
        { level: 'warn', emit: 'event' },
        { level: 'error', emit: 'event' },
      ],
    });
  }

  async onModuleInit(): Promise<void> {
    await this.$connect();
  }

  async onModuleDestroy(): Promise<void> {
    await this.$disconnect();
  }

  // Returns a tenant-scoped Prisma client. Use this for ANY business code that reads or
  // writes tenant-scoped models. Plain `this.<model>` bypasses tenant isolation and is
  // reserved for explicitly cross-tenant operations (super-admin tools, the auth
  // module looking up users by tenant slug, etc).
  forTenant() {
    return this.$extends({
      query: {
        $allModels: {
          $allOperations: ({ model, operation, args, query }) => {
            if (!model || !TENANT_SCOPED_MODELS.has(model)) return query(args);
            const tenantId = this.tenantContext.tenantIdOrNull();
            if (!tenantId) return query(args);

            if (SCOPED_READ_OPS.has(operation)) {
              const argObj = args as { where?: Record<string, unknown> };
              argObj.where = { ...(argObj.where ?? {}), tenantId };
              return query(args);
            }

            if (SCOPED_WRITE_OPS.has(operation)) {
              const argObj = args as { where?: Record<string, unknown> };
              argObj.where = { ...(argObj.where ?? {}), tenantId };
              return query(args);
            }

            if (operation === 'create') {
              const argObj = args as { data?: Record<string, unknown> };
              argObj.data = { ...(argObj.data ?? {}), tenantId };
              return query(args);
            }

            if (operation === 'createMany') {
              const argObj = args as { data?: Record<string, unknown> | Record<string, unknown>[] };
              const data = argObj.data;
              if (Array.isArray(data)) {
                argObj.data = data.map((row) => ({ ...row, tenantId }));
              } else if (data) {
                argObj.data = { ...data, tenantId };
              }
              return query(args);
            }

            return query(args);
          },
        },
      },
    });
  }

  // Re-export Prisma namespace for places that need types.
  static readonly Prisma = Prisma;
}

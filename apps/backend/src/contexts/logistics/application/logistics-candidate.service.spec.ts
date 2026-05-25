import { DomainEventName } from '@binexus/events';
import { DeliveryRouteCandidateStatus } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { type PrismaService } from '../../../common/prisma/prisma.service';
import { type TenantContextService } from '../../../common/tenant/tenant-context.service';

import { LogisticsCandidateService } from './logistics-candidate.service';

describe('LogisticsCandidateService', () => {
  it('creates delivery route candidate from ORDER_READY_FOR_DELIVERY_ROUTE', async () => {
    const create = vi.fn().mockResolvedValue({ id: 'cand-1' });
    const findFirst = vi.fn().mockResolvedValue(null);

    const db = { deliveryRouteCandidate: { findFirst, create, update: vi.fn() } };
    const prisma = { forTenant: vi.fn().mockReturnValue(db) } as unknown as PrismaService;
    const tenantContext = {
      run: vi.fn((_ctx: unknown, fn: () => Promise<void>) => fn()),
    } as unknown as TenantContextService;

    const service = new LogisticsCandidateService(prisma, tenantContext);

    await service.handleOrderReadyForDeliveryRoute({
      id: 'evt-1',
      name: DomainEventName.ORDER_READY_FOR_DELIVERY_ROUTE,
      tenantId: 'tenant-1',
      occurredAt: '2026-05-25T00:00:00.000Z',
      version: 1,
      payload: { orderId: 'order-1', branchId: 'branch-1', readyBy: 'user-1' },
    });

    expect(create).toHaveBeenCalledWith({
      data: expect.objectContaining({
        orderId: 'order-1',
        branchId: 'branch-1',
        status: DeliveryRouteCandidateStatus.READY,
        createdFromEventId: 'evt-1',
      }),
    });
  });

  it('no-ops when same event id replayed', async () => {
    const create = vi.fn();
    const findFirst = vi.fn().mockResolvedValue({
      id: 'cand-1',
      status: DeliveryRouteCandidateStatus.READY,
      createdFromEventId: 'evt-1',
    });

    const db = { deliveryRouteCandidate: { findFirst, create, update: vi.fn() } };
    const prisma = { forTenant: vi.fn().mockReturnValue(db) } as unknown as PrismaService;
    const tenantContext = {
      run: vi.fn((_ctx: unknown, fn: () => Promise<void>) => fn()),
    } as unknown as TenantContextService;

    const service = new LogisticsCandidateService(prisma, tenantContext);

    await service.handleOrderReadyForDeliveryRoute({
      id: 'evt-1',
      name: DomainEventName.ORDER_READY_FOR_DELIVERY_ROUTE,
      tenantId: 'tenant-1',
      occurredAt: '2026-05-25T00:00:00.000Z',
      version: 1,
      payload: { orderId: 'order-1', branchId: 'branch-1', readyBy: 'user-1' },
    });

    expect(create).not.toHaveBeenCalled();
  });
});

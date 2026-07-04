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

  it('resets ASSIGNED candidate to READY and clears deliveryRouteId on requeue', async () => {
    const update = vi.fn().mockResolvedValue({ id: 'cand-1' });
    const findFirst = vi.fn().mockResolvedValue({
      id: 'cand-1',
      status: DeliveryRouteCandidateStatus.ASSIGNED,
      deliveryRouteId: 'route-old-1',
      createdFromEventId: 'evt-original',
    });

    const db = { deliveryRouteCandidate: { findFirst, create: vi.fn(), update } };
    const prisma = { forTenant: vi.fn().mockReturnValue(db) } as unknown as PrismaService;
    const tenantContext = {
      run: vi.fn((_ctx: unknown, fn: () => Promise<void>) => fn()),
    } as unknown as TenantContextService;

    const service = new LogisticsCandidateService(prisma, tenantContext);

    await service.handleOrderReadyForDeliveryRoute({
      id: 'evt-requeue-2',
      name: DomainEventName.ORDER_READY_FOR_DELIVERY_ROUTE,
      tenantId: 'tenant-1',
      occurredAt: '2026-05-25T18:00:00.000Z',
      version: 1,
      payload: { orderId: 'order-1', branchId: 'branch-1', readyBy: 'dispatcher-1' },
    });

    expect(update).toHaveBeenCalledWith({
      where: { id: 'cand-1' },
      data: {
        branchId: 'branch-1',
        status: DeliveryRouteCandidateStatus.READY,
        deliveryRouteId: null,
        createdFromEventId: 'evt-requeue-2',
      },
    });
  });

  it('skips requeue when candidate is CANCELLED', async () => {
    const update = vi.fn();
    const findFirst = vi.fn().mockResolvedValue({
      id: 'cand-1',
      status: DeliveryRouteCandidateStatus.CANCELLED,
      createdFromEventId: 'evt-old',
    });

    const db = { deliveryRouteCandidate: { findFirst, create: vi.fn(), update } };
    const prisma = { forTenant: vi.fn().mockReturnValue(db) } as unknown as PrismaService;
    const tenantContext = {
      run: vi.fn((_ctx: unknown, fn: () => Promise<void>) => fn()),
    } as unknown as TenantContextService;

    const service = new LogisticsCandidateService(prisma, tenantContext);

    await service.handleOrderReadyForDeliveryRoute({
      id: 'evt-requeue-3',
      name: DomainEventName.ORDER_READY_FOR_DELIVERY_ROUTE,
      tenantId: 'tenant-1',
      occurredAt: '2026-05-25T18:00:00.000Z',
      version: 1,
      payload: { orderId: 'order-1', branchId: 'branch-1', readyBy: 'dispatcher-1' },
    });

    expect(update).not.toHaveBeenCalled();
  });

  it('marks candidate CANCELLED on ORDER_CANCELLED', async () => {
    const update = vi.fn().mockResolvedValue({ id: 'cand-1' });
    const findFirst = vi.fn().mockResolvedValue({
      id: 'cand-1',
      status: DeliveryRouteCandidateStatus.ASSIGNED,
      deliveryRouteId: 'route-1',
    });

    const db = { deliveryRouteCandidate: { findFirst, update } };
    const prisma = { forTenant: vi.fn().mockReturnValue(db) } as unknown as PrismaService;
    const tenantContext = {
      run: vi.fn((_ctx: unknown, fn: () => Promise<void>) => fn()),
    } as unknown as TenantContextService;

    const service = new LogisticsCandidateService(prisma, tenantContext);

    await service.handleOrderCancelled({
      id: 'evt-cancel-1',
      name: DomainEventName.ORDER_CANCELLED,
      tenantId: 'tenant-1',
      occurredAt: '2026-05-25T19:00:00.000Z',
      version: 1,
      payload: { orderId: 'order-1', cancelledBy: 'user-1', reason: 'Customer gave up' },
    });

    expect(update).toHaveBeenCalledWith({
      where: { id: 'cand-1' },
      data: {
        status: DeliveryRouteCandidateStatus.CANCELLED,
        deliveryRouteId: null,
      },
    });
  });

  it('no-ops ORDER_CANCELLED when candidate already CANCELLED', async () => {
    const update = vi.fn();
    const findFirst = vi.fn().mockResolvedValue({
      id: 'cand-1',
      status: DeliveryRouteCandidateStatus.CANCELLED,
    });

    const db = { deliveryRouteCandidate: { findFirst, update } };
    const prisma = { forTenant: vi.fn().mockReturnValue(db) } as unknown as PrismaService;
    const tenantContext = {
      run: vi.fn((_ctx: unknown, fn: () => Promise<void>) => fn()),
    } as unknown as TenantContextService;

    const service = new LogisticsCandidateService(prisma, tenantContext);

    await service.handleOrderCancelled({
      id: 'evt-cancel-2',
      name: DomainEventName.ORDER_CANCELLED,
      tenantId: 'tenant-1',
      occurredAt: '2026-05-25T19:00:00.000Z',
      version: 1,
      payload: { orderId: 'order-1', cancelledBy: 'user-1' },
    });

    expect(update).not.toHaveBeenCalled();
  });
});

import { DomainEventName, type DomainEvent } from '@binexus/events';
import { PickingTaskStatus } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { type PrismaService } from '../../../common/prisma/prisma.service';
import { type SystemUserService } from '../../../common/tenant/system-user.service';
import { type TenantContextService } from '../../../common/tenant/tenant-context.service';

import { WarehousePickingService } from './warehouse-picking.service';

describe('WarehousePickingService', () => {
  it('creates picking task and lines idempotently', async () => {
    const tx = {
      pickingTask: {
        create: vi.fn().mockResolvedValue({ id: 'task-1' }),
      },
      pickingLine: {
        create: vi.fn(),
      },
    };

    const prisma = {
      forTenant: vi.fn().mockReturnValue({
        pickingTask: {
          findFirst: vi.fn().mockResolvedValueOnce(null).mockResolvedValueOnce({ id: 'task-1' }),
        },
        order: {
          findFirst: vi.fn().mockResolvedValue({
            id: 'order-1',
            branchId: 'branch-1',
            lines: [
              { id: 'line-1', productId: 'sku-1', quantity: 2 },
              { id: 'line-2', productId: 'sku-2', quantity: 1 },
            ],
          }),
        },
      }),
      $transaction: vi.fn((callback: (client: typeof tx) => Promise<void>) => callback(tx)),
    };

    const tenantContext = {
      run: vi.fn((_ctx: unknown, fn: () => Promise<void>) => fn()),
    };
    const systemUser = { resolveForTenant: vi.fn().mockResolvedValue('system-user-1') };

    const service = new WarehousePickingService(
      prisma as unknown as PrismaService,
      tenantContext as unknown as TenantContextService,
      systemUser as unknown as SystemUserService,
    );

    const event: DomainEvent<typeof DomainEventName.ORDER_PICKING_STARTED> = {
      id: 'event-pick-1',
      name: DomainEventName.ORDER_PICKING_STARTED,
      tenantId: 'tenant-1',
      occurredAt: '2026-05-25T00:00:00.000Z',
      version: 1,
      payload: { orderId: 'order-1', branchId: 'branch-1', lineCount: 2 },
    };

    await service.handleOrderPickingStarted(event);
    await service.handleOrderPickingStarted(event);

    expect(tx.pickingTask.create).toHaveBeenCalledTimes(1);
    expect(tx.pickingTask.create).toHaveBeenCalledWith({
      data: expect.objectContaining({
        orderId: 'order-1',
        status: PickingTaskStatus.PENDING,
        createdFromEventId: 'event-pick-1',
      }),
    });
    expect(tx.pickingLine.create).toHaveBeenCalledTimes(2);
  });
});

import { DeliveryRouteCandidateStatus } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { type PrismaService } from '../../../common/prisma/prisma.service';

import { LogisticsReadService } from './logistics-read.service';

describe('LogisticsReadService', () => {
  it('lists READY delivery route candidates', async () => {
    const rows = [
      {
        id: 'cand-1',
        tenantId: 'tenant-1',
        orderId: 'order-1',
        branchId: 'branch-1',
        status: DeliveryRouteCandidateStatus.READY,
        deliveryRouteId: null,
        createdFromEventId: 'evt-1',
        createdAt: new Date('2026-05-25T10:00:00Z'),
        updatedAt: new Date('2026-05-25T10:00:00Z'),
      },
    ];

    const db = {
      deliveryRouteCandidate: { findMany: vi.fn().mockResolvedValue(rows) },
    };
    const prisma = { forTenant: vi.fn().mockReturnValue(db) } as unknown as PrismaService;

    const service = new LogisticsReadService(prisma);
    const result = await service.listDeliveryRouteCandidates({ status: 'READY', limit: 10 });

    expect(result.items).toHaveLength(1);
    expect(result.items[0]?.orderId).toBe('order-1');
    expect(result.nextCursor).toBeNull();
  });
});

import { BadRequestException } from '@nestjs/common';
import { describe, expect, it, vi } from 'vitest';

import { type PrismaService } from '../../../common/prisma/prisma.service';

import { InventoryReadService } from './inventory-read.service';

const createdAt = new Date('2026-05-25T10:00:00.000Z');
const updatedAt = new Date('2026-05-25T10:00:00.000Z');

function createFixture(): {
  service: InventoryReadService;
  db: {
    stockItem: {
      findMany: ReturnType<typeof vi.fn>;
      findFirst: ReturnType<typeof vi.fn>;
    };
  };
} {
  const db = {
    stockItem: {
      findMany: vi.fn(),
      findFirst: vi.fn(),
    },
  };

  const prisma = {
    forTenant: vi.fn().mockReturnValue(db),
  } as unknown as PrismaService;

  return { service: new InventoryReadService(prisma), db };
}

describe('InventoryReadService', () => {
  it('lists stock items and computes available', async () => {
    const { service, db } = createFixture();
    db.stockItem.findMany.mockResolvedValue([
      {
        id: 'stock-1',
        tenantId: 'tenant-1',
        branchId: 'branch-1',
        productId: 'product-demo-1',
        onHand: 100,
        reserved: 30,
        createdAt,
        updatedAt,
      },
    ]);

    const result = await service.listStockItems();

    expect(result.items).toHaveLength(1);
    expect(result.items[0]).toEqual({
      id: 'stock-1',
      branchId: 'branch-1',
      productId: 'product-demo-1',
      onHand: 100,
      reserved: 30,
      available: 70,
      createdAt: createdAt.toISOString(),
      updatedAt: updatedAt.toISOString(),
    });
    expect(result.nextCursor).toBeNull();
  });

  it('applies branchId and productId filters', async () => {
    const { service, db } = createFixture();
    db.stockItem.findMany.mockResolvedValue([]);

    await service.listStockItems({
      branchId: 'branch-1',
      productId: 'product-demo-2',
    });

    expect(db.stockItem.findMany).toHaveBeenCalledWith(
      expect.objectContaining({
        where: { branchId: 'branch-1', productId: 'product-demo-2' },
      }),
    );
  });

  it('caps limit at 100', async () => {
    const { service, db } = createFixture();
    db.stockItem.findMany.mockResolvedValue([]);

    await service.listStockItems({ limit: 500 });

    expect(db.stockItem.findMany).toHaveBeenCalledWith(expect.objectContaining({ take: 101 }));
  });

  it('returns nextCursor when more rows exist', async () => {
    const { service, db } = createFixture();
    db.stockItem.findMany.mockResolvedValue([
      {
        id: 'stock-2',
        tenantId: 'tenant-1',
        branchId: 'branch-1',
        productId: 'product-demo-2',
        onHand: 50,
        reserved: 0,
        createdAt,
        updatedAt,
      },
      {
        id: 'stock-1',
        tenantId: 'tenant-1',
        branchId: 'branch-1',
        productId: 'product-demo-1',
        onHand: 100,
        reserved: 0,
        createdAt: new Date('2026-05-25T09:00:00.000Z'),
        updatedAt,
      },
    ]);

    const result = await service.listStockItems({ limit: 1 });

    expect(result.items).toHaveLength(1);
    expect(result.items[0]?.id).toBe('stock-2');
    expect(result.nextCursor).toBe('stock-2');
  });

  it('rejects an invalid cursor', async () => {
    const { service, db } = createFixture();
    db.stockItem.findFirst.mockResolvedValue(null);

    await expect(service.listStockItems({ cursor: 'missing' })).rejects.toBeInstanceOf(
      BadRequestException,
    );
  });
});

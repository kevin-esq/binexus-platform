import { type BranchId, type UserId } from '@binexus/types';
import { BadRequestException } from '@nestjs/common';
import { StockTransferStatus } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { validateAppCommand } from '../../../../common/commands/command-validation';
import { type PrismaService } from '../../../../common/prisma/prisma.service';
import { type TenantContextService } from '../../../../common/tenant/tenant-context.service';

import {
  CreateStockTransferCommand,
  CreateStockTransferHandler,
} from './create-stock-transfer.command';

const tenantContext = {
  tenantId: 'tenant-1',
  userId: 'user-1',
  role: 'ADMIN',
  branchId: 'branch-src',
  requestId: 'request-1',
};

function createHandlerFixture(source: { id: string; onHand: number; reserved: number } | null): {
  handler: CreateStockTransferHandler;
  tx: {
    stockItem: { findFirst: ReturnType<typeof vi.fn>; update: ReturnType<typeof vi.fn> };
    stockTransfer: { create: ReturnType<typeof vi.fn> };
  };
} {
  const tx = {
    stockItem: {
      findFirst: vi.fn().mockResolvedValue(source),
      update: vi.fn(),
    },
    stockTransfer: {
      create: vi.fn().mockResolvedValue({
        id: 'transfer-1',
        tenantId: 'tenant-1',
        sourceBranchId: 'branch-src',
        destinationBranchId: 'branch-dst',
        productId: 'sku-1',
        quantity: 10,
        status: StockTransferStatus.PENDING,
        reason: 'restock dst',
        createdByUserId: 'user-1',
        receivedByUserId: null,
        cancelledByUserId: null,
        createdAt: new Date(),
        updatedAt: new Date(),
        receivedAt: null,
        cancelledAt: null,
      }),
    },
  };

  const prisma = {
    $transaction: vi.fn((callback: (client: typeof tx) => Promise<unknown>) => callback(tx)),
  } as unknown as PrismaService;

  const tenant = {
    current: vi.fn().mockReturnValue(tenantContext),
  } as unknown as TenantContextService;

  return { handler: new CreateStockTransferHandler(prisma, tenant), tx };
}

describe('CreateStockTransferCommand', () => {
  it('validates branches, product, and quantity', async () => {
    const branch = 'branch-a' as BranchId;
    const branchB = 'branch-b' as BranchId;

    await expect(
      validateAppCommand(
        new CreateStockTransferCommand(branch, branch, 'sku-1', 5, 'user-1' as UserId),
      ),
    ).rejects.toBeInstanceOf(BadRequestException);

    await expect(
      validateAppCommand(
        new CreateStockTransferCommand(branch, branchB, '', 5, 'user-1' as UserId),
      ),
    ).rejects.toBeInstanceOf(BadRequestException);

    await expect(
      validateAppCommand(
        new CreateStockTransferCommand(branch, branchB, 'sku-1', 0, 'user-1' as UserId),
      ),
    ).rejects.toBeInstanceOf(BadRequestException);
  });
});

describe('CreateStockTransferHandler', () => {
  it('reserves source stock and creates pending transfer', async () => {
    const { handler, tx } = createHandlerFixture({
      id: 'stock-src',
      onHand: 100,
      reserved: 20,
    });

    const result = await handler.execute(
      new CreateStockTransferCommand(
        'branch-src' as BranchId,
        'branch-dst' as BranchId,
        'sku-1',
        10,
        'user-1' as UserId,
        'restock dst',
      ),
    );

    expect(tx.stockItem.update).toHaveBeenCalledWith({
      where: { id: 'stock-src' },
      data: { reserved: { increment: 10 } },
    });
    expect(tx.stockTransfer.create).toHaveBeenCalledWith({
      data: expect.objectContaining({
        status: StockTransferStatus.PENDING,
        quantity: 10,
      }),
    });
    expect(result.transfer.status).toBe('PENDING');
  });

  it('rejects when source available is insufficient', async () => {
    const { handler } = createHandlerFixture({
      id: 'stock-src',
      onHand: 100,
      reserved: 95,
    });

    await expect(
      handler.execute(
        new CreateStockTransferCommand(
          'branch-src' as BranchId,
          'branch-dst' as BranchId,
          'sku-1',
          10,
          'user-1' as UserId,
        ),
      ),
    ).rejects.toBeInstanceOf(BadRequestException);
  });

  it('rejects when source stock item is missing', async () => {
    const { handler } = createHandlerFixture(null);

    await expect(
      handler.execute(
        new CreateStockTransferCommand(
          'branch-src' as BranchId,
          'branch-dst' as BranchId,
          'sku-1',
          5,
          'user-1' as UserId,
        ),
      ),
    ).rejects.toBeInstanceOf(BadRequestException);
  });
});

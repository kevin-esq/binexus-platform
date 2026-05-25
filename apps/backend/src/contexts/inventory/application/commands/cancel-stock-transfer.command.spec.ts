import { type UserId } from '@binexus/types';
import { BadRequestException } from '@nestjs/common';
import { StockTransferStatus } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { type PrismaService } from '../../../../common/prisma/prisma.service';
import { type TenantContextService } from '../../../../common/tenant/tenant-context.service';

import {
  CancelStockTransferCommand,
  CancelStockTransferHandler,
} from './cancel-stock-transfer.command';

const tenantContext = {
  tenantId: 'tenant-1',
  userId: 'user-1',
  role: 'ADMIN',
  branchId: 'branch-src',
  requestId: 'request-1',
};

const pendingTransfer = {
  id: 'transfer-1',
  tenantId: 'tenant-1',
  sourceBranchId: 'branch-src',
  destinationBranchId: 'branch-dst',
  productId: 'sku-1',
  quantity: 10,
  status: StockTransferStatus.PENDING,
  reason: null,
  createdByUserId: 'user-1',
  receivedByUserId: null,
  cancelledByUserId: null,
  createdAt: new Date(),
  updatedAt: new Date(),
  receivedAt: null,
  cancelledAt: null,
};

function createHandlerFixture(transferStatus: StockTransferStatus = StockTransferStatus.PENDING): {
  handler: CancelStockTransferHandler;
  tx: {
    stockTransfer: {
      findFirst: ReturnType<typeof vi.fn>;
      update: ReturnType<typeof vi.fn>;
    };
    stockItem: { findFirst: ReturnType<typeof vi.fn>; update: ReturnType<typeof vi.fn> };
  };
} {
  const transfer =
    transferStatus === StockTransferStatus.PENDING
      ? pendingTransfer
      : { ...pendingTransfer, status: transferStatus };

  const tx = {
    stockTransfer: {
      findFirst: vi.fn().mockResolvedValue(transfer),
      update: vi.fn().mockResolvedValue({
        ...transfer,
        status: StockTransferStatus.CANCELLED,
        cancelledByUserId: 'user-1',
        cancelledAt: new Date(),
      }),
    },
    stockItem: {
      findFirst: vi.fn().mockResolvedValue({ id: 'stock-src', onHand: 100, reserved: 10 }),
      update: vi.fn(),
    },
  };

  const prisma = {
    $transaction: vi.fn((callback: (client: typeof tx) => Promise<unknown>) => callback(tx)),
  } as unknown as PrismaService;

  const tenant = {
    current: vi.fn().mockReturnValue(tenantContext),
  } as unknown as TenantContextService;

  return { handler: new CancelStockTransferHandler(prisma, tenant), tx };
}

describe('CancelStockTransferHandler', () => {
  it('releases source reserved and marks transfer cancelled', async () => {
    const { handler, tx } = createHandlerFixture();

    const result = await handler.execute(
      new CancelStockTransferCommand('transfer-1', 'user-1' as UserId),
    );

    expect(tx.stockItem.update).toHaveBeenCalledWith({
      where: { id: 'stock-src' },
      data: { reserved: { decrement: 10 } },
    });
    expect(result.transfer.status).toBe('CANCELLED');
  });

  it('rejects non-pending transfer', async () => {
    const { handler } = createHandlerFixture(StockTransferStatus.CANCELLED);

    await expect(
      handler.execute(new CancelStockTransferCommand('transfer-1', 'user-1' as UserId)),
    ).rejects.toBeInstanceOf(BadRequestException);
  });
});

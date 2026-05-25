import { type UserId } from '@binexus/types';
import { BadRequestException } from '@nestjs/common';
import { StockMovementType, StockTransferStatus } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { type PrismaService } from '../../../../common/prisma/prisma.service';
import { type TenantContextService } from '../../../../common/tenant/tenant-context.service';

import {
  ReceiveStockTransferCommand,
  ReceiveStockTransferHandler,
} from './receive-stock-transfer.command';

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
  handler: ReceiveStockTransferHandler;
  tx: {
    stockTransfer: {
      findFirst: ReturnType<typeof vi.fn>;
      update: ReturnType<typeof vi.fn>;
    };
    stockItem: {
      findFirst: ReturnType<typeof vi.fn>;
      update: ReturnType<typeof vi.fn>;
      create: ReturnType<typeof vi.fn>;
    };
    stockMovement: { create: ReturnType<typeof vi.fn> };
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
        status: StockTransferStatus.RECEIVED,
        receivedByUserId: 'user-1',
        receivedAt: new Date(),
      }),
    },
    stockItem: {
      findFirst: vi
        .fn()
        .mockResolvedValueOnce({ id: 'stock-src', onHand: 100, reserved: 10 })
        .mockResolvedValueOnce(null),
      update: vi.fn(),
      create: vi.fn(),
    },
    stockMovement: {
      create: vi
        .fn()
        .mockResolvedValueOnce({ id: 'mov-out' })
        .mockResolvedValueOnce({ id: 'mov-in' }),
    },
  };

  const prisma = {
    $transaction: vi.fn((callback: (client: typeof tx) => Promise<unknown>) => callback(tx)),
  } as unknown as PrismaService;

  const tenant = {
    current: vi.fn().mockReturnValue(tenantContext),
  } as unknown as TenantContextService;

  return { handler: new ReceiveStockTransferHandler(prisma, tenant), tx };
}

describe('ReceiveStockTransferHandler', () => {
  it('moves stock atomically and records transfer movements', async () => {
    const { handler, tx } = createHandlerFixture();

    const result = await handler.execute(
      new ReceiveStockTransferCommand('transfer-1', 'user-1' as UserId, {
        commandId: 'cmd-recv-1',
        correlationId: 'corr-1',
      }),
    );

    expect(tx.stockItem.update).toHaveBeenCalledWith({
      where: { id: 'stock-src' },
      data: {
        onHand: { decrement: 10 },
        reserved: { decrement: 10 },
      },
    });
    expect(tx.stockItem.create).toHaveBeenCalledWith({
      data: expect.objectContaining({
        branchId: 'branch-dst',
        onHand: 10,
      }),
    });
    expect(tx.stockMovement.create).toHaveBeenNthCalledWith(1, {
      data: expect.objectContaining({
        type: StockMovementType.TRANSFER_OUT,
        quantity: -10,
        correlationId: 'corr-1',
        causationId: 'cmd-recv-1',
      }),
    });
    expect(tx.stockMovement.create).toHaveBeenNthCalledWith(2, {
      data: expect.objectContaining({
        type: StockMovementType.TRANSFER_IN,
        quantity: 10,
      }),
    });
    expect(result.transfer.status).toBe('RECEIVED');
    expect(result.sourceMovementId).toBe('mov-out');
    expect(result.destinationMovementId).toBe('mov-in');
  });

  it('rejects non-pending transfer', async () => {
    const { handler } = createHandlerFixture(StockTransferStatus.RECEIVED);

    await expect(
      handler.execute(new ReceiveStockTransferCommand('transfer-1', 'user-1' as UserId)),
    ).rejects.toBeInstanceOf(BadRequestException);
  });
});

import { type AdjustStockResult } from '@binexus/types';
import { type BranchId, type UserId } from '@binexus/types';
import { BadRequestException } from '@nestjs/common';
import { StockMovementType } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { validateAppCommand } from '../../../../common/commands/command-validation';
import { type PrismaService } from '../../../../common/prisma/prisma.service';
import { type TenantContextService } from '../../../../common/tenant/tenant-context.service';

import { AdjustStockCommand, AdjustStockHandler } from './adjust-stock.command';

const tenantContext = {
  tenantId: 'tenant-1',
  userId: 'user-1',
  role: 'ADMIN',
  branchId: 'branch-1',
  requestId: 'request-1',
};

function createHandlerFixture(
  existing: {
    id: string;
    onHand: number;
    reserved: number;
  } | null,
): {
  handler: AdjustStockHandler;
  tx: {
    stockItem: {
      findFirst: ReturnType<typeof vi.fn>;
      create: ReturnType<typeof vi.fn>;
      update: ReturnType<typeof vi.fn>;
    };
    stockMovement: { create: ReturnType<typeof vi.fn> };
  };
} {
  const tx = {
    stockItem: {
      findFirst: vi.fn().mockResolvedValue(existing),
      create: vi.fn(),
      update: vi.fn(),
    },
    stockMovement: { create: vi.fn().mockResolvedValue({ id: 'movement-1' }) },
  };

  const prisma = {
    $transaction: vi.fn((callback: (client: typeof tx) => Promise<AdjustStockResult>) =>
      callback(tx),
    ),
  } as unknown as PrismaService;

  const tenant = {
    current: vi.fn().mockReturnValue(tenantContext),
  } as unknown as TenantContextService;

  return { handler: new AdjustStockHandler(prisma, tenant), tx };
}

describe('AdjustStockCommand', () => {
  it('validates required fields and non-zero delta', async () => {
    await expect(
      validateAppCommand(
        new AdjustStockCommand('' as BranchId, 'sku-1', 5, 'restock', 'user-1' as UserId),
      ),
    ).rejects.toBeInstanceOf(BadRequestException);

    await expect(
      validateAppCommand(
        new AdjustStockCommand('branch-1' as BranchId, '', 5, 'restock', 'user-1' as UserId),
      ),
    ).rejects.toBeInstanceOf(BadRequestException);

    await expect(
      validateAppCommand(
        new AdjustStockCommand('branch-1' as BranchId, 'sku-1', 0, 'restock', 'user-1' as UserId),
      ),
    ).rejects.toBeInstanceOf(BadRequestException);

    await expect(
      validateAppCommand(
        new AdjustStockCommand('branch-1' as BranchId, 'sku-1', 1.5, 'restock', 'user-1' as UserId),
      ),
    ).rejects.toBeInstanceOf(BadRequestException);

    await expect(
      validateAppCommand(
        new AdjustStockCommand('branch-1' as BranchId, 'sku-1', 5, 'ab', 'user-1' as UserId),
      ),
    ).rejects.toBeInstanceOf(BadRequestException);
  });
});

describe('AdjustStockHandler', () => {
  it('increments onHand and records ADJUSTMENT movement', async () => {
    const { handler, tx } = createHandlerFixture({
      id: 'stock-1',
      onHand: 100,
      reserved: 20,
    });
    const updatedAt = new Date('2026-05-25T10:00:00.000Z');
    tx.stockItem.update.mockResolvedValue({
      id: 'stock-1',
      tenantId: 'tenant-1',
      branchId: 'branch-1',
      productId: 'sku-1',
      onHand: 110,
      reserved: 20,
      createdAt: new Date('2026-05-24T10:00:00.000Z'),
      updatedAt,
    });

    const command = new AdjustStockCommand(
      'branch-1' as BranchId,
      'sku-1',
      10,
      'cycle count correction',
      'user-1' as UserId,
      { commandId: 'cmd-adj-1', correlationId: 'corr-1' },
    );

    const result = await handler.execute(command);

    expect(result.stockItem.onHand).toBe(110);
    expect(result.stockItem.available).toBe(90);
    expect(result.movementId).toBe('movement-1');
    expect(tx.stockItem.update).toHaveBeenCalledWith({
      where: { id: 'stock-1' },
      data: { onHand: 110 },
    });
    expect(tx.stockMovement.create).toHaveBeenCalledWith({
      data: expect.objectContaining({
        type: StockMovementType.ADJUSTMENT,
        quantity: 10,
        correlationId: 'corr-1',
        causationId: 'cmd-adj-1',
      }),
    });
  });

  it('decrements onHand when enough available against reservations', async () => {
    const { handler, tx } = createHandlerFixture({
      id: 'stock-1',
      onHand: 100,
      reserved: 20,
    });
    tx.stockItem.update.mockResolvedValue({
      id: 'stock-1',
      tenantId: 'tenant-1',
      branchId: 'branch-1',
      productId: 'sku-1',
      onHand: 95,
      reserved: 20,
      createdAt: new Date(),
      updatedAt: new Date(),
    });

    const result = await handler.execute(
      new AdjustStockCommand('branch-1' as BranchId, 'sku-1', -5, 'shrinkage', 'user-1' as UserId),
    );

    expect(result.stockItem.onHand).toBe(95);
    expect(result.stockItem.available).toBe(75);
  });

  it('rejects when adjustment would leave available negative', async () => {
    const { handler } = createHandlerFixture({
      id: 'stock-1',
      onHand: 100,
      reserved: 95,
    });

    await expect(
      handler.execute(
        new AdjustStockCommand(
          'branch-1' as BranchId,
          'sku-1',
          -10,
          'too much shrinkage',
          'user-1' as UserId,
        ),
      ),
    ).rejects.toBeInstanceOf(BadRequestException);
  });

  it('creates stock item when missing and delta is positive', async () => {
    const { handler, tx } = createHandlerFixture(null);
    tx.stockItem.create.mockResolvedValue({
      id: 'stock-new',
      tenantId: 'tenant-1',
      branchId: 'branch-1',
      productId: 'sku-new',
      onHand: 25,
      reserved: 0,
      createdAt: new Date(),
      updatedAt: new Date(),
    });

    const result = await handler.execute(
      new AdjustStockCommand(
        'branch-1' as BranchId,
        'sku-new',
        25,
        'initial stock',
        'user-1' as UserId,
      ),
    );

    expect(tx.stockItem.create).toHaveBeenCalledWith({
      data: {
        tenantId: 'tenant-1',
        branchId: 'branch-1',
        productId: 'sku-new',
        onHand: 25,
        reserved: 0,
      },
    });
    expect(result.stockItem.onHand).toBe(25);
    expect(result.stockItem.available).toBe(25);
  });

  it('rejects negative delta when stock item does not exist', async () => {
    const { handler } = createHandlerFixture(null);

    await expect(
      handler.execute(
        new AdjustStockCommand(
          'branch-1' as BranchId,
          'missing-sku',
          -1,
          'invalid',
          'user-1' as UserId,
        ),
      ),
    ).rejects.toBeInstanceOf(BadRequestException);
  });
});

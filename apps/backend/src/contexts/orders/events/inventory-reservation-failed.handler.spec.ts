import { DomainEventName, type DomainEvent } from '@binexus/events';
import { OrderState } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { type AppCommandBus } from '../../../common/commands/command-bus.service';
import { type PrismaService } from '../../../common/prisma/prisma.service';
import { type SystemUserService } from '../../../common/tenant/system-user.service';
import { type TenantContextService } from '../../../common/tenant/tenant-context.service';
import { CancelOrderCommand } from '../application/commands/cancel-order.command';

import { InventoryReservationFailedOrdersHandler } from './inventory-reservation-failed.handler';

const tenantId = 'tenant-1';
const systemUserId = 'system-user-1';
const orderId = 'order-1';

const event: DomainEvent<typeof DomainEventName.INVENTORY_RESERVATION_FAILED> = {
  id: 'evt-inv-failed-1',
  name: DomainEventName.INVENTORY_RESERVATION_FAILED,
  tenantId,
  occurredAt: '2026-05-25T00:00:00.000Z',
  version: 1,
  correlationId: 'corr-1',
  payload: {
    orderId,
    branchId: 'branch-1',
    failures: [{ orderLineId: 'line-1', productId: 'sku-1', requested: 5, available: 0 }],
  },
};

function createHandlerFixture() {
  const db = {
    order: { findFirst: vi.fn() },
  };

  const prisma = {
    forTenant: vi.fn().mockReturnValue(db),
  } as unknown as PrismaService;

  const tenantContext = {
    run: vi.fn((_ctx: unknown, fn: () => Promise<void>) => fn()),
  } as unknown as TenantContextService;

  const systemUser = {
    resolveForTenant: vi.fn().mockResolvedValue(systemUserId),
  } as unknown as SystemUserService;

  const commandBus = {
    execute: vi.fn().mockResolvedValue({ id: orderId, state: 'CANCELLED' }),
  } as unknown as AppCommandBus;

  const handler = new InventoryReservationFailedOrdersHandler(
    prisma,
    tenantContext,
    systemUser,
    commandBus,
  );

  return { handler, db, prisma, tenantContext, systemUser, commandBus };
}

describe('InventoryReservationFailedOrdersHandler', () => {
  it('dispatches CancelOrderCommand when order is APPROVED', async () => {
    const { handler, db, systemUser, commandBus, tenantContext } = createHandlerFixture();
    db.order.findFirst.mockResolvedValue({ id: orderId, state: OrderState.APPROVED });

    await handler.handle(event);

    expect(systemUser.resolveForTenant).toHaveBeenCalledWith(tenantId);
    expect(tenantContext.run).toHaveBeenCalledWith(
      expect.objectContaining({ tenantId, userId: systemUserId, role: 'SUPER_ADMIN' }),
      expect.any(Function),
    );
    const executeMock = vi.mocked(commandBus.execute);
    expect(executeMock).toHaveBeenCalledWith(
      expect.objectContaining({
        orderId,
        issuedBy: systemUserId,
        reason: 'auto: inventory reservation failed',
        correlationId: 'corr-1',
        causationId: event.id,
      }),
    );
    expect(executeMock.mock.calls[0]?.[0]).toBeInstanceOf(CancelOrderCommand);
  });

  it('is idempotent when order is already CANCELLED', async () => {
    const { handler, db, commandBus } = createHandlerFixture();
    db.order.findFirst.mockResolvedValue({ id: orderId, state: OrderState.CANCELLED });

    await handler.handle(event);

    expect(commandBus.execute).not.toHaveBeenCalled();
  });

  it('is idempotent when order is not found', async () => {
    const { handler, db, commandBus } = createHandlerFixture();
    db.order.findFirst.mockResolvedValue(null);

    await handler.handle(event);

    expect(commandBus.execute).not.toHaveBeenCalled();
  });

  it('resolves system user for event tenantId', async () => {
    const { handler, db, systemUser } = createHandlerFixture();
    db.order.findFirst.mockResolvedValue({ id: orderId, state: OrderState.DRAFT });

    await handler.handle(event);

    expect(systemUser.resolveForTenant).toHaveBeenCalledWith(tenantId);
  });
});

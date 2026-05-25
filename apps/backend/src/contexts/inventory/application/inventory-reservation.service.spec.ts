import { DomainEventName, type DomainEvent } from '@binexus/events';
import { OrderState, StockMovementType, StockReservationStatus } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { type EventBusService } from '../../../common/events/event-bus.service';
import { type OutboxService } from '../../../common/events/outbox.service';
import { type PrismaService } from '../../../common/prisma/prisma.service';
import { type TenantContextService } from '../../../common/tenant/tenant-context.service';

import { InventoryReservationService } from './inventory-reservation.service';

const tenantId = 'tenant-1';
const branchId = 'branch-1';
const orderId = 'order-1';
const approvedBy = 'user-1';

const orderApprovedEvent: DomainEvent<typeof DomainEventName.ORDER_APPROVED> = {
  id: 'evt-approved-1',
  name: DomainEventName.ORDER_APPROVED,
  tenantId,
  occurredAt: '2026-05-25T00:00:00.000Z',
  version: 1,
  correlationId: 'corr-1',
  payload: { orderId, approvedBy },
};

const orderCancelledEvent: DomainEvent<typeof DomainEventName.ORDER_CANCELLED> = {
  id: 'evt-cancelled-1',
  name: DomainEventName.ORDER_CANCELLED,
  tenantId,
  occurredAt: '2026-05-25T00:00:00.000Z',
  version: 1,
  correlationId: 'corr-2',
  payload: { orderId, cancelledBy: 'user-2', reason: 'test' },
};

function createServiceFixture() {
  const tenantContext = {
    run: vi.fn((_ctx: unknown, fn: () => Promise<void>) => fn()),
  } as unknown as TenantContextService;

  const db = {
    order: { findFirst: vi.fn() },
    stockReservation: { findMany: vi.fn() },
    stockItem: { findFirst: vi.fn() },
  };

  const tx = {
    stockItem: { updateMany: vi.fn().mockResolvedValue({ count: 1 }), findFirst: vi.fn() },
    stockReservation: { upsert: vi.fn().mockResolvedValue({}), update: vi.fn() },
    stockMovement: { create: vi.fn().mockResolvedValue({}) },
  };

  const prisma = {
    forTenant: vi.fn().mockReturnValue(db),
    $transaction: vi.fn((callback: (client: typeof tx) => Promise<void>) => callback(tx)),
  } as unknown as PrismaService;

  const eventBus = { build: vi.fn() };
  const outbox = { record: vi.fn().mockResolvedValue(undefined) };

  const service = new InventoryReservationService(
    prisma,
    tenantContext,
    eventBus as unknown as EventBusService,
    outbox as unknown as OutboxService,
  );

  return { service, db, prisma, tx, eventBus, outbox, tenantContext };
}

describe('InventoryReservationService', () => {
  describe('handleOrderApproved', () => {
    it('reserves stock for all lines and writes INVENTORY_RESERVED to outbox', async () => {
      const { service, db, tx, eventBus, outbox } = createServiceFixture();

      db.order.findFirst.mockResolvedValue({
        id: orderId,
        branchId,
        state: OrderState.APPROVED,
        lines: [
          { id: 'line-1', productId: 'sku-1', quantity: 2 },
          { id: 'line-2', productId: 'sku-2', quantity: 1 },
        ],
      });
      db.stockReservation.findMany.mockResolvedValue([]);
      db.stockItem.findFirst
        .mockResolvedValueOnce({ onHand: 10, reserved: 0 })
        .mockResolvedValueOnce({ onHand: 5, reserved: 1 });

      tx.stockItem.findFirst
        .mockResolvedValueOnce({ id: 'stock-1' })
        .mockResolvedValueOnce({ id: 'stock-2' });

      const reservedEvent = {
        id: 'inv-reserved-1',
        name: DomainEventName.INVENTORY_RESERVED,
        tenantId,
        occurredAt: orderApprovedEvent.occurredAt,
        version: 1,
        payload: { orderId, branchId, lineCount: 2 },
      };
      eventBus.build.mockReturnValue(reservedEvent);

      await service.handleOrderApproved(orderApprovedEvent);

      expect(tx.stockItem.updateMany).toHaveBeenCalledTimes(2);
      expect(tx.stockReservation.upsert).toHaveBeenCalledTimes(2);
      expect(tx.stockMovement.create).toHaveBeenCalledWith(
        expect.objectContaining({
          data: expect.objectContaining({
            type: StockMovementType.RESERVE,
            orderId,
            orderLineId: 'line-1',
            quantity: 2,
            causationId: orderApprovedEvent.id,
          }),
        }),
      );
      expect(eventBus.build).toHaveBeenCalledWith(
        DomainEventName.INVENTORY_RESERVED,
        { orderId, branchId, lineCount: 2 },
        expect.objectContaining({ tenantId, causationId: orderApprovedEvent.id }),
      );
      expect(outbox.record).toHaveBeenCalledWith(reservedEvent, tx);
    });

    it('emits INVENTORY_RESERVATION_FAILED when stock is insufficient', async () => {
      const { service, db, tx, eventBus, outbox } = createServiceFixture();

      db.order.findFirst.mockResolvedValue({
        id: orderId,
        branchId,
        state: OrderState.APPROVED,
        lines: [{ id: 'line-1', productId: 'sku-1', quantity: 5 }],
      });
      db.stockReservation.findMany.mockResolvedValue([]);
      db.stockItem.findFirst.mockResolvedValue({ onHand: 3, reserved: 1 });

      const failedEvent = {
        id: 'inv-failed-1',
        name: DomainEventName.INVENTORY_RESERVATION_FAILED,
        tenantId,
        occurredAt: orderApprovedEvent.occurredAt,
        version: 1,
        payload: {
          orderId,
          branchId,
          failures: [{ orderLineId: 'line-1', productId: 'sku-1', requested: 5, available: 2 }],
        },
      };
      eventBus.build.mockReturnValue(failedEvent);

      await service.handleOrderApproved(orderApprovedEvent);

      expect(tx.stockReservation.upsert).toHaveBeenCalledWith(
        expect.objectContaining({
          create: expect.objectContaining({ status: StockReservationStatus.FAILED }),
        }),
      );
      expect(tx.stockItem.updateMany).not.toHaveBeenCalled();
      expect(eventBus.build).toHaveBeenCalledWith(
        DomainEventName.INVENTORY_RESERVATION_FAILED,
        expect.objectContaining({
          orderId,
          failures: expect.arrayContaining([
            expect.objectContaining({ available: 2, requested: 5 }),
          ]),
        }),
        expect.any(Object),
      );
      expect(outbox.record).toHaveBeenCalledWith(failedEvent, tx);
    });

    it('is idempotent when all lines are already ACTIVE', async () => {
      const { service, db, prisma, eventBus, outbox } = createServiceFixture();

      db.order.findFirst.mockResolvedValue({
        id: orderId,
        branchId,
        state: OrderState.APPROVED,
        lines: [{ id: 'line-1', productId: 'sku-1', quantity: 2 }],
      });
      db.stockReservation.findMany.mockResolvedValue([
        {
          orderLineId: 'line-1',
          status: StockReservationStatus.ACTIVE,
          quantity: 2,
        },
      ]);

      await service.handleOrderApproved(orderApprovedEvent);

      expect(prisma.$transaction).not.toHaveBeenCalled();
      expect(eventBus.build).not.toHaveBeenCalled();
      expect(outbox.record).not.toHaveBeenCalled();
    });
  });

  describe('handleOrderCancelled', () => {
    it('releases active reservations and writes INVENTORY_RELEASED to outbox', async () => {
      const { service, db, tx, eventBus, outbox } = createServiceFixture();

      db.order.findFirst.mockResolvedValue({ id: orderId, branchId });
      db.stockReservation.findMany.mockResolvedValue([
        {
          id: 'res-1',
          orderLineId: 'line-1',
          branchId,
          productId: 'sku-1',
          quantity: 2,
          status: StockReservationStatus.ACTIVE,
        },
      ]);

      const releasedEvent = {
        id: 'inv-released-1',
        name: DomainEventName.INVENTORY_RELEASED,
        tenantId,
        occurredAt: orderCancelledEvent.occurredAt,
        version: 1,
        payload: { orderId, branchId, lineCount: 1, releasedBy: 'user-2' },
      };
      eventBus.build.mockReturnValue(releasedEvent);

      await service.handleOrderCancelled(orderCancelledEvent);

      expect(tx.stockItem.updateMany).toHaveBeenCalledWith({
        where: { tenantId, branchId, productId: 'sku-1' },
        data: { reserved: { decrement: 2 } },
      });
      expect(tx.stockReservation.update).toHaveBeenCalledWith({
        where: { id: 'res-1' },
        data: { status: StockReservationStatus.RELEASED },
      });
      expect(tx.stockMovement.create).toHaveBeenCalledWith(
        expect.objectContaining({
          data: expect.objectContaining({
            type: StockMovementType.RELEASE,
            quantity: 2,
            causationId: orderCancelledEvent.id,
          }),
        }),
      );
      expect(outbox.record).toHaveBeenCalledWith(releasedEvent, tx);
    });

    it('is idempotent when there are no active reservations', async () => {
      const { service, db, prisma, outbox } = createServiceFixture();

      db.order.findFirst.mockResolvedValue({ id: orderId, branchId });
      db.stockReservation.findMany.mockResolvedValue([]);

      await service.handleOrderCancelled(orderCancelledEvent);

      expect(prisma.$transaction).not.toHaveBeenCalled();
      expect(outbox.record).not.toHaveBeenCalled();
    });
  });
});

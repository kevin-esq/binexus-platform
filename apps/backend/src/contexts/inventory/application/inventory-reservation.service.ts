import {
  type DomainEvent,
  DomainEventName,
  type OrderApprovedPayload,
  type OrderCancelledPayload,
  orderApprovedPayload,
  orderCancelledPayload,
} from '@binexus/events';
import { Inject, Injectable, Logger } from '@nestjs/common';
import { OrderState, StockMovementType, StockReservationStatus } from '@prisma/client';

import { EventBusService } from '../../../common/events/event-bus.service';
import { OutboxService } from '../../../common/events/outbox.service';
import { PrismaService } from '../../../common/prisma/prisma.service';
import { TenantContextService } from '../../../common/tenant/tenant-context.service';

function availableQuantity(item: { onHand: number; reserved: number } | null): number {
  if (!item) {
    return 0;
  }
  return item.onHand - item.reserved;
}

@Injectable()
export class InventoryReservationService {
  private readonly logger = new Logger(InventoryReservationService.name);

  constructor(
    @Inject(PrismaService) private readonly prisma: PrismaService,
    @Inject(TenantContextService) private readonly tenantContext: TenantContextService,
    @Inject(EventBusService) private readonly eventBus: EventBusService,
    @Inject(OutboxService) private readonly outbox: OutboxService,
  ) {}

  async handleOrderApproved(
    event: DomainEvent<typeof DomainEventName.ORDER_APPROVED>,
  ): Promise<void> {
    const payload: OrderApprovedPayload = orderApprovedPayload.parse(event.payload);

    await this.tenantContext.run(
      {
        tenantId: event.tenantId,
        userId: payload.approvedBy,
        role: 'SYSTEM',
        branchId: null,
        requestId: event.correlationId ?? event.id,
      },
      () => this.reserveForApprovedOrder(event, payload),
    );
  }

  async handleOrderCancelled(
    event: DomainEvent<typeof DomainEventName.ORDER_CANCELLED>,
  ): Promise<void> {
    const payload: OrderCancelledPayload = orderCancelledPayload.parse(event.payload);

    await this.tenantContext.run(
      {
        tenantId: event.tenantId,
        userId: payload.cancelledBy,
        role: 'SYSTEM',
        branchId: null,
        requestId: event.correlationId ?? event.id,
      },
      () => this.releaseForCancelledOrder(event, payload),
    );
  }

  private async reserveForApprovedOrder(
    event: DomainEvent<typeof DomainEventName.ORDER_APPROVED>,
    payload: OrderApprovedPayload,
  ): Promise<void> {
    const db = this.prisma.forTenant();

    const order = await db.order.findFirst({
      where: { id: payload.orderId },
      include: { lines: true },
    });

    if (!order) {
      this.logger.warn(`reserve skipped: order ${payload.orderId} not found`);
      return;
    }

    if (order.state !== OrderState.APPROVED) {
      this.logger.debug(
        `reserve skipped: order ${order.id} state=${order.state} (expected APPROVED)`,
      );
      return;
    }

    if (order.lines.length === 0) {
      this.logger.warn(`reserve skipped: order ${order.id} has no lines`);
      return;
    }

    const existing = await db.stockReservation.findMany({
      where: { orderId: order.id },
    });

    const allLinesActive = order.lines.every((line) =>
      existing.some(
        (r) =>
          r.orderLineId === line.id &&
          r.status === StockReservationStatus.ACTIVE &&
          r.quantity === line.quantity,
      ),
    );
    if (allLinesActive) {
      this.logger.debug(`reserve idempotent: order ${order.id} already fully reserved`);
      return;
    }

    const allLinesFailed = order.lines.every((line) =>
      existing.some(
        (r) =>
          r.orderLineId === line.id &&
          r.status === StockReservationStatus.FAILED &&
          r.quantity === line.quantity,
      ),
    );

    const failures: Array<{
      orderLineId: string;
      productId: string;
      requested: number;
      available: number;
    }> = [];

    for (const line of order.lines) {
      const stockItem = await db.stockItem.findFirst({
        where: { branchId: order.branchId, productId: line.productId },
      });
      const available = availableQuantity(stockItem);
      if (available < line.quantity) {
        failures.push({
          orderLineId: line.id,
          productId: line.productId,
          requested: line.quantity,
          available,
        });
      }
    }

    if (failures.length > 0) {
      if (allLinesFailed) {
        this.logger.debug(`reserve failure idempotent: order ${order.id} already marked FAILED`);
        return;
      }

      await this.recordReservationFailure(event, order.id, order.branchId, order.lines, failures);
      return;
    }

    await this.recordReservationSuccess(event, order);
  }

  private async recordReservationFailure(
    event: DomainEvent<typeof DomainEventName.ORDER_APPROVED>,
    orderId: string,
    branchId: string,
    lines: Array<{ id: string; productId: string; quantity: number }>,
    failures: Array<{
      orderLineId: string;
      productId: string;
      requested: number;
      available: number;
    }>,
  ): Promise<void> {
    await this.prisma.$transaction(async (tx) => {
      for (const line of lines) {
        await tx.stockReservation.upsert({
          where: {
            tenantId_orderId_orderLineId: {
              tenantId: event.tenantId,
              orderId,
              orderLineId: line.id,
            },
          },
          create: {
            tenantId: event.tenantId,
            orderId,
            orderLineId: line.id,
            branchId,
            productId: line.productId,
            quantity: line.quantity,
            status: StockReservationStatus.FAILED,
          },
          update: {
            status: StockReservationStatus.FAILED,
            quantity: line.quantity,
            productId: line.productId,
            branchId,
          },
        });
      }

      const failEvent = this.eventBus.build(
        DomainEventName.INVENTORY_RESERVATION_FAILED,
        { orderId, branchId, failures },
        {
          tenantId: event.tenantId,
          correlationId: event.correlationId,
          causationId: event.id,
        },
      );

      await this.outbox.record(failEvent, tx);
    });

    this.logger.warn(
      `reservation failed order=${orderId} tenant=${event.tenantId} lines=${failures.length}`,
    );
  }

  private async recordReservationSuccess(
    event: DomainEvent<typeof DomainEventName.ORDER_APPROVED>,
    order: {
      id: string;
      branchId: string;
      lines: Array<{ id: string; productId: string; quantity: number }>;
    },
  ): Promise<void> {
    await this.prisma.$transaction(async (tx) => {
      for (const line of order.lines) {
        await tx.stockItem.updateMany({
          where: {
            tenantId: event.tenantId,
            branchId: order.branchId,
            productId: line.productId,
          },
          data: { reserved: { increment: line.quantity } },
        });

        const updated = await tx.stockItem.findFirst({
          where: {
            tenantId: event.tenantId,
            branchId: order.branchId,
            productId: line.productId,
          },
        });

        if (!updated) {
          throw new Error(
            `StockItem missing for product ${line.productId} at branch ${order.branchId}`,
          );
        }

        await tx.stockReservation.upsert({
          where: {
            tenantId_orderId_orderLineId: {
              tenantId: event.tenantId,
              orderId: order.id,
              orderLineId: line.id,
            },
          },
          create: {
            tenantId: event.tenantId,
            orderId: order.id,
            orderLineId: line.id,
            branchId: order.branchId,
            productId: line.productId,
            quantity: line.quantity,
            status: StockReservationStatus.ACTIVE,
          },
          update: {
            status: StockReservationStatus.ACTIVE,
            quantity: line.quantity,
            productId: line.productId,
            branchId: order.branchId,
          },
        });

        await tx.stockMovement.create({
          data: {
            tenantId: event.tenantId,
            branchId: order.branchId,
            productId: line.productId,
            orderId: order.id,
            orderLineId: line.id,
            type: StockMovementType.RESERVE,
            quantity: line.quantity,
            correlationId: event.correlationId ?? null,
            causationId: event.id,
          },
        });
      }

      const successEvent = this.eventBus.build(
        DomainEventName.INVENTORY_RESERVED,
        {
          orderId: order.id,
          branchId: order.branchId,
          lineCount: order.lines.length,
        },
        {
          tenantId: event.tenantId,
          correlationId: event.correlationId,
          causationId: event.id,
        },
      );

      await this.outbox.record(successEvent, tx);
    });

    this.logger.debug(`reserved stock for order=${order.id} tenant=${event.tenantId}`);
  }

  private async releaseForCancelledOrder(
    event: DomainEvent<typeof DomainEventName.ORDER_CANCELLED>,
    payload: OrderCancelledPayload,
  ): Promise<void> {
    const db = this.prisma.forTenant();

    const order = await db.order.findFirst({
      where: { id: payload.orderId },
      select: { id: true, branchId: true },
    });

    if (!order) {
      this.logger.warn(`release skipped: order ${payload.orderId} not found`);
      return;
    }

    const activeReservations = await db.stockReservation.findMany({
      where: { orderId: order.id, status: StockReservationStatus.ACTIVE },
    });

    if (activeReservations.length === 0) {
      this.logger.debug(`release idempotent: no active reservations for order ${order.id}`);
      return;
    }

    await this.prisma.$transaction(async (tx) => {
      for (const reservation of activeReservations) {
        await tx.stockItem.updateMany({
          where: {
            tenantId: event.tenantId,
            branchId: reservation.branchId,
            productId: reservation.productId,
          },
          data: { reserved: { decrement: reservation.quantity } },
        });

        await tx.stockReservation.update({
          where: { id: reservation.id },
          data: { status: StockReservationStatus.RELEASED },
        });

        await tx.stockMovement.create({
          data: {
            tenantId: event.tenantId,
            branchId: reservation.branchId,
            productId: reservation.productId,
            orderId: order.id,
            orderLineId: reservation.orderLineId,
            type: StockMovementType.RELEASE,
            quantity: reservation.quantity,
            correlationId: event.correlationId ?? null,
            causationId: event.id,
          },
        });
      }

      const releaseEvent = this.eventBus.build(
        DomainEventName.INVENTORY_RELEASED,
        {
          orderId: order.id,
          branchId: order.branchId,
          lineCount: activeReservations.length,
          releasedBy: payload.cancelledBy,
        },
        {
          tenantId: event.tenantId,
          correlationId: event.correlationId,
          causationId: event.id,
        },
      );

      await this.outbox.record(releaseEvent, tx);
    });

    this.logger.debug(`released reservations for order=${order.id} tenant=${event.tenantId}`);
  }
}

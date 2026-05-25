import {
  type DomainEvent,
  DomainEventName,
  type OrderApprovedPayload,
  type OrderCreatedPayload,
  orderApprovedPayload,
  orderCreatedPayload,
} from '@binexus/events';
import { Inject, Injectable, Logger } from '@nestjs/common';
import type { Prisma } from '@prisma/client';

import { PrismaService } from '../prisma/prisma.service';

@Injectable()
export class AuditLogService {
  private readonly logger = new Logger(AuditLogService.name);

  constructor(@Inject(PrismaService) private readonly prisma: PrismaService) {}

  /**
   * Persists an audit row for ORDER_CREATED. Idempotent on `event.id` so dispatcher
   * retries do not duplicate audit entries.
   */
  async recordOrderCreated(
    event: DomainEvent<typeof DomainEventName.ORDER_CREATED>,
  ): Promise<void> {
    const payload: OrderCreatedPayload = orderCreatedPayload.parse(event.payload);

    await this.prisma.auditLog.upsert({
      where: { eventId: event.id },
      create: {
        tenantId: event.tenantId,
        eventId: event.id,
        eventName: event.name,
        actorUserId: payload.createdBy,
        entityType: 'Order',
        entityId: payload.orderId,
        action: DomainEventName.ORDER_CREATED,
        payload: payload as unknown as Prisma.InputJsonValue,
        occurredAt: new Date(event.occurredAt),
      },
      update: {},
    });

    this.logger.debug(
      `audit recorded event=${event.name} orderId=${payload.orderId} eventId=${event.id}`,
    );
  }

  /**
   * Persists an audit row for ORDER_APPROVED. Idempotent on `event.id`.
   */
  async recordOrderApproved(
    event: DomainEvent<typeof DomainEventName.ORDER_APPROVED>,
  ): Promise<void> {
    const payload: OrderApprovedPayload = orderApprovedPayload.parse(event.payload);

    await this.prisma.auditLog.upsert({
      where: { eventId: event.id },
      create: {
        tenantId: event.tenantId,
        eventId: event.id,
        eventName: event.name,
        actorUserId: payload.approvedBy,
        entityType: 'Order',
        entityId: payload.orderId,
        action: DomainEventName.ORDER_APPROVED,
        payload: payload as unknown as Prisma.InputJsonValue,
        occurredAt: new Date(event.occurredAt),
      },
      update: {},
    });

    this.logger.debug(
      `audit recorded event=${event.name} orderId=${payload.orderId} eventId=${event.id}`,
    );
  }
}

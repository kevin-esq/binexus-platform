import {
  type DomainEvent,
  type DomainEventName,
  orderCancelledPayload,
  orderReadyForDeliveryRoutePayload,
} from '@binexus/events';
import { Inject, Injectable, Logger } from '@nestjs/common';
import { DeliveryRouteCandidateStatus } from '@prisma/client';

import { PrismaService } from '../../../common/prisma/prisma.service';
import { TenantContextService } from '../../../common/tenant/tenant-context.service';

@Injectable()
export class LogisticsCandidateService {
  private readonly logger = new Logger(LogisticsCandidateService.name);

  constructor(
    @Inject(PrismaService) private readonly prisma: PrismaService,
    @Inject(TenantContextService) private readonly tenantContext: TenantContextService,
  ) {}

  async handleOrderReadyForDeliveryRoute(
    event: DomainEvent<typeof DomainEventName.ORDER_READY_FOR_DELIVERY_ROUTE>,
  ): Promise<void> {
    const payload = orderReadyForDeliveryRoutePayload.parse(event.payload);

    await this.tenantContext.run(
      {
        tenantId: event.tenantId,
        userId: payload.readyBy,
        role: 'SYSTEM',
        branchId: payload.branchId,
        requestId: event.correlationId ?? event.id,
      },
      async () => {
        const db = this.prisma.forTenant();
        const existing = await db.deliveryRouteCandidate.findFirst({
          where: { orderId: payload.orderId },
        });

        if (existing) {
          if (existing.createdFromEventId === event.id) {
            this.logger.debug(
              `delivery route candidate idempotent: order ${payload.orderId} event ${event.id}`,
            );
            return;
          }
          if (existing.status === DeliveryRouteCandidateStatus.READY) {
            await db.deliveryRouteCandidate.update({
              where: { id: existing.id },
              data: { branchId: payload.branchId },
            });
            return;
          }
          if (existing.status === DeliveryRouteCandidateStatus.ASSIGNED) {
            await db.deliveryRouteCandidate.update({
              where: { id: existing.id },
              data: {
                branchId: payload.branchId,
                status: DeliveryRouteCandidateStatus.READY,
                deliveryRouteId: null,
                createdFromEventId: event.id,
              },
            });
            this.logger.debug(
              `requeued delivery route candidate for order=${payload.orderId} (ASSIGNED -> READY)`,
            );
            return;
          }
          this.logger.debug(
            `delivery route candidate skip: order ${payload.orderId} status=${existing.status}`,
          );
          return;
        }

        await db.deliveryRouteCandidate.create({
          data: {
            tenantId: event.tenantId,
            orderId: payload.orderId,
            branchId: payload.branchId,
            status: DeliveryRouteCandidateStatus.READY,
            createdFromEventId: event.id,
          },
        });

        this.logger.debug(`created delivery route candidate for order=${payload.orderId}`);
      },
    );
  }

  async handleOrderCancelled(
    event: DomainEvent<typeof DomainEventName.ORDER_CANCELLED>,
  ): Promise<void> {
    const payload = orderCancelledPayload.parse(event.payload);

    await this.tenantContext.run(
      {
        tenantId: event.tenantId,
        userId: payload.cancelledBy,
        role: 'SYSTEM',
        branchId: null,
        requestId: event.correlationId ?? event.id,
      },
      async () => {
        const db = this.prisma.forTenant();
        const existing = await db.deliveryRouteCandidate.findFirst({
          where: { orderId: payload.orderId },
        });

        if (!existing || existing.status === DeliveryRouteCandidateStatus.CANCELLED) {
          return;
        }

        await db.deliveryRouteCandidate.update({
          where: { id: existing.id },
          data: {
            status: DeliveryRouteCandidateStatus.CANCELLED,
            deliveryRouteId: null,
          },
        });

        this.logger.debug(`cancelled delivery route candidate for order=${payload.orderId}`);
      },
    );
  }
}

import {
  type DomainEvent,
  type DomainEventName,
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
}

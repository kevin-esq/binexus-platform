import {
  type DomainEvent,
  type DomainEventName,
  orderPickingStartedPayload,
} from '@binexus/events';
import { Inject, Injectable, Logger } from '@nestjs/common';
import { PickingTaskStatus } from '@prisma/client';

import { PrismaService } from '../../../common/prisma/prisma.service';
import { SystemUserService } from '../../../common/tenant/system-user.service';
import { TenantContextService } from '../../../common/tenant/tenant-context.service';

@Injectable()
export class WarehousePickingService {
  private readonly logger = new Logger(WarehousePickingService.name);

  constructor(
    @Inject(PrismaService) private readonly prisma: PrismaService,
    @Inject(TenantContextService) private readonly tenantContext: TenantContextService,
    @Inject(SystemUserService) private readonly systemUser: SystemUserService,
  ) {}

  async handleOrderPickingStarted(
    event: DomainEvent<typeof DomainEventName.ORDER_PICKING_STARTED>,
  ): Promise<void> {
    const payload = orderPickingStartedPayload.parse(event.payload);
    const systemUserId = await this.systemUser.resolveForTenant(event.tenantId);

    await this.tenantContext.run(
      {
        tenantId: event.tenantId,
        userId: systemUserId,
        role: 'SUPER_ADMIN',
        branchId: payload.branchId,
        requestId: event.correlationId ?? event.id,
      },
      () => this.createPickingTask(event, payload),
    );
  }

  private async createPickingTask(
    event: DomainEvent<typeof DomainEventName.ORDER_PICKING_STARTED>,
    payload: { orderId: string; branchId: string },
  ): Promise<void> {
    const db = this.prisma.forTenant();

    const existing = await db.pickingTask.findFirst({
      where: { orderId: payload.orderId },
    });

    if (existing) {
      this.logger.debug(`picking task idempotent: order ${payload.orderId} already has task`);
      return;
    }

    const order = await db.order.findFirst({
      where: { id: payload.orderId },
      include: { lines: true },
    });

    if (!order) {
      this.logger.warn(`picking task skipped: order ${payload.orderId} not found`);
      return;
    }

    await this.prisma.$transaction(async (tx) => {
      const task = await tx.pickingTask.create({
        data: {
          tenantId: event.tenantId,
          orderId: order.id,
          branchId: order.branchId,
          status: PickingTaskStatus.PENDING,
          createdFromEventId: event.id,
        },
      });

      for (const line of order.lines) {
        await tx.pickingLine.create({
          data: {
            tenantId: event.tenantId,
            pickingTaskId: task.id,
            orderLineId: line.id,
            productId: line.productId,
            quantity: line.quantity,
            pickedQuantity: 0,
          },
        });
      }
    });

    this.logger.debug(`created picking task for order=${payload.orderId}`);
  }
}

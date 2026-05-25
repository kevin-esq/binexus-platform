import { type DomainEvent, DomainEventName, pickingCompletedPayload } from '@binexus/events';
import { type OrderId, type UserId } from '@binexus/types';
import { Inject, Injectable } from '@nestjs/common';
import { OnEvent } from '@nestjs/event-emitter';
import { OrderState } from '@prisma/client';

import { AppCommandBus } from '../../../common/commands/command-bus.service';
import { PrismaService } from '../../../common/prisma/prisma.service';
import { TenantContextService } from '../../../common/tenant/tenant-context.service';
import { MarkOrderReadyForDeliveryRouteCommand } from '../application/commands/mark-order-ready-for-delivery-route.command';

@Injectable()
export class PickingCompletedOrdersHandler {
  constructor(
    @Inject(PrismaService) private readonly prisma: PrismaService,
    @Inject(TenantContextService) private readonly tenantContext: TenantContextService,
    @Inject(AppCommandBus) private readonly commandBus: AppCommandBus,
  ) {}

  @OnEvent(DomainEventName.PICKING_COMPLETED)
  async handle(event: DomainEvent<typeof DomainEventName.PICKING_COMPLETED>): Promise<void> {
    const payload = pickingCompletedPayload.parse(event.payload);

    await this.tenantContext.run(
      {
        tenantId: event.tenantId,
        userId: payload.completedBy,
        role: 'SYSTEM',
        branchId: null,
        requestId: event.correlationId ?? event.id,
      },
      async () => {
        const order = await this.prisma.forTenant().order.findFirst({
          where: { id: payload.orderId },
          select: { id: true, state: true },
        });

        if (!order || order.state === OrderState.READY_FOR_DELIVERY_ROUTE) {
          return;
        }

        if (order.state !== OrderState.PICKING) {
          return;
        }

        await this.commandBus.execute(
          new MarkOrderReadyForDeliveryRouteCommand(
            order.id as OrderId,
            payload.completedBy as UserId,
            {
              correlationId: event.correlationId,
              causationId: event.id,
            },
          ),
        );
      },
    );
  }
}

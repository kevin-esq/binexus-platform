import { type DomainEvent, DomainEventName, deliveryRouteDispatchedPayload } from '@binexus/events';
import { type OrderId, type UserId } from '@binexus/types';
import { Inject, Injectable } from '@nestjs/common';
import { OnEvent } from '@nestjs/event-emitter';
import { OrderState } from '@prisma/client';

import { AppCommandBus } from '../../../common/commands/command-bus.service';
import { PrismaService } from '../../../common/prisma/prisma.service';
import { SystemUserService } from '../../../common/tenant/system-user.service';
import { TenantContextService } from '../../../common/tenant/tenant-context.service';
import { MarkOrderOutForDeliveryCommand } from '../application/commands/mark-order-out-for-delivery.command';

@Injectable()
export class DeliveryRouteDispatchedOrdersHandler {
  constructor(
    @Inject(PrismaService) private readonly prisma: PrismaService,
    @Inject(TenantContextService) private readonly tenantContext: TenantContextService,
    @Inject(SystemUserService) private readonly systemUser: SystemUserService,
    @Inject(AppCommandBus) private readonly commandBus: AppCommandBus,
  ) {}

  @OnEvent(DomainEventName.DELIVERY_ROUTE_DISPATCHED)
  async handle(
    event: DomainEvent<typeof DomainEventName.DELIVERY_ROUTE_DISPATCHED>,
  ): Promise<void> {
    const payload = deliveryRouteDispatchedPayload.parse(event.payload);
    const systemUserId = await this.systemUser.resolveForTenant(event.tenantId);

    await this.tenantContext.run(
      {
        tenantId: event.tenantId,
        userId: systemUserId,
        role: 'SUPER_ADMIN',
        branchId: payload.branchId,
        requestId: event.correlationId ?? event.id,
      },
      async () => {
        for (const orderId of payload.orderIds) {
          const order = await this.prisma.forTenant().order.findFirst({
            where: { id: orderId },
            select: { id: true, state: true },
          });

          if (!order || order.state !== OrderState.READY_FOR_DELIVERY_ROUTE) {
            continue;
          }

          await this.commandBus.execute(
            new MarkOrderOutForDeliveryCommand(order.id as OrderId, systemUserId as UserId, {
              correlationId: event.correlationId,
              causationId: event.id,
            }),
          );
        }
      },
    );
  }
}

import { type DomainEvent, DomainEventName, deliveryConfirmedPayload } from '@binexus/events';
import { type OrderId, type UserId } from '@binexus/types';
import { Inject, Injectable } from '@nestjs/common';
import { OnEvent } from '@nestjs/event-emitter';
import { OrderState } from '@prisma/client';

import { AppCommandBus } from '../../../common/commands/command-bus.service';
import { PrismaService } from '../../../common/prisma/prisma.service';
import { SystemUserService } from '../../../common/tenant/system-user.service';
import { TenantContextService } from '../../../common/tenant/tenant-context.service';
import { MarkOrderDeliveredCommand } from '../application/commands/mark-order-delivered.command';

@Injectable()
export class DeliveryConfirmedOrdersHandler {
  constructor(
    @Inject(PrismaService) private readonly prisma: PrismaService,
    @Inject(TenantContextService) private readonly tenantContext: TenantContextService,
    @Inject(SystemUserService) private readonly systemUser: SystemUserService,
    @Inject(AppCommandBus) private readonly commandBus: AppCommandBus,
  ) {}

  @OnEvent(DomainEventName.DELIVERY_CONFIRMED)
  async handle(event: DomainEvent<typeof DomainEventName.DELIVERY_CONFIRMED>): Promise<void> {
    const payload = deliveryConfirmedPayload.parse(event.payload);
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
        const order = await this.prisma.forTenant().order.findFirst({
          where: { id: payload.orderId },
          select: { id: true, state: true },
        });

        if (!order || order.state !== OrderState.OUT_FOR_DELIVERY) {
          return;
        }

        await this.commandBus.execute(
          new MarkOrderDeliveredCommand(order.id as OrderId, systemUserId as UserId, {
            correlationId: event.correlationId,
            causationId: event.id,
          }),
        );
      },
    );
  }
}

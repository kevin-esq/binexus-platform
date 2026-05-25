import {
  type DomainEvent,
  DomainEventName,
  inventoryReservationFailedPayload,
} from '@binexus/events';
import { type OrderId, type UserId } from '@binexus/types';
import { Inject, Injectable } from '@nestjs/common';
import { OnEvent } from '@nestjs/event-emitter';
import { OrderState } from '@prisma/client';

import { AppCommandBus } from '../../../common/commands/command-bus.service';
import { PrismaService } from '../../../common/prisma/prisma.service';
import { SystemUserService } from '../../../common/tenant/system-user.service';
import { TenantContextService } from '../../../common/tenant/tenant-context.service';
import { CancelOrderCommand } from '../application/commands/cancel-order.command';

const AUTO_CANCEL_REASON = 'auto: inventory reservation failed';

@Injectable()
export class InventoryReservationFailedOrdersHandler {
  constructor(
    @Inject(PrismaService) private readonly prisma: PrismaService,
    @Inject(TenantContextService) private readonly tenantContext: TenantContextService,
    @Inject(SystemUserService) private readonly systemUser: SystemUserService,
    @Inject(AppCommandBus) private readonly commandBus: AppCommandBus,
  ) {}

  @OnEvent(DomainEventName.INVENTORY_RESERVATION_FAILED)
  async handle(
    event: DomainEvent<typeof DomainEventName.INVENTORY_RESERVATION_FAILED>,
  ): Promise<void> {
    const payload = inventoryReservationFailedPayload.parse(event.payload);
    const systemUserId = await this.systemUser.resolveForTenant(event.tenantId);

    await this.tenantContext.run(
      {
        tenantId: event.tenantId,
        userId: systemUserId,
        role: 'SUPER_ADMIN',
        branchId: null,
        requestId: event.correlationId ?? event.id,
      },
      async () => {
        const order = await this.prisma.forTenant().order.findFirst({
          where: { id: payload.orderId },
          select: { id: true, state: true },
        });

        if (!order || order.state !== OrderState.APPROVED) {
          return;
        }

        await this.commandBus.execute(
          new CancelOrderCommand(order.id as OrderId, systemUserId as UserId, AUTO_CANCEL_REASON, {
            correlationId: event.correlationId,
            causationId: event.id,
          }),
        );
      },
    );
  }
}

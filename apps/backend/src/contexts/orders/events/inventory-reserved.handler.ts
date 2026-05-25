import { type DomainEvent, DomainEventName, inventoryReservedPayload } from '@binexus/events';
import { type OrderId, type UserId } from '@binexus/types';
import { Inject, Injectable } from '@nestjs/common';
import { OnEvent } from '@nestjs/event-emitter';
import { OrderState } from '@prisma/client';

import { AppCommandBus } from '../../../common/commands/command-bus.service';
import { PrismaService } from '../../../common/prisma/prisma.service';
import { SystemUserService } from '../../../common/tenant/system-user.service';
import { TenantContextService } from '../../../common/tenant/tenant-context.service';
import { MoveOrderToPickingCommand } from '../application/commands/move-order-to-picking.command';

@Injectable()
export class InventoryReservedOrdersHandler {
  constructor(
    @Inject(PrismaService) private readonly prisma: PrismaService,
    @Inject(TenantContextService) private readonly tenantContext: TenantContextService,
    @Inject(SystemUserService) private readonly systemUser: SystemUserService,
    @Inject(AppCommandBus) private readonly commandBus: AppCommandBus,
  ) {}

  @OnEvent(DomainEventName.INVENTORY_RESERVED)
  async handle(event: DomainEvent<typeof DomainEventName.INVENTORY_RESERVED>): Promise<void> {
    const payload = inventoryReservedPayload.parse(event.payload);
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
          new MoveOrderToPickingCommand(order.id as OrderId, systemUserId as UserId, {
            correlationId: event.correlationId,
            causationId: event.id,
          }),
        );
      },
    );
  }
}

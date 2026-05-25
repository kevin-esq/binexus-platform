import {
  canTransition,
  type MarkOrderReadyForDeliveryRouteResult,
  OrderState as SharedOrderState,
  type OrderId,
  type UserId,
} from '@binexus/types';
import { BadRequestException, Inject, Injectable, NotFoundException } from '@nestjs/common';
import { CommandHandler } from '@nestjs/cqrs';
import { OrderState } from '@prisma/client';

import { AppCommand, type AppCommandMetadata } from '../../../../common/commands/app-command';
import { AppCommandHandler } from '../../../../common/commands/app-command-handler';
import { PrismaService } from '../../../../common/prisma/prisma.service';
import { TenantContextService } from '../../../../common/tenant/tenant-context.service';

export class MarkOrderReadyForDeliveryRouteCommand extends AppCommand<MarkOrderReadyForDeliveryRouteResult> {
  constructor(
    readonly orderId: OrderId,
    readonly issuedBy: UserId,
    metadata?: AppCommandMetadata,
  ) {
    super(metadata);
  }

  validate(): void {
    if (!this.orderId.trim()) {
      throw new BadRequestException('orderId is required.');
    }
  }
}

@Injectable()
@CommandHandler(MarkOrderReadyForDeliveryRouteCommand)
export class MarkOrderReadyForDeliveryRouteHandler extends AppCommandHandler<MarkOrderReadyForDeliveryRouteCommand> {
  constructor(
    @Inject(PrismaService)
    private readonly prisma: PrismaService,
    @Inject(TenantContextService)
    private readonly tenantContext: TenantContextService,
  ) {
    super();
  }

  async execute(
    command: MarkOrderReadyForDeliveryRouteCommand,
  ): Promise<MarkOrderReadyForDeliveryRouteResult> {
    const ctx = this.tenantContext.current();
    const targetState = SharedOrderState.READY_FOR_DELIVERY_ROUTE;

    return this.prisma.$transaction(async (tx) => {
      const order = await tx.order.findFirst({
        where: { id: command.orderId, tenantId: ctx.tenantId },
        select: { id: true, state: true },
      });

      if (!order) {
        throw new NotFoundException(`Order ${command.orderId} not found`);
      }

      const fromState = order.state as SharedOrderState;
      if (fromState === targetState) {
        return { id: order.id as OrderId, state: targetState };
      }

      if (!canTransition(fromState, targetState)) {
        throw new BadRequestException(
          `Cannot transition order from ${order.state} to ${targetState}`,
        );
      }

      await tx.order.update({
        where: { id: order.id },
        data: { state: OrderState.READY_FOR_DELIVERY_ROUTE },
      });

      await tx.orderTransition.create({
        data: {
          tenantId: ctx.tenantId,
          orderId: order.id,
          fromState: order.state,
          toState: OrderState.READY_FOR_DELIVERY_ROUTE,
          reason: 'Picking completed',
          byUserId: command.issuedBy,
        },
      });

      return { id: order.id as OrderId, state: targetState };
    });
  }
}

import {
  canTransition,
  type MarkOrderOutForDeliveryResult,
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

export class MarkOrderOutForDeliveryCommand extends AppCommand<MarkOrderOutForDeliveryResult> {
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
@CommandHandler(MarkOrderOutForDeliveryCommand)
export class MarkOrderOutForDeliveryHandler extends AppCommandHandler<MarkOrderOutForDeliveryCommand> {
  constructor(
    @Inject(PrismaService)
    private readonly prisma: PrismaService,
    @Inject(TenantContextService)
    private readonly tenantContext: TenantContextService,
  ) {
    super();
  }

  async execute(command: MarkOrderOutForDeliveryCommand): Promise<MarkOrderOutForDeliveryResult> {
    const ctx = this.tenantContext.current();
    const targetState = SharedOrderState.OUT_FOR_DELIVERY;

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
        data: { state: OrderState.OUT_FOR_DELIVERY },
      });

      await tx.orderTransition.create({
        data: {
          tenantId: ctx.tenantId,
          orderId: order.id,
          fromState: order.state,
          toState: OrderState.OUT_FOR_DELIVERY,
          reason: 'Delivery route dispatched',
          byUserId: command.issuedBy,
        },
      });

      return { id: order.id as OrderId, state: targetState };
    });
  }
}

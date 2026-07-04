import {
  canTransition,
  type DeliveryFailureReason,
  type MarkOrderDeliveryAttemptFailedResult,
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

export class MarkOrderDeliveryAttemptFailedCommand extends AppCommand<MarkOrderDeliveryAttemptFailedResult> {
  constructor(
    readonly orderId: OrderId,
    readonly issuedBy: UserId,
    readonly failureReason: DeliveryFailureReason,
    readonly failureNotes?: string,
    metadata?: AppCommandMetadata,
  ) {
    super(metadata);
  }

  validate(): void {
    if (!this.orderId.trim()) {
      throw new BadRequestException('orderId is required.');
    }
    if (!this.failureReason.trim()) {
      throw new BadRequestException('failureReason is required.');
    }
  }
}

@Injectable()
@CommandHandler(MarkOrderDeliveryAttemptFailedCommand)
export class MarkOrderDeliveryAttemptFailedHandler extends AppCommandHandler<MarkOrderDeliveryAttemptFailedCommand> {
  constructor(
    @Inject(PrismaService)
    private readonly prisma: PrismaService,
    @Inject(TenantContextService)
    private readonly tenantContext: TenantContextService,
  ) {
    super();
  }

  async execute(
    command: MarkOrderDeliveryAttemptFailedCommand,
  ): Promise<MarkOrderDeliveryAttemptFailedResult> {
    const ctx = this.tenantContext.current();
    const targetState = SharedOrderState.DELIVERY_ATTEMPT_FAILED;
    const notes = command.failureNotes?.trim();
    const reasonLabel = formatFailureReason(command.failureReason);
    const transitionReason = notes
      ? `Delivery failed: ${reasonLabel} — ${notes}`
      : `Delivery failed: ${reasonLabel}`;

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
        data: { state: OrderState.DELIVERY_ATTEMPT_FAILED },
      });

      await tx.orderTransition.create({
        data: {
          tenantId: ctx.tenantId,
          orderId: order.id,
          fromState: order.state,
          toState: OrderState.DELIVERY_ATTEMPT_FAILED,
          reason: transitionReason,
          byUserId: command.issuedBy,
        },
      });

      return { id: order.id as OrderId, state: targetState };
    });
  }
}

function formatFailureReason(reason: DeliveryFailureReason): string {
  return reason.toLowerCase().replaceAll('_', ' ');
}

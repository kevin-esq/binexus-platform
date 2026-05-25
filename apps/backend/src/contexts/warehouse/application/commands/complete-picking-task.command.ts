import { DomainEventName } from '@binexus/events';
import { type CompletePickingTaskResult } from '@binexus/types';
import { type UserId } from '@binexus/types';
import { BadRequestException, Inject, Injectable, NotFoundException } from '@nestjs/common';
import { CommandHandler } from '@nestjs/cqrs';
import { PickingTaskStatus } from '@prisma/client';

import { AppCommand, type AppCommandMetadata } from '../../../../common/commands/app-command';
import { AppCommandHandler } from '../../../../common/commands/app-command-handler';
import { EventBusService } from '../../../../common/events/event-bus.service';
import { OutboxService } from '../../../../common/events/outbox.service';
import { PrismaService } from '../../../../common/prisma/prisma.service';
import { TenantContextService } from '../../../../common/tenant/tenant-context.service';
import { toPickingTaskSummary } from '../picking-task-summary';

export class CompletePickingTaskCommand extends AppCommand<CompletePickingTaskResult> {
  constructor(
    readonly pickingTaskId: string,
    readonly issuedBy: UserId,
    metadata?: AppCommandMetadata,
  ) {
    super(metadata);
  }

  validate(): void {
    if (!this.pickingTaskId.trim()) {
      throw new BadRequestException('pickingTaskId is required.');
    }
  }
}

@Injectable()
@CommandHandler(CompletePickingTaskCommand)
export class CompletePickingTaskHandler extends AppCommandHandler<CompletePickingTaskCommand> {
  constructor(
    @Inject(PrismaService)
    private readonly prisma: PrismaService,
    @Inject(TenantContextService)
    private readonly tenantContext: TenantContextService,
    @Inject(EventBusService)
    private readonly eventBus: EventBusService,
    @Inject(OutboxService)
    private readonly outbox: OutboxService,
  ) {
    super();
  }

  async execute(command: CompletePickingTaskCommand): Promise<CompletePickingTaskResult> {
    const ctx = this.tenantContext.current();
    const now = new Date();

    return this.prisma.$transaction(async (tx) => {
      const task = await tx.pickingTask.findFirst({
        where: { id: command.pickingTaskId, tenantId: ctx.tenantId },
        include: { lines: true },
      });

      if (!task) {
        throw new NotFoundException(`Picking task ${command.pickingTaskId} not found`);
      }

      if (task.status !== PickingTaskStatus.PENDING) {
        throw new BadRequestException(
          `Picking task ${command.pickingTaskId} is not pending (status=${task.status})`,
        );
      }

      for (const line of task.lines) {
        await tx.pickingLine.update({
          where: { id: line.id },
          data: { pickedQuantity: line.quantity },
        });
      }

      const updated = await tx.pickingTask.update({
        where: { id: task.id },
        data: {
          status: PickingTaskStatus.COMPLETED,
          completedByUserId: command.issuedBy,
          completedAt: now,
        },
      });

      const event = this.eventBus.build(
        DomainEventName.PICKING_COMPLETED,
        {
          orderId: task.orderId,
          pickingTaskId: task.id,
          completedBy: command.issuedBy,
        },
        {
          correlationId: command.correlationId,
          causationId: command.causationId ?? command.commandId,
        },
      );

      await this.outbox.record(event, tx);

      return {
        pickingTask: toPickingTaskSummary(updated, task.lines.length),
      };
    });
  }
}

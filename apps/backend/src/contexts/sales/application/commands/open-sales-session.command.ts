import { DomainEventName } from '@binexus/events';
import { type BranchId, type OpenSalesSessionResult, type UserId } from '@binexus/types';
import { BadRequestException, ConflictException, Inject, Injectable } from '@nestjs/common';
import { CommandHandler } from '@nestjs/cqrs';
import { SalesSessionStatus } from '@prisma/client';

import { AppCommand, type AppCommandMetadata } from '../../../../common/commands/app-command';
import { AppCommandHandler } from '../../../../common/commands/app-command-handler';
import { EventBusService } from '../../../../common/events/event-bus.service';
import { OutboxService } from '../../../../common/events/outbox.service';
import { PrismaService } from '../../../../common/prisma/prisma.service';
import { TenantContextService } from '../../../../common/tenant/tenant-context.service';
import { toSalesSessionSummary } from '../sales-session-summary';

const MIN_TERMINAL_LENGTH = 1;
const MAX_TERMINAL_LENGTH = 50;

export class OpenSalesSessionCommand extends AppCommand<OpenSalesSessionResult> {
  constructor(
    readonly branchId: BranchId | undefined,
    readonly terminalId: string,
    readonly openingFloatCents: number,
    readonly currency: string,
    readonly issuedBy: UserId,
    metadata?: AppCommandMetadata,
  ) {
    super(metadata);
  }

  validate(): void {
    const terminal = this.terminalId.trim();
    if (terminal.length < MIN_TERMINAL_LENGTH || terminal.length > MAX_TERMINAL_LENGTH) {
      throw new BadRequestException(
        `terminalId must be between ${MIN_TERMINAL_LENGTH} and ${MAX_TERMINAL_LENGTH} characters.`,
      );
    }
    if (!Number.isInteger(this.openingFloatCents) || this.openingFloatCents < 0) {
      throw new BadRequestException('openingFloatCents must be a non-negative integer.');
    }
    if (!/^[A-Z]{3}$/.test(this.currency)) {
      throw new BadRequestException('currency must be a 3-letter ISO code.');
    }
  }
}

@Injectable()
@CommandHandler(OpenSalesSessionCommand)
export class OpenSalesSessionHandler extends AppCommandHandler<OpenSalesSessionCommand> {
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

  async execute(command: OpenSalesSessionCommand): Promise<OpenSalesSessionResult> {
    const ctx = this.tenantContext.current();
    const branchId = command.branchId ?? (ctx.branchId as BranchId | null);
    const terminalId = command.terminalId.trim();

    if (!branchId) {
      throw new BadRequestException('branchId is required to open a sales session.');
    }

    return this.prisma.$transaction(async (tx) => {
      const branch = await tx.branch.findFirst({
        where: { id: branchId, tenantId: ctx.tenantId },
        select: { id: true },
      });

      if (!branch) {
        throw new BadRequestException('branchId does not belong to the current tenant.');
      }

      const existingOpen = await tx.salesSession.findFirst({
        where: {
          tenantId: ctx.tenantId,
          branchId,
          terminalId,
          status: SalesSessionStatus.OPEN,
        },
        select: { id: true },
      });

      if (existingOpen) {
        throw new ConflictException(
          `Terminal ${terminalId} already has an OPEN sales session on this branch.`,
        );
      }

      const session = await tx.salesSession.create({
        data: {
          tenantId: ctx.tenantId,
          branchId,
          terminalId,
          status: SalesSessionStatus.OPEN,
          openingFloatCents: command.openingFloatCents,
          currency: command.currency,
          openedByUserId: command.issuedBy,
        },
      });

      const event = this.eventBus.build(
        DomainEventName.SALES_SESSION_OPENED,
        {
          sessionId: session.id,
          branchId,
          terminalId,
          openingFloatCents: command.openingFloatCents,
          currency: command.currency,
          openedBy: command.issuedBy,
        },
        {
          correlationId: command.correlationId,
          causationId: command.causationId ?? command.commandId,
        },
      );

      await this.outbox.record(event, tx);

      return { session: toSalesSessionSummary(session) };
    });
  }
}

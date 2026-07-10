import { DomainEventName } from '@binexus/events';
import {
  type CloseSalesSessionInput,
  type CloseSalesSessionResult,
  type UserId,
} from '@binexus/types';
import { BadRequestException, Inject, Injectable, NotFoundException } from '@nestjs/common';
import { CommandHandler } from '@nestjs/cqrs';
import { type Role, SalesSessionStatus } from '@prisma/client';

import { assertCashDiscrepancyCloseAllowed } from '../../../../common/cash-reconciliation/assert-cash-discrepancy-close-allowed';
import { AppCommand, type AppCommandMetadata } from '../../../../common/commands/app-command';
import { AppCommandHandler } from '../../../../common/commands/app-command-handler';
import { EventBusService } from '../../../../common/events/event-bus.service';
import { OutboxService } from '../../../../common/events/outbox.service';
import { PrismaService } from '../../../../common/prisma/prisma.service';
import { TenantContextService } from '../../../../common/tenant/tenant-context.service';
import { toSalesSessionSummary } from '../sales-session-summary';
import { computeSessionCashExpected } from '../session-cash-expected';

export class CloseSalesSessionCommand extends AppCommand<CloseSalesSessionResult> {
  constructor(
    readonly sessionId: string,
    readonly input: CloseSalesSessionInput,
    readonly issuedBy: UserId,
    metadata?: AppCommandMetadata,
  ) {
    super(metadata);
  }

  validate(): void {
    if (!this.sessionId.trim()) {
      throw new BadRequestException('sessionId is required.');
    }
    if (!Number.isInteger(this.input.declaredClosingCents) || this.input.declaredClosingCents < 0) {
      throw new BadRequestException('declaredClosingCents must be a non-negative integer.');
    }
  }
}

@Injectable()
@CommandHandler(CloseSalesSessionCommand)
export class CloseSalesSessionHandler extends AppCommandHandler<CloseSalesSessionCommand> {
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

  async execute(command: CloseSalesSessionCommand): Promise<CloseSalesSessionResult> {
    const ctx = this.tenantContext.current();
    const notes = command.input.notes?.trim() || undefined;
    const discrepancyReason = command.input.discrepancyReason?.trim() || undefined;

    return this.prisma.$transaction(async (tx) => {
      const session = await tx.salesSession.findFirst({
        where: { id: command.sessionId, tenantId: ctx.tenantId },
      });

      if (!session) {
        throw new NotFoundException(`Sales session ${command.sessionId} not found`);
      }

      if (session.status !== SalesSessionStatus.OPEN) {
        throw new BadRequestException('Sales session is already closed.');
      }

      let expected: Awaited<ReturnType<typeof computeSessionCashExpected>>;
      try {
        expected = await computeSessionCashExpected(tx, session.id, ctx.tenantId);
      } catch (err) {
        if (err instanceof Error && err.message === 'SESSION_CASH_CURRENCY_MISMATCH') {
          throw new BadRequestException('Cash payments in this session use multiple currencies.');
        }
        throw err;
      }

      const expectedCents = expected.expectedCents;
      const declaredCents = command.input.declaredClosingCents;
      const discrepancyCents = declaredCents - expectedCents;
      const hasDiscrepancy = discrepancyCents !== 0;

      assertCashDiscrepancyCloseAllowed(hasDiscrepancy, ctx.role as Role, discrepancyReason);

      const closedAt = new Date();
      const updated = await tx.salesSession.update({
        where: { id: session.id },
        data: {
          status: SalesSessionStatus.CLOSED,
          closedByUserId: command.issuedBy,
          closedAt,
          expectedClosingCents: expectedCents,
          declaredClosingCents: declaredCents,
          discrepancyCents,
          discrepancyReason: hasDiscrepancy ? discrepancyReason : null,
          closeNotes: notes ?? null,
        },
      });

      const event = this.eventBus.build(
        DomainEventName.SALES_SESSION_CLOSED,
        {
          sessionId: session.id,
          branchId: session.branchId,
          terminalId: session.terminalId,
          expectedClosingCents: expectedCents,
          declaredClosingCents: declaredCents,
          discrepancyCents,
          currency: session.currency,
          closedBy: command.issuedBy,
        },
        {
          correlationId: command.correlationId,
          causationId: command.causationId ?? command.commandId,
        },
      );

      await this.outbox.record(event, tx);

      return { session: toSalesSessionSummary(updated) };
    });
  }
}

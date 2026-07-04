import { DomainEventName } from '@binexus/events';
import {
  type LiquidateDeliveryRouteInput,
  type LiquidateDeliveryRouteResult,
  type UserId,
} from '@binexus/types';
import {
  BadRequestException,
  ConflictException,
  ForbiddenException,
  Inject,
  Injectable,
  NotFoundException,
} from '@nestjs/common';
import { CommandHandler } from '@nestjs/cqrs';
import { DeliveryRouteStatus as PrismaRouteStatus, Role } from '@prisma/client';

import { AppCommand, type AppCommandMetadata } from '../../../../common/commands/app-command';
import { AppCommandHandler } from '../../../../common/commands/app-command-handler';
import { EventBusService } from '../../../../common/events/event-bus.service';
import { OutboxService } from '../../../../common/events/outbox.service';
import { PrismaService } from '../../../../common/prisma/prisma.service';
import { TenantContextService } from '../../../../common/tenant/tenant-context.service';
import { toLiquidateDeliveryRouteResult } from '../delivery-route-liquidation-summary';
import { computeRouteCodExpected } from '../route-cod-expected';

const SUPERVISOR_ROLES: readonly Role[] = [Role.ADMIN, Role.SUPER_ADMIN];

export class LiquidateDeliveryRouteCommand extends AppCommand<LiquidateDeliveryRouteResult> {
  constructor(
    readonly deliveryRouteId: string,
    readonly input: LiquidateDeliveryRouteInput,
    readonly issuedBy: UserId,
    metadata?: AppCommandMetadata,
  ) {
    super(metadata);
  }

  validate(): void {
    if (!this.deliveryRouteId.trim()) {
      throw new BadRequestException('deliveryRouteId is required.');
    }
    if (!Number.isInteger(this.input.declaredCents) || this.input.declaredCents < 0) {
      throw new BadRequestException('declaredCents must be a non-negative integer.');
    }
  }
}

@Injectable()
@CommandHandler(LiquidateDeliveryRouteCommand)
export class LiquidateDeliveryRouteHandler extends AppCommandHandler<LiquidateDeliveryRouteCommand> {
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

  async execute(command: LiquidateDeliveryRouteCommand): Promise<LiquidateDeliveryRouteResult> {
    const ctx = this.tenantContext.current();
    const notes = command.input.notes?.trim() || undefined;
    const discrepancyReason = command.input.discrepancyReason?.trim() || undefined;

    return this.prisma.$transaction(async (tx) => {
      const route = await tx.deliveryRoute.findFirst({
        where: { id: command.deliveryRouteId, tenantId: ctx.tenantId },
        include: { liquidation: true },
      });

      if (!route) {
        throw new NotFoundException(`Delivery route ${command.deliveryRouteId} not found`);
      }

      if (route.status !== PrismaRouteStatus.COMPLETED) {
        throw new BadRequestException(
          `Delivery route must be COMPLETED to liquidate (current: ${route.status})`,
        );
      }

      if (route.liquidation) {
        throw new ConflictException(
          `Delivery route ${command.deliveryRouteId} is already liquidated`,
        );
      }

      let codExpected: Awaited<ReturnType<typeof computeRouteCodExpected>>;
      try {
        codExpected = await computeRouteCodExpected(tx, route.id, ctx.tenantId);
      } catch (err) {
        if (err instanceof Error && err.message === 'ROUTE_COD_CURRENCY_MISMATCH') {
          throw new BadRequestException('COD orders on this route use multiple currencies.');
        }
        throw err;
      }

      const { expectedCents, currency, cashOrderIds, stops: codStops } = codExpected;
      const declaredCents = command.input.declaredCents;
      const discrepancyCents = declaredCents - expectedCents;
      const hasDiscrepancy = discrepancyCents !== 0;

      if (hasDiscrepancy) {
        if (!SUPERVISOR_ROLES.includes(ctx.role as Role)) {
          throw new ForbiddenException(
            'Closing a liquidation with a cash discrepancy requires ADMIN or SUPER_ADMIN role.',
          );
        }
        if (!discrepancyReason) {
          throw new BadRequestException(
            'discrepancyReason is required when declaredCents does not match expectedCents.',
          );
        }
        if (!command.input.lines || command.input.lines.length === 0) {
          throw new BadRequestException(
            'lines are required when declaredCents does not match expectedCents.',
          );
        }

        const codStopIds = new Set(codStops.map((stop) => stop.stopId));
        if (command.input.lines.length !== codStops.length) {
          throw new BadRequestException(
            'lines must include every delivered COD stop when there is a discrepancy.',
          );
        }

        let lineSum = 0;
        const seenStops = new Set<string>();
        for (const line of command.input.lines) {
          if (!codStopIds.has(line.deliveryRouteStopId)) {
            throw new BadRequestException(
              `Stop ${line.deliveryRouteStopId} is not a delivered COD stop on this route.`,
            );
          }
          if (seenStops.has(line.deliveryRouteStopId)) {
            throw new BadRequestException(`Duplicate line for stop ${line.deliveryRouteStopId}.`);
          }
          seenStops.add(line.deliveryRouteStopId);
          if (!Number.isInteger(line.declaredCents) || line.declaredCents < 0) {
            throw new BadRequestException(
              'Each line declaredCents must be a non-negative integer.',
            );
          }
          lineSum += line.declaredCents;
        }

        if (lineSum !== declaredCents) {
          throw new BadRequestException('Sum of line declaredCents must equal declaredCents.');
        }

        for (const codStop of codStops) {
          const line = command.input.lines.find(
            (entry) => entry.deliveryRouteStopId === codStop.stopId,
          );
          if (!line) {
            throw new BadRequestException(`Missing line for COD stop ${codStop.stopId}.`);
          }
        }
      } else if (command.input.lines && command.input.lines.length > 0) {
        throw new BadRequestException(
          'lines must not be sent when declaredCents matches expectedCents.',
        );
      }

      const closedAt = new Date();

      const liquidation = await tx.deliveryRouteLiquidation.create({
        data: {
          tenantId: ctx.tenantId,
          deliveryRouteId: route.id,
          expectedCents,
          declaredCents,
          discrepancyCents,
          currency,
          closedByUserId: command.issuedBy,
          closedAt,
          discrepancyReason: hasDiscrepancy ? discrepancyReason : null,
          notes,
          lines: hasDiscrepancy
            ? {
                create: command.input.lines!.map((line) => {
                  const codStop = codStops.find(
                    (stop) => stop.stopId === line.deliveryRouteStopId,
                  )!;
                  return {
                    tenantId: ctx.tenantId,
                    deliveryRouteStopId: line.deliveryRouteStopId,
                    orderId: codStop.orderId,
                    expectedCents: codStop.expectedCents,
                    declaredCents: line.declaredCents,
                  };
                }),
              }
            : undefined,
        },
        include: { lines: true },
      });

      const event = this.eventBus.build(
        DomainEventName.DELIVERY_ROUTE_LIQUIDATED,
        {
          deliveryRouteId: route.id,
          liquidationId: liquidation.id,
          branchId: route.branchId,
          expectedCents,
          declaredCents,
          discrepancyCents,
          currency,
          cashOrderIds,
          liquidatedBy: command.issuedBy,
          liquidatedAt: closedAt.toISOString(),
          discrepancyReason: hasDiscrepancy ? discrepancyReason : undefined,
        },
        {
          correlationId: command.correlationId,
          causationId: command.causationId ?? command.commandId,
        },
      );

      await this.outbox.record(event, tx);

      return toLiquidateDeliveryRouteResult(route.id, liquidation);
    });
  }
}

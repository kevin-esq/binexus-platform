import { type AdjustStockResult, type StockItemSummary } from '@binexus/types';
import { type BranchId, type UserId } from '@binexus/types';
import { BadRequestException, Inject, Injectable } from '@nestjs/common';
import { CommandHandler } from '@nestjs/cqrs';
import { StockMovementType, type StockItem } from '@prisma/client';

import { AppCommand, type AppCommandMetadata } from '../../../../common/commands/app-command';
import { AppCommandHandler } from '../../../../common/commands/app-command-handler';
import { PrismaService } from '../../../../common/prisma/prisma.service';
import { TenantContextService } from '../../../../common/tenant/tenant-context.service';

const MIN_REASON_LENGTH = 3;
const MAX_REASON_LENGTH = 200;

export class AdjustStockCommand extends AppCommand<AdjustStockResult> {
  constructor(
    readonly branchId: BranchId,
    readonly productId: string,
    readonly delta: number,
    readonly reason: string,
    readonly issuedBy: UserId,
    metadata?: AppCommandMetadata,
  ) {
    super(metadata);
  }

  validate(): void {
    if (!this.branchId.trim()) {
      throw new BadRequestException('branchId is required.');
    }
    if (!this.productId.trim()) {
      throw new BadRequestException('productId is required.');
    }
    if (!Number.isInteger(this.delta) || this.delta === 0) {
      throw new BadRequestException('delta must be a non-zero integer.');
    }
    const trimmedReason = this.reason.trim();
    if (trimmedReason.length < MIN_REASON_LENGTH) {
      throw new BadRequestException(`reason must be at least ${MIN_REASON_LENGTH} characters.`);
    }
    if (trimmedReason.length > MAX_REASON_LENGTH) {
      throw new BadRequestException(`reason must be at most ${MAX_REASON_LENGTH} characters.`);
    }
  }
}

@Injectable()
@CommandHandler(AdjustStockCommand)
export class AdjustStockHandler extends AppCommandHandler<AdjustStockCommand> {
  constructor(
    @Inject(PrismaService)
    private readonly prisma: PrismaService,
    @Inject(TenantContextService)
    private readonly tenantContext: TenantContextService,
  ) {
    super();
  }

  async execute(command: AdjustStockCommand): Promise<AdjustStockResult> {
    const ctx = this.tenantContext.current();

    return this.prisma.$transaction(async (tx) => {
      const existing = await tx.stockItem.findFirst({
        where: {
          tenantId: ctx.tenantId,
          branchId: command.branchId,
          productId: command.productId,
        },
      });

      if (!existing) {
        if (command.delta < 0) {
          throw new BadRequestException(
            `No stock item for product ${command.productId} at branch ${command.branchId}.`,
          );
        }

        const created = await tx.stockItem.create({
          data: {
            tenantId: ctx.tenantId,
            branchId: command.branchId,
            productId: command.productId,
            onHand: command.delta,
            reserved: 0,
          },
        });

        const movement = await tx.stockMovement.create({
          data: {
            tenantId: ctx.tenantId,
            branchId: command.branchId,
            productId: command.productId,
            type: StockMovementType.ADJUSTMENT,
            quantity: command.delta,
            correlationId: command.correlationId ?? null,
            causationId: command.causationId ?? command.commandId,
          },
        });

        return {
          stockItem: this.toSummary(created),
          movementId: movement.id,
        };
      }

      const nextOnHand = existing.onHand + command.delta;
      if (nextOnHand < 0) {
        throw new BadRequestException('Adjustment would make onHand negative.');
      }
      if (nextOnHand < existing.reserved) {
        throw new BadRequestException(
          'Adjustment would leave available quantity negative against active reservations.',
        );
      }

      const updated = await tx.stockItem.update({
        where: { id: existing.id },
        data: { onHand: nextOnHand },
      });

      const movement = await tx.stockMovement.create({
        data: {
          tenantId: ctx.tenantId,
          branchId: command.branchId,
          productId: command.productId,
          type: StockMovementType.ADJUSTMENT,
          quantity: command.delta,
          correlationId: command.correlationId ?? null,
          causationId: command.causationId ?? command.commandId,
        },
      });

      return {
        stockItem: this.toSummary(updated),
        movementId: movement.id,
      };
    });
  }

  private toSummary(row: StockItem): StockItemSummary {
    return {
      id: row.id,
      branchId: row.branchId as StockItemSummary['branchId'],
      productId: row.productId,
      onHand: row.onHand,
      reserved: row.reserved,
      available: row.onHand - row.reserved,
      createdAt: row.createdAt.toISOString(),
      updatedAt: row.updatedAt.toISOString(),
    };
  }
}

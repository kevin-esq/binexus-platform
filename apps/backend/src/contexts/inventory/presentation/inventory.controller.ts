import {
  type AdjustStockResult,
  type BranchId,
  type CancelStockTransferResult,
  type CreateStockTransferResult,
  type ListStockItemsResult,
  type ListStockTransfersResult,
  type ReceiveStockTransferResult,
  type StockTransferStatus,
  type UserId,
} from '@binexus/types';
import {
  Body,
  Controller,
  Get,
  Headers,
  Inject,
  Param,
  Post,
  Query,
  UnauthorizedException,
} from '@nestjs/common';
import { Type } from 'class-transformer';
import {
  IsIn,
  IsInt,
  IsNotEmpty,
  IsOptional,
  IsString,
  MaxLength,
  Min,
  MinLength,
} from 'class-validator';

import { AppCommandBus } from '../../../common/commands/command-bus.service';
import { CurrentUser, type RequestUser } from '../../../common/decorators/current-user.decorator';
import { AdjustStockCommand } from '../application/commands/adjust-stock.command';
import { CancelStockTransferCommand } from '../application/commands/cancel-stock-transfer.command';
import { CreateStockTransferCommand } from '../application/commands/create-stock-transfer.command';
import { ReceiveStockTransferCommand } from '../application/commands/receive-stock-transfer.command';
import { InventoryReadService } from '../application/inventory-read.service';

class AdjustStockDto {
  @IsString()
  @IsNotEmpty()
  branchId!: string;

  @IsString()
  @IsNotEmpty()
  productId!: string;

  @IsInt()
  delta!: number;

  @IsString()
  @MinLength(3)
  @MaxLength(200)
  reason!: string;
}

class CreateStockTransferDto {
  @IsString()
  @IsNotEmpty()
  sourceBranchId!: string;

  @IsString()
  @IsNotEmpty()
  destinationBranchId!: string;

  @IsString()
  @IsNotEmpty()
  productId!: string;

  @IsInt()
  @Min(1)
  quantity!: number;

  @IsOptional()
  @IsString()
  @MaxLength(200)
  reason?: string;
}

class ListStockQueryDto {
  @IsOptional()
  @IsString()
  branchId?: string;

  @IsOptional()
  @IsString()
  productId?: string;

  @IsOptional()
  @IsInt()
  @Min(1)
  @Type(() => Number)
  limit?: number;

  @IsOptional()
  @IsString()
  cursor?: string;
}

class ListStockTransfersQueryDto {
  @IsOptional()
  @IsIn(['PENDING', 'RECEIVED', 'CANCELLED'])
  status?: StockTransferStatus;

  @IsOptional()
  @IsInt()
  @Min(1)
  @Type(() => Number)
  limit?: number;

  @IsOptional()
  @IsString()
  cursor?: string;
}

@Controller('inventory')
export class InventoryController {
  constructor(
    @Inject(InventoryReadService) private readonly inventoryRead: InventoryReadService,
    @Inject(AppCommandBus) private readonly commandBus: AppCommandBus,
  ) {}

  @Post('stock/adjust')
  async adjustStock(
    @Body() dto: AdjustStockDto,
    @CurrentUser() user: RequestUser | null,
    @Headers('idempotency-key') idempotencyKey?: string,
    @Headers('x-correlation-id') correlationId?: string,
  ): Promise<AdjustStockResult> {
    if (!user) throw new UnauthorizedException();

    return this.commandBus.execute(
      new AdjustStockCommand(
        dto.branchId as BranchId,
        dto.productId,
        dto.delta,
        dto.reason,
        user.userId as UserId,
        { commandId: idempotencyKey, correlationId },
      ),
    );
  }

  @Post('stock/transfers')
  async createStockTransfer(
    @Body() dto: CreateStockTransferDto,
    @CurrentUser() user: RequestUser | null,
    @Headers('idempotency-key') idempotencyKey?: string,
    @Headers('x-correlation-id') correlationId?: string,
  ): Promise<CreateStockTransferResult> {
    if (!user) throw new UnauthorizedException();

    return this.commandBus.execute(
      new CreateStockTransferCommand(
        dto.sourceBranchId as BranchId,
        dto.destinationBranchId as BranchId,
        dto.productId,
        dto.quantity,
        user.userId as UserId,
        dto.reason,
        { commandId: idempotencyKey, correlationId },
      ),
    );
  }

  @Post('stock/transfers/:id/receive')
  async receiveStockTransfer(
    @Param('id') id: string,
    @CurrentUser() user: RequestUser | null,
    @Headers('idempotency-key') idempotencyKey?: string,
    @Headers('x-correlation-id') correlationId?: string,
  ): Promise<ReceiveStockTransferResult> {
    if (!user) throw new UnauthorizedException();

    return this.commandBus.execute(
      new ReceiveStockTransferCommand(id, user.userId as UserId, {
        commandId: idempotencyKey,
        correlationId,
      }),
    );
  }

  @Post('stock/transfers/:id/cancel')
  async cancelStockTransfer(
    @Param('id') id: string,
    @CurrentUser() user: RequestUser | null,
    @Headers('idempotency-key') idempotencyKey?: string,
    @Headers('x-correlation-id') correlationId?: string,
  ): Promise<CancelStockTransferResult> {
    if (!user) throw new UnauthorizedException();

    return this.commandBus.execute(
      new CancelStockTransferCommand(id, user.userId as UserId, {
        commandId: idempotencyKey,
        correlationId,
      }),
    );
  }

  @Get('stock/transfers')
  listStockTransfers(
    @Query() query: ListStockTransfersQueryDto,
  ): Promise<ListStockTransfersResult> {
    return this.inventoryRead.listStockTransfers({
      status: query.status,
      limit: query.limit,
      cursor: query.cursor,
    });
  }

  @Get('stock')
  listStock(@Query() query: ListStockQueryDto): Promise<ListStockItemsResult> {
    return this.inventoryRead.listStockItems({
      branchId: query.branchId,
      productId: query.productId,
      limit: query.limit,
      cursor: query.cursor,
    });
  }
}

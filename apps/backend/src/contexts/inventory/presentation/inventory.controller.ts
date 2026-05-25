import {
  type AdjustStockResult,
  type BranchId,
  type ListStockItemsResult,
  type UserId,
} from '@binexus/types';
import {
  Body,
  Controller,
  Get,
  Headers,
  Inject,
  Post,
  Query,
  UnauthorizedException,
} from '@nestjs/common';
import { Type } from 'class-transformer';
import {
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

import { type ListStockItemsResult } from '@binexus/types';
import { Controller, Get, Inject, Query } from '@nestjs/common';
import { Type } from 'class-transformer';
import { IsInt, IsOptional, IsString, Min } from 'class-validator';

import { InventoryReadService } from '../application/inventory-read.service';

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
  constructor(@Inject(InventoryReadService) private readonly inventoryRead: InventoryReadService) {}

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

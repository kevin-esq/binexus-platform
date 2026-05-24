import {
  type BranchId,
  type ListOrdersResult,
  type OrderDetail,
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
  ArrayMinSize,
  IsArray,
  IsInt,
  IsNotEmpty,
  IsOptional,
  IsString,
  Length,
  Min,
  ValidateNested,
} from 'class-validator';

import { AppCommandBus } from '../../../common/commands/command-bus.service';
import { CurrentUser, type RequestUser } from '../../../common/decorators/current-user.decorator';
import { CreateOrderCommand } from '../application/commands/create-order.command';
import type { CreateOrderInput } from '../application/commands/create-order.command';
import { OrdersReadService } from '../application/orders-read.service';

class CreateOrderLineDto {
  @IsString()
  @IsNotEmpty()
  productId!: string;

  @IsString()
  @IsNotEmpty()
  productName!: string;

  @IsInt()
  @Min(1)
  quantity!: number;

  @IsInt()
  @Min(0)
  unitPriceCents!: number;
}

class CreateOrderDto {
  @IsString()
  @IsNotEmpty()
  customerId!: string;

  @IsString()
  @IsOptional()
  branchId?: string;

  @IsString()
  @Length(3, 3)
  currency!: string;

  @IsArray()
  @ArrayMinSize(1)
  @ValidateNested({ each: true })
  @Type(() => CreateOrderLineDto)
  lines!: CreateOrderLineDto[];
}

class ListOrdersQueryDto {
  @IsOptional()
  @IsInt()
  @Min(1)
  @Type(() => Number)
  limit?: number;

  @IsOptional()
  @IsString()
  cursor?: string;
}

@Controller('orders')
export class OrdersController {
  constructor(
    @Inject(AppCommandBus) private readonly commandBus: AppCommandBus,
    @Inject(OrdersReadService) private readonly ordersRead: OrdersReadService,
  ) {}

  @Get()
  list(@Query() query: ListOrdersQueryDto): Promise<ListOrdersResult> {
    return this.ordersRead.listOrders({
      limit: query.limit,
      cursor: query.cursor,
    });
  }

  @Get(':id')
  getById(@Param('id') id: string): Promise<OrderDetail> {
    return this.ordersRead.getOrderById(id);
  }

  @Post()
  async create(
    @Body() dto: CreateOrderDto,
    @CurrentUser() user: RequestUser | null,
    @Headers('idempotency-key') idempotencyKey?: string,
    @Headers('x-correlation-id') correlationId?: string,
  ): Promise<{ id: string }> {
    if (!user) throw new UnauthorizedException();

    const input: CreateOrderInput = {
      customerId: dto.customerId,
      branchId: dto.branchId as BranchId | undefined,
      currency: dto.currency.toUpperCase(),
      lines: dto.lines,
    };

    const id = await this.commandBus.execute(
      new CreateOrderCommand(input, user.userId as UserId, {
        commandId: idempotencyKey,
        correlationId,
      }),
    );

    return { id };
  }
}

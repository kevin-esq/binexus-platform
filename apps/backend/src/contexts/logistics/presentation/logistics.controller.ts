import {
  type AssignOrderToDeliveryRouteResult,
  type BranchId,
  type ConfirmDeliveryResult,
  type CreateDeliveryRouteResult,
  type DispatchDeliveryRouteResult,
  type ListDeliveryRouteStopsResult,
  type DeliveryRouteCandidateStatus,
  type DeliveryRouteStatus,
  type ListDeliveryRouteCandidatesResult,
  type ListDeliveryRoutesResult,
  type OrderId,
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
import { IsArray, IsIn, IsInt, IsOptional, IsString, Min } from 'class-validator';

import { AppCommandBus } from '../../../common/commands/command-bus.service';
import { CurrentUser, type RequestUser } from '../../../common/decorators/current-user.decorator';
import { AssignOrderToDeliveryRouteCommand } from '../application/commands/assign-order-to-delivery-route.command';
import { ConfirmDeliveryCommand } from '../application/commands/confirm-delivery.command';
import { CreateDeliveryRouteCommand } from '../application/commands/create-delivery-route.command';
import { DispatchDeliveryRouteCommand } from '../application/commands/dispatch-delivery-route.command';
import { LogisticsReadService } from '../application/logistics-read.service';

class ListDeliveryRouteCandidatesQueryDto {
  @IsOptional()
  @IsIn(['READY', 'ASSIGNED', 'CANCELLED'])
  status?: DeliveryRouteCandidateStatus;

  @IsOptional()
  @IsString()
  branchId?: string;

  @IsOptional()
  @IsInt()
  @Min(1)
  @Type(() => Number)
  limit?: number;

  @IsOptional()
  @IsString()
  cursor?: string;
}

class ListDeliveryRoutesQueryDto {
  @IsOptional()
  @IsIn(['PLANNED', 'DISPATCHED', 'COMPLETED', 'CANCELLED'])
  status?: DeliveryRouteStatus;

  @IsOptional()
  @IsString()
  branchId?: string;

  @IsOptional()
  @IsInt()
  @Min(1)
  @Type(() => Number)
  limit?: number;

  @IsOptional()
  @IsString()
  cursor?: string;
}

class CreateDeliveryRouteDto {
  @IsString()
  branchId!: string;

  @IsOptional()
  @IsString()
  driverUserId?: string;

  @IsOptional()
  @IsString()
  plannedDate?: string;
}

class AssignOrdersToDeliveryRouteDto {
  @IsArray()
  @IsString({ each: true })
  orderIds!: string[];
}

class DispatchDeliveryRouteDto {
  @IsOptional()
  @IsString()
  driverUserId?: string;
}

@Controller('logistics')
export class LogisticsController {
  constructor(
    @Inject(LogisticsReadService) private readonly logisticsRead: LogisticsReadService,
    @Inject(AppCommandBus) private readonly commandBus: AppCommandBus,
  ) {}

  @Get('delivery-route-candidates')
  listDeliveryRouteCandidates(
    @Query() query: ListDeliveryRouteCandidatesQueryDto,
  ): Promise<ListDeliveryRouteCandidatesResult> {
    return this.logisticsRead.listDeliveryRouteCandidates({
      status: query.status,
      branchId: query.branchId as BranchId | undefined,
      limit: query.limit,
      cursor: query.cursor,
    });
  }

  @Get('delivery-routes/:id/stops')
  listDeliveryRouteStops(@Param('id') id: string): Promise<ListDeliveryRouteStopsResult> {
    return this.logisticsRead.listDeliveryRouteStops(id);
  }

  @Get('delivery-routes')
  listDeliveryRoutes(
    @Query() query: ListDeliveryRoutesQueryDto,
  ): Promise<ListDeliveryRoutesResult> {
    return this.logisticsRead.listDeliveryRoutes({
      status: query.status,
      branchId: query.branchId as BranchId | undefined,
      limit: query.limit,
      cursor: query.cursor,
    });
  }

  @Post('delivery-routes')
  async createDeliveryRoute(
    @Body() dto: CreateDeliveryRouteDto,
    @CurrentUser() user: RequestUser | null,
    @Headers('idempotency-key') idempotencyKey?: string,
    @Headers('x-correlation-id') correlationId?: string,
  ): Promise<CreateDeliveryRouteResult> {
    if (!user) throw new UnauthorizedException();

    return this.commandBus.execute(
      new CreateDeliveryRouteCommand(
        dto.branchId as BranchId,
        user.userId as UserId,
        dto.driverUserId as UserId | undefined,
        dto.plannedDate,
        { commandId: idempotencyKey, correlationId },
      ),
    );
  }

  @Post('delivery-routes/:id/assign-orders')
  async assignOrdersToDeliveryRoute(
    @Param('id') id: string,
    @Body() dto: AssignOrdersToDeliveryRouteDto,
    @CurrentUser() user: RequestUser | null,
    @Headers('idempotency-key') idempotencyKey?: string,
    @Headers('x-correlation-id') correlationId?: string,
  ): Promise<AssignOrderToDeliveryRouteResult> {
    if (!user) throw new UnauthorizedException();

    return this.commandBus.execute(
      new AssignOrderToDeliveryRouteCommand(id, dto.orderIds as OrderId[], user.userId as UserId, {
        commandId: idempotencyKey,
        correlationId,
      }),
    );
  }

  @Post('delivery-routes/:id/dispatch')
  async dispatchDeliveryRoute(
    @Param('id') id: string,
    @Body() dto: DispatchDeliveryRouteDto,
    @CurrentUser() user: RequestUser | null,
    @Headers('idempotency-key') idempotencyKey?: string,
    @Headers('x-correlation-id') correlationId?: string,
  ): Promise<DispatchDeliveryRouteResult> {
    if (!user) throw new UnauthorizedException();

    return this.commandBus.execute(
      new DispatchDeliveryRouteCommand(
        id,
        user.userId as UserId,
        dto.driverUserId as UserId | undefined,
        { commandId: idempotencyKey, correlationId },
      ),
    );
  }

  @Post('delivery-route-stops/:id/confirm-delivery')
  async confirmDelivery(
    @Param('id') id: string,
    @CurrentUser() user: RequestUser | null,
    @Headers('idempotency-key') idempotencyKey?: string,
    @Headers('x-correlation-id') correlationId?: string,
  ): Promise<ConfirmDeliveryResult> {
    if (!user) throw new UnauthorizedException();

    return this.commandBus.execute(
      new ConfirmDeliveryCommand(id, user.userId as UserId, {
        commandId: idempotencyKey,
        correlationId,
      }),
    );
  }
}

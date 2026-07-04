import {
  type AssignOrderToDeliveryRouteResult,
  type BranchId,
  type ConfirmDeliveryResult,
  type CreateDeliveryProofUploadResult,
  type CreateDeliveryRouteResult,
  type DispatchDeliveryRouteResult,
  type DeliveryFailureReason,
  type ListDeliveryRouteStopsResult,
  type DeliveryRouteCandidateStatus,
  type DeliveryRouteStatus,
  type DeliveryProofUploadKind,
  type ListDeliveryRouteCandidatesResult,
  type ListDeliveryRoutesResult,
  FeatureKey,
  type LiquidateDeliveryRouteResult,
  type OrderId,
  type ReportFailedDeliveryResult,
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
  UseGuards,
} from '@nestjs/common';
import { Role } from '@prisma/client';
import { Type } from 'class-transformer';
import {
  IsArray,
  IsIn,
  IsInt,
  IsNumber,
  IsOptional,
  IsString,
  Max,
  Min,
  ValidateNested,
} from 'class-validator';

import { AppCommandBus } from '../../../common/commands/command-bus.service';
import { CurrentUser, type RequestUser } from '../../../common/decorators/current-user.decorator';
import { RequireFeature } from '../../../common/decorators/require-feature.decorator';
import { Roles } from '../../../common/decorators/roles.decorator';
import { FeatureFlagGuard } from '../../../common/feature-flags/feature-flag.guard';
import { AssignOrderToDeliveryRouteCommand } from '../application/commands/assign-order-to-delivery-route.command';
import { ConfirmDeliveryCommand } from '../application/commands/confirm-delivery.command';
import { CreateDeliveryProofUploadCommand } from '../application/commands/create-delivery-proof-upload.command';
import { CreateDeliveryRouteCommand } from '../application/commands/create-delivery-route.command';
import { DispatchDeliveryRouteCommand } from '../application/commands/dispatch-delivery-route.command';
import { LiquidateDeliveryRouteCommand } from '../application/commands/liquidate-delivery-route.command';
import { ReportFailedDeliveryCommand } from '../application/commands/report-failed-delivery.command';
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

class ConfirmDeliveryProofDto {
  @IsOptional()
  @IsString()
  recipientName?: string;

  @IsOptional()
  @IsString()
  notes?: string;

  @IsOptional()
  @IsString()
  photoObjectKey?: string;

  @IsOptional()
  @IsString()
  signatureObjectKey?: string;

  @IsOptional()
  @IsNumber()
  @Type(() => Number)
  latitude?: number;

  @IsOptional()
  @IsNumber()
  @Type(() => Number)
  longitude?: number;
}

class ConfirmDeliveryDto {
  @IsOptional()
  @ValidateNested()
  @Type(() => ConfirmDeliveryProofDto)
  proof?: ConfirmDeliveryProofDto;
}

class ReportFailedDeliveryDto {
  @IsIn(['NO_RECIPIENT', 'WRONG_ADDRESS', 'REFUSED', 'DAMAGED', 'OTHER'])
  reason!: DeliveryFailureReason;

  @IsOptional()
  @IsString()
  notes?: string;
}

class CreateDeliveryProofUploadDto {
  @IsIn(['PHOTO', 'SIGNATURE'])
  kind!: DeliveryProofUploadKind;

  @IsString()
  contentType!: string;

  @IsInt()
  @Min(1)
  @Max(10_485_760)
  @Type(() => Number)
  sizeBytes!: number;
}

class LiquidateDeliveryRouteLineDto {
  @IsString()
  deliveryRouteStopId!: string;

  @IsInt()
  @Min(0)
  declaredCents!: number;
}

class LiquidateDeliveryRouteDto {
  @IsInt()
  @Min(0)
  declaredCents!: number;

  @IsOptional()
  @IsString()
  notes?: string;

  @IsOptional()
  @IsString()
  discrepancyReason?: string;

  @IsOptional()
  @IsArray()
  @ValidateNested({ each: true })
  @Type(() => LiquidateDeliveryRouteLineDto)
  lines?: LiquidateDeliveryRouteLineDto[];
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

  @Post('delivery-route-stops/:id/proof-uploads')
  async createDeliveryProofUpload(
    @Param('id') id: string,
    @Body() dto: CreateDeliveryProofUploadDto,
    @CurrentUser() user: RequestUser | null,
    @Headers('idempotency-key') idempotencyKey?: string,
    @Headers('x-correlation-id') correlationId?: string,
  ): Promise<CreateDeliveryProofUploadResult> {
    if (!user) throw new UnauthorizedException();

    return this.commandBus.execute(
      new CreateDeliveryProofUploadCommand(id, dto.kind, dto.contentType, dto.sizeBytes, {
        commandId: idempotencyKey,
        correlationId,
      }),
    );
  }

  @Post('delivery-route-stops/:id/confirm-delivery')
  async confirmDelivery(
    @Param('id') id: string,
    @Body() dto: ConfirmDeliveryDto,
    @CurrentUser() user: RequestUser | null,
    @Headers('idempotency-key') idempotencyKey?: string,
    @Headers('x-correlation-id') correlationId?: string,
  ): Promise<ConfirmDeliveryResult> {
    if (!user) throw new UnauthorizedException();

    return this.commandBus.execute(
      new ConfirmDeliveryCommand(id, user.userId as UserId, dto.proof, {
        commandId: idempotencyKey,
        correlationId,
      }),
    );
  }

  @Post('delivery-route-stops/:id/report-failed-delivery')
  async reportFailedDelivery(
    @Param('id') id: string,
    @Body() dto: ReportFailedDeliveryDto,
    @CurrentUser() user: RequestUser | null,
    @Headers('idempotency-key') idempotencyKey?: string,
    @Headers('x-correlation-id') correlationId?: string,
  ): Promise<ReportFailedDeliveryResult> {
    if (!user) throw new UnauthorizedException();

    return this.commandBus.execute(
      new ReportFailedDeliveryCommand(id, user.userId as UserId, dto.reason, dto.notes, {
        commandId: idempotencyKey,
        correlationId,
      }),
    );
  }

  @Post('delivery-routes/:id/liquidate')
  @UseGuards(FeatureFlagGuard)
  @RequireFeature(FeatureKey.LIQUIDATION)
  @Roles(Role.ADMIN, Role.SUPER_ADMIN)
  async liquidateDeliveryRoute(
    @Param('id') id: string,
    @Body() dto: LiquidateDeliveryRouteDto,
    @CurrentUser() user: RequestUser | null,
    @Headers('idempotency-key') idempotencyKey?: string,
    @Headers('x-correlation-id') correlationId?: string,
  ): Promise<LiquidateDeliveryRouteResult> {
    if (!user) throw new UnauthorizedException();

    return this.commandBus.execute(
      new LiquidateDeliveryRouteCommand(
        id,
        {
          declaredCents: dto.declaredCents,
          notes: dto.notes,
          discrepancyReason: dto.discrepancyReason,
          lines: dto.lines,
        },
        user.userId as UserId,
        { commandId: idempotencyKey, correlationId },
      ),
    );
  }
}

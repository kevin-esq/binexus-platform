import {
  type BranchId,
  type CloseSalesSessionResult,
  type CreateSaleResult,
  type GetCurrentSalesSessionResult,
  FeatureKey,
  type OpenSalesSessionResult,
  type SalesSessionSummary,
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
import { RequireFeature } from '../../../common/decorators/require-feature.decorator';
import { Roles } from '../../../common/decorators/roles.decorator';
import { FeatureFlagGuard } from '../../../common/feature-flags/feature-flag.guard';
import { CloseSalesSessionCommand } from '../application/commands/close-sales-session.command';
import { CreateSaleCommand } from '../application/commands/create-sale.command';
import { OpenSalesSessionCommand } from '../application/commands/open-sales-session.command';
import { SalesReadService } from '../application/sales-read.service';

class OpenSalesSessionDto {
  @IsOptional()
  @IsString()
  branchId?: string;

  @IsString()
  @IsNotEmpty()
  terminalId!: string;

  @IsInt()
  @Min(0)
  openingFloatCents!: number;

  @IsOptional()
  @IsString()
  @Length(3, 3)
  currency?: string;
}

class CreateSaleLineDto {
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

class CreateSaleDto {
  @IsArray()
  @ArrayMinSize(1)
  @ValidateNested({ each: true })
  @Type(() => CreateSaleLineDto)
  lines!: CreateSaleLineDto[];

  @IsOptional()
  @IsString()
  @Length(3, 3)
  currency?: string;
}

class CloseSalesSessionDto {
  @IsInt()
  @Min(0)
  declaredClosingCents!: number;

  @IsOptional()
  @IsString()
  notes?: string;

  @IsOptional()
  @IsString()
  discrepancyReason?: string;
}

class GetCurrentSalesSessionQueryDto {
  @IsString()
  @IsNotEmpty()
  terminalId!: string;

  @IsOptional()
  @IsString()
  branchId?: string;
}

@Controller('sales')
@UseGuards(FeatureFlagGuard)
@RequireFeature(FeatureKey.POS_RETAIL)
@Roles(Role.CASHIER, Role.ADMIN, Role.SUPER_ADMIN)
export class SalesController {
  constructor(
    @Inject(AppCommandBus)
    private readonly commandBus: AppCommandBus,
    @Inject(SalesReadService)
    private readonly readService: SalesReadService,
  ) {}

  @Post('sessions/open')
  async openSession(
    @Body() dto: OpenSalesSessionDto,
    @CurrentUser() user: RequestUser | null,
    @Headers('idempotency-key') idempotencyKey?: string,
    @Headers('x-correlation-id') correlationId?: string,
  ): Promise<OpenSalesSessionResult> {
    if (!user) throw new UnauthorizedException();

    return this.commandBus.execute(
      new OpenSalesSessionCommand(
        dto.branchId as BranchId | undefined,
        dto.terminalId,
        dto.openingFloatCents,
        dto.currency ?? 'MXN',
        user.userId as UserId,
        { commandId: idempotencyKey, correlationId },
      ),
    );
  }

  @Get('sessions/current')
  async getCurrentSession(
    @Query() query: GetCurrentSalesSessionQueryDto,
  ): Promise<GetCurrentSalesSessionResult> {
    return this.readService.getCurrentSession(query.terminalId, query.branchId);
  }

  @Get('sessions/:id')
  async getSession(@Param('id') id: string): Promise<SalesSessionSummary> {
    return this.readService.getSessionById(id);
  }

  @Post('sessions/:id/sales')
  async createSale(
    @Param('id') id: string,
    @Body() dto: CreateSaleDto,
    @CurrentUser() user: RequestUser | null,
    @Headers('idempotency-key') idempotencyKey?: string,
    @Headers('x-correlation-id') correlationId?: string,
  ): Promise<CreateSaleResult> {
    if (!user) throw new UnauthorizedException();

    return this.commandBus.execute(
      new CreateSaleCommand(id, dto, user.userId as UserId, {
        commandId: idempotencyKey,
        correlationId,
      }),
    );
  }

  @Post('sessions/:id/close')
  async closeSession(
    @Param('id') id: string,
    @Body() dto: CloseSalesSessionDto,
    @CurrentUser() user: RequestUser | null,
    @Headers('idempotency-key') idempotencyKey?: string,
    @Headers('x-correlation-id') correlationId?: string,
  ): Promise<CloseSalesSessionResult> {
    if (!user) throw new UnauthorizedException();

    return this.commandBus.execute(
      new CloseSalesSessionCommand(id, dto, user.userId as UserId, {
        commandId: idempotencyKey,
        correlationId,
      }),
    );
  }
}

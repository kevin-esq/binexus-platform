import { Module } from '@nestjs/common';

import { CommandsModule } from '../../common/commands/commands.module';
import { EventsModule } from '../../common/events/events.module';
import { FeatureFlagsModule } from '../../common/feature-flags/feature-flags.module';
import { PrismaModule } from '../../common/prisma/prisma.module';
import { TenantModule } from '../../common/tenant/tenant.module';

import { CloseSalesSessionHandler } from './application/commands/close-sales-session.command';
import { CreateSaleHandler } from './application/commands/create-sale.command';
import { OpenSalesSessionHandler } from './application/commands/open-sales-session.command';
import { SalesReadService } from './application/sales-read.service';
import { SalesController } from './presentation/sales.controller';

@Module({
  imports: [CommandsModule, EventsModule, FeatureFlagsModule, PrismaModule, TenantModule],
  controllers: [SalesController],
  providers: [
    SalesReadService,
    OpenSalesSessionHandler,
    CreateSaleHandler,
    CloseSalesSessionHandler,
  ],
})
export class SalesModule {}

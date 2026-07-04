import { Module } from '@nestjs/common';

import { CommandsModule } from '../../common/commands/commands.module';
import { EventsModule } from '../../common/events/events.module';
import { PrismaModule } from '../../common/prisma/prisma.module';
import { TenantModule } from '../../common/tenant/tenant.module';

import { AssignOrderToDeliveryRouteHandler } from './application/commands/assign-order-to-delivery-route.command';
import { ConfirmDeliveryHandler } from './application/commands/confirm-delivery.command';
import { CreateDeliveryProofUploadHandler } from './application/commands/create-delivery-proof-upload.command';
import { CreateDeliveryRouteHandler } from './application/commands/create-delivery-route.command';
import { DispatchDeliveryRouteHandler } from './application/commands/dispatch-delivery-route.command';
import { ReportFailedDeliveryHandler } from './application/commands/report-failed-delivery.command';
import { LogisticsCandidateService } from './application/logistics-candidate.service';
import { LogisticsReadService } from './application/logistics-read.service';
import { OrderReadyForDeliveryRouteLogisticsHandler } from './events/order-ready-for-delivery-route.handler';
import { LogisticsController } from './presentation/logistics.controller';

@Module({
  imports: [CommandsModule, EventsModule, PrismaModule, TenantModule],
  controllers: [LogisticsController],
  providers: [
    LogisticsCandidateService,
    LogisticsReadService,
    CreateDeliveryRouteHandler,
    AssignOrderToDeliveryRouteHandler,
    DispatchDeliveryRouteHandler,
    ConfirmDeliveryHandler,
    CreateDeliveryProofUploadHandler,
    ReportFailedDeliveryHandler,
    OrderReadyForDeliveryRouteLogisticsHandler,
  ],
})
export class LogisticsModule {}

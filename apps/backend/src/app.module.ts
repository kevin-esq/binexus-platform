import { Module, type MiddlewareConsumer, type NestModule } from '@nestjs/common';
import { ConfigModule } from '@nestjs/config';
import { APP_GUARD } from '@nestjs/core';
import { CqrsModule } from '@nestjs/cqrs';
import { EventEmitterModule } from '@nestjs/event-emitter';

import { AuditModule } from './common/audit/audit.module';
import { CommandsModule } from './common/commands/commands.module';
import { EventsModule } from './common/events/events.module';
import { FeatureFlagsModule } from './common/feature-flags/feature-flags.module';
import { JwtAuthGuard } from './common/guards/jwt-auth.guard';
import { RolesGuard } from './common/guards/roles.guard';
import { HealthModule } from './common/health/health.module';
import { LoggerModule } from './common/logger/logger.module';
import { PrismaModule } from './common/prisma/prisma.module';
import { TenantContextMiddleware } from './common/tenant/tenant-context.middleware';
import { TenantModule } from './common/tenant/tenant.module';
import { IdentityModule } from './contexts/identity/identity.module';
import { OrdersModule } from './contexts/orders/orders.module';

@Module({
  imports: [
    ConfigModule.forRoot({ isGlobal: true, cache: true }),
    LoggerModule,
    EventEmitterModule.forRoot({ wildcard: true, maxListeners: 50 }),
    CqrsModule.forRoot(),
    PrismaModule,
    TenantModule,
    EventsModule,
    AuditModule,
    CommandsModule,
    FeatureFlagsModule,
    HealthModule,
    IdentityModule,
    OrdersModule,
  ],
  providers: [
    { provide: APP_GUARD, useClass: JwtAuthGuard },
    { provide: APP_GUARD, useClass: RolesGuard },
  ],
})
export class AppModule implements NestModule {
  configure(consumer: MiddlewareConsumer): void {
    consumer.apply(TenantContextMiddleware).forRoutes('*');
  }
}

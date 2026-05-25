import { Global, Module } from '@nestjs/common';

import { SystemUserService } from './system-user.service';
import { TenantContextService } from './tenant-context.service';

@Global()
@Module({
  providers: [TenantContextService, SystemUserService],
  exports: [TenantContextService, SystemUserService],
})
export class TenantModule {}

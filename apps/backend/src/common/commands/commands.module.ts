import { Module } from '@nestjs/common';
import { CqrsModule } from '@nestjs/cqrs';

import { AppCommandBus } from './command-bus.service';

@Module({
  imports: [CqrsModule],
  providers: [AppCommandBus],
  exports: [AppCommandBus, CqrsModule],
})
export class CommandsModule {}

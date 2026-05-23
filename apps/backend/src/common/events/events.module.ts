import { Global, Module } from '@nestjs/common';

import { EventBusService } from './event-bus.service';
import { OutboxService } from './outbox.service';
import { EVENT_TRANSPORT } from './transports/event-transport.token';
import { InProcessEventTransport } from './transports/in-process.transport';

@Global()
@Module({
  providers: [
    EventBusService,
    OutboxService,
    { provide: EVENT_TRANSPORT, useClass: InProcessEventTransport },
  ],
  exports: [EventBusService, OutboxService],
})
export class EventsModule {}

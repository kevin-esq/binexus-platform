import type { DomainEvent } from '@binexus/events';
import { Injectable, Logger } from '@nestjs/common';
import { type EventEmitter2 } from '@nestjs/event-emitter';

import type { EventTransport } from './event-transport.token';

@Injectable()
export class InProcessEventTransport implements EventTransport {
  private readonly logger = new Logger(InProcessEventTransport.name);

  constructor(private readonly emitter: EventEmitter2) {}

  async publish<TEvent extends DomainEvent>(event: TEvent): Promise<void> {
    this.logger.debug(
      `publish in-process event=${event.name} tenant=${event.tenantId} id=${event.id}`,
    );
    this.emitter.emit(event.name, event);
  }
}

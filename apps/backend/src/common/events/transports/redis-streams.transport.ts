import type { DomainEvent } from '@binexus/events';
import { Injectable, Logger } from '@nestjs/common';

import type { EventTransport } from './event-transport.token';

// Placeholder transport for the future Redis Streams adapter.
// Phase 0 keeps everything in-process; this stub documents the shape so we can swap
// `EVENT_TRANSPORT` provider in EventsModule when we're ready to scale out.
@Injectable()
export class RedisStreamsEventTransport implements EventTransport {
  private readonly logger = new Logger(RedisStreamsEventTransport.name);

  async publish<TEvent extends DomainEvent>(_event: TEvent): Promise<void> {
    this.logger.warn(
      'RedisStreamsEventTransport.publish() called but Redis Streams is not implemented yet.',
    );
    // Intentionally not throwing — keeps Phase 0 callable for tests if someone wires it up
    // before the real implementation lands.
    return Promise.resolve();
  }
}

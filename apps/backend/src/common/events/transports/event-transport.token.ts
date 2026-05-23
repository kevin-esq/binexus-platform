export const EVENT_TRANSPORT = Symbol.for('binexus.eventTransport');

import type { DomainEvent } from '@binexus/events';

export interface EventTransport {
  publish<TEvent extends DomainEvent>(event: TEvent): Promise<void>;
}

import { randomUUID } from 'node:crypto';

import type { DomainEvent, DomainEventName } from '@binexus/events';
import { EventPayloadSchemas, type EventPayloadFor } from '@binexus/events';
import { Inject, Injectable } from '@nestjs/common';

import { type TenantContextService } from '../tenant/tenant-context.service';

import { EVENT_TRANSPORT, type EventTransport } from './transports/event-transport.token';

@Injectable()
export class EventBusService {
  constructor(
    @Inject(EVENT_TRANSPORT) private readonly transport: EventTransport,
    private readonly tenantContext: TenantContextService,
  ) {}

  // Build a fully-formed envelope from a name + payload, pulling tenantId / correlation
  // from the AsyncLocalStorage context if not provided.
  build<TName extends DomainEventName>(
    name: TName,
    payload: EventPayloadFor<TName>,
    opts: { tenantId?: string; correlationId?: string; causationId?: string } = {},
  ): DomainEvent<TName, EventPayloadFor<TName>> {
    const ctx = this.tenantContext.tenantIdOrNull();
    const tenantId = opts.tenantId ?? ctx;
    if (!tenantId) {
      throw new Error(`Cannot build event ${name}: no tenantId in context and none provided.`);
    }
    const schema = EventPayloadSchemas[name];
    schema.parse(payload);
    return {
      id: randomUUID(),
      name,
      tenantId,
      occurredAt: new Date().toISOString(),
      version: 1,
      correlationId: opts.correlationId,
      causationId: opts.causationId,
      payload,
    };
  }

  async publish<TName extends DomainEventName>(
    name: TName,
    payload: EventPayloadFor<TName>,
    opts?: { tenantId?: string; correlationId?: string; causationId?: string },
  ): Promise<void> {
    const event = this.build(name, payload, opts);
    await this.transport.publish(event);
  }
}

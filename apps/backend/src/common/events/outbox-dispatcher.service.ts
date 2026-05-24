import { type DomainEvent, type DomainEventName, EventPayloadSchemas } from '@binexus/events';
import { Inject, Injectable, Logger } from '@nestjs/common';
import type { OutboxEvent } from '@prisma/client';

import { PrismaService } from '../prisma/prisma.service';

import { EVENT_TRANSPORT, type EventTransport } from './transports/event-transport.token';

export interface DispatchPendingOptions {
  /** Maximum unpublished rows to process in one call. Default 50. */
  limit?: number;
}

export interface DispatchPendingResult {
  published: number;
  failed: number;
}

const DEFAULT_BATCH_LIMIT = 50;
const MAX_LAST_ERROR_LENGTH = 2000;

@Injectable()
export class OutboxDispatcherService {
  private readonly logger = new Logger(OutboxDispatcherService.name);

  constructor(
    @Inject(PrismaService) private readonly prisma: PrismaService,
    @Inject(EVENT_TRANSPORT) private readonly transport: EventTransport,
  ) {}

  /**
   * Reads unpublished outbox rows, publishes each envelope through the configured
   * transport, and marks `publishedAt` on success. Failures increment `attempts`
   * and store `lastError` without blocking the rest of the batch.
   *
   * Intended to be invoked explicitly (tests, future worker). No cron in this phase.
   */
  async dispatchPending(options: DispatchPendingOptions = {}): Promise<DispatchPendingResult> {
    const limit = options.limit ?? DEFAULT_BATCH_LIMIT;

    const rows = await this.prisma.outboxEvent.findMany({
      where: { publishedAt: null },
      orderBy: { occurredAt: 'asc' },
      take: limit,
    });

    let published = 0;
    let failed = 0;

    for (const row of rows) {
      try {
        const event = this.toDomainEvent(row);
        await this.transport.publish(event);
        await this.prisma.outboxEvent.update({
          where: { id: row.id },
          data: { publishedAt: new Date() },
        });
        published++;
        this.logger.debug(`dispatched event=${event.name} id=${event.id} tenant=${event.tenantId}`);
      } catch (error) {
        failed++;
        const message = error instanceof Error ? error.message : String(error);
        this.logger.warn(`failed to dispatch outbox id=${row.id} name=${row.name}: ${message}`);
        await this.prisma.outboxEvent.update({
          where: { id: row.id },
          data: {
            attempts: { increment: 1 },
            lastError: message.slice(0, MAX_LAST_ERROR_LENGTH),
          },
        });
      }
    }

    return { published, failed };
  }

  private toDomainEvent(row: OutboxEvent): DomainEvent {
    const name = row.name as DomainEventName;
    const schema = EventPayloadSchemas[name];
    if (!schema) {
      throw new Error(`Unknown or unsupported domain event name: ${row.name}`);
    }

    const payload = schema.parse(row.payload);

    return {
      id: row.id,
      name,
      tenantId: row.tenantId,
      occurredAt: row.occurredAt.toISOString(),
      version: row.version,
      correlationId: row.correlationId ?? undefined,
      causationId: row.causationId ?? undefined,
      payload,
    };
  }
}

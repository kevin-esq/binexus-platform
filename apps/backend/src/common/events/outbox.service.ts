import type { DomainEvent } from '@binexus/events';
import { Injectable } from '@nestjs/common';
import type { Prisma } from '@prisma/client';

import { type PrismaService } from '../prisma/prisma.service';

// Transactional Outbox.
// Persist the event in the same DB transaction as the command's state change.
// A background dispatcher (`OutboxDispatcherService.dispatchPending`) reads rows where
// `publishedAt IS NULL` and forwards them to the event transport.
@Injectable()
export class OutboxService {
  constructor(private readonly prisma: PrismaService) {}

  async record<TEvent extends DomainEvent>(
    event: TEvent,
    tx?: Prisma.TransactionClient,
  ): Promise<void> {
    const client = tx ?? this.prisma;
    await client.outboxEvent.create({
      data: {
        id: event.id,
        tenantId: event.tenantId,
        name: event.name,
        payload: event.payload as Prisma.InputJsonValue,
        version: event.version,
        occurredAt: new Date(event.occurredAt),
        correlationId: event.correlationId,
        causationId: event.causationId,
      },
    });
  }
}

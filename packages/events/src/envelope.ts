import { z } from 'zod';

import type { DomainEventName } from './registry';

// Every domain event in Binexus is wrapped in this envelope.
// Persisted to the OutboxEvent table and re-hydrated by transports.

export interface DomainEvent<TName extends DomainEventName = DomainEventName, TPayload = unknown> {
  id: string;
  name: TName;
  tenantId: string;
  occurredAt: string; // ISO 8601
  version: number;
  correlationId?: string;
  causationId?: string;
  payload: TPayload;
}

export const domainEventEnvelopeSchema = z.object({
  id: z.string().min(1),
  name: z.string().min(1),
  tenantId: z.string().min(1),
  occurredAt: z.string().datetime(),
  version: z.number().int().positive(),
  correlationId: z.string().optional(),
  causationId: z.string().optional(),
  payload: z.unknown(),
});

export type DomainEventEnvelope = z.infer<typeof domainEventEnvelopeSchema>;

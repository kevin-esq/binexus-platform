import { z } from 'zod';

export const salesSessionClosedPayload = z.object({
  sessionId: z.string(),
  branchId: z.string(),
  terminalId: z.string(),
  expectedClosingCents: z.number().int(),
  declaredClosingCents: z.number().int(),
  discrepancyCents: z.number().int(),
  currency: z.string().length(3),
  closedBy: z.string(),
});

export type SalesSessionClosedPayload = z.infer<typeof salesSessionClosedPayload>;

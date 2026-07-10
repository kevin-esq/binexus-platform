import { z } from 'zod';

export const salesSessionOpenedPayload = z.object({
  sessionId: z.string(),
  branchId: z.string(),
  terminalId: z.string(),
  openingFloatCents: z.number().int().nonnegative(),
  currency: z.string().length(3),
  openedBy: z.string(),
});

export type SalesSessionOpenedPayload = z.infer<typeof salesSessionOpenedPayload>;

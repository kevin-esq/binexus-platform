import { z } from 'zod';

export const deliveryRouteLiquidatedPayload = z.object({
  deliveryRouteId: z.string(),
  liquidationId: z.string(),
  branchId: z.string(),
  expectedCents: z.number().int().nonnegative(),
  declaredCents: z.number().int().nonnegative(),
  discrepancyCents: z.number().int(),
  currency: z.string().length(3),
  cashOrderIds: z.array(z.string()),
  liquidatedBy: z.string(),
  liquidatedAt: z.string(),
  discrepancyReason: z.string().optional(),
});

export type DeliveryRouteLiquidatedPayload = z.infer<typeof deliveryRouteLiquidatedPayload>;

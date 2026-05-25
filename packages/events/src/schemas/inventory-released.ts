import { z } from 'zod';

export const inventoryReleasedPayload = z.object({
  orderId: z.string(),
  branchId: z.string(),
  lineCount: z.number().int().nonnegative(),
  releasedBy: z.string().optional(),
});

export type InventoryReleasedPayload = z.infer<typeof inventoryReleasedPayload>;

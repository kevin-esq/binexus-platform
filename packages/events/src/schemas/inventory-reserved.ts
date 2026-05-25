import { z } from 'zod';

export const inventoryReservedPayload = z.object({
  orderId: z.string(),
  branchId: z.string(),
  lineCount: z.number().int().nonnegative(),
});

export type InventoryReservedPayload = z.infer<typeof inventoryReservedPayload>;

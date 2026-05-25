import { z } from 'zod';

export const inventoryReservationFailureLine = z.object({
  orderLineId: z.string(),
  productId: z.string(),
  requested: z.number().int().positive(),
  available: z.number().int().nonnegative(),
});

export const inventoryReservationFailedPayload = z.object({
  orderId: z.string(),
  branchId: z.string(),
  failures: z.array(inventoryReservationFailureLine).min(1),
});

export type InventoryReservationFailedPayload = z.infer<typeof inventoryReservationFailedPayload>;

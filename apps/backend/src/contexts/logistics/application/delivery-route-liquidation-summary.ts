import type {
  DeliveryRouteLiquidationSummary,
  LiquidateDeliveryRouteResult,
  OrderId,
} from '@binexus/types';
import type { DeliveryRouteLiquidation, DeliveryRouteLiquidationLine } from '@prisma/client';

export function toDeliveryRouteLiquidationSummary(
  liquidation: DeliveryRouteLiquidation & { lines?: DeliveryRouteLiquidationLine[] },
): DeliveryRouteLiquidationSummary {
  return {
    id: liquidation.id,
    deliveryRouteId: liquidation.deliveryRouteId,
    expectedCents: liquidation.expectedCents,
    declaredCents: liquidation.declaredCents,
    discrepancyCents: liquidation.discrepancyCents,
    currency: liquidation.currency,
    closedAt: liquidation.closedAt.toISOString(),
    discrepancyReason: liquidation.discrepancyReason,
    lines: (liquidation.lines ?? []).map((line) => ({
      deliveryRouteStopId: line.deliveryRouteStopId,
      orderId: line.orderId as OrderId,
      expectedCents: line.expectedCents,
      declaredCents: line.declaredCents,
    })),
  };
}

export function toLiquidateDeliveryRouteResult(
  deliveryRouteId: string,
  liquidation: DeliveryRouteLiquidation & { lines?: DeliveryRouteLiquidationLine[] },
): LiquidateDeliveryRouteResult {
  return {
    deliveryRouteId,
    liquidation: toDeliveryRouteLiquidationSummary(liquidation),
  };
}

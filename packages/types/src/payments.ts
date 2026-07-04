// Shared payment method values — aligned with PAYMENT_REGISTERED event schema.

export const PaymentMethod = {
  CASH: 'CASH',
  CARD: 'CARD',
  TRANSFER: 'TRANSFER',
  CREDIT: 'CREDIT',
} as const;

export type PaymentMethod = (typeof PaymentMethod)[keyof typeof PaymentMethod];

export const ALL_PAYMENT_METHODS: PaymentMethod[] = Object.values(PaymentMethod);

export function isPaymentMethod(value: string): value is PaymentMethod {
  return (ALL_PAYMENT_METHODS as string[]).includes(value);
}

/** Methods that expect cash collection on the delivery route. */
export const ROUTE_CASH_PAYMENT_METHODS: readonly PaymentMethod[] = [PaymentMethod.CASH];

/** Methods that auto-settle operationally on delivery confirmation (ADR-0012 D1). */
export const AUTO_SETTLE_ON_DELIVERY_METHODS: readonly PaymentMethod[] = [
  PaymentMethod.CARD,
  PaymentMethod.TRANSFER,
];

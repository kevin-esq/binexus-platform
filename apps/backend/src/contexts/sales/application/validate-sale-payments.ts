import {
  type CreateSalePaymentInput,
  isPosWalkInPaymentMethod,
  PaymentMethod,
} from '@binexus/types';
import { BadRequestException } from '@nestjs/common';

export function validateSalePayments(payments: CreateSalePaymentInput[], totalCents: number): void {
  if (!payments?.length) {
    throw new BadRequestException('payments must include at least one capture.');
  }

  let sumCents = 0;
  for (const payment of payments) {
    if (!isPosWalkInPaymentMethod(payment.method)) {
      if (payment.method === PaymentMethod.CREDIT) {
        throw new BadRequestException(
          'CREDIT payment is not supported in POS walk-in sales (deferred to 5.3).',
        );
      }
      throw new BadRequestException(`Invalid payment method: ${payment.method}`);
    }
    if (!Number.isInteger(payment.amountCents) || payment.amountCents <= 0) {
      throw new BadRequestException('Each payment amountCents must be a positive integer.');
    }
    sumCents += payment.amountCents;
  }

  if (sumCents !== totalCents) {
    throw new BadRequestException(
      `Payment captures must sum to ticket total (${totalCents} cents); received ${sumCents} cents.`,
    );
  }
}

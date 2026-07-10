import { PaymentMethod } from '@binexus/types';
import { BadRequestException } from '@nestjs/common';
import { describe, expect, it } from 'vitest';

import { validateSalePayments } from './validate-sale-payments';

describe('validateSalePayments', () => {
  it('accepts a single CASH capture matching the total', () => {
    expect(() =>
      validateSalePayments([{ method: PaymentMethod.CASH, amountCents: 10000 }], 10000),
    ).not.toThrow();
  });

  it('accepts split captures across multiple methods', () => {
    expect(() =>
      validateSalePayments(
        [
          { method: PaymentMethod.CASH, amountCents: 5000 },
          { method: PaymentMethod.CARD, amountCents: 3000 },
          { method: PaymentMethod.TRANSFER, amountCents: 2000 },
        ],
        10000,
      ),
    ).not.toThrow();
  });

  it('rejects empty payments', () => {
    expect(() => validateSalePayments([], 10000)).toThrow(BadRequestException);
  });

  it('rejects CREDIT', () => {
    expect(() =>
      validateSalePayments([{ method: PaymentMethod.CREDIT, amountCents: 10000 }], 10000),
    ).toThrow(/5\.3/);
  });

  it('rejects zero or negative amounts', () => {
    expect(() => validateSalePayments([{ method: PaymentMethod.CASH, amountCents: 0 }], 0)).toThrow(
      BadRequestException,
    );
  });

  it('rejects when sum does not match ticket total', () => {
    expect(() =>
      validateSalePayments(
        [
          { method: PaymentMethod.CASH, amountCents: 4000 },
          { method: PaymentMethod.CARD, amountCents: 5000 },
        ],
        10000,
      ),
    ).toThrow(/must sum to ticket total/);
  });
});

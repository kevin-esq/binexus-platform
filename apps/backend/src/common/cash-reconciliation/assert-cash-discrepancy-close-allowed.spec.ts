import { BadRequestException, ForbiddenException } from '@nestjs/common';
import { Role } from '@prisma/client';
import { describe, expect, it } from 'vitest';

import { assertCashDiscrepancyCloseAllowed } from './assert-cash-discrepancy-close-allowed';

describe('assertCashDiscrepancyCloseAllowed', () => {
  it('allows matching close for any role', () => {
    expect(() => assertCashDiscrepancyCloseAllowed(false, Role.CASHIER, undefined)).not.toThrow();
  });

  it('rejects cashier on discrepancy without reason', () => {
    expect(() => assertCashDiscrepancyCloseAllowed(true, Role.CASHIER, undefined)).toThrow(
      ForbiddenException,
    );
  });

  it('allows admin with reason on discrepancy', () => {
    expect(() =>
      assertCashDiscrepancyCloseAllowed(true, Role.ADMIN, 'counting error'),
    ).not.toThrow();
  });

  it('rejects admin without reason on discrepancy', () => {
    expect(() => assertCashDiscrepancyCloseAllowed(true, Role.ADMIN, '   ')).toThrow(
      BadRequestException,
    );
  });
});

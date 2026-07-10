import { BadRequestException, ForbiddenException } from '@nestjs/common';
import { Role } from '@prisma/client';

export const SUPERVISOR_ROLES_FOR_CASH_DISCREPANCY: readonly Role[] = [
  Role.ADMIN,
  Role.SUPER_ADMIN,
];

export function assertCashDiscrepancyCloseAllowed(
  hasDiscrepancy: boolean,
  role: Role,
  discrepancyReason: string | undefined,
): void {
  if (!hasDiscrepancy) return;

  if (!SUPERVISOR_ROLES_FOR_CASH_DISCREPANCY.includes(role)) {
    throw new ForbiddenException(
      'Closing with a cash discrepancy requires ADMIN or SUPER_ADMIN role.',
    );
  }

  if (!discrepancyReason?.trim()) {
    throw new BadRequestException(
      'discrepancyReason is required when declaredCents does not match expectedCents.',
    );
  }
}

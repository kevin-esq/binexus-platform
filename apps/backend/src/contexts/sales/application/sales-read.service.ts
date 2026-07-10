import { type GetCurrentSalesSessionResult, type SalesSessionSummary } from '@binexus/types';
import { Inject, Injectable, NotFoundException } from '@nestjs/common';
import { SalesSessionStatus } from '@prisma/client';

import { PrismaService } from '../../../common/prisma/prisma.service';
import { TenantContextService } from '../../../common/tenant/tenant-context.service';

import { toSalesSessionSummary } from './sales-session-summary';

@Injectable()
export class SalesReadService {
  constructor(
    @Inject(PrismaService)
    private readonly prisma: PrismaService,
    @Inject(TenantContextService)
    private readonly tenantContext: TenantContextService,
  ) {}

  async getCurrentSession(
    terminalId: string,
    branchId?: string,
  ): Promise<GetCurrentSalesSessionResult> {
    const ctx = this.tenantContext.current();
    const resolvedBranchId = branchId ?? ctx.branchId;

    if (!resolvedBranchId) {
      return { session: null };
    }

    const session = await this.prisma.forTenant().salesSession.findFirst({
      where: {
        branchId: resolvedBranchId,
        terminalId: terminalId.trim(),
        status: SalesSessionStatus.OPEN,
      },
    });

    return { session: session ? toSalesSessionSummary(session) : null };
  }

  async getSessionById(sessionId: string): Promise<SalesSessionSummary> {
    const session = await this.prisma.forTenant().salesSession.findFirst({
      where: { id: sessionId },
    });

    if (!session) {
      throw new NotFoundException(`Sales session ${sessionId} not found`);
    }

    return toSalesSessionSummary(session);
  }
}

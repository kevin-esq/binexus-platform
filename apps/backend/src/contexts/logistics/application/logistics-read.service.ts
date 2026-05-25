import {
  type ListDeliveryRouteCandidatesQuery,
  type ListDeliveryRouteCandidatesResult,
  type ListDeliveryRoutesQuery,
  type ListDeliveryRoutesResult,
} from '@binexus/types';
import { BadRequestException, Inject, Injectable } from '@nestjs/common';
import type { DeliveryRouteCandidateStatus, DeliveryRouteStatus } from '@prisma/client';

import { PrismaService } from '../../../common/prisma/prisma.service';

import { toDeliveryRouteCandidateSummary } from './delivery-route-candidate-summary';
import { toDeliveryRouteSummary } from './delivery-route-summary';

const DEFAULT_LIMIT = 50;
const MAX_LIMIT = 100;

@Injectable()
export class LogisticsReadService {
  constructor(@Inject(PrismaService) private readonly prisma: PrismaService) {}

  async listDeliveryRouteCandidates(
    query: ListDeliveryRouteCandidatesQuery = {},
  ): Promise<ListDeliveryRouteCandidatesResult> {
    const limit = Math.min(Math.max(query.limit ?? DEFAULT_LIMIT, 1), MAX_LIMIT);
    const db = this.prisma.forTenant();

    const filters: Record<string, unknown> = {};
    if (query.status) {
      filters.status = query.status as DeliveryRouteCandidateStatus;
    }
    if (query.branchId) {
      filters.branchId = query.branchId;
    }

    const cursorWhere = query.cursor
      ? await this.resolveCandidateCursorWhere(db, query.cursor, filters)
      : filters;

    const rows = await db.deliveryRouteCandidate.findMany({
      where: cursorWhere,
      orderBy: [{ createdAt: 'desc' }, { id: 'desc' }],
      take: limit + 1,
    });

    const hasMore = rows.length > limit;
    const page = hasMore ? rows.slice(0, limit) : rows;

    return {
      items: page.map((row) => toDeliveryRouteCandidateSummary(row)),
      nextCursor: hasMore ? (page[page.length - 1]?.id ?? null) : null,
    };
  }

  async listDeliveryRoutes(query: ListDeliveryRoutesQuery = {}): Promise<ListDeliveryRoutesResult> {
    const limit = Math.min(Math.max(query.limit ?? DEFAULT_LIMIT, 1), MAX_LIMIT);
    const db = this.prisma.forTenant();

    const filters: Record<string, unknown> = {};
    if (query.status) {
      filters.status = query.status as DeliveryRouteStatus;
    }
    if (query.branchId) {
      filters.branchId = query.branchId;
    }

    const cursorWhere = query.cursor
      ? await this.resolveRouteCursorWhere(db, query.cursor, filters)
      : filters;

    const rows = await db.deliveryRoute.findMany({
      where: cursorWhere,
      orderBy: [{ createdAt: 'desc' }, { id: 'desc' }],
      take: limit + 1,
      include: { _count: { select: { stops: true } } },
    });

    const hasMore = rows.length > limit;
    const page = hasMore ? rows.slice(0, limit) : rows;

    return {
      items: page.map((row) => toDeliveryRouteSummary(row)),
      nextCursor: hasMore ? (page[page.length - 1]?.id ?? null) : null,
    };
  }

  private async resolveCandidateCursorWhere(
    db: ReturnType<PrismaService['forTenant']>,
    cursor: string,
    filters: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    const anchor = await db.deliveryRouteCandidate.findFirst({
      where: { id: cursor, ...filters },
      select: { id: true, createdAt: true },
    });

    if (!anchor) {
      throw new BadRequestException('Invalid cursor');
    }

    return {
      ...filters,
      OR: [
        { createdAt: { lt: anchor.createdAt } },
        { createdAt: anchor.createdAt, id: { lt: anchor.id } },
      ],
    };
  }

  private async resolveRouteCursorWhere(
    db: ReturnType<PrismaService['forTenant']>,
    cursor: string,
    filters: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    const anchor = await db.deliveryRoute.findFirst({
      where: { id: cursor, ...filters },
      select: { id: true, createdAt: true },
    });

    if (!anchor) {
      throw new BadRequestException('Invalid cursor');
    }

    return {
      ...filters,
      OR: [
        { createdAt: { lt: anchor.createdAt } },
        { createdAt: anchor.createdAt, id: { lt: anchor.id } },
      ],
    };
  }
}

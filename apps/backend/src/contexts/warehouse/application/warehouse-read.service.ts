import { type ListPickingTasksQuery, type ListPickingTasksResult } from '@binexus/types';
import { BadRequestException, Inject, Injectable } from '@nestjs/common';
import type { PickingTaskStatus } from '@prisma/client';

import { PrismaService } from '../../../common/prisma/prisma.service';

import { toPickingTaskSummary } from './picking-task-summary';

const DEFAULT_LIMIT = 50;
const MAX_LIMIT = 100;

@Injectable()
export class WarehouseReadService {
  constructor(@Inject(PrismaService) private readonly prisma: PrismaService) {}

  async listPickingTasks(query: ListPickingTasksQuery = {}): Promise<ListPickingTasksResult> {
    const limit = Math.min(Math.max(query.limit ?? DEFAULT_LIMIT, 1), MAX_LIMIT);
    const db = this.prisma.forTenant();

    const filters: Record<string, unknown> = {};
    if (query.status) {
      filters.status = query.status as PickingTaskStatus;
    }

    const cursorWhere = query.cursor
      ? await this.resolveCursorWhere(db, query.cursor, filters)
      : filters;

    const rows = await db.pickingTask.findMany({
      where: cursorWhere,
      orderBy: [{ createdAt: 'desc' }, { id: 'desc' }],
      take: limit + 1,
      include: { _count: { select: { lines: true } } },
    });

    const hasMore = rows.length > limit;
    const page = hasMore ? rows.slice(0, limit) : rows;

    return {
      items: page.map((row) => toPickingTaskSummary(row)),
      nextCursor: hasMore ? (page[page.length - 1]?.id ?? null) : null,
    };
  }

  private async resolveCursorWhere(
    db: ReturnType<PrismaService['forTenant']>,
    cursor: string,
    filters: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    const anchor = await db.pickingTask.findFirst({
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

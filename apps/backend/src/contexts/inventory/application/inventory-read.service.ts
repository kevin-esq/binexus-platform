import {
  type ListStockItemsQuery,
  type ListStockItemsResult,
  type StockItemSummary,
} from '@binexus/types';
import { BadRequestException, Inject, Injectable } from '@nestjs/common';
import type { StockItem } from '@prisma/client';

import { PrismaService } from '../../../common/prisma/prisma.service';

const DEFAULT_LIMIT = 50;
const MAX_LIMIT = 100;

@Injectable()
export class InventoryReadService {
  constructor(@Inject(PrismaService) private readonly prisma: PrismaService) {}

  async listStockItems(query: ListStockItemsQuery = {}): Promise<ListStockItemsResult> {
    const limit = Math.min(Math.max(query.limit ?? DEFAULT_LIMIT, 1), MAX_LIMIT);
    const db = this.prisma.forTenant();

    const filters: Record<string, unknown> = {};
    if (query.branchId) {
      filters.branchId = query.branchId;
    }
    if (query.productId) {
      filters.productId = query.productId;
    }

    const cursorWhere = query.cursor
      ? await this.resolveCursorWhere(db, query.cursor, filters)
      : filters;

    const rows = await db.stockItem.findMany({
      where: cursorWhere,
      orderBy: [{ createdAt: 'desc' }, { id: 'desc' }],
      take: limit + 1,
    });

    const hasMore = rows.length > limit;
    const page = hasMore ? rows.slice(0, limit) : rows;
    const items = page.map((row) => this.toSummary(row));

    return {
      items,
      nextCursor: hasMore ? (page[page.length - 1]?.id ?? null) : null,
    };
  }

  private async resolveCursorWhere(
    db: ReturnType<PrismaService['forTenant']>,
    cursor: string,
    filters: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    const anchor = await db.stockItem.findFirst({
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

  private toSummary(row: StockItem): StockItemSummary {
    return {
      id: row.id,
      branchId: row.branchId as StockItemSummary['branchId'],
      productId: row.productId,
      onHand: row.onHand,
      reserved: row.reserved,
      available: row.onHand - row.reserved,
      createdAt: row.createdAt.toISOString(),
      updatedAt: row.updatedAt.toISOString(),
    };
  }
}

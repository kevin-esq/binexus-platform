import {
  type ListOrdersQuery,
  type ListOrdersResult,
  type OrderDetail,
  type OrderId,
  type OrderLineSummary,
  type OrderState,
  type OrderSummary,
  type OrderTransitionSummary,
  type PaymentMethod,
} from '@binexus/types';
import { BadRequestException, Inject, Injectable, NotFoundException } from '@nestjs/common';
import type { Order, OrderLine, OrderTransition } from '@prisma/client';

import { PrismaService } from '../../../common/prisma/prisma.service';

const DEFAULT_LIMIT = 20;
const MAX_LIMIT = 100;

@Injectable()
export class OrdersReadService {
  constructor(@Inject(PrismaService) private readonly prisma: PrismaService) {}

  async listOrders(query: ListOrdersQuery = {}): Promise<ListOrdersResult> {
    const limit = Math.min(Math.max(query.limit ?? DEFAULT_LIMIT, 1), MAX_LIMIT);
    const db = this.prisma.forTenant();

    const cursorWhere = query.cursor ? await this.resolveCursorWhere(db, query.cursor) : {};

    const rows = await db.order.findMany({
      where: cursorWhere,
      orderBy: [{ createdAt: 'desc' }, { id: 'desc' }],
      take: limit + 1,
      select: {
        id: true,
        branchId: true,
        customerId: true,
        state: true,
        paymentMethod: true,
        totalCents: true,
        currency: true,
        createdAt: true,
        _count: { select: { lines: true } },
      },
    });

    const hasMore = rows.length > limit;
    const page = hasMore ? rows.slice(0, limit) : rows;
    const items = page.map((row) => this.toSummary(row));

    return {
      items,
      nextCursor: hasMore ? (page[page.length - 1]?.id ?? null) : null,
    };
  }

  async getOrderById(id: string): Promise<OrderDetail> {
    const db = this.prisma.forTenant();

    const order = await db.order.findFirst({
      where: { id },
      include: {
        lines: { orderBy: { createdAt: 'asc' } },
        transitions: { orderBy: { occurredAt: 'asc' } },
        _count: { select: { lines: true } },
      },
    });

    if (!order) {
      throw new NotFoundException(`Order ${id} not found`);
    }

    return this.toDetail(order);
  }

  private async resolveCursorWhere(
    db: ReturnType<PrismaService['forTenant']>,
    cursor: string,
  ): Promise<Record<string, unknown>> {
    const anchor = await db.order.findFirst({
      where: { id: cursor },
      select: { id: true, createdAt: true },
    });

    if (!anchor) {
      throw new BadRequestException('Invalid cursor');
    }

    return {
      OR: [
        { createdAt: { lt: anchor.createdAt } },
        { createdAt: anchor.createdAt, id: { lt: anchor.id } },
      ],
    };
  }

  private toSummary(
    row: Pick<
      Order,
      | 'id'
      | 'branchId'
      | 'customerId'
      | 'state'
      | 'paymentMethod'
      | 'totalCents'
      | 'currency'
      | 'createdAt'
    > & {
      _count: { lines: number };
    },
  ): OrderSummary {
    return {
      id: row.id as OrderId,
      branchId: row.branchId as OrderSummary['branchId'],
      customerId: row.customerId,
      state: row.state as OrderState,
      paymentMethod: row.paymentMethod as PaymentMethod,
      totalCents: row.totalCents,
      currency: row.currency,
      createdAt: row.createdAt.toISOString(),
      lineCount: row._count.lines,
    };
  }

  private toDetail(
    order: Order & {
      lines: OrderLine[];
      transitions: OrderTransition[];
      _count: { lines: number };
    },
  ): OrderDetail {
    const summary = this.toSummary(order);

    const lines: OrderLineSummary[] = order.lines.map((line) => ({
      id: line.id,
      productId: line.productId,
      productName: line.productName,
      quantity: line.quantity,
      unitPriceCents: line.unitPriceCents,
      lineTotalCents: line.lineTotalCents,
    }));

    const transitions: OrderTransitionSummary[] = order.transitions.map((t) => ({
      id: t.id,
      fromState: t.fromState as OrderState | null,
      toState: t.toState as OrderState,
      reason: t.reason,
      occurredAt: t.occurredAt.toISOString(),
      byUserId: t.byUserId as OrderTransitionSummary['byUserId'],
    }));

    return {
      ...summary,
      createdByUserId: order.createdByUserId as OrderDetail['createdByUserId'],
      updatedAt: order.updatedAt.toISOString(),
      lines,
      transitions,
    };
  }
}

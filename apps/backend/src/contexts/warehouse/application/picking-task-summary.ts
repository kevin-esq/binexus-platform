import { type PickingTaskSummary } from '@binexus/types';
import type { PickingTask } from '@prisma/client';

export function toPickingTaskSummary(
  row: PickingTask & { _count?: { lines: number }; lines?: unknown[] },
  lineCount?: number,
): PickingTaskSummary {
  const count = lineCount ?? row._count?.lines ?? (Array.isArray(row.lines) ? row.lines.length : 0);

  return {
    id: row.id,
    orderId: row.orderId as PickingTaskSummary['orderId'],
    branchId: row.branchId as PickingTaskSummary['branchId'],
    status: row.status,
    lineCount: count,
    createdAt: row.createdAt.toISOString(),
    updatedAt: row.updatedAt.toISOString(),
    completedAt: row.completedAt?.toISOString() ?? null,
  };
}

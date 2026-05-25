'use client';

import type { PickingTaskSummary } from '@binexus/types';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useCallback, useEffect, useState } from 'react';

import { api } from '../../lib/api';
import { formatDate, shortId } from '../../lib/format';
import { hasStoredSession } from '../../lib/token-storage';

export default function WarehousePage() {
  const router = useRouter();
  const [tasks, setTasks] = useState<PickingTaskSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [completingId, setCompletingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const loadTasks = useCallback(async () => {
    const result = await api.listPickingTasks({ status: 'PENDING', limit: 50 });
    setTasks(result.items);
  }, []);

  useEffect(() => {
    if (!hasStoredSession()) {
      router.replace('/login');
      return;
    }

    let cancelled = false;
    (async () => {
      try {
        await loadTasks();
        if (!cancelled) setError(null);
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Failed to load picking tasks');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [loadTasks, router]);

  async function onRefresh(): Promise<void> {
    setRefreshing(true);
    try {
      await loadTasks();
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to refresh picking tasks');
    } finally {
      setRefreshing(false);
    }
  }

  async function onComplete(task: PickingTaskSummary): Promise<void> {
    if (!window.confirm(`Complete picking for order ${shortId(task.orderId)}?`)) return;

    setCompletingId(task.id);
    try {
      await api.completePickingTask(task.id);
      await loadTasks();
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to complete picking task');
    } finally {
      setCompletingId(null);
    }
  }

  return (
    <main className="mx-auto min-h-screen max-w-5xl p-6">
      <header className="mb-6 flex flex-wrap items-center justify-between gap-4">
        <div>
          <div className="flex flex-wrap gap-3">
            <Link
              href="/orders"
              className="text-sm font-medium text-brand-600 hover:text-brand-700"
            >
              ← Back to orders
            </Link>
            <Link
              href="/logistics"
              className="text-sm font-medium text-brand-600 hover:text-brand-700"
            >
              Logistics
            </Link>
          </div>
          <p className="mt-2 text-xs font-semibold uppercase tracking-wide text-brand-600">
            Warehouse
          </p>
          <h1 className="text-2xl font-bold text-slate-900">Picking tasks</h1>
          <p className="mt-1 text-sm text-slate-500">
            Pending picks created after inventory reservation. Complete to move orders to ready for
            delivery route.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <button
            type="button"
            disabled={loading || refreshing}
            onClick={() => void onRefresh()}
            className="h-10 rounded border border-slate-300 px-4 text-sm font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
          >
            {refreshing ? 'Refreshing…' : 'Refresh'}
          </button>
          <button
            type="button"
            className="text-sm font-medium text-slate-600 hover:text-slate-900"
            onClick={() => {
              void (async () => {
                await api.logout();
                router.replace('/login');
              })();
            }}
          >
            Sign out
          </button>
        </div>
      </header>

      {loading ? (
        <p className="text-sm text-slate-500">Loading picking tasks…</p>
      ) : error ? (
        <div className="rounded border border-red-200 bg-red-50 p-3 text-sm text-red-700">
          {error}
        </div>
      ) : tasks.length === 0 ? (
        <div className="rounded border border-slate-200 bg-white p-6 text-sm text-slate-600">
          No pending picking tasks. Approve an order with available stock to generate one.
        </div>
      ) : (
        <div className="overflow-hidden rounded-lg border border-slate-200 bg-white">
          <table className="w-full text-left text-sm">
            <thead className="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
              <tr>
                <th className="px-4 py-3">Order</th>
                <th className="px-4 py-3">Branch</th>
                <th className="px-4 py-3 text-right">Lines</th>
                <th className="px-4 py-3">Created</th>
                <th className="px-4 py-3 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {tasks.map((task) => (
                <tr key={task.id} className="border-b border-slate-100 last:border-0">
                  <td className="px-4 py-3 font-medium text-slate-900">
                    <Link
                      href={`/orders/${task.orderId}`}
                      className="text-brand-600 hover:text-brand-700"
                    >
                      {shortId(task.orderId)}
                    </Link>
                  </td>
                  <td className="px-4 py-3 text-slate-700">{task.branchId}</td>
                  <td className="px-4 py-3 text-right text-slate-700">{task.lineCount}</td>
                  <td className="px-4 py-3 text-slate-500">{formatDate(task.createdAt)}</td>
                  <td className="px-4 py-3 text-right">
                    <button
                      type="button"
                      disabled={completingId === task.id}
                      onClick={() => void onComplete(task)}
                      className="rounded border border-slate-300 px-3 py-1 text-xs font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
                    >
                      {completingId === task.id ? 'Completing…' : 'Complete'}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </main>
  );
}

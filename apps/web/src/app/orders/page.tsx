'use client';

import type { OrderSummary } from '@binexus/types';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useCallback, useEffect, useState } from 'react';

import { api } from '../../lib/api';
import { formatDate, formatMoney, shortId } from '../../lib/format';
import { hasStoredSession } from '../../lib/token-storage';

export default function OrdersPage() {
  const router = useRouter();
  const [orders, setOrders] = useState<OrderSummary[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadOrders = useCallback(async (cursor?: string) => {
    const result = await api.listOrders({ limit: 20, cursor });
    if (cursor) {
      setOrders((prev) => [...prev, ...result.items]);
    } else {
      setOrders(result.items);
    }
    setNextCursor(result.nextCursor);
  }, []);

  useEffect(() => {
    if (!hasStoredSession()) {
      router.replace('/login');
      return;
    }

    let cancelled = false;
    (async () => {
      try {
        await loadOrders();
        if (!cancelled) setError(null);
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Failed to load orders');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [loadOrders, router]);

  async function onLoadMore(): Promise<void> {
    if (!nextCursor || loadingMore) return;
    setLoadingMore(true);
    try {
      await loadOrders(nextCursor);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load more orders');
    } finally {
      setLoadingMore(false);
    }
  }

  return (
    <main className="mx-auto min-h-screen max-w-4xl p-6">
      <header className="mb-6 flex items-center justify-between gap-4">
        <div>
          <p className="text-xs font-semibold uppercase tracking-wide text-brand-600">Orders</p>
          <h1 className="text-2xl font-bold text-slate-900">Order list</h1>
        </div>
        <div className="flex items-center gap-3">
          <Link
            href="/inventory"
            className="text-sm font-medium text-brand-600 hover:text-brand-700"
          >
            Inventory
          </Link>
          <Link
            href="/warehouse"
            className="text-sm font-medium text-brand-600 hover:text-brand-700"
          >
            Warehouse
          </Link>
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
        <p className="text-sm text-slate-500">Loading orders…</p>
      ) : error ? (
        <div className="rounded border border-red-200 bg-red-50 p-3 text-sm text-red-700">
          {error}
        </div>
      ) : orders.length === 0 ? (
        <div className="rounded border border-slate-200 bg-white p-6 text-sm text-slate-600">
          No orders yet. Create one via the API or seed, then refresh.
        </div>
      ) : (
        <div className="overflow-hidden rounded-lg border border-slate-200 bg-white">
          <table className="w-full text-left text-sm">
            <thead className="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
              <tr>
                <th className="px-4 py-3">Order</th>
                <th className="px-4 py-3">Customer</th>
                <th className="px-4 py-3">State</th>
                <th className="px-4 py-3">Total</th>
                <th className="px-4 py-3">Created</th>
              </tr>
            </thead>
            <tbody>
              {orders.map((order) => (
                <tr key={order.id} className="border-b border-slate-100 last:border-0">
                  <td className="px-4 py-3">
                    <Link
                      href={`/orders/${order.id}`}
                      className="font-medium text-brand-600 hover:text-brand-700"
                    >
                      #{shortId(order.id)}
                    </Link>
                    <div className="text-xs text-slate-400">{order.lineCount} lines</div>
                  </td>
                  <td className="px-4 py-3 text-slate-700">{order.customerId}</td>
                  <td className="px-4 py-3">
                    <span className="rounded bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-700">
                      {order.state}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-slate-700">
                    {formatMoney(order.totalCents, order.currency)}
                  </td>
                  <td className="px-4 py-3 text-slate-500">{formatDate(order.createdAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {nextCursor ? (
        <button
          type="button"
          disabled={loadingMore}
          onClick={() => void onLoadMore()}
          className="mt-4 h-10 rounded border border-slate-300 px-4 text-sm font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
        >
          {loadingMore ? 'Loading…' : 'Load more'}
        </button>
      ) : null}
    </main>
  );
}

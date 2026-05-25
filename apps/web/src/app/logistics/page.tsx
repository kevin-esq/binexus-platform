'use client';

import type {
  BranchId,
  DeliveryRouteCandidateSummary,
  DeliveryRouteSummary,
  OrderId,
  UserId,
} from '@binexus/types';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useCallback, useEffect, useState } from 'react';

import { api } from '../../lib/api';
import { formatDate, shortId } from '../../lib/format';
import { hasStoredSession } from '../../lib/token-storage';

export default function LogisticsPage() {
  const router = useRouter();
  const [candidates, setCandidates] = useState<DeliveryRouteCandidateSummary[]>([]);
  const [routes, setRoutes] = useState<DeliveryRouteSummary[]>([]);
  const [dispatchedRoutes, setDispatchedRoutes] = useState<DeliveryRouteSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [assigningRouteId, setAssigningRouteId] = useState<string | null>(null);
  const [dispatchingRouteId, setDispatchingRouteId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const loadData = useCallback(async () => {
    const [candidateResult, routeResult, dispatchedResult] = await Promise.all([
      api.listDeliveryRouteCandidates({ status: 'READY', limit: 50 }),
      api.listDeliveryRoutes({ status: 'PLANNED', limit: 50 }),
      api.listDeliveryRoutes({ status: 'DISPATCHED', limit: 50 }),
    ]);
    setCandidates(candidateResult.items);
    setRoutes(routeResult.items);
    setDispatchedRoutes(dispatchedResult.items);
  }, []);

  useEffect(() => {
    if (!hasStoredSession()) {
      router.replace('/login');
      return;
    }

    let cancelled = false;
    (async () => {
      try {
        await loadData();
        if (!cancelled) setError(null);
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Failed to load logistics data');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [loadData, router]);

  async function onRefresh(): Promise<void> {
    setRefreshing(true);
    try {
      await loadData();
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to refresh');
    } finally {
      setRefreshing(false);
    }
  }

  async function onCreateRoute(): Promise<void> {
    const branchId = window.prompt('Branch ID for the new delivery route?');
    if (!branchId?.trim()) return;

    try {
      await api.createDeliveryRoute({ branchId: branchId.trim() as BranchId });
      await loadData();
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create delivery route');
    }
  }

  async function onAssignOrders(route: DeliveryRouteSummary): Promise<void> {
    const raw = window.prompt(`Assign order IDs to route ${shortId(route.id)} (comma-separated)?`);
    if (!raw?.trim()) return;

    const orderIds = raw
      .split(',')
      .map((s) => s.trim())
      .filter(Boolean);
    if (!orderIds.length) return;

    setAssigningRouteId(route.id);
    try {
      await api.assignOrderToDeliveryRoute(route.id, { orderIds: orderIds as OrderId[] });
      await loadData();
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to assign orders');
    } finally {
      setAssigningRouteId(null);
    }
  }

  async function onDispatchRoute(route: DeliveryRouteSummary): Promise<void> {
    let driverUserId = route.driverUserId;
    if (!driverUserId) {
      const raw = window.prompt(`Driver user ID for route ${shortId(route.id)}?`);
      if (!raw?.trim()) return;
      driverUserId = raw.trim() as UserId;
    }

    setDispatchingRouteId(route.id);
    try {
      await api.dispatchDeliveryRoute(
        route.id,
        driverUserId && !route.driverUserId ? { driverUserId } : {},
      );
      await loadData();
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to dispatch route');
    } finally {
      setDispatchingRouteId(null);
    }
  }

  return (
    <main className="mx-auto min-h-screen max-w-5xl p-6">
      <header className="mb-6 flex flex-wrap items-center justify-between gap-4">
        <div>
          <Link href="/orders" className="text-sm font-medium text-brand-600 hover:text-brand-700">
            ← Back to orders
          </Link>
          <p className="mt-2 text-xs font-semibold uppercase tracking-wide text-brand-600">
            Logistics
          </p>
          <h1 className="text-2xl font-bold text-slate-900">Delivery route planning</h1>
          <p className="mt-1 text-sm text-slate-500">
            Orders ready for delivery route appear as candidates. Create a planned route, assign
            stops, then dispatch to send orders out for delivery.
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
            disabled={loading}
            onClick={() => void onCreateRoute()}
            className="h-10 rounded bg-brand-600 px-4 text-sm font-medium text-white hover:bg-brand-700 disabled:opacity-50"
          >
            New route
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
        <p className="text-sm text-slate-500">Loading logistics…</p>
      ) : error ? (
        <div className="rounded border border-red-200 bg-red-50 p-3 text-sm text-red-700">
          {error}
        </div>
      ) : (
        <div className="space-y-8">
          <section>
            <h2 className="mb-3 text-lg font-semibold text-slate-900">Ready candidates</h2>
            {candidates.length === 0 ? (
              <div className="rounded border border-slate-200 bg-white p-4 text-sm text-slate-600">
                No orders ready for delivery route. Complete picking in warehouse first.
              </div>
            ) : (
              <div className="overflow-hidden rounded-lg border border-slate-200 bg-white">
                <table className="w-full text-left text-sm">
                  <thead className="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                    <tr>
                      <th className="px-4 py-3">Order</th>
                      <th className="px-4 py-3">Branch</th>
                      <th className="px-4 py-3">Updated</th>
                    </tr>
                  </thead>
                  <tbody>
                    {candidates.map((c) => (
                      <tr key={c.id} className="border-b border-slate-100 last:border-0">
                        <td className="px-4 py-3 font-medium text-slate-900">
                          <Link
                            href={`/orders/${c.orderId}`}
                            className="text-brand-600 hover:text-brand-700"
                          >
                            {shortId(c.orderId)}
                          </Link>
                        </td>
                        <td className="px-4 py-3 text-slate-700">{c.branchId}</td>
                        <td className="px-4 py-3 text-slate-500">{formatDate(c.updatedAt)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          <section>
            <h2 className="mb-3 text-lg font-semibold text-slate-900">Planned delivery routes</h2>
            {routes.length === 0 ? (
              <div className="rounded border border-slate-200 bg-white p-4 text-sm text-slate-600">
                No planned routes yet. Use New route to create one.
              </div>
            ) : (
              <div className="overflow-hidden rounded-lg border border-slate-200 bg-white">
                <table className="w-full text-left text-sm">
                  <thead className="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                    <tr>
                      <th className="px-4 py-3">Route</th>
                      <th className="px-4 py-3">Branch</th>
                      <th className="px-4 py-3 text-right">Stops</th>
                      <th className="px-4 py-3">Driver</th>
                      <th className="px-4 py-3">Created</th>
                      <th className="px-4 py-3 text-right">Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {routes.map((route) => (
                      <tr key={route.id} className="border-b border-slate-100 last:border-0">
                        <td className="px-4 py-3 font-medium text-slate-900">
                          {shortId(route.id)}
                        </td>
                        <td className="px-4 py-3 text-slate-700">{route.branchId}</td>
                        <td className="px-4 py-3 text-right text-slate-700">{route.stopCount}</td>
                        <td className="px-4 py-3 text-slate-500">
                          {route.driverUserId ? shortId(route.driverUserId) : '—'}
                        </td>
                        <td className="px-4 py-3 text-slate-500">{formatDate(route.createdAt)}</td>
                        <td className="px-4 py-3 text-right">
                          <div className="flex justify-end gap-2">
                            <button
                              type="button"
                              disabled={
                                assigningRouteId === route.id || dispatchingRouteId === route.id
                              }
                              onClick={() => void onAssignOrders(route)}
                              className="rounded border border-slate-300 px-3 py-1 text-xs font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
                            >
                              {assigningRouteId === route.id ? 'Assigning…' : 'Assign orders'}
                            </button>
                            {route.stopCount > 0 ? (
                              <button
                                type="button"
                                disabled={
                                  assigningRouteId === route.id || dispatchingRouteId === route.id
                                }
                                onClick={() => void onDispatchRoute(route)}
                                className="rounded bg-brand-600 px-3 py-1 text-xs font-medium text-white hover:bg-brand-700 disabled:opacity-50"
                              >
                                {dispatchingRouteId === route.id ? 'Dispatching…' : 'Dispatch'}
                              </button>
                            ) : null}
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          <section>
            <h2 className="mb-3 text-lg font-semibold text-slate-900">Dispatched routes</h2>
            {dispatchedRoutes.length === 0 ? (
              <div className="rounded border border-slate-200 bg-white p-4 text-sm text-slate-600">
                No dispatched routes yet.
              </div>
            ) : (
              <div className="overflow-hidden rounded-lg border border-slate-200 bg-white">
                <table className="w-full text-left text-sm">
                  <thead className="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                    <tr>
                      <th className="px-4 py-3">Route</th>
                      <th className="px-4 py-3">Branch</th>
                      <th className="px-4 py-3 text-right">Stops</th>
                      <th className="px-4 py-3">Driver</th>
                      <th className="px-4 py-3">Dispatched</th>
                    </tr>
                  </thead>
                  <tbody>
                    {dispatchedRoutes.map((route) => (
                      <tr key={route.id} className="border-b border-slate-100 last:border-0">
                        <td className="px-4 py-3 font-medium text-slate-900">
                          {shortId(route.id)}
                        </td>
                        <td className="px-4 py-3 text-slate-700">{route.branchId}</td>
                        <td className="px-4 py-3 text-right text-slate-700">{route.stopCount}</td>
                        <td className="px-4 py-3 text-slate-500">
                          {route.driverUserId ? shortId(route.driverUserId) : '—'}
                        </td>
                        <td className="px-4 py-3 text-slate-500">
                          {route.dispatchedAt ? formatDate(route.dispatchedAt) : '—'}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        </div>
      )}
    </main>
  );
}

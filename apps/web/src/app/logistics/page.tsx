'use client';

import type {
  BranchId,
  DeliveryRouteCandidateSummary,
  DeliveryRouteStopSummary,
  DeliveryRouteSummary,
  OrderId,
  UserId,
} from '@binexus/types';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { Fragment, useCallback, useEffect, useState } from 'react';

import { api } from '../../lib/api';
import { formatDate, shortId } from '../../lib/format';
import { hasStoredSession } from '../../lib/token-storage';

export default function LogisticsPage() {
  const router = useRouter();
  const [candidates, setCandidates] = useState<DeliveryRouteCandidateSummary[]>([]);
  const [routes, setRoutes] = useState<DeliveryRouteSummary[]>([]);
  const [dispatchedRoutes, setDispatchedRoutes] = useState<DeliveryRouteSummary[]>([]);
  const [completedRoutes, setCompletedRoutes] = useState<DeliveryRouteSummary[]>([]);
  const [expandedRouteId, setExpandedRouteId] = useState<string | null>(null);
  const [routeStops, setRouteStops] = useState<Record<string, DeliveryRouteStopSummary[]>>({});
  const [loadingStopsRouteId, setLoadingStopsRouteId] = useState<string | null>(null);
  const [confirmingStopId, setConfirmingStopId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [assigningRouteId, setAssigningRouteId] = useState<string | null>(null);
  const [dispatchingRouteId, setDispatchingRouteId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const loadData = useCallback(async () => {
    const [candidateResult, routeResult, dispatchedResult, completedResult] = await Promise.all([
      api.listDeliveryRouteCandidates({ status: 'READY', limit: 50 }),
      api.listDeliveryRoutes({ status: 'PLANNED', limit: 50 }),
      api.listDeliveryRoutes({ status: 'DISPATCHED', limit: 50 }),
      api.listDeliveryRoutes({ status: 'COMPLETED', limit: 50 }),
    ]);
    setCandidates(candidateResult.items);
    setRoutes(routeResult.items);
    setDispatchedRoutes(dispatchedResult.items);
    setCompletedRoutes(completedResult.items);
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
      if (expandedRouteId) {
        const stops = await api.listDeliveryRouteStops(expandedRouteId);
        setRouteStops((prev) => ({ ...prev, [expandedRouteId]: stops.items }));
      }
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

  async function toggleRouteStops(routeId: string): Promise<void> {
    if (expandedRouteId === routeId) {
      setExpandedRouteId(null);
      return;
    }

    setExpandedRouteId(routeId);
    if (routeStops[routeId]) return;

    setLoadingStopsRouteId(routeId);
    try {
      const result = await api.listDeliveryRouteStops(routeId);
      setRouteStops((prev) => ({ ...prev, [routeId]: result.items }));
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load route stops');
    } finally {
      setLoadingStopsRouteId(null);
    }
  }

  async function onConfirmDelivery(stop: DeliveryRouteStopSummary, routeId: string): Promise<void> {
    setConfirmingStopId(stop.id);
    try {
      await api.confirmDelivery(stop.id);
      const stops = await api.listDeliveryRouteStops(routeId);
      setRouteStops((prev) => ({ ...prev, [routeId]: stops.items }));
      await loadData();
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to confirm delivery');
    } finally {
      setConfirmingStopId(null);
    }
  }

  function renderDispatchedRouteRows(): React.ReactNode {
    return dispatchedRoutes.map((route) => {
      const expanded = expandedRouteId === route.id;
      const stops = routeStops[route.id] ?? [];

      return (
        <Fragment key={route.id}>
          <tr className="border-b border-slate-100 last:border-0">
            <td className="px-4 py-3 font-medium text-slate-900">
              <button
                type="button"
                onClick={() => void toggleRouteStops(route.id)}
                className="text-left hover:text-brand-600"
              >
                {expanded ? '▼' : '▶'} {shortId(route.id)}
              </button>
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
          {expanded ? (
            <tr key={`${route.id}-stops`} className="border-b border-slate-100 bg-slate-50">
              <td colSpan={5} className="px-4 py-3">
                {loadingStopsRouteId === route.id ? (
                  <p className="text-sm text-slate-500">Loading stops…</p>
                ) : stops.length === 0 ? (
                  <p className="text-sm text-slate-500">No stops on this route.</p>
                ) : (
                  <table className="w-full text-left text-sm">
                    <thead>
                      <tr className="text-xs uppercase tracking-wide text-slate-500">
                        <th className="pb-2 pr-4">Seq</th>
                        <th className="pb-2 pr-4">Order</th>
                        <th className="pb-2 pr-4">Status</th>
                        <th className="pb-2 pr-4">Delivered</th>
                        <th className="pb-2 text-right">Actions</th>
                      </tr>
                    </thead>
                    <tbody>
                      {stops.map((stop) => (
                        <tr key={stop.id}>
                          <td className="py-2 pr-4 text-slate-700">{stop.sequence}</td>
                          <td className="py-2 pr-4">
                            <Link
                              href={`/orders/${stop.orderId}`}
                              className="font-medium text-brand-600 hover:text-brand-700"
                            >
                              {shortId(stop.orderId)}
                            </Link>
                          </td>
                          <td className="py-2 pr-4 text-slate-700">{stop.status}</td>
                          <td className="py-2 pr-4 text-slate-500">
                            {stop.deliveredAt ? formatDate(stop.deliveredAt) : '—'}
                          </td>
                          <td className="py-2 text-right">
                            {stop.status === 'PLANNED' ? (
                              <button
                                type="button"
                                disabled={confirmingStopId === stop.id}
                                onClick={() => void onConfirmDelivery(stop, route.id)}
                                className="rounded bg-brand-600 px-3 py-1 text-xs font-medium text-white hover:bg-brand-700 disabled:opacity-50"
                              >
                                {confirmingStopId === stop.id ? 'Confirming…' : 'Confirm delivery'}
                              </button>
                            ) : null}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </td>
            </tr>
          ) : null}
        </Fragment>
      );
    });
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
          <h1 className="text-2xl font-bold text-slate-900">Delivery routes</h1>
          <p className="mt-1 text-sm text-slate-500">
            Plan routes, dispatch to drivers, and confirm deliveries stop by stop.
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
                  <tbody>{renderDispatchedRouteRows()}</tbody>
                </table>
              </div>
            )}
          </section>

          <section>
            <h2 className="mb-3 text-lg font-semibold text-slate-900">Completed routes</h2>
            {completedRoutes.length === 0 ? (
              <div className="rounded border border-slate-200 bg-white p-4 text-sm text-slate-600">
                No completed routes yet. Confirm all stops on a dispatched route to complete it.
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
                      <th className="px-4 py-3">Completed</th>
                    </tr>
                  </thead>
                  <tbody>
                    {completedRoutes.map((route) => (
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
                          {route.completedAt ? formatDate(route.completedAt) : '—'}
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

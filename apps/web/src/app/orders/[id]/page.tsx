'use client';

import { OrderState, type OrderDetail } from '@binexus/types';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { useEffect, useState } from 'react';

import { api } from '../../../lib/api';
import { formatApiError } from '../../../lib/error-messages';
import { formatDate, formatMoney, shortId } from '../../../lib/format';
import { hasStoredSession } from '../../../lib/token-storage';

export default function OrderDetailPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const [order, setOrder] = useState<OrderDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [approving, setApproving] = useState(false);
  const [cancelling, setCancelling] = useState(false);
  const [requeueing, setRequeueing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function loadOrder(id: string): Promise<void> {
    const detail = await api.getOrder(id);
    setOrder(detail);
    setError(null);
  }

  useEffect(() => {
    if (!hasStoredSession()) {
      router.replace('/login');
      return;
    }

    const id = params.id;
    if (!id) return;

    let cancelled = false;
    (async () => {
      try {
        await loadOrder(id);
      } catch (err) {
        if (!cancelled) {
          setError(formatApiError(err));
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [params.id, router]);

  async function onApprove(): Promise<void> {
    const id = params.id;
    if (!id || !order || order.state !== OrderState.DRAFT) return;

    setApproving(true);
    setError(null);
    try {
      await api.approveOrder(id);
      await loadOrder(id);
    } catch (err) {
      setError(formatApiError(err));
    } finally {
      setApproving(false);
    }
  }

  async function onCancel(): Promise<void> {
    const id = params.id;
    if (
      !id ||
      !order ||
      (order.state !== OrderState.DRAFT &&
        order.state !== OrderState.APPROVED &&
        order.state !== OrderState.DELIVERY_ATTEMPT_FAILED)
    ) {
      return;
    }

    const confirmed = window.confirm('Cancel this order? This cannot be undone.');
    if (!confirmed) return;

    setCancelling(true);
    setError(null);
    try {
      const reason =
        order.state === OrderState.DELIVERY_ATTEMPT_FAILED
          ? 'Cancelled after failed delivery'
          : 'Cancelled from web';
      await api.cancelOrder(id, { reason });
      await loadOrder(id);
    } catch (err) {
      setError(formatApiError(err));
    } finally {
      setCancelling(false);
    }
  }

  async function onRequeueForDelivery(): Promise<void> {
    const id = params.id;
    if (!id || !order || order.state !== OrderState.DELIVERY_ATTEMPT_FAILED) return;

    const notes = window.prompt('Requeue notes (optional)?')?.trim();
    const confirmed = window.confirm('Requeue this order for a new delivery route?');
    if (!confirmed) return;

    setRequeueing(true);
    setError(null);
    try {
      await api.requeueFailedDeliveryOrder(id, notes ? { reason: notes } : {});
      await loadOrder(id);
    } catch (err) {
      setError(formatApiError(err));
    } finally {
      setRequeueing(false);
    }
  }

  return (
    <main className="mx-auto min-h-screen max-w-3xl p-6">
      <Link href="/orders" className="text-sm font-medium text-brand-600 hover:text-brand-700">
        ← Back to orders
      </Link>

      {loading ? (
        <p className="mt-6 text-sm text-slate-500">Loading order…</p>
      ) : error ? (
        <div className="mt-6 rounded border border-red-200 bg-red-50 p-3 text-sm text-red-700">
          {error}
        </div>
      ) : order ? (
        <div className="mt-6 space-y-6">
          <header className="flex flex-wrap items-start justify-between gap-4">
            <div>
              <p className="text-xs font-semibold uppercase tracking-wide text-brand-600">
                Order detail
              </p>
              <h1 className="text-2xl font-bold text-slate-900">#{shortId(order.id)}</h1>
              <p className="mt-1 text-sm text-slate-500">Full id: {order.id}</p>
            </div>
            <div className="flex flex-wrap gap-2">
              {order.state === OrderState.DRAFT ? (
                <button
                  type="button"
                  disabled={approving || cancelling}
                  onClick={() => void onApprove()}
                  className="h-10 rounded bg-brand-600 px-4 text-sm font-medium text-white hover:bg-brand-700 disabled:bg-brand-300"
                >
                  {approving ? 'Approving…' : 'Approve order'}
                </button>
              ) : null}
              {order.state === OrderState.DRAFT || order.state === OrderState.APPROVED ? (
                <button
                  type="button"
                  disabled={approving || cancelling || requeueing}
                  onClick={() => void onCancel()}
                  className="h-10 rounded border border-red-200 px-4 text-sm font-medium text-red-700 hover:bg-red-50 disabled:text-red-300"
                >
                  {cancelling ? 'Cancelling…' : 'Cancel order'}
                </button>
              ) : null}
              {order.state === OrderState.DELIVERY_ATTEMPT_FAILED ? (
                <>
                  <button
                    type="button"
                    disabled={cancelling || requeueing}
                    onClick={() => void onRequeueForDelivery()}
                    className="h-10 rounded bg-brand-600 px-4 text-sm font-medium text-white hover:bg-brand-700 disabled:bg-brand-300"
                  >
                    {requeueing ? 'Requeueing…' : 'Reintentar entrega'}
                  </button>
                  <button
                    type="button"
                    disabled={cancelling || requeueing}
                    onClick={() => void onCancel()}
                    className="h-10 rounded border border-red-200 px-4 text-sm font-medium text-red-700 hover:bg-red-50 disabled:text-red-300"
                  >
                    {cancelling ? 'Cancelling…' : 'Cancelar orden'}
                  </button>
                </>
              ) : null}
            </div>
          </header>

          <section className="grid gap-3 rounded-lg border border-slate-200 bg-white p-4 text-sm sm:grid-cols-2">
            <Field label="State" value={order.state} />
            <Field label="Payment" value={order.paymentMethod} />
            <Field label="Customer" value={order.customerId} />
            <Field label="Branch" value={order.branchId} />
            <Field label="Total" value={formatMoney(order.totalCents, order.currency)} />
            <Field label="Created" value={formatDate(order.createdAt)} />
            <Field label="Updated" value={formatDate(order.updatedAt)} />
          </section>

          <section>
            <h2 className="mb-2 text-sm font-semibold uppercase tracking-wide text-slate-500">
              Lines
            </h2>
            <div className="overflow-hidden rounded-lg border border-slate-200 bg-white">
              <table className="w-full text-left text-sm">
                <thead className="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                  <tr>
                    <th className="px-4 py-3">Product</th>
                    <th className="px-4 py-3">Qty</th>
                    <th className="px-4 py-3">Unit</th>
                    <th className="px-4 py-3">Line total</th>
                  </tr>
                </thead>
                <tbody>
                  {order.lines.map((line) => (
                    <tr key={line.id} className="border-b border-slate-100 last:border-0">
                      <td className="px-4 py-3">
                        <div className="font-medium text-slate-900">{line.productName}</div>
                        <div className="text-xs text-slate-400">{line.productId}</div>
                      </td>
                      <td className="px-4 py-3">{line.quantity}</td>
                      <td className="px-4 py-3">
                        {formatMoney(line.unitPriceCents, order.currency)}
                      </td>
                      <td className="px-4 py-3">
                        {formatMoney(line.lineTotalCents, order.currency)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>

          <section>
            <h2 className="mb-2 text-sm font-semibold uppercase tracking-wide text-slate-500">
              Transitions
            </h2>
            <ul className="space-y-2">
              {order.transitions.map((transition) => (
                <li
                  key={transition.id}
                  className="rounded border border-slate-200 bg-white px-4 py-3 text-sm"
                >
                  <div className="font-medium text-slate-900">
                    {transition.fromState ?? '—'} → {transition.toState}
                  </div>
                  <div className="mt-1 text-xs text-slate-500">
                    {formatDate(transition.occurredAt)}
                    {transition.reason ? ` · ${transition.reason}` : ''}
                  </div>
                </li>
              ))}
            </ul>
          </section>
        </div>
      ) : null}
    </main>
  );
}

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <div className="text-xs font-medium uppercase tracking-wide text-slate-500">{label}</div>
      <div className="mt-1 text-slate-900">{value}</div>
    </div>
  );
}

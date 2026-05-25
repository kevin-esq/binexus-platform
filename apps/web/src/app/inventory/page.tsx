'use client';

import type { BranchId, StockItemSummary, StockTransferSummary } from '@binexus/types';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useCallback, useEffect, useState } from 'react';

import { api } from '../../lib/api';
import { formatDate } from '../../lib/format';
import { hasStoredSession } from '../../lib/token-storage';

export default function InventoryPage() {
  const router = useRouter();
  const [items, setItems] = useState<StockItemSummary[]>([]);
  const [pendingTransfers, setPendingTransfers] = useState<StockTransferSummary[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [adjustingId, setAdjustingId] = useState<string | null>(null);
  const [transferringId, setTransferringId] = useState<string | null>(null);
  const [transferActionId, setTransferActionId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const loadAll = useCallback(async (stockCursor?: string) => {
    const [stockResult, transfersResult] = await Promise.all([
      api.listStockItems({ limit: 50, cursor: stockCursor }),
      api.listStockTransfers({ status: 'PENDING', limit: 50 }),
    ]);
    if (stockCursor) {
      setItems((prev) => [...prev, ...stockResult.items]);
    } else {
      setItems(stockResult.items);
    }
    setNextCursor(stockResult.nextCursor);
    setPendingTransfers(transfersResult.items);
  }, []);

  useEffect(() => {
    if (!hasStoredSession()) {
      router.replace('/login');
      return;
    }

    let cancelled = false;
    (async () => {
      try {
        await loadAll();
        if (!cancelled) setError(null);
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Failed to load inventory');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [loadAll, router]);

  async function onRefresh(): Promise<void> {
    setRefreshing(true);
    try {
      await loadAll();
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to refresh inventory');
    } finally {
      setRefreshing(false);
    }
  }

  async function onAdjust(item: StockItemSummary): Promise<void> {
    const deltaRaw = window.prompt(
      `Adjust stock for ${item.productId} at ${item.branchId}\n\nDelta (+/- integer):`,
      '0',
    );
    if (deltaRaw === null) return;

    const delta = Number.parseInt(deltaRaw.trim(), 10);
    if (!Number.isInteger(delta) || delta === 0) {
      setError('Delta must be a non-zero integer.');
      return;
    }

    const reason = window.prompt('Reason (3–200 characters):');
    if (reason === null) return;
    if (reason.trim().length < 3) {
      setError('Reason must be at least 3 characters.');
      return;
    }

    setAdjustingId(item.id);
    try {
      await api.adjustStock({
        branchId: item.branchId,
        productId: item.productId,
        delta,
        reason: reason.trim(),
      });
      await loadAll();
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to adjust stock');
    } finally {
      setAdjustingId(null);
    }
  }

  async function onTransfer(item: StockItemSummary): Promise<void> {
    const destinationBranchId = window.prompt(
      `Transfer from ${item.branchId}\n\nDestination branch ID:`,
    );
    if (destinationBranchId === null) return;
    if (!destinationBranchId.trim()) {
      setError('Destination branch is required.');
      return;
    }

    const quantityRaw = window.prompt(
      `Transfer ${item.productId}\n\nQuantity (positive integer, max available ${item.available}):`,
      '1',
    );
    if (quantityRaw === null) return;

    const quantity = Number.parseInt(quantityRaw.trim(), 10);
    if (!Number.isInteger(quantity) || quantity <= 0) {
      setError('Quantity must be a positive integer.');
      return;
    }

    const reason = window.prompt('Reason (optional, 3–200 characters):');
    if (reason === null) return;
    if (reason.trim().length > 0 && reason.trim().length < 3) {
      setError('Reason must be at least 3 characters when provided.');
      return;
    }

    setTransferringId(item.id);
    try {
      await api.createStockTransfer({
        sourceBranchId: item.branchId,
        destinationBranchId: destinationBranchId.trim() as BranchId,
        productId: item.productId,
        quantity,
        reason: reason.trim() || undefined,
      });
      await loadAll();
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create transfer');
    } finally {
      setTransferringId(null);
    }
  }

  async function onReceiveTransfer(transfer: StockTransferSummary): Promise<void> {
    if (!window.confirm(`Receive transfer ${transfer.id} (${transfer.quantity} units)?`)) return;

    setTransferActionId(transfer.id);
    try {
      await api.receiveStockTransfer(transfer.id);
      await loadAll();
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to receive transfer');
    } finally {
      setTransferActionId(null);
    }
  }

  async function onCancelTransfer(transfer: StockTransferSummary): Promise<void> {
    if (!window.confirm(`Cancel transfer ${transfer.id}?`)) return;

    setTransferActionId(transfer.id);
    try {
      await api.cancelStockTransfer(transfer.id);
      await loadAll();
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to cancel transfer');
    } finally {
      setTransferActionId(null);
    }
  }

  async function onLoadMore(): Promise<void> {
    if (!nextCursor || loadingMore) return;
    setLoadingMore(true);
    try {
      const stockResult = await api.listStockItems({ limit: 50, cursor: nextCursor });
      setItems((prev) => [...prev, ...stockResult.items]);
      setNextCursor(stockResult.nextCursor);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load more stock');
    } finally {
      setLoadingMore(false);
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
            Inventory
          </p>
          <h1 className="text-2xl font-bold text-slate-900">Stock levels</h1>
          <p className="mt-1 text-sm text-slate-500">
            On hand, reserved, available, adjustments, and branch transfers.
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
        <p className="text-sm text-slate-500">Loading inventory…</p>
      ) : error ? (
        <div className="rounded border border-red-200 bg-red-50 p-3 text-sm text-red-700">
          {error}
        </div>
      ) : (
        <>
          {pendingTransfers.length > 0 ? (
            <section className="mb-6 overflow-hidden rounded-lg border border-amber-200 bg-amber-50/50">
              <div className="border-b border-amber-200 px-4 py-3">
                <h2 className="text-sm font-semibold text-slate-900">Pending transfers</h2>
                <p className="text-xs text-slate-600">
                  Stock reserved at source until received or cancelled.
                </p>
              </div>
              <table className="w-full text-left text-sm">
                <thead className="border-b border-amber-200 bg-amber-50 text-xs uppercase tracking-wide text-slate-500">
                  <tr>
                    <th className="px-4 py-2">Product</th>
                    <th className="px-4 py-2">From → To</th>
                    <th className="px-4 py-2 text-right">Qty</th>
                    <th className="px-4 py-2">Created</th>
                    <th className="px-4 py-2 text-right">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {pendingTransfers.map((transfer) => (
                    <tr key={transfer.id} className="border-b border-amber-100 last:border-0">
                      <td className="px-4 py-2 font-medium text-slate-900">{transfer.productId}</td>
                      <td className="px-4 py-2 text-slate-700">
                        {transfer.sourceBranchId} → {transfer.destinationBranchId}
                      </td>
                      <td className="px-4 py-2 text-right text-slate-700">{transfer.quantity}</td>
                      <td className="px-4 py-2 text-slate-500">{formatDate(transfer.createdAt)}</td>
                      <td className="px-4 py-2 text-right">
                        <div className="flex justify-end gap-2">
                          <button
                            type="button"
                            disabled={transferActionId === transfer.id}
                            onClick={() => void onReceiveTransfer(transfer)}
                            className="rounded border border-slate-300 px-2 py-1 text-xs font-medium text-slate-700 hover:bg-white disabled:opacity-50"
                          >
                            Receive
                          </button>
                          <button
                            type="button"
                            disabled={transferActionId === transfer.id}
                            onClick={() => void onCancelTransfer(transfer)}
                            className="rounded border border-slate-300 px-2 py-1 text-xs font-medium text-slate-600 hover:bg-white disabled:opacity-50"
                          >
                            Cancel
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </section>
          ) : null}

          {items.length === 0 ? (
            <div className="rounded border border-slate-200 bg-white p-6 text-sm text-slate-600">
              No stock items yet. Run <code className="text-xs">pnpm db:seed</code> to load demo
              products.
            </div>
          ) : (
            <div className="overflow-hidden rounded-lg border border-slate-200 bg-white">
              <table className="w-full text-left text-sm">
                <thead className="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                  <tr>
                    <th className="px-4 py-3">Product</th>
                    <th className="px-4 py-3">Branch</th>
                    <th className="px-4 py-3 text-right">On hand</th>
                    <th className="px-4 py-3 text-right">Reserved</th>
                    <th className="px-4 py-3 text-right">Available</th>
                    <th className="px-4 py-3">Updated</th>
                    <th className="px-4 py-3 text-right">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((item) => (
                    <tr key={item.id} className="border-b border-slate-100 last:border-0">
                      <td className="px-4 py-3 font-medium text-slate-900">{item.productId}</td>
                      <td className="px-4 py-3 text-slate-700">{item.branchId}</td>
                      <td className="px-4 py-3 text-right text-slate-700">{item.onHand}</td>
                      <td className="px-4 py-3 text-right text-slate-700">{item.reserved}</td>
                      <td className="px-4 py-3 text-right font-medium text-slate-900">
                        {item.available}
                      </td>
                      <td className="px-4 py-3 text-slate-500">{formatDate(item.updatedAt)}</td>
                      <td className="px-4 py-3 text-right">
                        <div className="flex justify-end gap-2">
                          <button
                            type="button"
                            disabled={transferringId === item.id}
                            onClick={() => void onTransfer(item)}
                            className="rounded border border-slate-300 px-3 py-1 text-xs font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
                          >
                            {transferringId === item.id ? 'Transferring…' : 'Transfer'}
                          </button>
                          <button
                            type="button"
                            disabled={adjustingId === item.id}
                            onClick={() => void onAdjust(item)}
                            className="rounded border border-slate-300 px-3 py-1 text-xs font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
                          >
                            {adjustingId === item.id ? 'Adjusting…' : 'Adjust'}
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
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

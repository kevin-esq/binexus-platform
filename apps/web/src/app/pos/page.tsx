'use client';

import type { SalesSessionSummary, StockItemSummary } from '@binexus/types';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useCallback, useEffect, useMemo, useState } from 'react';

import { api } from '../../lib/api';
import { formatMoney } from '../../lib/format';
import { hasStoredSession } from '../../lib/token-storage';

type CartLine = {
  productId: string;
  productName: string;
  quantity: number;
  unitPriceCents: number;
};

export default function PosPage() {
  const router = useRouter();
  const [terminalId, setTerminalId] = useState('Caja 1');
  const [session, setSession] = useState<SalesSessionSummary | null>(null);
  const [stock, setStock] = useState<StockItemSummary[]>([]);
  const [cart, setCart] = useState<CartLine[]>([]);
  const [openingFloatCents, setOpeningFloatCents] = useState('50000');
  const [declaredClosingCents, setDeclaredClosingCents] = useState('');
  const [discrepancyReason, setDiscrepancyReason] = useState('');
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  const cartTotalCents = useMemo(
    () => cart.reduce((sum, line) => sum + line.quantity * line.unitPriceCents, 0),
    [cart],
  );

  const loadTerminalState = useCallback(async (terminal: string) => {
    const [sessionResult, stockResult] = await Promise.all([
      api.getCurrentSalesSession({ terminalId: terminal }),
      api.listStockItems({ limit: 50 }),
    ]);
    setSession(sessionResult.session);
    setStock(stockResult.items);
  }, []);

  useEffect(() => {
    if (!hasStoredSession()) {
      router.replace('/login');
      return;
    }

    let cancelled = false;
    (async () => {
      try {
        await loadTerminalState(terminalId);
        if (!cancelled) setError(null);
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Failed to load POS state');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [loadTerminalState, router, terminalId]);

  async function onOpenSession(): Promise<void> {
    const floatCents = Number.parseInt(openingFloatCents.trim(), 10);
    if (!Number.isInteger(floatCents) || floatCents < 0) {
      setError('Opening float must be a non-negative integer (cents).');
      return;
    }

    setBusy(true);
    try {
      const result = await api.openSalesSession({
        terminalId: terminalId.trim(),
        openingFloatCents: floatCents,
        currency: 'MXN',
      });
      setSession(result.session);
      setCart([]);
      setMessage(`Session opened on ${result.session.terminalId}`);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to open session');
    } finally {
      setBusy(false);
    }
  }

  function addToCart(item: StockItemSummary): void {
    const unitPriceRaw = window.prompt(`Unit price in cents for ${item.productId}:`, '10000');
    if (unitPriceRaw === null) return;
    const unitPriceCents = Number.parseInt(unitPriceRaw.trim(), 10);
    if (!Number.isInteger(unitPriceCents) || unitPriceCents < 0) {
      setError('Unit price must be a non-negative integer (cents).');
      return;
    }

    setCart((prev) => {
      const existing = prev.find((line) => line.productId === item.productId);
      if (existing) {
        return prev.map((line) =>
          line.productId === item.productId
            ? { ...line, quantity: line.quantity + 1, unitPriceCents }
            : line,
        );
      }
      return [
        ...prev,
        {
          productId: item.productId,
          productName: item.productId,
          quantity: 1,
          unitPriceCents,
        },
      ];
    });
    setError(null);
  }

  async function onCheckout(): Promise<void> {
    if (!session) {
      setError('Open a session before selling.');
      return;
    }
    if (cart.length === 0) {
      setError('Cart is empty.');
      return;
    }

    setBusy(true);
    try {
      await api.createSale(session.id, {
        currency: session.currency,
        lines: cart,
      });
      setCart([]);
      await loadTerminalState(terminalId);
      setMessage('Sale completed (cash).');
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to complete sale');
    } finally {
      setBusy(false);
    }
  }

  async function onCloseSession(): Promise<void> {
    if (!session) return;

    const declaredRaw =
      declaredClosingCents.trim() ||
      window.prompt('Declared closing amount in cents:', String(session.openingFloatCents));
    if (declaredRaw === null) return;

    const declared = Number.parseInt(declaredRaw.trim(), 10);
    if (!Number.isInteger(declared) || declared < 0) {
      setError('Declared closing must be a non-negative integer (cents).');
      return;
    }

    setBusy(true);
    try {
      const result = await api.closeSalesSession(session.id, {
        declaredClosingCents: declared,
        discrepancyReason: discrepancyReason.trim() || undefined,
      });
      setSession(null);
      setDeclaredClosingCents('');
      setDiscrepancyReason('');
      setMessage(
        result.session.discrepancyCents
          ? `Session closed with discrepancy ${result.session.discrepancyCents} cents`
          : 'Session closed — arqueo matched',
      );
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to close session');
    } finally {
      setBusy(false);
    }
  }

  if (loading) {
    return <main className="mx-auto min-h-screen max-w-5xl p-6 text-slate-600">Loading POS…</main>;
  }

  return (
    <main className="mx-auto min-h-screen max-w-5xl p-6">
      <header className="mb-6 flex flex-wrap items-center justify-between gap-4">
        <div>
          <p className="text-xs font-semibold uppercase tracking-wide text-brand-600">POS · F5.1</p>
          <h1 className="text-2xl font-bold text-slate-900">Retail register</h1>
          <p className="mt-1 text-sm text-slate-600">
            Session is scoped per terminal. Enable <code className="text-xs">POS_RETAIL</code> for
            the tenant before using this screen.
          </p>
        </div>
        <div className="flex gap-2">
          <Link
            href="/"
            className="inline-flex h-9 items-center rounded border border-slate-300 px-3 text-sm text-slate-700 hover:bg-slate-100"
          >
            Home
          </Link>
        </div>
      </header>

      {error ? (
        <div className="mb-4 rounded border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800">
          {error}
        </div>
      ) : null}
      {message ? (
        <div className="mb-4 rounded border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-800">
          {message}
        </div>
      ) : null}

      <section className="mb-6 grid gap-4 md:grid-cols-2">
        <div className="rounded border border-slate-200 bg-white p-4">
          <h2 className="font-semibold text-slate-900">Terminal</h2>
          <label className="mt-3 block text-sm text-slate-600">
            Terminal label
            <input
              className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
              value={terminalId}
              onChange={(e) => setTerminalId(e.target.value)}
              disabled={Boolean(session) || busy}
            />
          </label>
          {!session ? (
            <div className="mt-3 space-y-2">
              <label className="block text-sm text-slate-600">
                Opening float (cents)
                <input
                  className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
                  value={openingFloatCents}
                  onChange={(e) => setOpeningFloatCents(e.target.value)}
                  disabled={busy}
                />
              </label>
              <button
                type="button"
                onClick={() => void onOpenSession()}
                disabled={busy || !terminalId.trim()}
                className="rounded bg-brand-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-700 disabled:opacity-50"
              >
                Open session
              </button>
            </div>
          ) : (
            <div className="mt-3 space-y-1 text-sm text-slate-700">
              <p>
                <span className="font-medium">Status:</span> {session.status}
              </p>
              <p>
                <span className="font-medium">Opening float:</span>{' '}
                {formatMoney(session.openingFloatCents, session.currency)}
              </p>
              <p>
                <span className="font-medium">Session ID:</span> {session.id}
              </p>
            </div>
          )}
        </div>

        <div className="rounded border border-slate-200 bg-white p-4">
          <h2 className="font-semibold text-slate-900">Close session (arqueo)</h2>
          {session ? (
            <div className="mt-3 space-y-2">
              <label className="block text-sm text-slate-600">
                Declared closing (cents)
                <input
                  className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
                  value={declaredClosingCents}
                  onChange={(e) => setDeclaredClosingCents(e.target.value)}
                  disabled={busy}
                />
              </label>
              <label className="block text-sm text-slate-600">
                Discrepancy reason (ADMIN if mismatch)
                <input
                  className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
                  value={discrepancyReason}
                  onChange={(e) => setDiscrepancyReason(e.target.value)}
                  disabled={busy}
                />
              </label>
              <button
                type="button"
                onClick={() => void onCloseSession()}
                disabled={busy}
                className="rounded border border-slate-400 px-4 py-2 text-sm font-medium text-slate-800 hover:bg-slate-100 disabled:opacity-50"
              >
                Close session
              </button>
            </div>
          ) : (
            <p className="mt-3 text-sm text-slate-600">No open session for this terminal.</p>
          )}
        </div>
      </section>

      <div className="grid gap-4 lg:grid-cols-2">
        <section className="rounded border border-slate-200 bg-white p-4">
          <h2 className="font-semibold text-slate-900">Stock (add to cart)</h2>
          <ul className="mt-3 divide-y divide-slate-100">
            {stock.map((item) => (
              <li key={item.id} className="flex items-center justify-between gap-3 py-2 text-sm">
                <div>
                  <div className="font-medium text-slate-900">{item.productId}</div>
                  <div className="text-slate-500">
                    available {item.available} · branch {item.branchId}
                  </div>
                </div>
                <button
                  type="button"
                  disabled={!session || busy || item.available < 1}
                  onClick={() => addToCart(item)}
                  className="rounded border border-brand-600 px-3 py-1 text-brand-700 hover:bg-brand-50 disabled:opacity-40"
                >
                  Add
                </button>
              </li>
            ))}
          </ul>
        </section>

        <section className="rounded border border-slate-200 bg-white p-4">
          <h2 className="font-semibold text-slate-900">Cart (cash checkout)</h2>
          {cart.length === 0 ? (
            <p className="mt-3 text-sm text-slate-600">No lines yet.</p>
          ) : (
            <ul className="mt-3 space-y-2 text-sm">
              {cart.map((line) => (
                <li key={line.productId} className="flex justify-between gap-2">
                  <span>
                    {line.productName} × {line.quantity}
                  </span>
                  <span>{formatMoney(line.quantity * line.unitPriceCents, 'MXN')}</span>
                </li>
              ))}
            </ul>
          )}
          <div className="mt-4 flex items-center justify-between border-t border-slate-100 pt-3">
            <span className="font-semibold text-slate-900">Total</span>
            <span className="font-semibold">{formatMoney(cartTotalCents, 'MXN')}</span>
          </div>
          <button
            type="button"
            onClick={() => void onCheckout()}
            disabled={!session || busy || cart.length === 0}
            className="mt-4 w-full rounded bg-brand-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-700 disabled:opacity-50"
          >
            Charge cash
          </button>
        </section>
      </div>
    </main>
  );
}

'use client';

import type { PaymentMethod, SalesSessionSummary, StockItemSummary } from '@binexus/types';
import { POS_WALK_IN_PAYMENT_METHODS } from '@binexus/types';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useCallback, useEffect, useMemo, useState } from 'react';

import { api } from '../../lib/api';
import { formatApiError } from '../../lib/error-messages';
import { formatMoney } from '../../lib/format';
import { hasStoredSession } from '../../lib/token-storage';

type CartLine = {
  productId: string;
  productName: string;
  quantity: number;
  unitPriceCents: number;
};

type PaymentLine = {
  method: PaymentMethod;
  amountCents: string;
};

const DEFAULT_PAYMENT_LINE: PaymentLine = { method: 'CASH', amountCents: '' };

function availablePaymentMethods(
  paymentLines: PaymentLine[],
  lineIndex: number,
): readonly PaymentMethod[] {
  const usedElsewhere = new Set(
    paymentLines.filter((_, index) => index !== lineIndex).map((line) => line.method),
  );
  return POS_WALK_IN_PAYMENT_METHODS.filter(
    (method) => paymentLines[lineIndex]?.method === method || !usedElsewhere.has(method),
  );
}

function nextUnusedPaymentMethod(paymentLines: PaymentLine[]): PaymentMethod | null {
  const used = new Set(paymentLines.map((line) => line.method));
  return POS_WALK_IN_PAYMENT_METHODS.find((method) => !used.has(method)) ?? null;
}

export default function PosPage() {
  const router = useRouter();
  const [terminalInput, setTerminalInput] = useState('Caja 1');
  const [terminalId, setTerminalId] = useState('Caja 1');
  const [session, setSession] = useState<SalesSessionSummary | null>(null);
  const [stock, setStock] = useState<StockItemSummary[]>([]);
  const [cart, setCart] = useState<CartLine[]>([]);
  const [paymentLines, setPaymentLines] = useState<PaymentLine[]>([{ ...DEFAULT_PAYMENT_LINE }]);
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

  const paymentsTotalCents = useMemo(
    () =>
      paymentLines.reduce((sum, line) => {
        const amount = Number.parseInt(line.amountCents.trim(), 10);
        return sum + (Number.isInteger(amount) && amount > 0 ? amount : 0);
      }, 0),
    [paymentLines],
  );

  const paymentsMatchTotal = cartTotalCents > 0 && paymentsTotalCents === cartTotalCents;

  const canAddPaymentLine = paymentLines.length < POS_WALK_IN_PAYMENT_METHODS.length;

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
          setError(formatApiError(err));
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [loadTerminalState, router, terminalId]);

  function resetPaymentLines(): void {
    setPaymentLines([{ ...DEFAULT_PAYMENT_LINE }]);
  }

  async function switchTerminal(): Promise<void> {
    const nextTerminal = terminalInput.trim();
    if (!nextTerminal) {
      setError('Terminal label is required.');
      return;
    }
    if (nextTerminal === terminalId) return;

    if (cart.length > 0) {
      const confirmed = window.confirm('Cambiar de terminal vacía el carrito actual. ¿Continuar?');
      if (!confirmed) {
        setTerminalInput(terminalId);
        return;
      }
    }

    setBusy(true);
    try {
      setTerminalId(nextTerminal);
      setCart([]);
      resetPaymentLines();
      await loadTerminalState(nextTerminal);
      setMessage(`Terminal cargada: ${nextTerminal}`);
      setError(null);
    } catch (err) {
      setTerminalInput(terminalId);
      setError(formatApiError(err));
    } finally {
      setBusy(false);
    }
  }

  async function onOpenSession(): Promise<void> {
    const terminal = terminalInput.trim();
    if (!terminal) {
      setError('Terminal label is required.');
      return;
    }

    const floatCents = Number.parseInt(openingFloatCents.trim(), 10);
    if (!Number.isInteger(floatCents) || floatCents < 0) {
      setError('Opening float must be a non-negative integer (cents).');
      return;
    }

    setBusy(true);
    try {
      const result = await api.openSalesSession({
        terminalId: terminal,
        openingFloatCents: floatCents,
        currency: 'MXN',
      });
      setTerminalId(terminal);
      setSession(result.session);
      setCart([]);
      resetPaymentLines();
      setMessage(`Session opened on ${result.session.terminalId}`);
      setError(null);
    } catch (err) {
      setError(formatApiError(err));
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

  function addPaymentLine(): void {
    const nextMethod = nextUnusedPaymentMethod(paymentLines);
    if (!nextMethod) return;
    setPaymentLines((prev) => [...prev, { method: nextMethod, amountCents: '' }]);
  }

  function removePaymentLine(index: number): void {
    setPaymentLines((prev) => (prev.length <= 1 ? prev : prev.filter((_, i) => i !== index)));
  }

  function updatePaymentLine(index: number, patch: Partial<PaymentLine>): void {
    setPaymentLines((prev) => prev.map((line, i) => (i === index ? { ...line, ...patch } : line)));
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
    if (!paymentsMatchTotal) {
      setError('Payment lines must sum exactly to the cart total.');
      return;
    }

    const payments = paymentLines.map((line) => {
      const amountCents = Number.parseInt(line.amountCents.trim(), 10);
      return { method: line.method, amountCents };
    });

    setBusy(true);
    try {
      await api.createSale(session.id, {
        currency: session.currency,
        lines: cart,
        payments,
      });
      setCart([]);
      resetPaymentLines();
      await loadTerminalState(terminalId);
      setMessage('Sale completed.');
      setError(null);
    } catch (err) {
      setError(formatApiError(err));
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
      setError(formatApiError(err));
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
          <p className="text-xs font-semibold uppercase tracking-wide text-brand-600">POS · F5.2</p>
          <h1 className="text-2xl font-bold text-slate-900">Retail register</h1>
          <p className="mt-1 text-sm text-slate-600">
            Split payment supported. Session arqueo counts CASH captures only.
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
            <div className="mt-1 flex gap-2">
              <input
                className="min-w-0 flex-1 rounded border border-slate-300 px-3 py-2 text-sm"
                value={terminalInput}
                onChange={(e) => setTerminalInput(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') void switchTerminal();
                }}
                disabled={busy}
              />
              <button
                type="button"
                onClick={() => void switchTerminal()}
                disabled={busy || !terminalInput.trim() || terminalInput.trim() === terminalId}
                className="shrink-0 rounded border border-slate-300 px-3 py-2 text-sm text-slate-700 hover:bg-slate-100 disabled:opacity-50"
              >
                Cargar
              </button>
            </div>
          </label>
          {session ? (
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
              <p className="pt-1 text-xs text-slate-500">
                Una sesión OPEN por terminal. Para abrir otra en <strong>{terminalId}</strong>,
                cierra esta con arqueo. Para operar otra caja sin cerrar, cambia el label y pulsa
                Cargar.
              </p>
            </div>
          ) : (
            <div className="mt-3 space-y-2">
              <p className="text-xs text-slate-500">
                Sin sesión abierta en <strong>{terminalId}</strong>.
              </p>
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
          <h2 className="font-semibold text-slate-900">Cart &amp; checkout</h2>
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

          <div className="mt-4 border-t border-slate-100 pt-4">
            <div className="mb-2 flex items-center justify-between">
              <h3 className="text-sm font-semibold text-slate-900">Payment lines</h3>
              <button
                type="button"
                onClick={addPaymentLine}
                disabled={!session || busy || cart.length === 0 || !canAddPaymentLine}
                className="text-xs font-medium text-brand-700 hover:underline disabled:opacity-40"
              >
                Add payment line
              </button>
            </div>
            <ul className="space-y-2">
              {paymentLines.map((line, index) => (
                <li key={index} className="flex flex-wrap items-center gap-2">
                  <select
                    className="rounded border border-slate-300 px-2 py-1 text-sm"
                    value={line.method}
                    disabled={!session || busy}
                    onChange={(e) =>
                      updatePaymentLine(index, { method: e.target.value as PaymentMethod })
                    }
                  >
                    {availablePaymentMethods(paymentLines, index).map((method) => (
                      <option key={method} value={method}>
                        {method}
                      </option>
                    ))}
                  </select>
                  <input
                    className="min-w-0 flex-1 rounded border border-slate-300 px-2 py-1 text-sm"
                    placeholder="Amount (cents)"
                    value={line.amountCents}
                    disabled={!session || busy}
                    onChange={(e) => updatePaymentLine(index, { amountCents: e.target.value })}
                  />
                  <button
                    type="button"
                    onClick={() => removePaymentLine(index)}
                    disabled={!session || busy || paymentLines.length <= 1}
                    className="text-xs text-slate-500 hover:text-slate-800 disabled:opacity-40"
                  >
                    Remove
                  </button>
                </li>
              ))}
            </ul>
            <p
              className={`mt-2 text-xs ${paymentsMatchTotal ? 'text-emerald-700' : 'text-slate-600'}`}
            >
              Paid {formatMoney(paymentsTotalCents, 'MXN')} / {formatMoney(cartTotalCents, 'MXN')}
            </p>
          </div>

          <button
            type="button"
            onClick={() => void onCheckout()}
            disabled={!session || busy || cart.length === 0 || !paymentsMatchTotal}
            className="mt-4 w-full rounded bg-brand-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-700 disabled:opacity-50"
          >
            Complete sale
          </button>
        </section>
      </div>
    </main>
  );
}

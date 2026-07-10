import Link from 'next/link';

export default function HomePage() {
  return (
    <main className="mx-auto flex min-h-screen max-w-3xl flex-col items-start justify-center gap-6 p-8">
      <div>
        <p className="text-xs font-semibold uppercase tracking-wide text-brand-600">
          F5.1 complete · Next: F5.2 split payment
        </p>
        <h1 className="mt-1 text-4xl font-bold text-slate-900">Binexus Platform</h1>
        <p className="mt-3 max-w-prose text-slate-600">
          Operational SaaS — modular monolith, event-driven, offline-first, multi-tenant. The web UI
          runs orders through inventory, picking, delivery routes, proof uploads, failed-delivery
          resolution, and route liquidation end-to-end.
        </p>
      </div>

      <div className="flex gap-3">
        <Link
          href="/login"
          className="inline-flex h-10 items-center justify-center rounded bg-brand-600 px-4 text-sm font-medium text-white hover:bg-brand-700"
        >
          Go to login
        </Link>
        <Link
          href="/orders"
          className="inline-flex h-10 items-center justify-center rounded border border-brand-600 px-4 text-sm font-medium text-brand-700 hover:bg-brand-50"
        >
          Orders
        </Link>
        <Link
          href="/inventory"
          className="inline-flex h-10 items-center justify-center rounded border border-brand-600 px-4 text-sm font-medium text-brand-700 hover:bg-brand-50"
        >
          Inventory
        </Link>
        <Link
          href="/warehouse"
          className="inline-flex h-10 items-center justify-center rounded border border-brand-600 px-4 text-sm font-medium text-brand-700 hover:bg-brand-50"
        >
          Warehouse
        </Link>
        <Link
          href="/logistics"
          className="inline-flex h-10 items-center justify-center rounded border border-brand-600 px-4 text-sm font-medium text-brand-700 hover:bg-brand-50"
        >
          Logistics
        </Link>
        <Link
          href="/pos"
          className="inline-flex h-10 items-center justify-center rounded border border-brand-600 px-4 text-sm font-medium text-brand-700 hover:bg-brand-50"
        >
          POS
        </Link>
        <a
          href="http://localhost:3001/health"
          target="_blank"
          rel="noreferrer"
          className="inline-flex h-10 items-center justify-center rounded border border-slate-300 px-4 text-sm font-medium text-slate-700 hover:bg-slate-100"
        >
          API health
        </a>
      </div>

      <section className="mt-6 grid w-full grid-cols-2 gap-3 text-sm text-slate-600">
        <div className="rounded border border-slate-200 bg-white p-4">
          <div className="font-semibold text-slate-900">Bounded contexts</div>
          <ul className="mt-2 list-disc pl-4">
            <li>F0 · Identity (active)</li>
            <li>F1 · Orders (complete)</li>
            <li>F2 · Inventory (complete)</li>
            <li>F3 · Warehouse (complete)</li>
            <li>F4 · Logistics (complete)</li>
            <li>F5 · Sales / POS (5.1 complete)</li>
          </ul>
        </div>
        <div className="rounded border border-slate-200 bg-white p-4">
          <div className="font-semibold text-slate-900">Foundations wired</div>
          <ul className="mt-2 list-disc pl-4">
            <li>JWT + RBAC + refresh</li>
            <li>Multi-tenant via ALS</li>
            <li>CQRS-lite commands</li>
            <li>Outbox events</li>
            <li>Feature flags</li>
            <li>Pino logging</li>
          </ul>
        </div>
      </section>
    </main>
  );
}

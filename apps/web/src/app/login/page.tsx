'use client';

import { createBinexusClient } from '@binexus/sdk';
import { useState, type FormEvent } from 'react';

const api = createBinexusClient({
  baseUrl: process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:3001',
});

export default function LoginPage() {
  const [tenantSlug, setTenantSlug] = useState('acme');
  const [email, setEmail] = useState('admin@acme.test');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<{ accessToken: string; refreshToken: string } | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function onSubmit(e: FormEvent<HTMLFormElement>): Promise<void> {
    e.preventDefault();
    setError(null);
    setResult(null);
    setSubmitting(true);
    try {
      const tokens = await api.login({ tenantSlug, email, password });
      setResult(tokens);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Login failed');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main className="flex min-h-screen items-center justify-center p-6">
      <div className="w-full max-w-sm rounded-lg border border-slate-200 bg-white p-6 shadow-sm">
        <h1 className="text-xl font-semibold text-slate-900">Sign in</h1>
        <p className="mt-1 text-sm text-slate-500">Binexus admin console</p>

        <form className="mt-6 space-y-4" onSubmit={onSubmit}>
          <Field label="Tenant slug">
            <input
              required
              value={tenantSlug}
              onChange={(e) => setTenantSlug(e.target.value)}
              className="h-10 w-full rounded border border-slate-300 px-3 text-sm"
            />
          </Field>
          <Field label="Email">
            <input
              required
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              className="h-10 w-full rounded border border-slate-300 px-3 text-sm"
            />
          </Field>
          <Field label="Password">
            <input
              required
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className="h-10 w-full rounded border border-slate-300 px-3 text-sm"
            />
          </Field>

          <button
            type="submit"
            disabled={submitting}
            className="h-10 w-full rounded bg-brand-600 text-sm font-medium text-white hover:bg-brand-700 disabled:bg-brand-300"
          >
            {submitting ? 'Signing in…' : 'Sign in'}
          </button>
        </form>

        {error ? (
          <div className="mt-4 rounded border border-red-200 bg-red-50 p-3 text-sm text-red-700">
            {error}
          </div>
        ) : null}

        {result ? (
          <div className="mt-4 rounded border border-emerald-200 bg-emerald-50 p-3 text-xs text-emerald-700">
            <div className="font-semibold">Logged in.</div>
            <div className="mt-1 break-all">access: {result.accessToken.slice(0, 24)}…</div>
          </div>
        ) : null}
      </div>
    </main>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <span className="text-xs font-medium uppercase tracking-wide text-slate-500">{label}</span>
      <div className="mt-1">{children}</div>
    </label>
  );
}

'use client';

import { useRouter } from 'next/navigation';
import { useState, type FormEvent, type ReactNode } from 'react';

import { api } from '../../lib/api';
import { formatApiError } from '../../lib/error-messages';

export default function LoginPage() {
  const router = useRouter();
  const [tenantSlug, setTenantSlug] = useState('acme');
  const [email, setEmail] = useState('admin@acme.test');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function onSubmit(e: FormEvent<HTMLFormElement>): Promise<void> {
    e.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      await api.login({ tenantSlug, email, password });
      router.push('/orders');
    } catch (err) {
      setError(formatApiError(err));
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
          <Field id="tenant-slug" label="Tenant slug">
            <input
              id="tenant-slug"
              required
              autoComplete="organization"
              value={tenantSlug}
              onChange={(e) => setTenantSlug(e.target.value)}
              className="h-10 w-full rounded border border-slate-300 px-3 text-sm"
            />
          </Field>
          <Field id="email" label="Email">
            <input
              id="email"
              required
              type="email"
              autoComplete="username"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              className="h-10 w-full rounded border border-slate-300 px-3 text-sm"
            />
          </Field>
          <Field id="password" label="Password">
            <input
              id="password"
              required
              type="password"
              autoComplete="current-password"
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
      </div>
    </main>
  );
}

function Field({ id, label, children }: { id: string; label: string; children: ReactNode }) {
  return (
    <div className="block">
      <label htmlFor={id} className="text-xs font-medium uppercase tracking-wide text-slate-500">
        {label}
      </label>
      <div className="mt-1">{children}</div>
    </div>
  );
}

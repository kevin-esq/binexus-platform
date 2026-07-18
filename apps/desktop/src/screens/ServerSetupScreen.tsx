import { type FormEvent, useState } from 'react';

interface ServerSetupScreenProps {
  initialBranchUrl?: string;
  error?: string;
  onSubmit: (branchUrl: string) => Promise<void>;
}

export function ServerSetupScreen({
  initialBranchUrl = '',
  error,
  onSubmit,
}: ServerSetupScreenProps) {
  const [branchUrl, setBranchUrl] = useState(initialBranchUrl);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string>();

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setIsSubmitting(true);
    setSubmitError(undefined);

    try {
      await onSubmit(branchUrl.trim());
    } catch (cause) {
      setSubmitError(cause instanceof Error ? cause.message : 'Could not reach the Branch Server.');
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <section className="screen">
      <p className="eyebrow">Device setup</p>
      <h1>Connect to a Branch Server</h1>
      <p>Enter the server address supplied by your branch administrator.</p>
      <form onSubmit={handleSubmit}>
        <label htmlFor="branch-url">Branch Server URL</label>
        <input
          id="branch-url"
          autoComplete="url"
          inputMode="url"
          placeholder="http://192.168.1.20:5102"
          required
          type="url"
          value={branchUrl}
          onChange={(event) => setBranchUrl(event.target.value)}
        />
        {(submitError || error) && <p className="error">{submitError ?? error}</p>}
        <button disabled={isSubmitting} type="submit">
          {isSubmitting ? 'Checking server…' : 'Check server'}
        </button>
      </form>
    </section>
  );
}

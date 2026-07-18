import { type FormEvent, useState } from 'react';

interface PairingInputScreenProps {
  branchUrl: string;
  notice?: string;
  deviceFingerprintShort?: string;
  onSubmit: (pairingCode: string, terminalName: string) => Promise<void>;
}

/**
 * Pairing payload format for PR5: `{pairingSessionId}:{8-digit-code}`.
 * The admin QR / paste includes both because the machine API requires session id + code.
 * The code is cleared from React state immediately after invoking Rust.
 */
export function PairingInputScreen({
  branchUrl,
  notice,
  deviceFingerprintShort,
  onSubmit,
}: PairingInputScreenProps) {
  const [pairingPayload, setPairingPayload] = useState('');
  const [terminalName, setTerminalName] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string>();

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const payloadForInvoke = pairingPayload.trim();

    setPairingPayload('');
    setIsSubmitting(true);
    setError(undefined);

    try {
      await onSubmit(payloadForInvoke, terminalName.trim());
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Pairing could not start. Try a new code.');
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <section className="screen">
      <p className="eyebrow">Branch Server</p>
      <h1>Pair this terminal</h1>
      <p className="muted">{branchUrl}</p>
      {deviceFingerprintShort && (
        <p className="fingerprint">
          Device fingerprint: <code>{deviceFingerprintShort}</code>
        </p>
      )}
      {notice && <p className="error">{notice}</p>}
      <p>
        Paste the pairing payload from the administrator (session id and 8-digit code), then choose
        the terminal name.
      </p>
      <form onSubmit={handleSubmit}>
        <label htmlFor="pairing-payload">Pairing payload</label>
        <input
          id="pairing-payload"
          autoComplete="one-time-code"
          placeholder="0197…7890:12345678"
          required
          value={pairingPayload}
          onChange={(event) => setPairingPayload(event.target.value)}
        />
        <label htmlFor="terminal-name">Terminal name</label>
        <input
          id="terminal-name"
          autoComplete="off"
          maxLength={80}
          required
          value={terminalName}
          onChange={(event) => setTerminalName(event.target.value)}
        />
        {error && <p className="error">{error}</p>}
        <button disabled={isSubmitting} type="submit">
          {isSubmitting ? 'Starting pairing…' : 'Pair terminal'}
        </button>
      </form>
    </section>
  );
}

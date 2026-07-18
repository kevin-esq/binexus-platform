import type { AppUiState } from '../ipc/types';

interface RecoveryScreenProps {
  state: Extract<
    AppUiState,
    { kind: 'recoveryRequired' | 'pairedCredentialsUnavailable' | 'blocked' }
  >;
  onRetire: () => Promise<void>;
}

const titles = {
  recoveryRequired: 'Recovery required',
  pairedCredentialsUnavailable: 'Paired credentials unavailable',
  blocked: 'This device is blocked',
} as const;

export function RecoveryScreen({ state, onRetire }: RecoveryScreenProps) {
  const message = 'message' in state ? state.message : undefined;

  return (
    <section className="screen">
      <p className="eyebrow">Device attention</p>
      <h1>{titles[state.kind]}</h1>
      <p>
        {message ??
          'The local device state cannot be used safely. Retire this device and pair it again.'}
      </p>
      {state.kind !== 'blocked' && (
        <button type="button" onClick={() => void onRetire()}>
          Retire device and restart setup
        </button>
      )}
    </section>
  );
}

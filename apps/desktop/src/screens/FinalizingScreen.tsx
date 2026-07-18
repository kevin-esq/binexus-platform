interface FinalizingScreenProps {
  terminalName?: string;
  onResume: () => Promise<void>;
}

export function FinalizingScreen({ terminalName, onResume }: FinalizingScreenProps) {
  return (
    <section className="screen screen--centered" aria-live="polite">
      <p className="eyebrow">Finishing pairing</p>
      <h1>Securing {terminalName || 'this device'}</h1>
      <p>Store the device credential and confirm the pairing with the Branch Server.</p>
      <button type="button" onClick={() => void onResume()}>
        Retry finalization
      </button>
    </section>
  );
}

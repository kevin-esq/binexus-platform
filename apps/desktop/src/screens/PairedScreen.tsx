interface PairedScreenProps {
  branchUrl: string;
  terminalName: string;
  onRetire: () => Promise<void>;
}

export function PairedScreen({ branchUrl, terminalName, onRetire }: PairedScreenProps) {
  return (
    <section className="screen">
      <p className="eyebrow">Terminal paired</p>
      <h1>This terminal is ready</h1>
      <p>
        This placeholder confirms the terminal connection. The operational desktop experience
        follows in a later slice.
      </p>
      <p className="muted">
        {terminalName || 'Terminal'} · {branchUrl}
      </p>
      <button className="secondary" type="button" onClick={() => void onRetire()}>
        Retire this device
      </button>
    </section>
  );
}

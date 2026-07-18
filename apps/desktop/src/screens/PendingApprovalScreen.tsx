interface PendingApprovalScreenProps {
  deviceFingerprintShort?: string;
  terminalName?: string;
  onCancel: () => Promise<void>;
  onResume: () => Promise<void>;
}

export function PendingApprovalScreen({
  deviceFingerprintShort,
  terminalName,
  onCancel,
  onResume,
}: PendingApprovalScreenProps) {
  return (
    <section className="screen">
      <p className="eyebrow">Approval needed</p>
      <h1>Waiting for an administrator</h1>
      <p>
        Approve {terminalName ? `terminal ${terminalName}` : 'this pairing request'} in the Branch
        Server before this device can continue.
      </p>
      {deviceFingerprintShort && (
        <p className="fingerprint">
          Device fingerprint: <code>{deviceFingerprintShort}</code>
        </p>
      )}
      <div className="actions">
        <button type="button" onClick={() => void onResume()}>
          Check approval
        </button>
        <button className="secondary" type="button" onClick={() => void onCancel()}>
          Cancel pairing
        </button>
      </div>
    </section>
  );
}

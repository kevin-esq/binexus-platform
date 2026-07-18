/** Wire kinds match Rust `AppUiState` serde (`tag = kind`, `rename_all = camelCase`). */
export type AppUiState =
  | {
      kind: 'booting';
    }
  | {
      kind: 'needsServerSetup';
      branchUrl?: string;
      message?: string;
      deviceFingerprintShort?: string;
    }
  | {
      kind: 'needsPairing';
      branchUrl: string;
      deviceName?: string;
      message?: string;
      deviceFingerprintShort?: string;
    }
  | {
      kind: 'pendingApproval';
      branchUrl: string;
      pairingRequestId: string;
      deviceFingerprintShort?: string;
      terminalName: string;
    }
  | {
      kind: 'finalizing';
      branchUrl: string;
      terminalName: string;
      deviceFingerprintShort?: string;
    }
  | {
      kind: 'paired';
      branchUrl: string;
      terminalName: string;
      deviceFingerprintShort?: string;
    }
  | {
      kind: 'recoveryRequired';
      message?: string;
      deviceFingerprintShort?: string;
    }
  | { kind: 'pairedCredentialsUnavailable'; message?: string }
  | { kind: 'blocked'; message: string };

export interface ConfigureBranchUrlInput {
  branchUrl: string;
}

export interface BeginPairingInput {
  pairingCode: string;
  terminalName: string;
}

export interface PairingProgress {
  phase: string;
  fingerprintShort?: string;
  expiresAt?: string;
  errorCode?: string;
}

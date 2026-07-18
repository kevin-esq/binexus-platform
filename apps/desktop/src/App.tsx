import { useEffect, useState } from 'react';

import {
  beginPairing,
  cancelPairing,
  configureBranchUrl,
  getAppState,
  initializeDevice,
  listenToPairingProgress,
  retireDevice,
  resumePairing,
} from './ipc/client';
import { mergeAppUiState } from './ipc/mergeAppUiState';
import type { AppUiState } from './ipc/types';
import { BootScreen } from './screens/BootScreen';
import { FinalizingScreen } from './screens/FinalizingScreen';
import { PairedScreen } from './screens/PairedScreen';
import { PairingInputScreen } from './screens/PairingInputScreen';
import { PendingApprovalScreen } from './screens/PendingApprovalScreen';
import { RecoveryScreen } from './screens/RecoveryScreen';
import { ServerSetupScreen } from './screens/ServerSetupScreen';

export default function App() {
  const [state, setState] = useState<AppUiState>({ kind: 'booting' });
  const [terminalName, setTerminalName] = useState('');

  useEffect(() => {
    let active = true;
    let unlisten: (() => void) | undefined;

    async function load() {
      try {
        await getAppState();
        const next = await initializeDevice();
        if (active) {
          setState(next);
        }
      } catch (cause) {
        if (active) {
          setState({
            kind: 'blocked',
            message:
              cause instanceof Error ? cause.message : 'The device could not be initialized.',
          });
        }
      }
    }

    void listenToPairingProgress(async (progress) => {
      if (!active) {
        return;
      }
      if (progress.phase === 'pendingApproval') {
        try {
          const next = await getAppState();
          if (!active) {
            return;
          }
          setState((previous) =>
            mergeAppUiState(
              previous,
              next.kind === 'pendingApproval'
                ? {
                    ...next,
                    terminalName: terminalName || next.terminalName,
                  }
                : next,
              progress,
            ),
          );
        } catch {
          /* keep current */
        }
        return;
      }
      if (progress.phase === 'error') {
        try {
          const next = await getAppState();
          if (!active) {
            return;
          }
          if (next.kind === 'needsPairing') {
            const message =
              progress.errorCode === 'PAIRING_REJECTED'
                ? 'An administrator rejected this pairing. Request a new code with the same device.'
                : progress.errorCode === 'PAIRING_EXPIRED'
                  ? 'This pairing request expired. Request a new code with the same device.'
                  : 'Pairing failed. Request a new code with the same device.';
            setState((previous) => mergeAppUiState(previous, { ...next, message }, progress));
          } else {
            setState((previous) => mergeAppUiState(previous, next, progress));
          }
        } catch {
          /* keep current */
        }
        return;
      }
      if (progress.phase === 'paired' || progress.phase === 'finalizing') {
        try {
          const next = await getAppState();
          if (active) {
            setState((previous) => mergeAppUiState(previous, next, progress));
          }
        } catch {
          /* keep current */
        }
      }
    }).then((dispose) => {
      unlisten = dispose;
    });
    void load();

    return () => {
      active = false;
      unlisten?.();
    };
  }, [terminalName]);

  async function updateState(action: () => Promise<AppUiState>) {
    const next = await action();
    setState((previous) => mergeAppUiState(previous, next));
  }

  let screen;
  switch (state.kind) {
    case 'booting':
      screen = <BootScreen />;
      break;
    case 'needsServerSetup':
      screen = (
        <ServerSetupScreen
          error={state.message}
          initialBranchUrl={state.branchUrl}
          onSubmit={(branchUrl) => updateState(() => configureBranchUrl({ branchUrl }))}
        />
      );
      break;
    case 'needsPairing':
      screen = (
        <PairingInputScreen
          branchUrl={state.branchUrl}
          notice={state.message}
          deviceFingerprintShort={state.deviceFingerprintShort}
          onSubmit={(pairingCode, name) => {
            setTerminalName(name);
            return updateState(() => beginPairing({ pairingCode, terminalName: name }));
          }}
        />
      );
      break;
    case 'pendingApproval':
      screen = (
        <PendingApprovalScreen
          deviceFingerprintShort={state.deviceFingerprintShort}
          terminalName={state.terminalName || terminalName}
          onCancel={() => updateState(cancelPairing)}
          onResume={() => updateState(resumePairing)}
        />
      );
      break;
    case 'finalizing':
      screen = (
        <FinalizingScreen
          terminalName={state.terminalName || terminalName}
          onResume={() => updateState(resumePairing)}
        />
      );
      break;
    case 'paired':
      screen = (
        <PairedScreen
          branchUrl={state.branchUrl}
          terminalName={state.terminalName || terminalName}
          onRetire={() => updateState(retireDevice)}
        />
      );
      break;
    case 'recoveryRequired':
    case 'pairedCredentialsUnavailable':
    case 'blocked':
      screen = <RecoveryScreen state={state} onRetire={() => updateState(retireDevice)} />;
      break;
  }

  return (
    <main className="app-shell">
      <header className="app-header">
        <span>Binexus</span>
        <span className="muted">Branch Client</span>
      </header>
      {screen}
    </main>
  );
}

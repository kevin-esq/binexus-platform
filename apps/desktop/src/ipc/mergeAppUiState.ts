import type { AppUiState, PairingProgress } from './types';

function fingerprintOf(state: AppUiState): string | undefined {
  if ('deviceFingerprintShort' in state) {
    return state.deviceFingerprintShort;
  }
  return undefined;
}

/**
 * Merge Rust snapshots with progress events without letting a partial DTO erase
 * the identity-derived fingerprint already shown in the UI.
 */
export function mergeAppUiState(
  previous: AppUiState,
  next: AppUiState,
  progress?: PairingProgress,
): AppUiState {
  const retained = fingerprintOf(next) ?? progress?.fingerprintShort ?? fingerprintOf(previous);

  switch (next.kind) {
    case 'pendingApproval':
      return {
        ...next,
        deviceFingerprintShort: retained,
        terminalName:
          next.terminalName ||
          (previous.kind === 'pendingApproval' ? previous.terminalName : next.terminalName),
      };
    case 'needsPairing':
      return { ...next, deviceFingerprintShort: retained };
    case 'needsServerSetup':
      return { ...next, deviceFingerprintShort: retained };
    case 'finalizing':
      return { ...next, deviceFingerprintShort: retained };
    case 'paired':
      return { ...next, deviceFingerprintShort: retained };
    case 'recoveryRequired':
      return { ...next, deviceFingerprintShort: retained };
    default:
      return next;
  }
}

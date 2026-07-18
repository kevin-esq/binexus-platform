import { describe, expect, it } from 'vitest';

import { mergeAppUiState } from './mergeAppUiState';
import type { AppUiState } from './types';

const pending = (fp?: string): AppUiState => ({
  kind: 'pendingApproval',
  branchUrl: 'http://127.0.0.1:5102',
  pairingRequestId: '01970000-0000-7000-8000-000000000001',
  deviceFingerprintShort: fp,
  terminalName: 'Front Desk',
});

describe('mergeAppUiState', () => {
  it('keeps fingerprint when begin_pairing response includes it', () => {
    const next = pending('A1B2-C3D4-E5F6');
    const merged = mergeAppUiState({ kind: 'booting' }, next);
    expect(merged.kind).toBe('pendingApproval');
    if (merged.kind === 'pendingApproval') {
      expect(merged.deviceFingerprintShort).toBe('A1B2-C3D4-E5F6');
    }
  });

  it('does not erase fingerprint when progress event omits it', () => {
    const previous = pending('A1B2-C3D4-E5F6');
    const next = pending(undefined);
    const merged = mergeAppUiState(previous, next, { phase: 'pendingApproval' });
    expect(merged.kind).toBe('pendingApproval');
    if (merged.kind === 'pendingApproval') {
      expect(merged.deviceFingerprintShort).toBe('A1B2-C3D4-E5F6');
    }
  });

  it('preserves fingerprint across poll-style get_app_state snapshots', () => {
    const previous = pending('A1B2-C3D4-E5F6');
    const next = pending(undefined);
    const merged = mergeAppUiState(previous, next, { phase: 'finalizing' });
    if (merged.kind === 'pendingApproval') {
      expect(merged.deviceFingerprintShort).toBe('A1B2-C3D4-E5F6');
    }
  });

  it('preserves fingerprint on needsPairing after reject/expire', () => {
    const previous = pending('A1B2-C3D4-E5F6');
    const next: AppUiState = {
      kind: 'needsPairing',
      branchUrl: 'http://127.0.0.1:5102',
      message: 'An administrator rejected this pairing. Request a new code with the same device.',
    };
    const merged = mergeAppUiState(previous, next, {
      phase: 'error',
      errorCode: 'PAIRING_REJECTED',
    });
    expect(merged.kind).toBe('needsPairing');
    if (merged.kind === 'needsPairing') {
      expect(merged.deviceFingerprintShort).toBe('A1B2-C3D4-E5F6');
    }
  });

  it('uses progress fingerprint when previous and next lack it', () => {
    const merged = mergeAppUiState({ kind: 'booting' }, pending(undefined), {
      phase: 'pendingApproval',
      fingerprintShort: 'FFFF-EEEE-DDDD',
    });
    if (merged.kind === 'pendingApproval') {
      expect(merged.deviceFingerprintShort).toBe('FFFF-EEEE-DDDD');
    }
  });
});

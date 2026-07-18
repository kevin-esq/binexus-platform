import { invoke } from '@tauri-apps/api/core';
import { listen, type UnlistenFn } from '@tauri-apps/api/event';

import type {
  AppUiState,
  BeginPairingInput,
  ConfigureBranchUrlInput,
  PairingProgress,
} from './types';

export const getAppState = () => invoke<AppUiState>('get_app_state');

export const initializeDevice = () => invoke<AppUiState>('initialize_device');

export const configureBranchUrl = ({ branchUrl }: ConfigureBranchUrlInput) =>
  invoke<AppUiState>('configure_branch_url', { branchUrl });

export const beginPairing = ({ pairingCode, terminalName }: BeginPairingInput) =>
  invoke<AppUiState>('begin_pairing', { pairingCode, terminalName });

export const cancelPairing = () => invoke<AppUiState>('cancel_pairing');

export const resumePairing = () => invoke<AppUiState>('resume_pairing');

export const retireDevice = () => invoke<AppUiState>('retire_device');

export const listenToPairingProgress = (
  handler: (progress: PairingProgress) => void,
): Promise<UnlistenFn> =>
  listen<PairingProgress>('pairing-progress', ({ payload }) => handler(payload));

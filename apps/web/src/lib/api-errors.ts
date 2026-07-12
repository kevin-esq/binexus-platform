import { BinexusApiError } from '@binexus/sdk';

import { formatApiError } from './error-messages';
import { hasStoredSession } from './token-storage';

/** Map API errors for UI; redirect to login when the session was cleared. */
export function handleOperatorApiError(
  err: unknown,
  setError: (message: string) => void,
  redirectToLogin: () => void,
): void {
  if (err instanceof BinexusApiError && (err.status === 401 || !hasStoredSession())) {
    redirectToLogin();
    return;
  }
  setError(formatApiError(err));
}

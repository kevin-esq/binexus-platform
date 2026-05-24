import type { TokenProvider } from '@binexus/sdk';

const ACCESS_KEY = 'binexus.accessToken';
const REFRESH_KEY = 'binexus.refreshToken';

export const browserTokenProvider: TokenProvider = {
  getAccessToken(): string | null {
    if (typeof window === 'undefined') return null;
    return window.localStorage.getItem(ACCESS_KEY);
  },
  setTokens(accessToken: string, refreshToken: string): void {
    window.localStorage.setItem(ACCESS_KEY, accessToken);
    window.localStorage.setItem(REFRESH_KEY, refreshToken);
  },
  clear(): void {
    window.localStorage.removeItem(ACCESS_KEY);
    window.localStorage.removeItem(REFRESH_KEY);
  },
};

export function hasStoredSession(): boolean {
  if (typeof window === 'undefined') return false;
  return Boolean(window.localStorage.getItem(ACCESS_KEY));
}

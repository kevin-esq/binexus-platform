import { createBinexusClient } from '@binexus/sdk';

import { browserTokenProvider } from './token-storage';

/** Default: .NET API (:5102). NestJS is not a supported backend (ADR-0015). */
export const DEFAULT_API_URL = 'http://localhost:5102';

export const api = createBinexusClient({
  baseUrl: process.env.NEXT_PUBLIC_API_URL ?? DEFAULT_API_URL,
  tokenProvider: browserTokenProvider,
});

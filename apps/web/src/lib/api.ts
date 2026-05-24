import { createBinexusClient } from '@binexus/sdk';

import { browserTokenProvider } from './token-storage';

export const api = createBinexusClient({
  baseUrl: process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:3001',
  tokenProvider: browserTokenProvider,
});

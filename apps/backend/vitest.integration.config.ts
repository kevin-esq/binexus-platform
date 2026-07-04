process.env.INTEGRATION = '1';

import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    include: ['src/**/__integration__/**/*.spec.ts'],
    testTimeout: 30_000,
    hookTimeout: 30_000,
  },
});

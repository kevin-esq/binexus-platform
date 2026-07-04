import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    include: ['src/**/*.spec.ts'],
    exclude: ['src/**/__integration__/**'],
    testTimeout: 5_000,
  },
});

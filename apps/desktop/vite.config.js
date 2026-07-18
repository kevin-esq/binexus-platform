import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';
const host = process.env.TAURI_DEV_HOST;
export default defineConfig({
  plugins: [react()],
  clearScreen: false,
  envPrefix: ['VITE_', 'TAURI_'],
  server: {
    host: host || false,
    port: 1420,
    strictPort: true,
  },
  test: {
    environment: 'jsdom',
    globals: true,
  },
});

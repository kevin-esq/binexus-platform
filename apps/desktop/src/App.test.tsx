import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('@tauri-apps/api/core', () => ({
  invoke: vi.fn(),
}));

vi.mock('@tauri-apps/api/event', () => ({
  listen: vi.fn().mockResolvedValue(vi.fn()),
}));

import { invoke } from '@tauri-apps/api/core';

import App from './App';

const mockInvoke = vi.mocked(invoke);

describe('App', () => {
  beforeEach(() => {
    mockInvoke.mockReset();
  });

  it('renders the boot screen while native state loads', () => {
    mockInvoke.mockImplementation(() => new Promise(() => undefined));

    render(<App />);

    expect(screen.getByRole('heading', { name: 'Preparing this device' })).not.toBeNull();
  });

  it('renders server setup when the device needs a Branch Server', async () => {
    const needsServerSetup = { kind: 'needsServerSetup' as const };
    mockInvoke.mockResolvedValueOnce(needsServerSetup).mockResolvedValueOnce(needsServerSetup);

    render(<App />);

    expect(
      await screen.findByRole('heading', { name: 'Connect to a Branch Server' }),
    ).not.toBeNull();
  });

  it('renders recovery when Rust sends camelCase recoveryRequired kind', async () => {
    const recovery = {
      kind: 'recoveryRequired' as const,
      message: 'Local secrets exist but configuration is missing.',
    };
    mockInvoke.mockResolvedValueOnce(recovery).mockResolvedValueOnce(recovery);

    render(<App />);

    expect(await screen.findByRole('heading', { name: 'Recovery required' })).not.toBeNull();
    expect(screen.getByText(/Local secrets exist/i)).not.toBeNull();
  });
});

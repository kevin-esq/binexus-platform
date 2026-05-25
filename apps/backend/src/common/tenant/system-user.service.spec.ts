import { describe, expect, it, vi } from 'vitest';

import { type PrismaService } from '../prisma/prisma.service';

import { SystemUserService } from './system-user.service';

describe('SystemUserService', () => {
  it('returns system user id for tenant', async () => {
    const prisma = {
      user: {
        findFirst: vi.fn().mockResolvedValue({ id: 'system-1' }),
      },
    } as unknown as PrismaService;

    const service = new SystemUserService(prisma);
    const id = await service.resolveForTenant('tenant-1');

    expect(id).toBe('system-1');
    expect(prisma.user.findFirst).toHaveBeenCalledWith({
      where: { tenantId: 'tenant-1', isSystem: true },
      select: { id: true },
    });
  });

  it('throws when no system user exists', async () => {
    const prisma = {
      user: { findFirst: vi.fn().mockResolvedValue(null) },
    } as unknown as PrismaService;

    const service = new SystemUserService(prisma);

    await expect(service.resolveForTenant('tenant-missing')).rejects.toThrow(
      'No system user for tenant tenant-missing',
    );
  });
});

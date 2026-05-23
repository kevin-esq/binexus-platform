import { Injectable } from '@nestjs/common';

import { type PrismaService } from '../prisma/prisma.service';

interface CacheEntry {
  enabled: boolean;
  expiresAt: number;
}

const TTL_MS = 30_000;

@Injectable()
export class FeatureFlagsService {
  private readonly cache = new Map<string, CacheEntry>();

  constructor(private readonly prisma: PrismaService) {}

  async isEnabled(tenantId: string, key: string): Promise<boolean> {
    const cacheKey = `${tenantId}::${key}`;
    const cached = this.cache.get(cacheKey);
    const now = Date.now();
    if (cached && cached.expiresAt > now) {
      return cached.enabled;
    }
    const row = await this.prisma.tenantFeature.findUnique({
      where: { tenantId_key: { tenantId, key } },
      select: { enabled: true },
    });
    const enabled = row?.enabled ?? false;
    this.cache.set(cacheKey, { enabled, expiresAt: now + TTL_MS });
    return enabled;
  }

  invalidate(tenantId: string, key?: string): void {
    if (key) {
      this.cache.delete(`${tenantId}::${key}`);
      return;
    }
    for (const k of this.cache.keys()) {
      if (k.startsWith(`${tenantId}::`)) this.cache.delete(k);
    }
  }

  async setEnabled(tenantId: string, key: string, enabled: boolean): Promise<void> {
    await this.prisma.tenantFeature.upsert({
      where: { tenantId_key: { tenantId, key } },
      update: { enabled },
      create: { tenantId, key, enabled },
    });
    this.invalidate(tenantId, key);
  }
}

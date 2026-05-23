import { SetMetadata } from '@nestjs/common';

// Marker for endpoints that operate across tenants (super-admin only).
// The TenantContextService still runs, but downstream code can opt out of tenant filtering
// by calling `prisma` directly (bypassing `prisma.forTenant()`).
export const SKIP_TENANT_KEY = 'skipTenant';

export const SkipTenant = (): MethodDecorator & ClassDecorator =>
  SetMetadata(SKIP_TENANT_KEY, true);

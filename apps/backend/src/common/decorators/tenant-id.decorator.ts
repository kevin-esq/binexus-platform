import { createParamDecorator, type ExecutionContext } from '@nestjs/common';

export const TenantId = createParamDecorator((_data: unknown, ctx: ExecutionContext): string => {
  const req = ctx.switchToHttp().getRequest<Record<string, unknown>>();
  const tenantId = req.tenantId as string | undefined;
  if (!tenantId) throw new Error('TenantId not present on request');
  return tenantId;
});

import { createParamDecorator, type ExecutionContext } from '@nestjs/common';

export interface RequestUser {
  userId: string;
  tenantId: string;
  role: string;
  branchId: string | null;
}

export const CurrentUser = createParamDecorator(
  (_data: unknown, ctx: ExecutionContext): RequestUser | null => {
    const req = ctx.switchToHttp().getRequest<Record<string, unknown>>();
    const userId = req.userId as string | undefined;
    const tenantId = req.tenantId as string | undefined;
    const role = req.role as string | undefined;
    if (!userId || !tenantId || !role) return null;
    return { userId, tenantId, role, branchId: (req.branchId as string | undefined) ?? null };
  },
);

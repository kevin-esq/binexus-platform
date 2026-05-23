import { randomUUID } from 'node:crypto';

import { Injectable, type NestMiddleware } from '@nestjs/common';
import { type JwtService } from '@nestjs/jwt';
import type { FastifyReply, FastifyRequest } from 'fastify';

import { type TenantContextService } from './tenant-context.service';

interface JwtClaims {
  sub: string;
  tenantId: string;
  role: string;
  branchId?: string | null;
}

@Injectable()
export class TenantContextMiddleware implements NestMiddleware {
  constructor(
    private readonly jwt: JwtService,
    private readonly tenantContext: TenantContextService,
  ) {}

  use(req: FastifyRequest['raw'], _res: FastifyReply['raw'], next: () => void): void {
    const requestId = (req.headers['x-request-id'] as string | undefined) ?? randomUUID();
    (req as unknown as { requestId: string }).requestId = requestId;

    const auth = req.headers.authorization;
    if (!auth?.startsWith('Bearer ')) {
      next();
      return;
    }

    const token = auth.slice('Bearer '.length);
    try {
      const claims = this.jwt.verify<JwtClaims>(token, {
        secret: process.env.JWT_ACCESS_SECRET ?? 'dev-access-secret-change-me-please',
      });
      this.tenantContext.run(
        {
          tenantId: claims.tenantId,
          userId: claims.sub,
          role: claims.role,
          branchId: claims.branchId ?? null,
          requestId,
        },
        () => next(),
      );
      const reqAny = req as unknown as {
        tenantId?: string;
        userId?: string;
        role?: string;
      };
      reqAny.tenantId = claims.tenantId;
      reqAny.userId = claims.sub;
      reqAny.role = claims.role;
    } catch {
      // Invalid / expired token — let JwtAuthGuard reject downstream.
      next();
    }
  }
}

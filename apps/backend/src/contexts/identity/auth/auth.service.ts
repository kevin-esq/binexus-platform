import { createHash, randomUUID } from 'node:crypto';

import { Inject, Injectable, UnauthorizedException } from '@nestjs/common';
import { JwtService, type JwtSignOptions } from '@nestjs/jwt';
import argon2 from 'argon2';

import { PrismaService } from '../../../common/prisma/prisma.service';

type ExpiresIn = JwtSignOptions['expiresIn'];

export interface LoginInput {
  tenantSlug: string;
  email: string;
  password: string;
}

export interface AuthTokens {
  accessToken: string;
  refreshToken: string;
}

@Injectable()
export class AuthService {
  constructor(
    @Inject(PrismaService) private readonly prisma: PrismaService,
    @Inject(JwtService) private readonly jwt: JwtService,
  ) {}

  private accessSecret(): string {
    return process.env.JWT_ACCESS_SECRET ?? 'dev-access-secret-change-me-please';
  }

  private refreshSecret(): string {
    return process.env.JWT_REFRESH_SECRET ?? 'dev-refresh-secret-change-me-please';
  }

  private accessTtl(): ExpiresIn {
    return (process.env.JWT_ACCESS_TTL ?? '15m') as ExpiresIn;
  }

  private refreshTtl(): ExpiresIn {
    return (process.env.JWT_REFRESH_TTL ?? '7d') as ExpiresIn;
  }

  private hashToken(token: string): string {
    return createHash('sha256').update(token).digest('hex');
  }

  async login(input: LoginInput): Promise<AuthTokens> {
    const tenant = await this.prisma.tenant.findUnique({ where: { slug: input.tenantSlug } });
    if (!tenant) throw new UnauthorizedException('Invalid credentials');

    const user = await this.prisma.user.findUnique({
      where: { tenantId_email: { tenantId: tenant.id, email: input.email } },
    });
    if (!user) throw new UnauthorizedException('Invalid credentials');

    const ok = await argon2.verify(user.passwordHash, input.password);
    if (!ok) throw new UnauthorizedException('Invalid credentials');

    return this.issueTokens({
      sub: user.id,
      tenantId: user.tenantId,
      role: user.role,
      branchId: user.branchId,
    });
  }

  async refresh(refreshToken: string): Promise<AuthTokens> {
    let payload: { sub: string; tenantId: string; role: string; branchId: string | null };
    try {
      payload = this.jwt.verify(refreshToken, { secret: this.refreshSecret() });
    } catch {
      throw new UnauthorizedException('Invalid refresh token');
    }

    const tokenHash = this.hashToken(refreshToken);
    const record = await this.prisma.refreshToken.findUnique({ where: { tokenHash } });
    if (!record || record.revokedAt || record.expiresAt < new Date()) {
      throw new UnauthorizedException('Refresh token revoked or expired');
    }

    await this.prisma.refreshToken.update({
      where: { id: record.id },
      data: { revokedAt: new Date() },
    });

    return this.issueTokens(payload);
  }

  async logout(refreshToken: string): Promise<void> {
    const tokenHash = this.hashToken(refreshToken);
    await this.prisma.refreshToken
      .update({ where: { tokenHash }, data: { revokedAt: new Date() } })
      .catch(() => undefined);
  }

  async me(userId: string): Promise<{
    user: {
      id: string;
      email: string;
      role: string;
      branchId: string | null;
      tenantId: string;
    };
    tenant: { id: string; slug: string; name: string };
    branch: { id: string; name: string } | null;
  }> {
    const user = await this.prisma.user.findUnique({
      where: { id: userId },
      include: { tenant: true, branch: true },
    });
    if (!user) throw new UnauthorizedException('User not found');
    return {
      user: {
        id: user.id,
        email: user.email,
        role: user.role,
        branchId: user.branchId,
        tenantId: user.tenantId,
      },
      tenant: { id: user.tenant.id, slug: user.tenant.slug, name: user.tenant.name },
      branch: user.branch ? { id: user.branch.id, name: user.branch.name } : null,
    };
  }

  private async issueTokens(payload: {
    sub: string;
    tenantId: string;
    role: string;
    branchId: string | null;
  }): Promise<AuthTokens> {
    const accessToken = this.jwt.sign(payload, {
      secret: this.accessSecret(),
      expiresIn: this.accessTtl(),
    });

    const refreshId = randomUUID();
    const refreshToken = this.jwt.sign(
      { ...payload, jti: refreshId },
      { secret: this.refreshSecret(), expiresIn: this.refreshTtl() },
    );

    const decoded = this.jwt.decode(refreshToken) as { exp: number };
    await this.prisma.refreshToken.create({
      data: {
        userId: payload.sub,
        tokenHash: this.hashToken(refreshToken),
        expiresAt: new Date(decoded.exp * 1000),
      },
    });

    return { accessToken, refreshToken };
  }
}

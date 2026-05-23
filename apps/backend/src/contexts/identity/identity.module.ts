import { Module } from '@nestjs/common';
import { JwtModule, type JwtSignOptions } from '@nestjs/jwt';

import { AuthController } from './auth/auth.controller';
import { AuthService } from './auth/auth.service';

@Module({
  imports: [
    JwtModule.register({
      // Default secret/ttl come from env at sign-time; this register call is mostly to
      // make JwtService available DI-wide. We sign with explicit secrets per call.
      secret: process.env.JWT_ACCESS_SECRET ?? 'dev-access-secret-change-me-please',
      signOptions: {
        expiresIn: (process.env.JWT_ACCESS_TTL ?? '15m') as JwtSignOptions['expiresIn'],
      },
    }),
  ],
  controllers: [AuthController],
  providers: [AuthService],
  exports: [AuthService, JwtModule],
})
export class IdentityModule {}

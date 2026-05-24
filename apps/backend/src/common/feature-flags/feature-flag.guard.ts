import {
  type CanActivate,
  type ExecutionContext,
  ForbiddenException,
  Inject,
  Injectable,
} from '@nestjs/common';
import { Reflector } from '@nestjs/core';

import { REQUIRE_FEATURE_KEY } from '../decorators/require-feature.decorator';

import { FeatureFlagsService } from './feature-flags.service';

@Injectable()
export class FeatureFlagGuard implements CanActivate {
  constructor(
    @Inject(Reflector) private readonly reflector: Reflector,
    @Inject(FeatureFlagsService) private readonly features: FeatureFlagsService,
  ) {}

  async canActivate(context: ExecutionContext): Promise<boolean> {
    const required = this.reflector.getAllAndOverride<string | undefined>(REQUIRE_FEATURE_KEY, [
      context.getHandler(),
      context.getClass(),
    ]);
    if (!required) return true;

    const req = context.switchToHttp().getRequest<{ tenantId?: string }>();
    const tenantId = req.tenantId;
    if (!tenantId) throw new ForbiddenException('Feature requires authenticated tenant');

    const enabled = await this.features.isEnabled(tenantId, required);
    if (!enabled) throw new ForbiddenException(`Feature "${required}" is not enabled for tenant`);
    return true;
  }
}

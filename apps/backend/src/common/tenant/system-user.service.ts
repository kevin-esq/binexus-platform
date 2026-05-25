import { Inject, Injectable } from '@nestjs/common';

import { PrismaService } from '../prisma/prisma.service';

@Injectable()
export class SystemUserService {
  constructor(@Inject(PrismaService) private readonly prisma: PrismaService) {}

  async resolveForTenant(tenantId: string): Promise<string> {
    const user = await this.prisma.user.findFirst({
      where: { tenantId, isSystem: true },
      select: { id: true },
    });

    if (!user) {
      throw new Error(`No system user for tenant ${tenantId}. Run db:seed.`);
    }

    return user.id;
  }
}

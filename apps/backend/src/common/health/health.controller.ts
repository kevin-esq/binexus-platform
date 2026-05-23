import { Controller, Get } from '@nestjs/common';

import { Public } from '../decorators/public.decorator';
import { type PrismaService } from '../prisma/prisma.service';

@Controller('health')
export class HealthController {
  constructor(private readonly prisma: PrismaService) {}

  @Public()
  @Get()
  async check(): Promise<{ status: 'ok'; db: boolean; uptime: number }> {
    let dbOk = false;
    try {
      await this.prisma.$queryRaw`SELECT 1`;
      dbOk = true;
    } catch {
      dbOk = false;
    }
    return { status: 'ok', db: dbOk, uptime: process.uptime() };
  }
}

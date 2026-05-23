import { Module } from '@nestjs/common';
import { LoggerModule as PinoLoggerModule } from 'nestjs-pino';

@Module({
  imports: [
    PinoLoggerModule.forRootAsync({
      useFactory: () => {
        const isDev = (process.env.NODE_ENV ?? 'development') !== 'production';
        const pretty = process.env.LOG_PRETTY === 'true' || isDev;
        return {
          pinoHttp: {
            level: process.env.LOG_LEVEL ?? (isDev ? 'debug' : 'info'),
            transport: pretty
              ? { target: 'pino-pretty', options: { singleLine: true, colorize: true } }
              : undefined,
            customProps: (req) => {
              const reqAny = req as unknown as {
                tenantId?: string;
                userId?: string;
                role?: string;
              };
              return {
                tenantId: reqAny.tenantId,
                userId: reqAny.userId,
                role: reqAny.role,
              };
            },
            redact: {
              paths: ['req.headers.authorization', 'req.headers.cookie', '*.password'],
              remove: true,
            },
          },
        };
      },
    }),
  ],
})
export class LoggerModule {}

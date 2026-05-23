import { Injectable } from '@nestjs/common';
import { type CommandBus } from '@nestjs/cqrs';

import type { AppCommand } from './app-command';

// Thin typed wrapper over @nestjs/cqrs CommandBus that preserves the result type
// declared in the AppCommand subclass.
@Injectable()
export class AppCommandBus {
  constructor(private readonly bus: CommandBus) {}

  async execute<TCommand extends AppCommand<unknown>>(
    command: TCommand,
  ): Promise<TCommand extends AppCommand<infer R> ? R : never> {
    return this.bus.execute(command);
  }
}

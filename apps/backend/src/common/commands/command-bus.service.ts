import { Inject, Injectable } from '@nestjs/common';
import { CommandBus } from '@nestjs/cqrs';

import type { AppCommand, AppCommandResult } from './app-command';
import { validateAppCommand } from './command-validation';

// Typed wrapper over @nestjs/cqrs CommandBus.
// It centralizes command validation before the handler executes and preserves the
// result type declared by the AppCommand subclass.
@Injectable()
export class AppCommandBus {
  constructor(@Inject(CommandBus) private readonly bus: CommandBus) {}

  async execute<TCommand extends AppCommand<unknown>>(
    command: TCommand,
  ): Promise<AppCommandResult<TCommand>> {
    await validateAppCommand(command);
    return this.bus.execute(command) as Promise<AppCommandResult<TCommand>>;
  }
}

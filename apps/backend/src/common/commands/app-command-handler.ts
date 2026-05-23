import type { AppCommand, AppCommandResult } from './app-command';

export abstract class AppCommandHandler<
  TCommand extends AppCommand<unknown>,
  TResult = AppCommandResult<TCommand>,
> {
  abstract execute(command: TCommand): Promise<TResult> | TResult;
}

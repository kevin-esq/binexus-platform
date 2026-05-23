import { randomUUID } from 'node:crypto';

export interface AppCommandMetadata {
  /**
   * Idempotency key for network-originated commands.
   * HTTP adapters should map the `Idempotency-Key` header into this value.
   */
  commandId?: string;
  correlationId?: string;
  causationId?: string;
  issuedAt?: Date;
}

export type AppCommandResult<TCommand> =
  TCommand extends AppCommand<infer TResult> ? TResult : never;

// Base class for every write use case in the platform.
// The generic parameter is the handler return type.
export abstract class AppCommand<TResult = void> {
  readonly commandId: string;
  readonly correlationId?: string;
  readonly causationId?: string;
  readonly issuedAt: Date;

  // Phantom marker so TS keeps the result type around for handler typings.
  readonly _result?: TResult;

  protected constructor(metadata: AppCommandMetadata = {}) {
    this.commandId = metadata.commandId ?? randomUUID();
    this.correlationId = metadata.correlationId;
    this.causationId = metadata.causationId;
    this.issuedAt = metadata.issuedAt ?? new Date();
  }

  get commandName(): string {
    return this.constructor.name;
  }
}

export interface AppCommandValidatable {
  validate(): void | Promise<void>;
}

export function hasCustomCommandValidation(
  command: AppCommand<unknown>,
): command is AppCommand<unknown> & AppCommandValidatable {
  return typeof (command as { validate?: unknown }).validate === 'function';
}

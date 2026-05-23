import { IsString } from 'class-validator';
import { describe, expect, it, vi } from 'vitest';

import { AppCommand } from './app-command';
import { AppCommandBus } from './command-bus.service';
import { AppCommandValidationError } from './command-validation';

class SampleCommand extends AppCommand<string> {
  @IsString()
  readonly value: unknown;

  constructor(value: unknown, commandId = 'cmd-123') {
    super({
      commandId,
      correlationId: 'corr-123',
      causationId: 'cause-123',
      issuedAt: new Date('2026-05-23T00:00:00.000Z'),
    });
    this.value = value;
  }
}

class CustomValidatedCommand extends AppCommand<void> {
  constructor(private readonly valid: boolean) {
    super({ commandId: 'cmd-custom' });
  }

  validate(): void {
    if (!this.valid) {
      throw new AppCommandValidationError(this.commandName, [
        { property: 'valid', constraints: ['custom validation failed'] },
      ]);
    }
  }
}

function createCommandBus(result: unknown = 'ok'): {
  appBus: AppCommandBus;
  execute: ReturnType<typeof vi.fn>;
} {
  const execute = vi.fn().mockResolvedValue(result);
  const appBus = new AppCommandBus({ execute } as never);
  return { appBus, execute };
}

describe('AppCommandBus', () => {
  it('preserves command metadata and delegates to Nest command bus', async () => {
    const { appBus, execute } = createCommandBus('result');
    const command = new SampleCommand('valid-value');

    await expect(appBus.execute(command)).resolves.toBe('result');

    expect(command.commandId).toBe('cmd-123');
    expect(command.correlationId).toBe('corr-123');
    expect(command.causationId).toBe('cause-123');
    expect(command.issuedAt.toISOString()).toBe('2026-05-23T00:00:00.000Z');
    expect(command.commandName).toBe('SampleCommand');
    expect(execute).toHaveBeenCalledTimes(1);
    expect(execute).toHaveBeenCalledWith(command);
  });

  it('generates a command id when none is provided', () => {
    class AutoIdCommand extends AppCommand<void> {
      constructor() {
        super();
      }
    }

    const command = new AutoIdCommand();

    expect(command.commandId).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/,
    );
  });

  it('rejects commands that fail decorator validation before dispatching', async () => {
    const { appBus, execute } = createCommandBus();
    const command = new SampleCommand(123);

    await expect(appBus.execute(command)).rejects.toBeInstanceOf(AppCommandValidationError);
    expect(execute).not.toHaveBeenCalled();
  });

  it('runs custom command validation before dispatching', async () => {
    const { appBus, execute } = createCommandBus();

    await expect(appBus.execute(new CustomValidatedCommand(false))).rejects.toBeInstanceOf(
      AppCommandValidationError,
    );
    expect(execute).not.toHaveBeenCalled();
  });
});

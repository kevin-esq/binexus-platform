import { BadRequestException } from '@nestjs/common';
import { validate, type ValidationError } from 'class-validator';

import { type AppCommand, hasCustomCommandValidation } from './app-command';

export interface AppCommandValidationIssue {
  property: string;
  constraints: string[];
}

export class AppCommandValidationError extends BadRequestException {
  constructor(commandName: string, issues: AppCommandValidationIssue[]) {
    super({
      message: `Command ${commandName} failed validation.`,
      commandName,
      issues,
    });
  }
}

export async function validateAppCommand(command: AppCommand<unknown>): Promise<void> {
  const decoratorIssues = flattenValidationErrors(
    await validate(command, {
      forbidUnknownValues: false,
      stopAtFirstError: false,
      validationError: { target: false, value: false },
    }),
  );

  if (decoratorIssues.length > 0) {
    throw new AppCommandValidationError(command.commandName, decoratorIssues);
  }

  if (hasCustomCommandValidation(command)) {
    await command.validate();
  }
}

function flattenValidationErrors(
  errors: ValidationError[],
  parentPath = '',
): AppCommandValidationIssue[] {
  return errors.flatMap((error) => {
    const path = parentPath ? `${parentPath}.${error.property}` : error.property;
    const ownIssues = error.constraints
      ? [
          {
            property: path,
            constraints: Object.values(error.constraints),
          },
        ]
      : [];

    return [...ownIssues, ...flattenValidationErrors(error.children ?? [], path)];
  });
}

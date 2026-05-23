export class BinexusApiError extends Error {
  readonly status: number;
  readonly code: string | undefined;
  readonly details: unknown;

  constructor(message: string, status: number, code?: string, details?: unknown) {
    super(message);
    this.name = 'BinexusApiError';
    this.status = status;
    this.code = code;
    this.details = details;
  }
}

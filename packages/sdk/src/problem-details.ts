/**
 * Normalize Nest filter errors and RFC 7807 Problem Details into a toast-friendly message + code.
 */
export function parseApiErrorPayload(
  payload: unknown,
  status: number,
): { message: string; code: string | undefined } {
  const err =
    payload !== null && typeof payload === 'object'
      ? (payload as Record<string, unknown>)
      : ({} as Record<string, unknown>);

  const extensions =
    err.extensions !== null && typeof err.extensions === 'object'
      ? (err.extensions as Record<string, unknown>)
      : undefined;

  const message =
    (typeof err.message === 'string' ? err.message : undefined) ??
    (typeof err.detail === 'string' ? err.detail : undefined) ??
    (typeof err.title === 'string' ? err.title : undefined) ??
    `Request failed: ${status}`;

  const code =
    (typeof err.code === 'string' ? err.code : undefined) ??
    (typeof extensions?.code === 'string' ? extensions.code : undefined);

  return { message, code };
}

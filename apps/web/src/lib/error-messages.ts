/**
 * Spanish actionable copy for known API problem codes.
 * Keep codes for logic; surface these strings to operators.
 */
const CODE_MESSAGES: Record<string, string> = {
  INVALID_CREDENTIALS: 'Correo o contraseña incorrectos. Verifica e intenta de nuevo.',
  UNAUTHORIZED: 'Tu sesión expiró. Inicia sesión otra vez.',
  FORBIDDEN: 'No tienes permiso para esta acción.',
  ACCOUNT_UNAVAILABLE: 'Esta cuenta no está disponible. Contacta al administrador.',
  REFRESH_TOKEN_REUSED: 'La sesión se invalidó por seguridad. Inicia sesión otra vez.',
  FEATURE_DISABLED: 'Esta función no está habilitada para tu empresa.',
  INSUFFICIENT_STOCK: 'No hay stock suficiente para completar la operación.',
  IDEMPOTENCY_KEY_CONFLICT:
    'Esta operación ya se ejecutó con otro resultado. Recarga e intenta de nuevo.',
  IDEMPOTENCY_KEY_REUSED: 'Solicitud duplicada detectada. Revisa el estado antes de reintentar.',
  INVALID_CURSOR: 'La paginación ya no es válida. Vuelve a cargar la lista.',
  INVALID_ORDER: 'Los datos del pedido no son válidos.',
  ORDER_NOT_FOUND: 'No encontramos ese pedido.',
  INVALID_TRANSITION: 'El pedido no puede pasar a ese estado desde el actual.',
  LIQUIDATION_DISABLED: 'La liquidación está desactivada por configuración.',
  LIQUIDATION_FORBIDDEN: 'Solo un administrador puede liquidar esta ruta.',
  PROOF_OBJECT_NOT_FOUND: 'Falta subir la foto o firma de entrega.',
  INVALID_OBJECT_KEY: 'La clave del archivo de prueba no es válida.',
  UNISSUED_OBJECT_KEY: 'Ese archivo no fue autorizado para subida.',
  OBJECT_ALREADY_UPLOADED: 'Ese archivo ya se subió. No se permite sobrescribir.',
  WRONG_CONTENT_TYPE: 'El tipo de archivo no coincide con el autorizado.',
  CONCURRENCY_CONFLICT: 'Otro usuario cambió este registro. Recarga e intenta de nuevo.',
};

export function messageForApiCode(code: string | undefined): string | null {
  if (!code) return null;
  return CODE_MESSAGES[code] ?? null;
}

/** Prefer mapped Spanish copy; fall back to API detail/message. */
export function formatApiError(err: unknown): string {
  if (err && typeof err === 'object') {
    const maybe = err as { code?: string; message?: string };
    const mapped = messageForApiCode(maybe.code);
    if (mapped) return mapped;
    if (typeof maybe.message === 'string' && maybe.message.trim()) return maybe.message;
  }
  if (err instanceof Error && err.message.trim()) return err.message;
  return 'Ocurrió un error. Intenta de nuevo.';
}

const DEFAULT_ENDPOINT = 'http://localhost:9000';
const PREFLIGHT_TIMEOUT_MS = 3_000;

export async function requireMinIo(
  endpoint = process.env.S3_ENDPOINT ?? DEFAULT_ENDPOINT,
): Promise<void> {
  const base = endpoint.replace(/\/$/, '');
  const healthUrl = `${base}/minio/health/live`;

  try {
    const response = await fetch(healthUrl, {
      signal: AbortSignal.timeout(PREFLIGHT_TIMEOUT_MS),
    });
    if (!response.ok) {
      throw new Error(`health check returned HTTP ${response.status}`);
    }
  } catch (cause) {
    const detail = cause instanceof Error ? cause.message : String(cause);
    throw new Error(
      [
        'Integration tests require MinIO. Start it with: pnpm docker:up',
        `Expected S3_ENDPOINT=${endpoint}. Health check failed: ${detail}`,
      ].join('\n'),
    );
  }
}

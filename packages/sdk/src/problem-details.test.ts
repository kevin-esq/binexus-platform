import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

// Node --experimental-strip-types resolves the .ts specifier at runtime.
// Excluded from package `tsc` via tsconfig exclude.
import { parseApiErrorPayload } from './problem-details';

describe('parseApiErrorPayload', () => {
  it('prefers Nest message', () => {
    const result = parseApiErrorPayload(
      { statusCode: 401, message: 'Invalid credentials', code: 'INVALID_CREDENTIALS' },
      401,
    );
    assert.equal(result.message, 'Invalid credentials');
    assert.equal(result.code, 'INVALID_CREDENTIALS');
  });

  it('reads RFC7807 detail and top-level code', () => {
    const result = parseApiErrorPayload(
      {
        type: 'https://httpstatuses.com/409',
        title: 'Conflict',
        status: 409,
        detail: 'Insufficient stock for reservation.',
        code: 'INSUFFICIENT_STOCK',
      },
      409,
    );
    assert.equal(result.message, 'Insufficient stock for reservation.');
    assert.equal(result.code, 'INSUFFICIENT_STOCK');
  });

  it('falls back to title then status text', () => {
    assert.equal(parseApiErrorPayload({ title: 'Forbidden' }, 403).message, 'Forbidden');
    assert.equal(parseApiErrorPayload({}, 500).message, 'Request failed: 500');
  });

  it('reads code from extensions when top-level missing', () => {
    const result = parseApiErrorPayload(
      { detail: 'Feature off', extensions: { code: 'FEATURE_DISABLED' } },
      403,
    );
    assert.equal(result.message, 'Feature off');
    assert.equal(result.code, 'FEATURE_DISABLED');
  });
});

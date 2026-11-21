import { describe, expect, it } from 'vitest';
import { normalizeGraphQlError } from './errors';

describe('GraphQL error normalization', () => {
  it('preserves actionable server metadata', () => {
    const error = normalizeGraphQlError({ message: 'Denied', extensions: { code: 'forbidden', status: 403, correlationId: 'abc-123' } });
    expect(error).toMatchObject({ message: 'Denied', code: 'forbidden', status: 403, correlationId: 'abc-123' });
  });
});

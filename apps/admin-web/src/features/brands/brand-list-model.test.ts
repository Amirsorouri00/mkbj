import { describe, expect, it } from 'vitest';
import { parseAliases, readBrandListState, writeBrandListState } from './brand-list-model';

describe('brand list model', () => {
  it('round-trips search and page state', () => {
    const params = writeBrandListState({ q: 'acme', page: 2, pageSize: 50 });
    expect(readBrandListState(params)).toEqual({ q: 'acme', page: 2, pageSize: 50 });
  });

  it('normalizes and de-duplicates aliases', () => {
    expect(parseAliases('ACME, Acme Corp\nACME')).toEqual(['ACME', 'Acme Corp']);
  });
});

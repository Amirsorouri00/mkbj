import { describe, expect, it } from 'vitest';
import { readProductListState, writeProductListState } from './url-state';

describe('product list URL state', () => {
  it('uses safe defaults for malformed pagination', () => {
    expect(readProductListState(new URLSearchParams('page=-2&pageSize=7'))).toEqual({ q: '', page: 1, pageSize: 25 });
  });

  it('round-trips meaningful filters without noisy defaults', () => {
    const params = writeProductListState({ q: 'camera', status: 'ACTIVE', brandId: '9007199254740993', categoryId: '9007199254740995', page: 3, pageSize: 50 });
    expect(params.toString()).toBe('q=camera&status=ACTIVE&brandId=9007199254740993&categoryId=9007199254740995&page=3&pageSize=50');
    expect(readProductListState(params)).toEqual({ q: 'camera', status: 'ACTIVE', brandId: '9007199254740993', categoryId: '9007199254740995', page: 3, pageSize: 50 });
  });
});

import { describe, expect, it } from 'vitest';
import { readCategoryListState, writeCategoryListState } from './category-list-model';

describe('category cursor URL state', () => {
  it('preserves opaque cursors without interpreting them', () => {
    const params = writeCategoryListState({ q: 'chairs', status: 'ACTIVE', cursor: 'opaque+/=', direction: 'backward', pageSize: 50 });
    expect(readCategoryListState(params)).toEqual({ q: 'chairs', status: 'ACTIVE', cursor: 'opaque+/=', direction: 'backward', pageSize: 50 });
  });

  it('does not fabricate a cursor or page number', () => {
    expect(writeCategoryListState({ q: '', direction: 'forward', pageSize: 25 }).toString()).toBe('');
  });
});

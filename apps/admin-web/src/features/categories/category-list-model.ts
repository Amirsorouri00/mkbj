import type { CategoryStatus } from '@mkbj/api-graphql';

export interface CategoryListState {
  q: string;
  status?: CategoryStatus;
  cursor?: string;
  direction: 'forward' | 'backward';
  pageSize: number;
}

const sizes = new Set([10, 25, 50, 100]);

export function readCategoryListState(params: URLSearchParams): CategoryListState {
  const pageSize = Number(params.get('pageSize'));
  const status = params.get('status');
  const cursor = params.get('cursor');
  return {
    q: params.get('q')?.trim() ?? '',
    ...(status ? { status: status as CategoryStatus } : {}),
    ...(cursor ? { cursor } : {}),
    direction: params.get('direction') === 'backward' ? 'backward' : 'forward',
    pageSize: sizes.has(pageSize) ? pageSize : 25,
  };
}

export function writeCategoryListState(state: CategoryListState): URLSearchParams {
  const params = new URLSearchParams();
  if (state.q) params.set('q', state.q);
  if (state.status) params.set('status', state.status);
  if (state.cursor) params.set('cursor', state.cursor);
  if (state.cursor && state.direction === 'backward') params.set('direction', 'backward');
  if (state.pageSize !== 25) params.set('pageSize', String(state.pageSize));
  return params;
}

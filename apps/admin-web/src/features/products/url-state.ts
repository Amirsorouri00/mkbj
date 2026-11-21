import type { ProductStatus } from '@mkbj/api-graphql';

export interface ProductListState {
  q: string;
  status?: ProductStatus;
  brandId?: string;
  categoryId?: string;
  page: number;
  pageSize: number;
}

const allowedPageSizes = new Set([10, 25, 50, 100]);

export function readProductListState(params: URLSearchParams): ProductListState {
  const page = Number(params.get('page'));
  const pageSize = Number(params.get('pageSize'));
  const rawStatus = params.get('status');
  return {
    q: params.get('q')?.trim() ?? '',
    ...(rawStatus ? { status: rawStatus as ProductStatus } : {}),
    ...(params.get('brandId') ? { brandId: params.get('brandId')! } : {}),
    ...(params.get('categoryId') ? { categoryId: params.get('categoryId')! } : {}),
    page: Number.isInteger(page) && page > 0 ? page : 1,
    pageSize: allowedPageSizes.has(pageSize) ? pageSize : 25,
  };
}

export function writeProductListState(state: ProductListState): URLSearchParams {
  const params = new URLSearchParams();
  if (state.q) params.set('q', state.q);
  if (state.status) params.set('status', state.status);
  if (state.brandId) params.set('brandId', state.brandId);
  if (state.categoryId) params.set('categoryId', state.categoryId);
  if (state.page > 1) params.set('page', String(state.page));
  if (state.pageSize !== 25) params.set('pageSize', String(state.pageSize));
  return params;
}

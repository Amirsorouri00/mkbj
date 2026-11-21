export interface BrandListState {
  q: string;
  page: number;
  pageSize: number;
}

const sizes = new Set([10, 25, 50, 100]);

export function readBrandListState(params: URLSearchParams): BrandListState {
  const page = Number(params.get('page'));
  const pageSize = Number(params.get('pageSize'));
  return {
    q: params.get('q')?.trim() ?? '',
    page: Number.isInteger(page) && page > 0 ? page : 1,
    pageSize: sizes.has(pageSize) ? pageSize : 25,
  };
}

export function writeBrandListState(state: BrandListState): URLSearchParams {
  const params = new URLSearchParams();
  if (state.q) params.set('q', state.q);
  if (state.page > 1) params.set('page', String(state.page));
  if (state.pageSize !== 25) params.set('pageSize', String(state.pageSize));
  return params;
}

export function parseAliases(value: string): string[] {
  return [...new Set(value.split(/[\n,]/).map((alias) => alias.trim()).filter(Boolean))];
}

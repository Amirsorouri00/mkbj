import type { AdminProductsQuery, AdminProductsQueryVariables } from '@mkbj/api-graphql';
import type { ProductListState } from './url-state';

export const productListCapabilities = {
  sorting: {
    supported: false,
    fixedOrder: 'createdAt.desc,id.asc',
    candidateFields: ['createdAt', 'updatedAt', 'title', 'status'] as const,
  },
  filters: {
    supported: ['q', 'status', 'brandId', 'categoryId'] as const,
    candidates: ['hasUsableImage', 'hasActiveVariant', 'activationReady', 'identifierType', 'updatedRange'] as const,
  },
  thumbnail: {
    urlSupported: false,
    readinessSupported: true,
  },
} as const;

export function toAdminProductsVariables(state: ProductListState): AdminProductsQueryVariables {
  return {
    page: state.page,
    pageSize: state.pageSize,
    ...(state.q ? { q: state.q } : {}),
    ...(state.status ? { status: state.status } : {}),
    ...(state.brandId ? { brandId: state.brandId } : {}),
    ...(state.categoryId ? { categoryId: state.categoryId } : {}),
  };
}

type ContractRow = AdminProductsQuery['adminProducts']['items'][number];

export type ProductThumbnail =
  | { kind: 'image'; url: string; alt: string }
  | { kind: 'available' }
  | { kind: 'missing' };

export interface ProductListItem extends ContractRow {
  thumbnail: ProductThumbnail;
  effectiveUpdatedAt: string;
}

export function toProductListItem(row: ContractRow): ProductListItem {
  return {
    ...row,
    // When the contract adds thumbnail URL/alt, only this boundary mapper and the
    // generated operation change; the table already understands the `image` variant.
    thumbnail: row.hasUsableImage ? { kind: 'available' } : { kind: 'missing' },
    effectiveUpdatedAt: row.updatedAt ?? row.createdAt,
  };
}

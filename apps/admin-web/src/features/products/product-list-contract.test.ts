import { describe, expect, it } from 'vitest';
import { productListCapabilities, toAdminProductsVariables, toProductListItem } from './product-list-contract';

describe('product list contract adapter', () => {
  it('maps only currently supported server arguments', () => {
    expect(toAdminProductsVariables({ q: 'camera', status: 'ACTIVE', brandId: '11', categoryId: '22', page: 2, pageSize: 25 })).toEqual({ q: 'camera', status: 'ACTIVE', brandId: '11', categoryId: '22', page: 2, pageSize: 25 });
    expect(productListCapabilities.sorting.supported).toBe(false);
  });

  it('keeps thumbnail readiness behind a replaceable view model', () => {
    const item = toProductListItem({ id: '9007199254740993', title: 'Camera', slug: 'camera', status: 'DRAFT', deactivatedByAdmin: false, brandId: '1', brandName: 'Brand', primaryCategoryId: '2', primaryCategoryName: 'Category', mpn: null, variantCount: 1, activeVariantCount: 1, hasUsableImage: true, mergedIntoProductId: null, createdAt: '2026-01-01T00:00:00Z', updatedAt: null });
    expect(item.thumbnail).toEqual({ kind: 'available' });
    expect(item.effectiveUpdatedAt).toBe(item.createdAt);
  });
});

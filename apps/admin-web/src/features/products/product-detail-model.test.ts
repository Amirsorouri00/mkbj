import { describe, expect, it } from 'vitest';
import type { ProductDetailFieldsFragment } from '@mkbj/api-graphql';
import { normalizeProductDetail } from './product-detail-model';

describe('normalizeProductDetail', () => {
    it('keeps an older or partially cached product response renderable', () => {
        const partial = {
            id: '1', title: 'Test', slug: 'test', status: 'DRAFT', brandId: '2', brandName: 'Brand',
            primaryCategoryId: '3', primaryCategoryName: 'Category', variants: [{ id: '4', sku: 'SKU', isActive: true }],
        } as unknown as ProductDetailFieldsFragment;
        const product = normalizeProductDetail(partial);
        expect(product.activationProblems).toEqual([]);
        expect(product.identifiers).toEqual([]);
        expect(product.media).toEqual([]);
        expect(product.variants[0]?.attributeValues).toEqual([]);
    });
});

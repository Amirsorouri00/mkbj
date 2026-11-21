import { describe, expect, it } from 'vitest';
import { flattenCategories, optionalText, productSchema } from './product-editor-model';

describe('product editor model', () => {
  it('requires backend-required classification fields', () => {
    const result = productSchema.safeParse({ title: '', slug: '', brandId: '', primaryCategoryId: '', shortDescription: '', description: '' });
    expect(result.success).toBe(false);
    if (!result.success) expect(result.error.flatten().fieldErrors).toMatchObject({ title: ['Enter a product title.'], brandId: ['Choose a brand.'], primaryCategoryId: ['Choose a primary category.'] });
  });

  it('flattens the verified category tree without converting IDs to numbers', () => {
    expect(flattenCategories([{ id: '9007199254740993', name: 'Root', status: 'ACTIVE', children: [{ id: '9007199254740995', name: 'Child', status: 'INACTIVE' }] }])).toEqual([
      { id: '9007199254740993', label: 'Root', status: 'ACTIVE' },
      { id: '9007199254740995', label: '— Child', status: 'INACTIVE' },
    ]);
  });

  it('sends blank optional text as null', () => {
    expect(optionalText('   ')).toBeNull();
    expect(optionalText('  copy  ')).toBe('copy');
  });
});

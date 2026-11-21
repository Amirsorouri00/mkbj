import { describe, expect, it } from 'vitest';
import { buildBreadcrumbs } from './app-shell';

describe('application breadcrumbs', () => {
  it('never links to the nonexistent catalog root', () => {
    expect(buildBreadcrumbs('/catalog/products')).toEqual([
      { label: 'Home', to: '/' },
      { label: 'Catalog' },
      { label: 'Products' },
    ]);
  });

  it('hides opaque IDs behind a meaningful detail label', () => {
    expect(buildBreadcrumbs('/catalog/products/9007199254740993/edit')).toEqual([
      { label: 'Home', to: '/' },
      { label: 'Catalog' },
      { label: 'Products', to: '/catalog/products' },
      { label: 'Edit product' },
    ]);
  });

  it('represents category attribute management as a supported deep route', () => {
    expect(buildBreadcrumbs('/catalog/categories/123/attributes')).toEqual([
      { label: 'Home', to: '/' },
      { label: 'Catalog' },
      { label: 'Categories', to: '/catalog/categories' },
      { label: 'Category attributes' },
    ]);
  });
});

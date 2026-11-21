import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ProductCard } from './product-card';

vi.mock('next/image', () => ({ default: (props: Record<string, unknown>) => {
  const imageProps = { ...props };
  delete imageProps.priority;
  delete imageProps.unoptimized;
  return <img {...imageProps} />;
} }));

const product = {
  productId: '113606994465553462', slug: 'notebook', title: 'Notebook', brandId: '10', brandName: 'Northwind', categoryId: '20',
  minPrice: { amount: '12.50', currency: 'USD' }, maxPrice: { amount: '18.00', currency: 'USD' }, variantCount: 3, imageUrl: null,
};

describe('ProductCard', () => {
  it('preserves the opaque slug link and renders a price range', () => {
    render(<ProductCard currency="SGD" product={product} />);
    expect(screen.getByRole('link', { name: 'Notebook' })).toHaveAttribute('href', '/products/notebook?currency=SGD');
    expect(screen.getByText('USD 12.50 – USD 18.00')).toBeVisible();
    expect(screen.getByText('3 options')).toBeVisible();
  });

  it('shows an honest reserved image state', () => {
    render(<ProductCard currency="USD" product={product} />);
    expect(screen.getByText('Image coming soon')).toBeVisible();
  });
});

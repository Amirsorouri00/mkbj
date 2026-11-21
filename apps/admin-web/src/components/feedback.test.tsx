// @vitest-environment jsdom
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { AppError } from '../api/errors';
import { EmptyState, ErrorState } from './feedback';

describe('feedback states', () => {
  it('renders a permission-specific recovery path', () => {
    render(<MemoryRouter><ErrorState error={new AppError('Catalog permission is required.', 'forbidden', 403)} /></MemoryRouter>);
    expect(screen.getByRole('heading', { name: 'Access restricted' })).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Return home' }).getAttribute('href')).toBe('/');
  });

  it('keeps empty-state guidance available to assistive technology', () => {
    render(<EmptyState title="No products">Clear the current filters.</EmptyState>);
    expect(screen.getByRole('heading', { name: 'No products' })).toBeTruthy();
    expect(screen.getByText('Clear the current filters.')).toBeTruthy();
  });
});

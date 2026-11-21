// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { clampOffsetPage, CursorPagination, OffsetPagination } from './pagination';

afterEach(cleanup);

describe('pagination', () => {
  it('clamps direct navigation to a real server page', async () => {
    const user = userEvent.setup();
    const onPageChange = vi.fn();
    render(<OffsetPagination itemLabel="Products" onPageChange={onPageChange} onPageSizeChange={vi.fn()} page={2} pageCount={4} pageSize={25} />);

    const pageInput = screen.getByLabelText('Page');
    await user.clear(pageInput);
    await user.type(pageInput, '99');
    await user.click(screen.getByRole('button', { name: 'Go' }));

    expect(onPageChange).toHaveBeenCalledWith(4);
    expect((pageInput as HTMLInputElement).value).toBe('4');
  });

  it('exposes first, previous, next, and last navigation', async () => {
    const user = userEvent.setup();
    const onPageChange = vi.fn();
    render(<OffsetPagination itemLabel="Products" onPageChange={onPageChange} onPageSizeChange={vi.fn()} page={2} pageCount={4} pageSize={25} />);

    await user.click(screen.getByRole('button', { name: 'First page' }));
    await user.click(screen.getByRole('button', { name: 'Previous page' }));
    await user.click(screen.getByRole('button', { name: 'Next page' }));
    await user.click(screen.getByRole('button', { name: 'Last page' }));

    expect(onPageChange.mock.calls).toEqual([[1], [1], [3], [4]]);
  });

  it('keeps cursor navigation semantic without fabricating page numbers', () => {
    render(<CursorPagination hasNextPage hasPreviousPage={false} itemLabel="Categories" onNextPage={vi.fn()} onPageSizeChange={vi.fn()} onPreviousPage={vi.fn()} pageSize={25} />);

    expect(screen.getByText('Cursor navigation')).toBeTruthy();
    expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Previous page' }).disabled).toBe(true);
    expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Next page' }).disabled).toBe(false);
    expect(screen.queryByLabelText('Page')).toBeNull();
  });

  it('normalizes invalid and empty server page ranges', () => {
    expect(clampOffsetPage(-4, 8)).toBe(1);
    expect(clampOffsetPage(5, 0)).toBe(1);
    expect(clampOffsetPage(5, 3)).toBe(3);
  });
});

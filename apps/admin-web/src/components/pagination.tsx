import { Button } from '@mkbj/ui';
import { ChevronsLeft, ChevronLeft, ChevronRight, ChevronsRight } from 'lucide-react';
import { type FormEvent, useEffect, useId, useState } from 'react';

export const PAGE_SIZES = [10, 25, 50, 100] as const;

export function clampOffsetPage(page: number, pageCount: number): number {
  if (!Number.isFinite(page) || page < 1) return 1;
  return Math.min(Math.trunc(page), Math.max(1, Math.trunc(pageCount)));
}

interface OffsetPaginationProps {
  itemLabel: string;
  page: number;
  pageCount: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  onPageSizeChange: (pageSize: number) => void;
}

export function OffsetPagination({ itemLabel, page, pageCount, pageSize, onPageChange, onPageSizeChange }: OffsetPaginationProps) {
  const inputId = useId();
  const safePageCount = Math.max(1, pageCount);
  const [draftPage, setDraftPage] = useState(String(clampOffsetPage(page, safePageCount)));

  useEffect(() => setDraftPage(String(clampOffsetPage(page, safePageCount))), [page, safePageCount]);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const next = clampOffsetPage(Number(draftPage), safePageCount);
    setDraftPage(String(next));
    if (next !== page) onPageChange(next);
  };

  return (
    <nav aria-label={`${itemLabel} pagination`} className="pagination">
      <label>
        Rows
        <select aria-label={`${itemLabel} per page`} onChange={(event) => onPageSizeChange(Number(event.target.value))} value={pageSize}>
          {PAGE_SIZES.map((size) => <option key={size} value={size}>{size}</option>)}
        </select>
      </label>
      <form className="pagination-jump" onSubmit={submit}>
        <label htmlFor={inputId}>Page</label>
        <input id={inputId} inputMode="numeric" min={1} onChange={(event) => setDraftPage(event.target.value)} type="number" value={draftPage} />
        <span>of {safePageCount}</span>
        <Button size="compact" type="submit" variant="secondary">Go</Button>
      </form>
      <div>
        <Button aria-label="First page" disabled={page <= 1} onClick={() => onPageChange(1)} size="icon" type="button" variant="secondary"><ChevronsLeft aria-hidden="true" /></Button>
        <Button aria-label="Previous page" disabled={page <= 1} onClick={() => onPageChange(page - 1)} size="icon" type="button" variant="secondary"><ChevronLeft aria-hidden="true" /></Button>
        <Button aria-label="Next page" disabled={page >= safePageCount} onClick={() => onPageChange(page + 1)} size="icon" type="button" variant="secondary"><ChevronRight aria-hidden="true" /></Button>
        <Button aria-label="Last page" disabled={page >= safePageCount} onClick={() => onPageChange(safePageCount)} size="icon" type="button" variant="secondary"><ChevronsRight aria-hidden="true" /></Button>
      </div>
    </nav>
  );
}

interface CursorPaginationProps {
  itemLabel: string;
  pageSize: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
  onPageSizeChange: (pageSize: number) => void;
  onPreviousPage: () => void;
  onNextPage: () => void;
}

export function CursorPagination({ itemLabel, pageSize, hasPreviousPage, hasNextPage, onPageSizeChange, onPreviousPage, onNextPage }: CursorPaginationProps) {
  return (
    <nav aria-label={`${itemLabel} pagination`} className="pagination">
      <label>
        Rows
        <select aria-label={`${itemLabel} per page`} onChange={(event) => onPageSizeChange(Number(event.target.value))} value={pageSize}>
          {PAGE_SIZES.map((size) => <option key={size} value={size}>{size}</option>)}
        </select>
      </label>
      <span>Cursor navigation</span>
      <div>
        <Button aria-label="Previous page" disabled={!hasPreviousPage} onClick={onPreviousPage} size="icon" type="button" variant="secondary"><ChevronLeft aria-hidden="true" /></Button>
        <Button aria-label="Next page" disabled={!hasNextPage} onClick={onNextPage} size="icon" type="button" variant="secondary"><ChevronRight aria-hidden="true" /></Button>
      </div>
    </nav>
  );
}

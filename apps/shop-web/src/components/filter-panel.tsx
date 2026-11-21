'use client';

import { SlidersHorizontal } from 'lucide-react';
import type { ReactNode } from 'react';
import { useState } from 'react';

export function FilterPanel({
    activeFilterCount,
    children,
}: {
    activeFilterCount: number;
    children: ReactNode;
}) {
    const [open, setOpen] = useState(false);

    return (
        <aside className={`filter-panel${open ? ' filter-panel--open' : ''}`}>
            <div className="filter-panel__title filter-panel__desktop-title">
                <SlidersHorizontal aria-hidden="true" />
                <h2>Filters</h2>
                {activeFilterCount > 0 && <span>{activeFilterCount}</span>}
            </div>
            <button
                aria-controls="catalog-filters"
                aria-expanded={open}
                className="filter-panel__mobile-toggle"
                onClick={() => setOpen((current) => !current)}
                type="button"
            >
                <SlidersHorizontal aria-hidden="true" />
                <strong>Filters</strong>
                {activeFilterCount > 0 && <span>{activeFilterCount}</span>}
                <small>{open ? 'Hide' : 'Show'}</small>
            </button>
            <div className="filter-panel__body" id="catalog-filters">
                {children}
            </div>
        </aside>
    );
}

import { Button } from '@mkbj/ui';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Search } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { restGet } from '../../api/rest';
import { useAuth } from '../../auth/auth-context';
import { EmptyState, ErrorState, PageLoading } from '../../components/feedback';
import { OffsetPagination } from '../../components/pagination';
import { StatusBadge } from '../../components/status-badge';
import type { CustomerRow, OffsetPage } from './customer-types';

function positiveInt(value: string | null, fallback: number): number {
    const parsed = Number(value);
    return Number.isInteger(parsed) && parsed > 0 ? parsed : fallback;
}

export function CustomerListPage() {
    const { token } = useAuth();
    const [params, setParams] = useSearchParams();
    const search = params.get('search') ?? '';
    const [draft, setDraft] = useState(search);
    const page = positiveInt(params.get('page'), 1);
    const pageSize = positiveInt(params.get('pageSize'), 20);
    useEffect(() => setDraft(search), [search]);
    useEffect(() => {
        if (draft.trim() === search) return;
        const timeout = window.setTimeout(
            () =>
                setParams(
                    (current) => {
                        const next = new URLSearchParams(current);
                        if (draft.trim()) next.set('search', draft.trim());
                        else next.delete('search');
                        next.set('page', '1');
                        return next;
                    },
                    { replace: true },
                ),
            300,
        );
        return () => window.clearTimeout(timeout);
    }, [draft, search, setParams]);
    const queryString = new URLSearchParams();
    ['kind', 'status', 'partnerId'].forEach((key) => {
        const value = params.get(key);
        if (value) queryString.set(key, value);
    });
    if (search) queryString.set('search', search);
    queryString.set('page', String(page));
    queryString.set('pageSize', String(pageSize));
    const query = useQuery({
        queryKey: ['admin-customers', queryString.toString()],
        queryFn: ({ signal }) =>
            restGet<OffsetPage<CustomerRow>>(
                `/api/v1/admin/customers?${queryString}`,
                token!,
                signal,
            ),
        enabled: Boolean(token),
        placeholderData: keepPreviousData,
    });
    const update = (key: string, value: string) =>
        setParams(
            (current) => {
                const next = new URLSearchParams(current);
                if (value) next.set(key, value);
                else next.delete(key);
                next.set('page', '1');
                return next;
            },
            { replace: true },
        );
    return (
        <section aria-labelledby="customers-title" className="page-stack">
            <div className="page-heading">
                <div>
                    <p className="eyebrow">Relationships</p>
                    <h1 id="customers-title">Customers</h1>
                    <p>
                        Find website and partner-linked buyers, review their history and manage
                        access.
                    </p>
                </div>
            </div>
            <div className="panel filter-panel">
                <div className="filters" role="search">
                    <label className="search-field">
                        <span>Search customers</span>
                        <span className="input-with-icon">
                            <Search aria-hidden="true" size={17} />
                            <input
                                onChange={(event) => setDraft(event.target.value)}
                                placeholder="Email, phone, name or external ID"
                                value={draft}
                            />
                        </span>
                    </label>
                    <label>
                        <span>Kind</span>
                        <select
                            onChange={(event) => update('kind', event.target.value)}
                            value={params.get('kind') ?? ''}
                        >
                            <option value="">All kinds</option>
                            <option value="Website">Website</option>
                            <option value="PartnerLinked">Partner linked</option>
                        </select>
                    </label>
                    <label>
                        <span>Status</span>
                        <select
                            onChange={(event) => update('status', event.target.value)}
                            value={params.get('status') ?? ''}
                        >
                            <option value="">All statuses</option>
                            <option value="Active">Active</option>
                            <option value="Suspended">Suspended</option>
                        </select>
                    </label>
                    <label>
                        <span>Partner ID</span>
                        <input
                            inputMode="numeric"
                            onChange={(event) => update('partnerId', event.target.value)}
                            placeholder="Any partner"
                            value={params.get('partnerId') ?? ''}
                        />
                    </label>
                    {[...params.keys()].length > 0 && (
                        <Button
                            onClick={() => {
                                setDraft('');
                                setParams({});
                            }}
                            variant="ghost"
                        >
                            Clear
                        </Button>
                    )}
                </div>
            </div>
            {query.isPending ? (
                <PageLoading label="Loading customers" />
            ) : query.isError ? (
                <ErrorState error={query.error} retry={() => void query.refetch()} />
            ) : !query.data.items.length ? (
                <EmptyState title="No customers found">
                    Try a broader search or clear the current filters.
                </EmptyState>
            ) : (
                <div className="panel table-panel" aria-busy={query.isFetching}>
                    <div className="table-summary">
                        <strong>{query.data.totalCount.toLocaleString()} customers</strong>
                        <span>{query.isFetching ? 'Updating…' : 'Newest first'}</span>
                    </div>
                    <div
                        className="table-scroll"
                        role="region"
                        aria-label="Customers table"
                        tabIndex={0}
                    >
                        <table>
                            <thead>
                                <tr>
                                    <th scope="col">Customer</th>
                                    <th scope="col">Kind</th>
                                    <th scope="col">Contact</th>
                                    <th scope="col">Partner reference</th>
                                    <th scope="col">Status</th>
                                    <th scope="col">
                                        <span className="sr-only">Actions</span>
                                    </th>
                                </tr>
                            </thead>
                            <tbody>
                                {query.data.items.map((customer) => (
                                    <tr key={customer.id}>
                                        <th scope="row">
                                            <Link
                                                className="product-link"
                                                to={`/customers/${customer.id}`}
                                            >
                                                {customer.fullName ||
                                                    customer.companyName ||
                                                    customer.email ||
                                                    `Customer ${customer.id}`}
                                            </Link>
                                            <span className="cell-subtitle">
                                                Created{' '}
                                                {new Intl.DateTimeFormat(undefined, {
                                                    dateStyle: 'medium',
                                                }).format(new Date(customer.createdAt))}
                                            </span>
                                        </th>
                                        <td>
                                            {customer.kind === 'PartnerLinked'
                                                ? 'Partner linked'
                                                : customer.kind}
                                        </td>
                                        <td>
                                            {customer.email ?? customer.phone ?? 'No contact'}
                                            {customer.email && customer.phone && (
                                                <span className="cell-subtitle">
                                                    {customer.phone}
                                                </span>
                                            )}
                                        </td>
                                        <td>
                                            {customer.externalCustomerId ?? '—'}
                                            {customer.partnerId && (
                                                <span className="cell-subtitle">
                                                    Partner {customer.partnerId}
                                                </span>
                                            )}
                                        </td>
                                        <td>
                                            <StatusBadge status={customer.status} />
                                        </td>
                                        <td>
                                            <Button asChild size="compact" variant="ghost">
                                                <Link to={`/customers/${customer.id}`}>View</Link>
                                            </Button>
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                    <OffsetPagination
                        itemLabel="Customers"
                        onPageChange={(nextPage) => update('page', String(nextPage))}
                        onPageSizeChange={(next) => {
                            update('pageSize', String(next));
                            update('page', '1');
                        }}
                        page={query.data.page}
                        pageCount={query.data.pageCount}
                        pageSize={query.data.pageSize}
                    />
                </div>
            )}
        </section>
    );
}

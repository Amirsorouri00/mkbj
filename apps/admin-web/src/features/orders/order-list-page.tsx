import { Button } from '@mkbj/ui';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Search } from 'lucide-react';
import { useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { restGet } from '../../api/rest';
import { useAuth } from '../../auth/auth-context';
import { EmptyState, ErrorState, PageLoading } from '../../components/feedback';
import { StatusBadge } from '../../components/status-badge';
import { type CursorPage, formatMinor, type OrderRow } from './order-types';

export function OrderListPage() {
    const { token } = useAuth();
    const [params, setParams] = useSearchParams();
    const [numberDraft, setNumberDraft] = useState(params.get('number') ?? '');
    const pageSize = Number(params.get('pageSize')) || 50;
    const queryString = new URLSearchParams();
    [
        'channel',
        'orderStatus',
        'paymentStatus',
        'currency',
        'customerId',
        'partnerId',
        'externalOrderId',
        'number',
        'from',
        'to',
        'cursor',
    ].forEach((key) => {
        const value = params.get(key);
        if (value) queryString.set(key, value);
    });
    queryString.set('pageSize', String(pageSize));
    const query = useQuery({
        queryKey: ['admin-orders', queryString.toString()],
        queryFn: ({ signal }) =>
            restGet<CursorPage<OrderRow>>(`/api/v1/admin/orders?${queryString}`, token!, signal),
        enabled: Boolean(token),
        placeholderData: keepPreviousData,
    });
    const update = (key: string, value: string) =>
        setParams(
            (current) => {
                const next = new URLSearchParams(current);
                if (value) next.set(key, value);
                else next.delete(key);
                next.delete('cursor');
                return next;
            },
            { replace: true },
        );
    return (
        <section aria-labelledby="orders-title" className="page-stack">
            <div className="page-heading">
                <div>
                    <p className="eyebrow">Sales</p>
                    <h1 id="orders-title">Orders</h1>
                    <p>Review website and partner orders, payments, documents and refunds.</p>
                </div>
            </div>
            <div className="panel filter-panel">
                <form
                    className="filters"
                    onSubmit={(event) => {
                        event.preventDefault();
                        update('number', numberDraft.trim());
                    }}
                    role="search"
                >
                    <label className="search-field">
                        <span>Order number</span>
                        <span className="input-with-icon">
                            <Search aria-hidden="true" size={17} />
                            <input
                                onChange={(event) => setNumberDraft(event.target.value)}
                                placeholder="Order number"
                                value={numberDraft}
                            />
                        </span>
                    </label>
                    <label>
                        <span>Channel</span>
                        <select
                            onChange={(event) => update('channel', event.target.value)}
                            value={params.get('channel') ?? ''}
                        >
                            <option value="">All channels</option>
                            <option value="Website">Website</option>
                            <option value="PartnerApi">Partner API</option>
                        </select>
                    </label>
                    <label>
                        <span>Order status</span>
                        <select
                            onChange={(event) => update('orderStatus', event.target.value)}
                            value={params.get('orderStatus') ?? ''}
                        >
                            <option value="">All statuses</option>
                            <option value="PendingPayment">Pending payment</option>
                            <option value="Paid">Paid</option>
                            <option value="Cancelled">Cancelled</option>
                            <option value="PartiallyRefunded">Partially refunded</option>
                            <option value="Refunded">Refunded</option>
                        </select>
                    </label>
                    <label>
                        <span>Payment</span>
                        <select
                            onChange={(event) => update('paymentStatus', event.target.value)}
                            value={params.get('paymentStatus') ?? ''}
                        >
                            <option value="">All payments</option>
                            <option value="Pending">Pending</option>
                            <option value="Paid">Paid</option>
                            <option value="PartiallyRefunded">Partially refunded</option>
                            <option value="Refunded">Refunded</option>
                        </select>
                    </label>
                    <Button type="submit" variant="secondary">
                        Search
                    </Button>
                    {[...params.keys()].length > 0 && (
                        <Button
                            onClick={() => {
                                setNumberDraft('');
                                setParams({});
                            }}
                            type="button"
                            variant="ghost"
                        >
                            Clear
                        </Button>
                    )}
                </form>
            </div>
            {query.isPending ? (
                <PageLoading label="Loading orders" />
            ) : query.isError ? (
                <ErrorState error={query.error} retry={() => void query.refetch()} />
            ) : !query.data.items.length ? (
                <EmptyState title="No orders found">
                    Try clearing filters or changing the date range.
                </EmptyState>
            ) : (
                <div className="panel table-panel" aria-busy={query.isFetching}>
                    <div className="table-summary">
                        <strong>{query.data.items.length} orders on this page</strong>
                        <span>{query.isFetching ? 'Updating…' : 'Newest first'}</span>
                    </div>
                    <div
                        className="table-scroll"
                        role="region"
                        aria-label="Orders table"
                        tabIndex={0}
                    >
                        <table>
                            <thead>
                                <tr>
                                    <th scope="col">Order</th>
                                    <th scope="col">Channel</th>
                                    <th scope="col">Customer</th>
                                    <th scope="col">Total</th>
                                    <th scope="col">Order status</th>
                                    <th scope="col">Payment</th>
                                    <th scope="col">Created</th>
                                </tr>
                            </thead>
                            <tbody>
                                {query.data.items.map((order) => (
                                    <tr key={order.id}>
                                        <th scope="row">
                                            <Link
                                                className="product-link"
                                                to={`/orders/${order.id}`}
                                            >
                                                {order.number}
                                            </Link>
                                            <span className="cell-subtitle">
                                                {order.externalOrderId || `ID ${order.id}`}
                                            </span>
                                        </th>
                                        <td>
                                            {order.channel === 'PartnerApi'
                                                ? 'Partner API'
                                                : order.channel}
                                        </td>
                                        <td>
                                            <Link to={`/customers/${order.customerId}`}>
                                                {order.customerId}
                                            </Link>
                                        </td>
                                        <td>
                                            {formatMinor(order.totalMinor, order.currency)}
                                            {order.refundedMinor > 0 && (
                                                <span className="cell-subtitle">
                                                    Refunded{' '}
                                                    {formatMinor(
                                                        order.refundedMinor,
                                                        order.currency,
                                                    )}
                                                </span>
                                            )}
                                        </td>
                                        <td>
                                            <StatusBadge status={order.status.order} />
                                        </td>
                                        <td>
                                            <StatusBadge status={order.status.payment} />
                                        </td>
                                        <td>
                                            {new Intl.DateTimeFormat(undefined, {
                                                dateStyle: 'medium',
                                            }).format(new Date(order.createdAt))}
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                    <nav aria-label="Orders pagination" className="pagination">
                        <label>
                            Rows
                            <select
                                onChange={(event) => update('pageSize', event.target.value)}
                                value={pageSize}
                            >
                                <option>10</option>
                                <option>25</option>
                                <option>50</option>
                                <option>100</option>
                            </select>
                        </label>
                        <span>Cursor navigation</span>
                        <Button
                            disabled={!query.data.nextCursor}
                            onClick={() =>
                                query.data.nextCursor &&
                                setParams((current) => {
                                    const next = new URLSearchParams(current);
                                    next.set('cursor', query.data.nextCursor!);
                                    return next;
                                })
                            }
                            variant="secondary"
                        >
                            Next page
                        </Button>
                    </nav>
                </div>
            )}
        </section>
    );
}

import { Button } from '@mkbj/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft } from 'lucide-react';
import { Link, useParams } from 'react-router-dom';
import { restGet, restSend } from '../../api/rest';
import { useAuth } from '../../auth/auth-context';
import { EmptyState, ErrorState, PageLoading } from '../../components/feedback';
import { StatusBadge } from '../../components/status-badge';
import type { Customer360 } from './customer-types';

export function CustomerDetailPage() {
    const { id } = useParams();
    const { token } = useAuth();
    const client = useQueryClient();
    const query = useQuery({
        queryKey: ['admin-customer-360', id],
        queryFn: ({ signal }) =>
            restGet<Customer360>(`/api/v1/admin/customers/${id}/360?orderLimit=20`, token!, signal),
        enabled: Boolean(id && token),
    });
    const status = useMutation({
        mutationFn: (action: 'suspend' | 'reactivate') =>
            restSend(`/api/v1/admin/customers/${id}/${action}`, token!, 'POST'),
        onSuccess: async () => {
            await client.invalidateQueries({ queryKey: ['admin-customer-360', id] });
            await client.invalidateQueries({ queryKey: ['admin-customers'] });
        },
    });
    if (query.isPending) return <PageLoading label="Loading customer" />;
    if (query.isError) return <ErrorState error={query.error} retry={() => void query.refetch()} />;
    const { customer, recentOrders } = query.data;
    return (
        <section aria-labelledby="customer-title" className="page-stack">
            <Button asChild className="back-link" variant="ghost">
                <Link to="/customers">
                    <ArrowLeft size={16} />
                    Customers
                </Link>
            </Button>
            <div className="page-heading">
                <div>
                    <p className="eyebrow">Customer 360</p>
                    <h1 id="customer-title">
                        {customer.fullName ||
                            customer.companyName ||
                            customer.email ||
                            `Customer ${customer.id}`}
                    </h1>
                    <p>
                        {customer.kind === 'PartnerLinked'
                            ? 'Partner-linked buyer'
                            : 'Website customer'}{' '}
                        · ID {customer.id}
                    </p>
                </div>
                <div className="heading-actions">
                    <StatusBadge status={customer.status} />
                    <Button
                        disabled={status.isPending}
                        onClick={() =>
                            status.mutate(customer.status === 'Active' ? 'suspend' : 'reactivate')
                        }
                        variant={customer.status === 'Active' ? 'secondary' : 'primary'}
                    >
                        {status.isPending
                            ? 'Updating…'
                            : customer.status === 'Active'
                              ? 'Suspend customer'
                              : 'Reactivate customer'}
                    </Button>
                </div>
            </div>
            {status.isError && <ErrorState error={status.error} />}
            <div className="detail-grid">
                <section className="panel detail-section">
                    <div className="section-heading">
                        <h2>Customer profile</h2>
                    </div>
                    <dl className="definition-grid">
                        <div>
                            <dt>Status</dt>
                            <dd>{customer.status}</dd>
                        </div>
                        <div>
                            <dt>Email verified</dt>
                            <dd>{customer.emailVerified ? 'Yes' : 'No'}</dd>
                        </div>
                        <div>
                            <dt>Email</dt>
                            <dd>{customer.email ?? '—'}</dd>
                        </div>
                        <div>
                            <dt>Phone</dt>
                            <dd>{customer.phone ?? '—'}</dd>
                        </div>
                        <div>
                            <dt>Company</dt>
                            <dd>{customer.companyName ?? '—'}</dd>
                        </div>
                        <div>
                            <dt>Registration number</dt>
                            <dd>{customer.registrationNumber ?? '—'}</dd>
                        </div>
                        <div>
                            <dt>Partner ID</dt>
                            <dd>{customer.partnerId ?? '—'}</dd>
                        </div>
                        <div>
                            <dt>External customer ID</dt>
                            <dd>{customer.externalCustomerId ?? '—'}</dd>
                        </div>
                        {customer.billingAddress && (
                            <div className="definition-wide">
                                <dt>Billing address</dt>
                                <dd>
                                    {[
                                        customer.billingAddress.line1,
                                        customer.billingAddress.line2,
                                        customer.billingAddress.city,
                                        customer.billingAddress.postalCode,
                                        customer.billingAddress.countryIso2,
                                    ]
                                        .filter(Boolean)
                                        .join(', ')}
                                </dd>
                            </div>
                        )}
                    </dl>
                </section>
                <section className="panel detail-section">
                    <div className="section-heading">
                        <h2>Contact history</h2>
                        <span>{customer.contactHistory.length}</span>
                    </div>
                    {customer.contactHistory.length ? (
                        <ul className="plain-list">
                            {customer.contactHistory.map((entry) => (
                                <li key={`${entry.changedAt}-${entry.field}`}>
                                    <strong>{entry.field}</strong>
                                    <span>
                                        {entry.oldValue ?? 'Empty'} → {entry.newValue ?? 'Empty'}
                                    </span>
                                    <small>
                                        {new Intl.DateTimeFormat(undefined, {
                                            dateStyle: 'medium',
                                            timeStyle: 'short',
                                        }).format(new Date(entry.changedAt))}
                                    </small>
                                </li>
                            ))}
                        </ul>
                    ) : (
                        <p className="muted">No contact changes recorded.</p>
                    )}
                </section>
            </div>
            <section className="panel detail-section">
                <div className="section-heading">
                    <h2>Recent orders</h2>
                    <span>{recentOrders.length}</span>
                </div>
                {recentOrders.length ? (
                    <div className="table-scroll">
                        <table>
                            <thead>
                                <tr>
                                    <th scope="col">Order</th>
                                    <th scope="col">Channel</th>
                                    <th scope="col">Total</th>
                                    <th scope="col">Payment</th>
                                    <th scope="col">Created</th>
                                </tr>
                            </thead>
                            <tbody>
                                {recentOrders.map((order) => (
                                    <tr key={order.id}>
                                        <th scope="row">
                                            <Link
                                                className="product-link"
                                                to={`/orders/${order.id}`}
                                            >
                                                {order.number}
                                            </Link>
                                        </th>
                                        <td>{order.channel}</td>
                                        <td>
                                            {order.currency} {(order.totalMinor / 100).toFixed(2)}
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
                ) : (
                    <EmptyState title="No orders yet">
                        This customer has no recent orders.
                    </EmptyState>
                )}
            </section>
        </section>
    );
}

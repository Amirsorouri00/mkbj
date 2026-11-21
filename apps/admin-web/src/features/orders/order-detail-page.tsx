import * as Dialog from '@radix-ui/react-dialog';
import { Button } from '@mkbj/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, FileText, X } from 'lucide-react';
import { type FormEvent, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { downloadAuthenticatedPdfData, restGet, restSend } from '../../api/rest';
import { toAppError } from '../../api/errors';
import { useAuth } from '../../auth/auth-context';
import { ConfirmationDialog } from '../../components/confirmation-dialog';
import { ErrorState, PageLoading } from '../../components/feedback';
import { StatusBadge } from '../../components/status-badge';
import { formatMinor, type Order360 } from './order-types';

export function OrderDetailPage() {
    const { id } = useParams();
    const { token, roles } = useAuth();
    const client = useQueryClient();
    const [confirmOpen, setConfirmOpen] = useState(false);
    const [cancelOpen, setCancelOpen] = useState(false);
    const [refundOpen, setRefundOpen] = useState(false);
    const [method, setMethod] = useState('bank-transfer');
    const [reference, setReference] = useState('');
    const [amount, setAmount] = useState('');
    const [reason, setReason] = useState('');
    const [actionError, setActionError] = useState<string | null>(null);
    const [documentError, setDocumentError] = useState<string | null>(null);
    const [downloadingDocumentId, setDownloadingDocumentId] = useState<string | null>(null);
    const query = useQuery({
        queryKey: ['admin-order-360', id],
        queryFn: ({ signal }) =>
            restGet<Order360>(`/api/v1/admin/orders/${id}/360`, token!, signal),
        enabled: Boolean(id && token),
    });
    const refresh = async () => {
        await client.invalidateQueries({ queryKey: ['admin-order-360', id] });
        await client.invalidateQueries({ queryKey: ['admin-orders'] });
    };
    const action = useMutation({
        mutationFn: ({ path, body }: { path: string; body?: unknown }) =>
            restSend(`/api/v1/admin/orders/${id}/${path}`, token!, 'POST', body),
        onSuccess: async () => {
            setConfirmOpen(false);
            setCancelOpen(false);
            setRefundOpen(false);
            setActionError(null);
            await refresh();
        },
        onError: (error) => setActionError(toAppError(error).message),
    });
    if (query.isPending) return <PageLoading label="Loading order" />;
    if (query.isError) return <ErrorState error={query.error} retry={() => void query.refetch()} />;
    const { order, customer } = query.data;
    const summary = order.summary;
    const submitRefund = (event: FormEvent) => {
        event.preventDefault();
        setActionError(null);
        action.mutate({ path: 'refunds', body: { amount, reason, externalReference: null } });
    };
    return (
        <section aria-labelledby="order-title" className="page-stack">
            <Button asChild className="back-link" variant="ghost">
                <Link to="/orders">
                    <ArrowLeft size={16} />
                    Orders
                </Link>
            </Button>
            <div className="page-heading">
                <div>
                    <p className="eyebrow">Order 360</p>
                    <h1 id="order-title">{summary.number}</h1>
                    <p>
                        {summary.channel === 'PartnerApi' ? 'Partner API' : summary.channel} order ·{' '}
                        {new Intl.DateTimeFormat(undefined, { dateStyle: 'long' }).format(
                            new Date(summary.createdAt),
                        )}
                    </p>
                </div>
                <div className="heading-actions">
                    <StatusBadge status={summary.status.order} />
                    <StatusBadge status={summary.status.payment} />
                    {summary.channel === 'Website' && summary.status.payment === 'Pending' && (
                        <Button onClick={() => setConfirmOpen(true)}>Confirm payment</Button>
                    )}
                    {summary.status.order !== 'Cancelled' && (
                        <Button onClick={() => setCancelOpen(true)} variant="secondary">
                            Cancel order
                        </Button>
                    )}
                    {roles.includes('super_admin') && summary.status.payment !== 'Pending' && (
                        <Button onClick={() => setRefundOpen(true)} variant="secondary">
                            Record refund
                        </Button>
                    )}
                </div>
            </div>
            {actionError && (
                <div className="dialog-error" role="alert">
                    {actionError}
                </div>
            )}
            <div className="detail-grid">
                <section className="panel detail-section">
                    <h2>Summary</h2>
                    <dl className="definition-grid">
                        <div>
                            <dt>Total</dt>
                            <dd>{formatMinor(summary.totalMinor, summary.currency)}</dd>
                        </div>
                        <div>
                            <dt>Refunded</dt>
                            <dd>{formatMinor(summary.refundedMinor, summary.currency)}</dd>
                        </div>
                        <div>
                            <dt>Customer</dt>
                            <dd>
                                <Link to={`/customers/${summary.customerId}`}>
                                    {customer?.fullName ||
                                        customer?.companyName ||
                                        customer?.email ||
                                        summary.customerId}
                                </Link>
                            </dd>
                        </div>
                        <div>
                            <dt>Lines</dt>
                            <dd>{order.lineCount}</dd>
                        </div>
                        <div>
                            <dt>External order ID</dt>
                            <dd>{summary.externalOrderId ?? '—'}</dd>
                        </div>
                        <div>
                            <dt>Correlation ID</dt>
                            <dd>{order.correlationId}</dd>
                        </div>
                        <div className="definition-wide">
                            <dt>Fulfilment statement</dt>
                            <dd>{summary.status.fulfillmentStatement}</dd>
                        </div>
                    </dl>
                </section>
                <section className="panel detail-section">
                    <div className="section-heading">
                        <h2>Documents</h2>
                        <span>{order.documents.length}</span>
                    </div>
                    {order.documents.length ? (
                        <>
                        {documentError && <div className="dialog-error" role="alert">{documentError}</div>}
                        <ul className="plain-list">
                            {order.documents.map((document) => (
                                <li key={document.id}>
                                    <FileText aria-hidden="true" size={18} />
                                    <strong>
                                        {document.type} · {document.number}
                                    </strong>
                                    <small>
                                        {new Intl.DateTimeFormat(undefined, {
                                            dateStyle: 'medium',
                                        }).format(new Date(document.issuedAt))}
                                    </small>
                                    <Button
                                        onClick={() => {
                                            void (async () => {
                                                setDocumentError(null);
                                                setDownloadingDocumentId(document.id);
                                                try {
                                                    await downloadAuthenticatedPdfData(
                                                        `/api/v1/admin/documents/${document.id}/pdf-data`,
                                                        token!,
                                                        `${document.number}.pdf`,
                                                    );
                                                } catch (caught) {
                                                    setDocumentError(toAppError(caught).message);
                                                } finally {
                                                    setDownloadingDocumentId(null);
                                                }
                                            })();
                                        }}
                                        disabled={downloadingDocumentId !== null}
                                        size="compact"
                                        variant="ghost"
                                    >
                                        {downloadingDocumentId === document.id ? 'Preparing PDF…' : 'Download PDF'}
                                    </Button>
                                </li>
                            ))}
                        </ul>
                        </>
                    ) : (
                        <p className="muted">No documents have been issued.</p>
                    )}
                </section>
            </div>
            <div className="detail-grid">
                <section className="panel detail-section">
                    <div className="section-heading">
                        <h2>Payments</h2>
                        <span>{order.payments.length}</span>
                    </div>
                    {order.payments.length ? (
                        <ul className="plain-list">
                            {order.payments.map((payment) => (
                                <li key={payment.id}>
                                    <strong>
                                        {formatMinor(payment.amountMinor, summary.currency)}
                                    </strong>
                                    <span>{payment.method || payment.source}</span>
                                    <small>{payment.reference || 'No reference'}</small>
                                </li>
                            ))}
                        </ul>
                    ) : (
                        <p className="muted">No payment recorded.</p>
                    )}
                </section>
                <section className="panel detail-section">
                    <div className="section-heading">
                        <h2>Refunds</h2>
                        <span>{order.refunds.length}</span>
                    </div>
                    {order.refunds.length ? (
                        <ul className="plain-list">
                            {order.refunds.map((refund) => (
                                <li key={refund.id}>
                                    <strong>
                                        {formatMinor(refund.amountMinor, summary.currency)}
                                    </strong>
                                    <span>{refund.reason}</span>
                                    <small>
                                        {refund.externalReference ||
                                            `Credit note ${refund.creditNoteDocumentId}`}
                                    </small>
                                </li>
                            ))}
                        </ul>
                    ) : (
                        <p className="muted">No refunds recorded.</p>
                    )}
                </section>
            </div>
            <Dialog.Root onOpenChange={setConfirmOpen} open={confirmOpen}>
                <Dialog.Portal>
                    <Dialog.Overlay className="dialog-overlay" />
                    <Dialog.Content className="dialog-panel">
                        <div className="dialog-heading">
                            <Dialog.Title>Confirm external payment</Dialog.Title>
                            <Dialog.Close asChild>
                                <Button aria-label="Close" size="icon" variant="ghost">
                                    <X />
                                </Button>
                            </Dialog.Close>
                        </div>
                        <Dialog.Description>
                            Confirm only after matching the external payment. This issues the
                            commercial invoice.
                        </Dialog.Description>
                        <form
                            className="dialog-form"
                            onSubmit={(event) => {
                                event.preventDefault();
                                action.mutate({
                                    path: 'confirm-payment',
                                    body: { method, reference: reference || null },
                                });
                            }}
                        >
                            <label className="field">
                                <span>Method *</span>
                                <input
                                    onChange={(event) => setMethod(event.target.value)}
                                    required
                                    value={method}
                                />
                            </label>
                            <label className="field">
                                <span>Reference</span>
                                <input
                                    onChange={(event) => setReference(event.target.value)}
                                    value={reference}
                                />
                            </label>
                            <div className="dialog-actions">
                                <Dialog.Close asChild>
                                    <Button variant="secondary">Cancel</Button>
                                </Dialog.Close>
                                <Button disabled={action.isPending} type="submit">
                                    {action.isPending ? 'Confirming…' : 'Confirm payment'}
                                </Button>
                            </div>
                        </form>
                    </Dialog.Content>
                </Dialog.Portal>
            </Dialog.Root>
            <Dialog.Root onOpenChange={setRefundOpen} open={refundOpen}>
                <Dialog.Portal>
                    <Dialog.Overlay className="dialog-overlay" />
                    <Dialog.Content className="dialog-panel">
                        <div className="dialog-heading">
                            <Dialog.Title>Record refund</Dialog.Title>
                            <Dialog.Close asChild>
                                <Button aria-label="Close" size="icon" variant="ghost">
                                    <X />
                                </Button>
                            </Dialog.Close>
                        </div>
                        <Dialog.Description>
                            A credit note will be issued. Amounts use exactly two decimal places.
                        </Dialog.Description>
                        <form className="dialog-form" onSubmit={submitRefund}>
                            <label className="field">
                                <span>Amount ({summary.currency}) *</span>
                                <input
                                    inputMode="decimal"
                                    onChange={(event) => setAmount(event.target.value)}
                                    pattern="\d+\.\d{2}"
                                    placeholder="12.00"
                                    required
                                    value={amount}
                                />
                            </label>
                            <label className="field">
                                <span>Reason *</span>
                                <textarea
                                    onChange={(event) => setReason(event.target.value)}
                                    required
                                    rows={3}
                                    value={reason}
                                />
                            </label>
                            <div className="dialog-actions">
                                <Dialog.Close asChild>
                                    <Button variant="secondary">Cancel</Button>
                                </Dialog.Close>
                                <Button disabled={action.isPending} type="submit">
                                    {action.isPending ? 'Recording…' : 'Record refund'}
                                </Button>
                            </div>
                        </form>
                    </Dialog.Content>
                </Dialog.Portal>
            </Dialog.Root>
            <ConfirmationDialog
                confirmLabel="Cancel order"
                description="Cancel this order? The backend will refuse cancellation when the order state no longer permits it."
                destructive
                onConfirm={() => action.mutate({ path: 'cancel' })}
                onOpenChange={setCancelOpen}
                open={cancelOpen}
                pending={action.isPending}
                title="Cancel this order?"
            />
        </section>
    );
}

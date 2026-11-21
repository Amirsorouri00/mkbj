import { Button } from '@mkbj/ui';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { downloadAuthenticatedPdfData, restGet } from '../../api/rest';
import { useAuth } from '../../auth/auth-context';
import { ErrorState, PageLoading } from '../../components/feedback';
import { appConfig } from '../../config';

interface Receipt {
    id: string;
    partnerId: string;
    receivedAt: string;
    completedAt: string | null;
    correlationId: string;
    path: string;
    externalOrderIds: string[];
    statusCode: number | null;
    replayed: boolean;
}
interface ReceiptDetail {
    request: Receipt;
    requestBody: string;
    responseBody: string | null;
    orders: {
        id: string;
        number: string;
        externalOrderId: string;
        documents: { id: string; number: string }[];
    }[];
}
function outcome(row: Receipt) {
    if (row.statusCode === null) return 'Outcome not recorded';
    if (row.replayed) return `${row.statusCode} · Duplicate replay`;
    return `${row.statusCode} · ${row.statusCode < 300 ? 'Completed' : 'Rejected or interrupted'}`;
}
function JsonPanel({ title, value }: { title: string; value: string | null }) {
    let formatted = value ?? 'No response was recorded. Check the linked orders before retrying.';
    try {
        formatted = JSON.stringify(JSON.parse(formatted), null, 2);
    } catch {
        /* Preserve malformed submissions for diagnosis. */
    }
    return (
        <section className="panel detail-section">
            <h2>{title}</h2>
            <pre
                style={{
                    whiteSpace: 'pre-wrap',
                    overflowWrap: 'anywhere',
                    fontSize: 13,
                    maxHeight: 520,
                    overflowY: 'auto',
                }}
                tabIndex={0}
                aria-label={title}
            >
                {formatted}
            </pre>
        </section>
    );
}
export function PartnerRequestsPage() {
    const { token } = useAuth();
    const [params, setParams] = useSearchParams();
    const [search, setSearch] = useState(params.get('search') ?? '');
    const query = useQuery({
        queryKey: ['partner-requests', params.toString()],
        queryFn: ({ signal }) =>
            restGet<{ items: Receipt[]; nextCursor: string | null }>(
                `/api/v1/admin/partner-requests?${params}`,
                token!,
                signal,
            ),
        enabled: Boolean(token),
    });
    return (
        <section className="page-stack" aria-labelledby="requests-title">
            <div className="page-heading">
                <div>
                    <p className="eyebrow">Partner API</p>
                    <h1 id="requests-title">API requests</h1>
                    <p>Review submitted inputs, recorded responses, orders and invoices.</p>
                </div>
                <Button asChild variant="secondary">
                    <a href={`${appConfig.apiBaseUrl}/scalar/v1`} target="_blank" rel="noreferrer">
                        Open API explorer
                    </a>
                </Button>
            </div>
            <form
                className="panel filters"
                role="search"
                onSubmit={(event) => {
                    event.preventDefault();
                    setParams(search.trim() ? { search: search.trim() } : {});
                }}
            >
                <label className="search-field">
                    <span>External order ID or correlation ID</span>
                    <input
                        value={search}
                        onChange={(event) => setSearch(event.target.value)}
                        placeholder="Exact reference"
                    />
                </label>
                <Button type="submit">Search</Button>
                <Button type="button" variant="secondary" onClick={() => void query.refetch()}>
                    Refresh
                </Button>
            </form>
            {query.isPending ? (
                <PageLoading label="Loading requests" />
            ) : query.isError ? (
                <ErrorState error={query.error} retry={() => void query.refetch()} />
            ) : (
                <>
                    <div className="panel table-scroll">
                        <table>
                            <caption className="sr-only">
                                Partner order submissions, newest first
                            </caption>
                            <thead>
                                <tr>
                                    <th>Received</th>
                                    <th>Reference</th>
                                    <th>Partner</th>
                                    <th>Outcome</th>
                                    <th>Details</th>
                                </tr>
                            </thead>
                            <tbody>
                                {query.data.items.map((row) => (
                                    <tr key={row.id}>
                                        <td>{new Date(row.receivedAt).toLocaleString()}</td>
                                        <td>
                                            {row.externalOrderIds.join(', ') ||
                                                'Invalid or missing reference'}
                                        </td>
                                        <td>{row.partnerId}</td>
                                        <td>{outcome(row)}</td>
                                        <td>
                                            <Link to={`/partner-requests/${row.id}`}>
                                                View request
                                            </Link>
                                        </td>
                                    </tr>
                                ))}
                                {!query.data.items.length && (
                                    <tr>
                                        <td colSpan={5}>
                                            No submissions found. Submit an order through the API
                                            explorer to see it here.
                                        </td>
                                    </tr>
                                )}
                            </tbody>
                        </table>
                    </div>
                    <div className="heading-actions">
                        <Button
                            variant="secondary"
                            disabled={!params.has('cursor')}
                            onClick={() => {
                                const next = new URLSearchParams(params);
                                next.delete('cursor');
                                setParams(next);
                            }}
                        >
                            Newest
                        </Button>
                        <Button
                            disabled={!query.data.nextCursor}
                            onClick={() => {
                                const next = new URLSearchParams(params);
                                next.set('cursor', query.data.nextCursor!);
                                setParams(next);
                            }}
                        >
                            Older requests
                        </Button>
                    </div>
                </>
            )}
        </section>
    );
}
export function PartnerRequestDetailPage() {
    const { id } = useParams();
    const { token } = useAuth();
    const [downloadError, setDownloadError] = useState<string | null>(null);
    const [downloading, setDownloading] = useState<string | null>(null);
    const query = useQuery({
        queryKey: ['partner-request', id],
        queryFn: ({ signal }) =>
            restGet<ReceiptDetail>(`/api/v1/admin/partner-requests/${id}`, token!, signal),
        enabled: Boolean(token && id),
    });
    if (query.isPending) return <PageLoading label="Loading request" />;
    if (query.isError) return <ErrorState error={query.error} retry={() => void query.refetch()} />;
    const { request, orders } = query.data;
    return (
        <section className="page-stack" aria-labelledby="request-title">
            <Link to="/partner-requests">Back to API requests</Link>
            <div className="page-heading">
                <div>
                    <p className="eyebrow">Partner API</p>
                    <h1 id="request-title">Request details</h1>
                    <p>{outcome(request)}</p>
                </div>
                <Button variant="secondary" onClick={() => void query.refetch()}>
                    Refresh
                </Button>
            </div>
            <section className="panel detail-section">
                <h2>Submission</h2>
                <dl className="definition-grid">
                    <div>
                        <dt>Request ID</dt>
                        <dd>{request.id}</dd>
                    </div>
                    <div>
                        <dt>Partner ID</dt>
                        <dd>{request.partnerId}</dd>
                    </div>
                    <div>
                        <dt>Received</dt>
                        <dd>{new Date(request.receivedAt).toLocaleString()}</dd>
                    </div>
                    <div>
                        <dt>Endpoint</dt>
                        <dd>POST {request.path}</dd>
                    </div>
                    <div>
                        <dt>Correlation ID</dt>
                        <dd style={{ overflowWrap: 'anywhere' }}>{request.correlationId}</dd>
                    </div>
                </dl>
            </section>
            <section className="panel detail-section">
                <h2>Orders & invoices</h2>
                <p>
                    Linked by partner and external order ID. A rejected retry can still reference
                    the original order.
                </p>
                {downloadError && <p role="alert">{downloadError}</p>}
                {!orders.length && <p>No committed order is linked to this submission.</p>}
                {orders.map((order) => (
                    <div className="heading-actions" key={order.id}>
                        <Link to={`/orders/${order.id}`}>{order.number}</Link>
                        {order.documents.map((document) => (
                            <Button
                                key={document.id}
                                variant="secondary"
                                disabled={downloading !== null}
                                onClick={() => {
                                    void (async () => {
                                        setDownloadError(null);
                                        setDownloading(document.id);
                                        try {
                                            await downloadAuthenticatedPdfData(
                                                `/api/v1/admin/documents/${document.id}/pdf-data`,
                                                token!,
                                                `${document.number}.pdf`,
                                            );
                                        } catch (error) {
                                            setDownloadError(
                                                error instanceof Error
                                                    ? error.message
                                                    : 'The PDF could not be downloaded. Try again.',
                                            );
                                        } finally {
                                            setDownloading(null);
                                        }
                                    })();
                                }}
                            >
                                {downloading === document.id
                                    ? 'Preparing PDF…'
                                    : `Download ${document.number}`}
                            </Button>
                        ))}
                    </div>
                ))}
            </section>
            <div className="detail-grid">
                <JsonPanel title="Submitted input" value={query.data.requestBody} />
                <JsonPanel title="Recorded response" value={query.data.responseBody} />
            </div>
        </section>
    );
}

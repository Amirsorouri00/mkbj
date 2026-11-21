import { Button } from '@mkbj/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft } from 'lucide-react';
import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { restGet, restSend } from '../../api/rest';
import { toAppError } from '../../api/errors';
import { useAuth } from '../../auth/auth-context';
import { ErrorState, PageLoading } from '../../components/feedback';
import { StatusBadge } from '../../components/status-badge';

interface StagedRecordDetail {
    summary: {
        id: string;
        lastJobId: string;
        sourceUrl: string;
        sourceDomain: string | null;
        status: string;
        matchKind: string;
        matchedProductId: string | null;
        issueCodes: string[];
        publishedProductId: string | null;
        publishSkippedDeactivated: boolean;
        createdAt: string;
    };
    normalized: Record<string, unknown> | null;
    issues: Array<{ code: string; severity: string; detail: string | null }>;
    candidates: Array<{ productId: string; title: string; similarity: number }>;
    reviewedBy: string | null;
    rightsApprovedBy: string | null;
    reviewNote: string | null;
}

export function StagedRecordDetailPage() {
    const { id = '' } = useParams();
    const { token } = useAuth();
    const client = useQueryClient();
    const [categoryId, setCategoryId] = useState('');
    const [productId, setProductId] = useState('');
    const [reason, setReason] = useState('');
    const [message, setMessage] = useState<string | null>(null);
    const [error, setError] = useState<string | null>(null);
    const record = useQuery({
        queryKey: ['admin-import-record', id],
        queryFn: ({ signal }) =>
            restGet<StagedRecordDetail>(`/api/v1/admin/imports/records/${id}`, token!, signal),
        enabled: Boolean(token && id),
    });
    const decide = useMutation({
        mutationFn: ({ action, body }: { action: string; body?: unknown }) =>
            restSend(`/api/v1/admin/imports/records/${id}/${action}`, token!, 'POST', body),
        onSuccess: async () => {
            setError(null);
            setMessage('Review decision saved and the record was re-evaluated.');
            await Promise.all([
                client.invalidateQueries({ queryKey: ['admin-import-record', id] }),
                client.invalidateQueries({ queryKey: ['admin-import-records'] }),
            ]);
        },
        onError: (reasonValue) => setError(toAppError(reasonValue).message),
    });
    if (record.isPending) return <PageLoading label="Loading staged record" />;
    if (record.isError)
        return <ErrorState error={record.error} retry={() => void record.refetch()} />;
    const detail = record.data;
    const reviewable = !['Published', 'Rejected'].includes(detail.summary.status);
    return (
        <section aria-labelledby="staged-record-title" className="page-stack">
            <Link className="back-link" to="/imports?view=records">
                <ArrowLeft size={16} /> Back to review queue
            </Link>
            <div className="page-heading">
                <div>
                    <p className="eyebrow">Import job {detail.summary.lastJobId}</p>
                    <h1 id="staged-record-title">Staged record {detail.summary.id}</h1>
                    <p>
                        Compare normalized source data, validation findings, and catalogue matches.
                    </p>
                </div>
                <StatusBadge status={detail.summary.status} />
            </div>
            {(message || error) && (
                <div
                    className={error ? 'dialog-error' : 'success-banner'}
                    role={error ? 'alert' : 'status'}
                >
                    {error ?? message}
                </div>
            )}
            <div className="detail-grid">
                <section className="panel detail-section">
                    <h2>Normalized product</h2>
                    {detail.normalized ? (
                        <pre className="json-preview">
                            {JSON.stringify(detail.normalized, null, 2)}
                        </pre>
                    ) : (
                        <p>No normalized document is available.</p>
                    )}
                </section>
                <section className="panel detail-section">
                    <h2>Source and match</h2>
                    <dl className="record-list">
                        <div>
                            <dt>Source</dt>
                            <dd>
                                <a href={detail.summary.sourceUrl} rel="noreferrer" target="_blank">
                                    {detail.summary.sourceDomain ?? detail.summary.sourceUrl}
                                </a>
                            </dd>
                        </div>
                        <div>
                            <dt>Match</dt>
                            <dd>{detail.summary.matchKind}</dd>
                        </div>
                        <div>
                            <dt>Matched product</dt>
                            <dd>{detail.summary.matchedProductId ?? 'None'}</dd>
                        </div>
                        <div>
                            <dt>Published product</dt>
                            <dd>{detail.summary.publishedProductId ?? 'Not published'}</dd>
                        </div>
                    </dl>
                </section>
            </div>
            <div className="detail-grid">
                <section className="panel detail-section">
                    <h2>Validation findings</h2>
                    {!detail.issues.length ? (
                        <p>No validation findings.</p>
                    ) : (
                        <ul className="plain-list">
                            {detail.issues.map((issue, index) => (
                                <li key={`${issue.code}-${index}`}>
                                    <strong>{issue.code}</strong>
                                    <StatusBadge status={issue.severity} />
                                    <span>{issue.detail ?? 'No additional detail.'}</span>
                                </li>
                            ))}
                        </ul>
                    )}
                </section>
                <section className="panel detail-section">
                    <h2>Possible matches</h2>
                    {!detail.candidates.length ? (
                        <p>No fuzzy candidates.</p>
                    ) : (
                        <ul className="plain-list">
                            {detail.candidates.map((candidate) => (
                                <li key={candidate.productId}>
                                    <Link to={`/catalog/products/${candidate.productId}`}>
                                        {candidate.title}
                                    </Link>
                                    <strong>{Math.round(candidate.similarity * 100)}%</strong>
                                    <span>Product {candidate.productId}</span>
                                </li>
                            ))}
                        </ul>
                    )}
                </section>
            </div>
            {reviewable && (
                <section
                    className="panel detail-section page-stack"
                    aria-labelledby="decision-title"
                >
                    <div>
                        <p className="eyebrow">Human decision</p>
                        <h2 id="decision-title">Resolve this record</h2>
                    </div>
                    <div className="form-grid">
                        <label className="field">
                            <span>Category override ID</span>
                            <input
                                inputMode="numeric"
                                onChange={(event) => setCategoryId(event.target.value)}
                                value={categoryId}
                            />
                        </label>
                        <div className="field action-field">
                            <span>Approve current match</span>
                            <Button
                                disabled={decide.isPending}
                                onClick={() =>
                                    decide.mutate({
                                        action: 'approve',
                                        body: {
                                            categoryOverrideId: categoryId
                                                ? Number(categoryId)
                                                : null,
                                        },
                                    })
                                }
                            >
                                Approve
                            </Button>
                        </div>
                        <label className="field">
                            <span>Merge into product ID</span>
                            <input
                                inputMode="numeric"
                                onChange={(event) => setProductId(event.target.value)}
                                value={productId}
                            />
                        </label>
                        <div className="field action-field">
                            <span>Use an existing catalogue product</span>
                            <Button
                                disabled={!productId || decide.isPending}
                                onClick={() =>
                                    decide.mutate({
                                        action: 'merge',
                                        body: { productId: Number(productId) },
                                    })
                                }
                                variant="secondary"
                            >
                                Merge record
                            </Button>
                        </div>
                        <div className="field">
                            <span>Create a distinct catalogue product</span>
                            <Button
                                disabled={decide.isPending}
                                onClick={() => decide.mutate({ action: 'create-new' })}
                                variant="secondary"
                            >
                                Create new product
                            </Button>
                        </div>
                        <label className="field">
                            <span>Rejection reason</span>
                            <input
                                onChange={(event) => setReason(event.target.value)}
                                value={reason}
                            />
                        </label>
                        <div className="field action-field">
                            <span>Keep this source out of the catalogue</span>
                            <Button
                                disabled={!reason.trim() || decide.isPending}
                                onClick={() =>
                                    decide.mutate({ action: 'reject', body: { reason } })
                                }
                                variant="destructive"
                            >
                                Reject record
                            </Button>
                        </div>
                    </div>
                </section>
            )}
        </section>
    );
}

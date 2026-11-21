import { Button } from '@mkbj/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, Download, FileUp, Play } from 'lucide-react';
import { type FormEvent, useEffect, useMemo, useState } from 'react';
import { Link, useLocation, useParams } from 'react-router-dom';
import { downloadAuthenticatedFile, restGet, restSend, uploadImportFile, type ImportFilePreview } from '../../api/rest';
import { toAppError } from '../../api/errors';
import { useAuth } from '../../auth/auth-context';
import { ErrorState, PageLoading } from '../../components/feedback';
import { StatusBadge } from '../../components/status-badge';

interface ImportJobDetail {
    summary: {
        id: string;
        mode: string;
        status: string;
        discovered: number;
        extracted: number;
        valid: number;
        invalid: number;
        duplicate: number;
        published: number;
        review: number;
        createdAt: string;
        finishedAt: string | null;
        errorCode: string | null;
    };
    sources: string[];
    queries: string[];
    targetCategoryId: string | null;
    publishPolicy: string;
    checkpoint: string;
    stepSequence: number;
    fileObjectKey: string | null;
    fileFormat: string | null;
}

const fields = [
    'ignore',
    'group_key',
    'title',
    'brand',
    'category_path',
    'short_description',
    'description',
    'gtin',
    'upc',
    'ean',
    'mpn',
    'sku',
    'source_url',
    'source_domain',
    'source_id',
    'price',
    'currency',
    'tax_included',
    'availability',
    'image_urls',
] as const;

function identityMapping(columns: string[]): Record<string, string> {
    return Object.fromEntries(
        columns.map((column) => {
            const normalized = column.toLowerCase();
            return [
                column,
                fields.includes(normalized as (typeof fields)[number]) || normalized.startsWith('attr:')
                    ? normalized
                    : 'ignore',
            ];
        }),
    );
}

export function ImportJobDetailPage() {
    const { id = '' } = useParams();
    const location = useLocation();
    const { token } = useAuth();
    const client = useQueryClient();
    const [file, setFile] = useState<File | null>(null);
    const [preview, setPreview] = useState<ImportFilePreview | null>(null);
    const [mapping, setMapping] = useState<Record<string, string>>({});
    const [message, setMessage] = useState<string | null>(null);
    const [error, setError] = useState<string | null>(
        (location.state as { uploadError?: string } | null)?.uploadError ?? null,
    );
    const [downloading, setDownloading] = useState<string | null>(null);
    const job = useQuery({
        queryKey: ['admin-import-job', id],
        queryFn: ({ signal }) =>
            restGet<ImportJobDetail>(`/api/v1/admin/imports/jobs/${id}`, token!, signal),
        enabled: Boolean(token && id),
        refetchInterval: (query) =>
            query.state.data && ['Queued', 'Running'].includes(query.state.data.summary.status)
                ? 5000
                : false,
    });
    const upload = useMutation({
        mutationFn: () => uploadImportFile(id, file!, token!),
        onSuccess: async (result) => {
            setPreview(result);
            setMapping(identityMapping(result.columns));
            setMessage('File uploaded. Confirm how each source column should be used.');
            setError(null);
            await client.invalidateQueries({ queryKey: ['admin-import-job', id] });
        },
        onError: (reason) => setError(toAppError(reason).message),
    });
    const storedPreview = useQuery({
        queryKey: ['admin-import-preview', id, job.data?.fileObjectKey],
        queryFn: () => restSend<ImportFilePreview>(`/api/v1/admin/imports/jobs/${id}/mapping/preview`, token!, 'POST', {}),
        enabled: Boolean(token && id && job.data?.summary.mode === 'File' && job.data.fileObjectKey && job.data.summary.status === 'Draft' && !preview),
        retry: false,
    });
    useEffect(() => {
        if (!storedPreview.data || preview) return;
        setPreview(storedPreview.data);
        setMapping(identityMapping(storedPreview.data.columns));
    }, [preview, storedPreview.data]);
    const write = useMutation({
        mutationFn: ({
            path,
            method = 'POST',
            body,
        }: {
            path: string;
            method?: 'POST' | 'PUT';
            body?: unknown;
        }) => restSend(path, token!, method, body),
        onSuccess: async () => {
            setMessage('Import job updated.');
            setError(null);
            await client.invalidateQueries({ queryKey: ['admin-import-job', id] });
        },
        onError: (reason) => setError(toAppError(reason).message),
    });
    const mappedFields = useMemo(() => new Set(Object.values(mapping)), [mapping]);
    const missing = ['title', 'brand', 'price', 'currency', 'image_urls'].filter(
        (field) => !mappedFields.has(field),
    );
    const submitUpload = (event: FormEvent) => {
        event.preventDefault();
        if (file) upload.mutate();
    };
    const downloadTemplate = async (format: 'csv' | 'xlsx' | 'json-schema') => {
        setDownloading(format);
        try {
            const names = { csv: 'mkbj-products-template.csv', xlsx: 'mkbj-products-template.xlsx', 'json-schema': 'mkbj-products.schema.json' };
            await downloadAuthenticatedFile(`/api/v1/admin/imports/templates/${format}`, token!, names[format]);
        } catch (caught) {
            setError(toAppError(caught).message);
        } finally {
            setDownloading(null);
        }
    };
    if (job.isPending) return <PageLoading label="Loading import job" />;
    if (job.isError) return <ErrorState error={job.error} retry={() => void job.refetch()} />;
    const detail = job.data;
    return (
        <section aria-labelledby="import-job-title" className="page-stack">
            <Link className="back-link" to="/imports">
                <ArrowLeft size={16} /> Back to imports
            </Link>
            <div className="page-heading">
                <div>
                    <p className="eyebrow">Sourcing · {detail.summary.mode}</p>
                    <h1 id="import-job-title">Import job {detail.summary.id}</h1>
                    <p>Inspect progress, attach a supplier file, and explicitly map its columns.</p>
                </div>
                <div className="heading-actions">
                    <StatusBadge status={detail.summary.status} />
                    {detail.summary.status === 'Draft' && (
                        <Button
                            disabled={
                                write.isPending ||
                                (detail.summary.mode === 'File' && !detail.fileObjectKey)
                            }
                            onClick={() =>
                                write.mutate({ path: `/api/v1/admin/imports/jobs/${id}/start` })
                            }
                        >
                            <Play size={16} /> Start import
                        </Button>
                    )}
                </div>
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
                    <h2>Progress</h2>
                    <dl className="definition-grid">
                        {[
                            ['Discovered', detail.summary.discovered],
                            ['Extracted', detail.summary.extracted],
                            ['Valid', detail.summary.valid],
                            ['Needs review', detail.summary.review],
                            ['Invalid', detail.summary.invalid],
                            ['Duplicates', detail.summary.duplicate],
                            ['Published', detail.summary.published],
                            ['Checkpoint', `${detail.checkpoint} · step ${detail.stepSequence}`],
                        ].map(([label, value]) => (
                            <div key={label}>
                                <dt>{label}</dt>
                                <dd>{value}</dd>
                            </div>
                        ))}
                    </dl>
                </section>
                <section className="panel detail-section">
                    <h2>Configuration</h2>
                    <dl className="record-list">
                        <div>
                            <dt>Publish policy</dt>
                            <dd>{detail.publishPolicy}</dd>
                        </div>
                        <div>
                            <dt>Target category</dt>
                            <dd>{detail.targetCategoryId ?? 'Not fixed'}</dd>
                        </div>
                        <div>
                            <dt>File</dt>
                            <dd>{detail.fileFormat ?? 'Not attached'}</dd>
                        </div>
                        <div>
                            <dt>Sources</dt>
                            <dd>{detail.sources.join(', ') || 'None'}</dd>
                        </div>
                        <div>
                            <dt>Queries</dt>
                            <dd>{detail.queries.join(', ') || 'None'}</dd>
                        </div>
                    </dl>
                </section>
            </div>
            {detail.summary.mode === 'File' && detail.summary.status === 'Draft' && (
                <section
                    className="panel detail-section page-stack"
                    aria-labelledby="file-import-title"
                >
                    <div>
                        <p className="eyebrow">File workflow</p>
                        <h2 id="file-import-title">Upload and map columns</h2>
                        <p>
                            CSV, XLSX, and JSON files are accepted. Nothing starts until the mapping
                            is saved.
                        </p>
                    </div>
                    <div className="template-panel">
                        <div><strong>Download a matching template</strong><small>image_urls accepts up to ten absolute image URLs separated with |. Attribute columns use attr:&lt;canonical_key&gt;.</small></div>
                        <div className="template-actions">
                            {(['csv', 'xlsx', 'json-schema'] as const).map((format) => (
                                <Button key={format} disabled={downloading !== null} onClick={() => void downloadTemplate(format)} size="compact" type="button" variant="secondary"><Download size={15} />{format === 'json-schema' ? 'JSON Schema' : format.toUpperCase()}</Button>
                            ))}
                        </div>
                    </div>
                    <form className="inline-form" onSubmit={submitUpload}>
                        <label className="field">
                            <span>Supplier file</span>
                            <input
                                accept=".csv,.xlsx,.json"
                                onChange={(event) => setFile(event.target.files?.[0] ?? null)}
                                required
                                type="file"
                            />
                        </label>
                        <Button disabled={!file || upload.isPending} type="submit">
                            <FileUp size={16} />{' '}
                            {upload.isPending ? 'Uploading…' : 'Upload and preview'}
                        </Button>
                    </form>
                    {preview && (
                        <div className="page-stack">
                            <div className="table-scroll">
                                <table>
                                    <thead>
                                        <tr>
                                            <th>Source column</th>
                                            <th>Use as</th>
                                            <th>Sample values</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {preview.columns.map((column) => (
                                            <tr key={column}>
                                                <th scope="row">{column}</th>
                                                <td>
                                                    <select
                                                        aria-label={`Map ${column}`}
                                                        onChange={(event) =>
                                                            setMapping((current) => ({
                                                                ...current,
                                                                [column]: event.target.value === '__attribute__' ? `attr:${column.toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_|_$/g, '')}` : event.target.value,
                                                            }))
                                                        }
                                                        value={mapping[column]?.startsWith('attr:') ? '__attribute__' : (mapping[column] ?? 'ignore')}
                                                    >
                                                        {fields.map((field) => (
                                                            <option key={field} value={field}>
                                                                {field}
                                                            </option>
                                                        ))}
                                                        <option value="__attribute__">Product attribute…</option>
                                                    </select>
                                                    {mapping[column]?.startsWith('attr:') && (
                                                        <input aria-label={`Attribute key for ${column}`} onChange={(event) => setMapping((current) => ({ ...current, [column]: `attr:${event.target.value.toLowerCase().replace(/[^a-z0-9_]/g, '')}` }))} placeholder="canonical_key" value={mapping[column].slice(5)} />
                                                    )}
                                                </td>
                                                <td>
                                                    {preview.sampleRows
                                                        .map((row) => row[column])
                                                        .filter(Boolean)
                                                        .slice(0, 3)
                                                        .join(' · ') || '—'}
                                                </td>
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            </div>
                            {missing.length > 0 && (
                                <div className="warning-banner" role="status">
                                    Recommended fields still unmapped: {missing.join(', ')}.
                                </div>
                            )}
                            <div className="dialog-actions">
                                <Button
                                    disabled={write.isPending}
                                    onClick={() =>
                                        write.mutate({
                                            path: `/api/v1/admin/imports/jobs/${id}/mapping`,
                                            method: 'PUT',
                                            body: { mapping },
                                        })
                                    }
                                >
                                    Save mapping
                                </Button>
                            </div>
                        </div>
                    )}
                </section>
            )}
        </section>
    );
}

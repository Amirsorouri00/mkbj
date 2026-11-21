import * as Dialog from '@radix-ui/react-dialog';
import { Button } from '@mkbj/ui';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Download, FileSearch, FileUp, Globe2, Plus, Search, X } from 'lucide-react';
import { type FormEvent, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { downloadAuthenticatedFile, restGet, restSend, uploadImportFile } from '../../api/rest';
import { toAppError } from '../../api/errors';
import { useAuth } from '../../auth/auth-context';
import { EmptyState, ErrorState, PageLoading } from '../../components/feedback';
import { OffsetPagination } from '../../components/pagination';
import { StatusBadge } from '../../components/status-badge';

interface ImportJob {
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
}
interface JobPage {
    page: number;
    pageSize: number;
    pageCount: number;
    totalCount: number;
    items: ImportJob[];
}
interface RecordRow {
    id: string;
    lastJobId: string;
    sourceUrl: string;
    sourceDomain: string | null;
    status: string;
    matchKind: string;
    matchedProductId: string | null;
    issueCodes: string[];
    publishedProductId: string | null;
    createdAt: string;
}
interface RecordPage {
    items: RecordRow[];
    nextCursor: string | null;
}

export function ImportsPage() {
    const { token } = useAuth();
    const client = useQueryClient();
    const navigate = useNavigate();
    const [params, setParams] = useSearchParams();
    const view = params.get('view') === 'records' ? 'records' : 'jobs';
    const page = Number(params.get('page')) || 1;
    const pageSize = Number(params.get('pageSize')) || 50;
    const status = params.get('status') ?? '';
    const [createOpen, setCreateOpen] = useState(false);
    const [mode, setMode] = useState('Crawl');
    const [sources, setSources] = useState('');
    const [queries, setQueries] = useState('');
    const [includeDomains, setIncludeDomains] = useState('');
    const [excludeDomains, setExcludeDomains] = useState('');
    const [targetCategoryId, setTargetCategoryId] = useState('');
    const [publishPolicy, setPublishPolicy] = useState('AutoPublishValidated');
    const [fileRightsAttested, setFileRightsAttested] = useState(false);
    const [importFile, setImportFile] = useState<File | null>(null);
    const [downloading, setDownloading] = useState<string | null>(null);
    const [formError, setFormError] = useState<string | null>(null);
    const jobs = useQuery({
        queryKey: ['admin-import-jobs', status, page, pageSize],
        queryFn: ({ signal }) =>
            restGet<JobPage>(
                `/api/v1/admin/imports/jobs?${new URLSearchParams({ ...(status ? { status } : {}), page: String(page), pageSize: String(pageSize) })}`,
                token!,
                signal,
            ),
        enabled: Boolean(token && view === 'jobs'),
        placeholderData: keepPreviousData,
        refetchInterval: (query) =>
            query.state.data?.items.some((job) => ['Queued', 'Running'].includes(job.status))
                ? 5000
                : false,
    });
    const recordsQuery = new URLSearchParams();
    ['jobId', 'recordStatus', 'sourceDomain', 'cursor'].forEach((key) => {
        const value = params.get(key);
        if (value) recordsQuery.set(key === 'recordStatus' ? 'status' : key, value);
    });
    recordsQuery.set('pageSize', String(pageSize));
    const records = useQuery({
        queryKey: ['admin-import-records', recordsQuery.toString()],
        queryFn: ({ signal }) =>
            restGet<RecordPage>(`/api/v1/admin/imports/records?${recordsQuery}`, token!, signal),
        enabled: Boolean(token && view === 'records'),
        placeholderData: keepPreviousData,
    });
    const write = useMutation({
        mutationFn: ({ path, body }: { path: string; body?: unknown }) =>
            restSend<string>(path, token!, 'POST', body),
        onSuccess: async () => {
            setCreateOpen(false);
            setFormError(null);
            await client.invalidateQueries({ queryKey: ['admin-import'] });
            await Promise.all([
                client.invalidateQueries({ queryKey: ['admin-import-jobs'] }),
                client.invalidateQueries({ queryKey: ['admin-import-records'] }),
            ]);
        },
        onError: (error) => setFormError(toAppError(error).message),
    });
    const createJob = useMutation({
        mutationFn: async (body: unknown) => {
            const jobId = await restSend<string>(
                '/api/v1/admin/imports/jobs', token!, 'POST', body,
            );
            if (mode !== 'File' || !importFile) return { jobId, uploadError: null };
            try {
                await uploadImportFile(jobId, importFile, token!);
                return { jobId, uploadError: null };
            } catch (caught) {
                return { jobId, uploadError: toAppError(caught).message };
            }
        },
        onSuccess: async ({ jobId, uploadError }) => {
            setCreateOpen(false);
            setFormError(null);
            await client.invalidateQueries({ queryKey: ['admin-import-jobs'] });
            void navigate(`/imports/${jobId}`, { state: uploadError ? { uploadError } : undefined });
        },
        onError: (caught) => setFormError(toAppError(caught).message),
    });
    const update = (key: string, value: string) =>
        setParams(
            (current) => {
                const next = new URLSearchParams(current);
                if (value) next.set(key, value);
                else next.delete(key);
                if (key !== 'page') next.set('page', '1');
                return next;
            },
            { replace: true },
        );
    const create = (event: FormEvent) => {
        event.preventDefault();
        setFormError(null);
        if (mode === 'File' && !importFile) {
            setFormError('Choose a CSV, XLSX, or JSON file.');
            return;
        }
        createJob.mutate({
                mode,
                sources: sources
                    .split('\n')
                    .map((x) => x.trim())
                    .filter(Boolean),
                queries: queries
                    .split('\n')
                    .map((x) => x.trim())
                    .filter(Boolean),
                targetCategoryId: targetCategoryId ? Number(targetCategoryId) : null,
                includeDomains: includeDomains
                    .split(',')
                    .map((x) => x.trim())
                    .filter(Boolean),
                excludeDomains: excludeDomains
                    .split(',')
                    .map((x) => x.trim())
                    .filter(Boolean),
                maxPages: 200,
                maxDepth: 2,
                maxRecords: 1000,
                publishPolicy,
                fileRightsAttested,
        });
    };
    const downloadTemplate = async (format: 'csv' | 'xlsx' | 'json-schema') => {
        setDownloading(format);
        setFormError(null);
        try {
            const names = {
                csv: 'mkbj-products-template.csv',
                xlsx: 'mkbj-products-template.xlsx',
                'json-schema': 'mkbj-products.schema.json',
            };
            await downloadAuthenticatedFile(
                `/api/v1/admin/imports/templates/${format}`, token!, names[format],
            );
        } catch (caught) {
            setFormError(toAppError(caught).message);
        } finally {
            setDownloading(null);
        }
    };
    return (
        <section aria-labelledby="imports-title" className="page-stack">
            <div className="page-heading">
                <div>
                    <p className="eyebrow">Sourcing</p>
                    <h1 id="imports-title">Product imports</h1>
                    <p>Run source discovery jobs and review staged records before publishing.</p>
                </div>
                <Button
                    onClick={() => {
                        setFormError(null);
                        setCreateOpen(true);
                    }}
                >
                    <Plus size={16} />
                    Create import job
                </Button>
            </div>
            <div className="panel segmented-nav" role="navigation" aria-label="Import views">
                <Button
                    aria-current={view === 'jobs' ? 'page' : undefined}
                    onClick={() => update('view', 'jobs')}
                    variant={view === 'jobs' ? 'primary' : 'ghost'}
                >
                    <Globe2 size={16} />
                    Jobs
                </Button>
                <Button
                    aria-current={view === 'records' ? 'page' : undefined}
                    onClick={() => update('view', 'records')}
                    variant={view === 'records' ? 'primary' : 'ghost'}
                >
                    <FileSearch size={16} />
                    Review queue
                </Button>
            </div>
            {view === 'jobs' ? (
                <>
                    <div className="panel filter-panel">
                        <div className="filters">
                            <label>
                                <span>Status</span>
                                <select
                                    onChange={(event) => update('status', event.target.value)}
                                    value={status}
                                >
                                    <option value="">All statuses</option>
                                    {[
                                        'Draft',
                                        'Queued',
                                        'Running',
                                        'Paused',
                                        'Completed',
                                        'Failed',
                                        'Cancelled',
                                    ].map((item) => (
                                        <option key={item}>{item}</option>
                                    ))}
                                </select>
                            </label>
                            {status && (
                                <Button onClick={() => update('status', '')} variant="ghost">
                                    Clear
                                </Button>
                            )}
                        </div>
                    </div>
                    {jobs.isPending ? (
                        <PageLoading label="Loading import jobs" />
                    ) : jobs.isError ? (
                        <ErrorState error={jobs.error} retry={() => void jobs.refetch()} />
                    ) : !jobs.data.items.length ? (
                        <EmptyState title="No import jobs">
                            Create a crawl, search, combined or file import job.
                        </EmptyState>
                    ) : (
                        <div className="panel table-panel">
                            <div className="table-summary">
                                <strong>{jobs.data.totalCount} import jobs</strong>
                                <span>
                                    {jobs.isFetching
                                        ? 'Refreshing progress…'
                                        : 'Progress comes from the server'}
                                </span>
                            </div>
                            <div className="table-scroll">
                                <table>
                                    <thead>
                                        <tr>
                                            <th scope="col">Job</th>
                                            <th scope="col">Mode</th>
                                            <th scope="col">Status</th>
                                            <th scope="col">Discovered</th>
                                            <th scope="col">Valid</th>
                                            <th scope="col">Review</th>
                                            <th scope="col">Published</th>
                                            <th scope="col">Actions</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {jobs.data.items.map((job) => (
                                            <tr key={job.id}>
                                                <th scope="row">
                                                    <Link
                                                        className="product-link"
                                                        to={`/imports/${job.id}`}
                                                    >
                                                        Job {job.id}
                                                    </Link>
                                                    <span className="cell-subtitle">
                                                        {new Intl.DateTimeFormat(undefined, {
                                                            dateStyle: 'medium',
                                                            timeStyle: 'short',
                                                        }).format(new Date(job.createdAt))}
                                                    </span>
                                                </th>
                                                <td>{job.mode}</td>
                                                <td>
                                                    <StatusBadge status={job.status} />
                                                    {job.errorCode && (
                                                        <span className="cell-subtitle">
                                                            {job.errorCode}
                                                        </span>
                                                    )}
                                                </td>
                                                <td>{job.discovered}</td>
                                                <td>{job.valid}</td>
                                                <td>{job.review}</td>
                                                <td>{job.published}</td>
                                                <td>
                                                    <div className="row-actions">
                                                        {job.status === 'Draft' && (
                                                            <Button
                                                                onClick={() =>
                                                                    write.mutate({
                                                                        path: `/api/v1/admin/imports/jobs/${job.id}/start`,
                                                                    })
                                                                }
                                                                size="compact"
                                                                variant="ghost"
                                                            >
                                                                Start
                                                            </Button>
                                                        )}
                                                        {job.status === 'Running' && (
                                                            <Button
                                                                onClick={() =>
                                                                    write.mutate({
                                                                        path: `/api/v1/admin/imports/jobs/${job.id}/pause`,
                                                                    })
                                                                }
                                                                size="compact"
                                                                variant="ghost"
                                                            >
                                                                Pause
                                                            </Button>
                                                        )}
                                                        {job.status === 'Paused' && (
                                                            <Button
                                                                onClick={() =>
                                                                    write.mutate({
                                                                        path: `/api/v1/admin/imports/jobs/${job.id}/resume`,
                                                                    })
                                                                }
                                                                size="compact"
                                                                variant="ghost"
                                                            >
                                                                Resume
                                                            </Button>
                                                        )}
                                                        {[
                                                            'Draft',
                                                            'Queued',
                                                            'Running',
                                                            'Paused',
                                                        ].includes(job.status) && (
                                                            <Button
                                                                onClick={() =>
                                                                    write.mutate({
                                                                        path: `/api/v1/admin/imports/jobs/${job.id}/cancel`,
                                                                    })
                                                                }
                                                                size="compact"
                                                                variant="ghost"
                                                            >
                                                                Cancel
                                                            </Button>
                                                        )}
                                                    </div>
                                                </td>
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            </div>
                            <OffsetPagination
                                itemLabel="Import jobs"
                                onPageChange={(next) => update('page', String(next))}
                                onPageSizeChange={(next) => update('pageSize', String(next))}
                                page={jobs.data.page}
                                pageCount={jobs.data.pageCount}
                                pageSize={jobs.data.pageSize}
                            />
                        </div>
                    )}
                </>
            ) : (
                <>
                    <div className="panel filter-panel">
                        <div className="filters" role="search">
                            <label className="search-field">
                                <span>Job ID</span>
                                <span className="input-with-icon">
                                    <Search aria-hidden="true" size={17} />
                                    <input
                                        inputMode="numeric"
                                        onChange={(event) => update('jobId', event.target.value)}
                                        placeholder="Any job"
                                        value={params.get('jobId') ?? ''}
                                    />
                                </span>
                            </label>
                            <label>
                                <span>Status</span>
                                <select
                                    onChange={(event) => update('recordStatus', event.target.value)}
                                    value={params.get('recordStatus') ?? ''}
                                >
                                    <option value="">All statuses</option>
                                    {[
                                        'NeedsReview',
                                        'Valid',
                                        'Invalid',
                                        'Duplicate',
                                        'Published',
                                        'Rejected',
                                    ].map((item) => (
                                        <option key={item}>{item}</option>
                                    ))}
                                </select>
                            </label>
                            <label>
                                <span>Source domain</span>
                                <input
                                    onChange={(event) => update('sourceDomain', event.target.value)}
                                    placeholder="example.com"
                                    value={params.get('sourceDomain') ?? ''}
                                />
                            </label>
                        </div>
                    </div>
                    {records.isPending ? (
                        <PageLoading label="Loading review queue" />
                    ) : records.isError ? (
                        <ErrorState error={records.error} retry={() => void records.refetch()} />
                    ) : !records.data.items.length ? (
                        <EmptyState title="No staged records">
                            No records match the current review filters.
                        </EmptyState>
                    ) : (
                        <div className="panel table-panel">
                            <div className="table-summary">
                                <strong>{records.data.items.length} staged records</strong>
                                <span>Server-authoritative review state</span>
                            </div>
                            <div className="table-scroll">
                                <table>
                                    <thead>
                                        <tr>
                                            <th scope="col">Record</th>
                                            <th scope="col">Source</th>
                                            <th scope="col">Status</th>
                                            <th scope="col">Match</th>
                                            <th scope="col">Issues</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {records.data.items.map((record) => (
                                            <tr key={record.id}>
                                                <th scope="row">
                                                    <Link
                                                        className="product-link"
                                                        to={`/imports/records/${record.id}`}
                                                    >
                                                        Record {record.id}
                                                    </Link>
                                                    <span className="cell-subtitle">
                                                        Job {record.lastJobId}
                                                    </span>
                                                </th>
                                                <td>
                                                    {record.sourceDomain ?? 'File'}
                                                    <span className="cell-subtitle">
                                                        {record.sourceUrl}
                                                    </span>
                                                </td>
                                                <td>
                                                    <StatusBadge status={record.status} />
                                                </td>
                                                <td>
                                                    {record.matchKind}
                                                    {record.matchedProductId && (
                                                        <span className="cell-subtitle">
                                                            Product {record.matchedProductId}
                                                        </span>
                                                    )}
                                                </td>
                                                <td>
                                                    {record.issueCodes.length
                                                        ? record.issueCodes.join(', ')
                                                        : 'None'}
                                                </td>
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            </div>
                            <nav className="pagination">
                                <span>Cursor navigation</span>
                                <Button
                                    disabled={!records.data.nextCursor}
                                    onClick={() =>
                                        records.data.nextCursor &&
                                        update('cursor', records.data.nextCursor)
                                    }
                                    variant="secondary"
                                >
                                    Next page
                                </Button>
                            </nav>
                        </div>
                    )}
                </>
            )}
            <Dialog.Root onOpenChange={setCreateOpen} open={createOpen}>
                <Dialog.Portal>
                    <Dialog.Overlay className="dialog-overlay" />
                    <Dialog.Content className="dialog-panel dialog-panel--large">
                        <div className="dialog-heading">
                            <Dialog.Title>Create import job</Dialog.Title>
                            <Dialog.Close asChild>
                                <Button aria-label="Close" size="icon" variant="ghost">
                                    <X />
                                </Button>
                            </Dialog.Close>
                        </div>
                        <Dialog.Description>
                            Choose one of the four approved import modes. Complex options appear
                            only when that mode uses them.
                        </Dialog.Description>
                        <form className="dialog-form dialog-form--scroll" onSubmit={create}>
                            <fieldset className="choice-grid">
                                <legend>Import mode</legend>
                                {['Crawl', 'WebSearch', 'Combined', 'File'].map((item) => (
                                    <label key={item}>
                                        <input
                                            checked={mode === item}
                                            name="mode"
                                            onChange={() => setMode(item)}
                                            type="radio"
                                        />
                                        {item === 'WebSearch' ? 'Web search' : item}
                                    </label>
                                ))}
                            </fieldset>
                            {['Crawl', 'Combined'].includes(mode) && (
                                <label className="field">
                                    <span>Source URLs *</span>
                                    <textarea
                                        onChange={(event) => setSources(event.target.value)}
                                        placeholder="One absolute URL per line"
                                        required
                                        rows={4}
                                        value={sources}
                                    />
                                </label>
                            )}
                            {['WebSearch', 'Combined'].includes(mode) && (
                                <label className="field">
                                    <span>Search queries *</span>
                                    <textarea
                                        onChange={(event) => setQueries(event.target.value)}
                                        placeholder="One search query per line"
                                        required
                                        rows={4}
                                        value={queries}
                                    />
                                </label>
                            )}
                            <div className="form-grid">
                                <label className="field">
                                    <span>Include domains</span>
                                    <input
                                        onChange={(event) => setIncludeDomains(event.target.value)}
                                        placeholder="Comma-separated"
                                        value={includeDomains}
                                    />
                                </label>
                                <label className="field">
                                    <span>Exclude domains</span>
                                    <input
                                        onChange={(event) => setExcludeDomains(event.target.value)}
                                        placeholder="Comma-separated"
                                        value={excludeDomains}
                                    />
                                </label>
                                <label className="field">
                                    <span>Target category ID</span>
                                    <input
                                        inputMode="numeric"
                                        onChange={(event) =>
                                            setTargetCategoryId(event.target.value)
                                        }
                                        value={targetCategoryId}
                                    />
                                </label>
                                <label className="field">
                                    <span>Publish policy</span>
                                    <select
                                        onChange={(event) => setPublishPolicy(event.target.value)}
                                        value={publishPolicy}
                                    >
                                        <option value="AutoPublishValidated">
                                            Auto-publish validated
                                        </option>
                                        <option value="ManualReview">Manual review</option>
                                    </select>
                                </label>
                            </div>
                            {mode === 'File' && (
                                <div className="file-import-setup">
                                    <div className="template-panel">
                                        <div>
                                            <strong>Start from a validated template</strong>
                                            <small>Use one row per product or variant. Separate multiple image URLs with | in image_urls.</small>
                                        </div>
                                        <div className="template-actions" aria-label="Import templates">
                                            {(['csv', 'xlsx', 'json-schema'] as const).map((format) => (
                                                <Button key={format} disabled={downloading !== null} onClick={() => void downloadTemplate(format)} size="compact" type="button" variant="secondary">
                                                    <Download size={15} />
                                                    {format === 'json-schema' ? 'JSON Schema' : format.toUpperCase()}
                                                </Button>
                                            ))}
                                        </div>
                                    </div>
                                    <label className="field">
                                        <span>Import file *</span>
                                        <input accept=".csv,.xlsx,.json,text/csv,application/json,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" onChange={(event) => setImportFile(event.target.files?.[0] ?? null)} required type="file" />
                                        <span className="field-meta">CSV, XLSX, or a flat JSON array. Maximum size is enforced by the server.</span>
                                    </label>
                                    <label className="removal-confirmation">
                                        <input checked={fileRightsAttested} onChange={(event) => setFileRightsAttested(event.target.checked)} required type="checkbox" />
                                        <span><strong>I attest the file content may be used.</strong><small>This covers product text and image URLs. Stored source policies still take precedence.</small></span>
                                    </label>
                                </div>
                            )}
                            {formError && (
                                <div className="dialog-error" role="alert">
                                    {formError}
                                </div>
                            )}
                            <div className="dialog-actions">
                                <Dialog.Close asChild>
                                    <Button variant="secondary">Cancel</Button>
                                </Dialog.Close>
                                <Button disabled={createJob.isPending} type="submit">
                                    {createJob.isPending ? <><FileUp size={16} /> Creating and uploading…</> : 'Create job'}
                                </Button>
                            </div>
                        </form>
                    </Dialog.Content>
                </Dialog.Portal>
            </Dialog.Root>
        </section>
    );
}

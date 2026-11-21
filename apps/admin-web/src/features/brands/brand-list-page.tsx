import * as Dialog from '@radix-ui/react-dialog';
import {
  AdminBrandsDocument,
  type AdminBrandsQuery,
  type AdminBrandsQueryVariables,
  CreateAdminBrandDocument,
  type CreateAdminBrandMutation,
  type CreateAdminBrandMutationVariables,
  RenameAdminBrandDocument,
  type RenameAdminBrandMutation,
  type RenameAdminBrandMutationVariables,
  SetAdminBrandActiveDocument,
  type SetAdminBrandActiveMutation,
  type SetAdminBrandActiveMutationVariables,
  SetAdminBrandAliasesDocument,
  type SetAdminBrandAliasesMutation,
  type SetAdminBrandAliasesMutationVariables,
} from '@mkbj/api-graphql';
import { Button } from '@mkbj/ui';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Plus, Search, X } from 'lucide-react';
import { type FormEvent, useCallback, useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { graphQlRequest } from '../../api/graphql';
import { toAppError } from '../../api/errors';
import { useAuth } from '../../auth/auth-context';
import { ConfirmationDialog } from '../../components/confirmation-dialog';
import { EmptyState, ErrorState, PageLoading } from '../../components/feedback';
import { OffsetPagination } from '../../components/pagination';
import { StatusBadge } from '../../components/status-badge';
import { parseAliases, readBrandListState, writeBrandListState, type BrandListState } from './brand-list-model';

type BrandRow = AdminBrandsQuery['adminBrands']['items'][number];
type Editor = { mode: 'create' | 'rename' | 'aliases'; brand?: BrandRow };

export function BrandListPage() {
  const { token } = useAuth();
  const queryClient = useQueryClient();
  const [searchParams, setSearchParams] = useSearchParams();
  const state = readBrandListState(searchParams);
  const [draft, setDraft] = useState(state.q);
  const [editor, setEditor] = useState<Editor | null>(null);
  const [name, setName] = useState('');
  const [aliases, setAliases] = useState('');
  const [formError, setFormError] = useState<string | null>(null);
  const [statusBrand, setStatusBrand] = useState<BrandRow | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => setDraft(state.q), [state.q]);
  const update = useCallback((next: Partial<BrandListState>) => setSearchParams((current) => writeBrandListState({ ...readBrandListState(current), ...next }), { replace: true }), [setSearchParams]);
  useEffect(() => {
    if (draft.trim() === state.q) return;
    const timeout = window.setTimeout(() => update({ q: draft.trim(), page: 1 }), 300);
    return () => window.clearTimeout(timeout);
  }, [draft, state.q, update]);

  const variables: AdminBrandsQueryVariables = { page: state.page, pageSize: state.pageSize, ...(state.q ? { search: state.q } : {}) };
  const query = useQuery({ queryKey: ['admin-brands', variables], queryFn: ({ signal }) => graphQlRequest<AdminBrandsQuery, AdminBrandsQueryVariables>(AdminBrandsDocument, variables, token!, signal), enabled: Boolean(token), placeholderData: keepPreviousData });

  const completed = async (message: string) => {
    await queryClient.invalidateQueries({ queryKey: ['admin-brands'] });
    await queryClient.invalidateQueries({ queryKey: ['product-editor-options'] });
    setEditor(null); setStatusBrand(null); setFormError(null); setNotice(message);
  };
  const create = useMutation({ mutationFn: () => graphQlRequest<CreateAdminBrandMutation, CreateAdminBrandMutationVariables>(CreateAdminBrandDocument, { displayName: name.trim(), aliases: parseAliases(aliases) }, token!), onSuccess: () => completed('Brand created.'), onError: (error) => setFormError(toAppError(error).message) });
  const rename = useMutation({ mutationFn: () => graphQlRequest<RenameAdminBrandMutation, RenameAdminBrandMutationVariables>(RenameAdminBrandDocument, { brandId: editor!.brand!.id, displayName: name.trim() }, token!), onSuccess: () => completed('Brand renamed.'), onError: (error) => setFormError(toAppError(error).message) });
  const saveAliases = useMutation({ mutationFn: () => graphQlRequest<SetAdminBrandAliasesMutation, SetAdminBrandAliasesMutationVariables>(SetAdminBrandAliasesDocument, { brandId: editor!.brand!.id, aliases: parseAliases(aliases) }, token!), onSuccess: () => completed('Brand aliases saved.'), onError: (error) => setFormError(toAppError(error).message) });
  const setActive = useMutation({ mutationFn: (brand: BrandRow) => graphQlRequest<SetAdminBrandActiveMutation, SetAdminBrandActiveMutationVariables>(SetAdminBrandActiveDocument, { brandId: brand.id, active: !brand.isActive }, token!), onSuccess: () => completed(statusBrand?.isActive ? 'Brand deactivated.' : 'Brand activated.') });

  const openEditor = (next: Editor) => {
    setEditor(next); setFormError(null); setName(next.brand?.displayName ?? ''); setAliases(next.brand?.aliases.join('\n') ?? '');
  };
  const submit = (event: FormEvent) => {
    event.preventDefault(); setFormError(null);
    if (editor?.mode !== 'aliases' && !name.trim()) return setFormError('Enter a brand name.');
    if (editor?.mode === 'create') create.mutate();
    if (editor?.mode === 'rename') rename.mutate();
    if (editor?.mode === 'aliases') saveAliases.mutate();
  };
  const page = query.data?.adminBrands;
  useEffect(() => { if (page && page.pageCount > 0 && state.page > page.pageCount) update({ page: page.pageCount }); }, [page, state.page, update]);
  const editorPending = create.isPending || rename.isPending || saveAliases.isPending;

  return (
    <section aria-labelledby="brands-title" className="page-stack">
      <div className="page-heading"><div><p className="eyebrow">Catalog</p><h1 id="brands-title">Brands</h1><p>Manage canonical display names, aliases and availability.</p></div><Button onClick={() => openEditor({ mode: 'create' })}><Plus size={16} />Create brand</Button></div>
      {notice && <div className="success-panel" role="status"><span>{notice}</span><Button onClick={() => setNotice(null)} size="compact" variant="ghost">Dismiss</Button></div>}
      <div className="panel filter-panel"><div className="filters" role="search"><label className="search-field"><span>Search brands</span><span className="input-with-icon"><Search aria-hidden="true" size={17} /><input onChange={(event) => setDraft(event.target.value)} placeholder="Name or alias" value={draft} /></span></label>{state.q && <Button onClick={() => { setDraft(''); setSearchParams({}); }} variant="ghost">Clear</Button>}</div></div>
      {query.isPending ? <PageLoading label="Loading brands" /> : query.isError ? <ErrorState error={query.error} retry={() => void query.refetch()} /> : !page?.items.length ? <EmptyState title="No brands found">Try a broader search or create a brand.</EmptyState> : <div className="panel table-panel" aria-busy={query.isFetching}><div className="table-summary"><strong>{page.totalCount.toLocaleString()} brands</strong>{query.isFetching && <span>Updating…</span>}</div><div className="table-scroll" role="region" aria-label="Brands table" tabIndex={0}><table><thead><tr><th scope="col">Brand</th><th scope="col">Status</th><th scope="col">Aliases</th><th scope="col">Provenance</th><th scope="col"><span className="sr-only">Actions</span></th></tr></thead><tbody>{page.items.map((brand) => <tr key={brand.id}><th scope="row"><strong>{brand.displayName}</strong><span className="cell-subtitle">{brand.canonicalName}</span></th><td><StatusBadge status={brand.isActive ? 'Active' : 'Inactive'} /></td><td>{brand.aliases.length ? brand.aliases.join(', ') : '—'}</td><td>{brand.provenance}</td><td><div className="row-actions"><Button onClick={() => openEditor({ mode: 'rename', brand })} size="compact" variant="ghost">Rename</Button><Button onClick={() => openEditor({ mode: 'aliases', brand })} size="compact" variant="ghost">Aliases</Button><Button onClick={() => setStatusBrand(brand)} size="compact" variant="ghost">{brand.isActive ? 'Deactivate' : 'Activate'}</Button></div></td></tr>)}</tbody></table></div><OffsetPagination itemLabel="Brands" onPageChange={(nextPage) => update({ page: nextPage })} onPageSizeChange={(nextPageSize) => update({ pageSize: nextPageSize, page: 1 })} page={page.page} pageCount={page.pageCount} pageSize={state.pageSize} /></div>}

      <Dialog.Root onOpenChange={(open) => !open && setEditor(null)} open={Boolean(editor)}><Dialog.Portal><Dialog.Overlay className="dialog-overlay" /><Dialog.Content aria-describedby="brand-dialog-description" className="dialog-panel"><div className="dialog-heading"><Dialog.Title>{editor?.mode === 'create' ? 'Create brand' : editor?.mode === 'rename' ? 'Rename brand' : 'Edit aliases'}</Dialog.Title><Dialog.Close asChild><Button aria-label="Close dialog" size="icon" variant="ghost"><X /></Button></Dialog.Close></div><Dialog.Description id="brand-dialog-description">Brand normalization and duplicate detection are enforced by the backend.</Dialog.Description><form className="dialog-form" onSubmit={submit}>{editor?.mode !== 'aliases' && <label className="field"><span>Display name *</span><input autoFocus onChange={(event) => setName(event.target.value)} value={name} /></label>}{editor?.mode !== 'rename' && <label className="field"><span>Aliases</span><textarea onChange={(event) => setAliases(event.target.value)} placeholder="One per line or comma-separated" rows={5} value={aliases} /></label>}{formError && <div className="dialog-error" role="alert">{formError}</div>}<div className="dialog-actions"><Dialog.Close asChild><Button type="button" variant="secondary">Cancel</Button></Dialog.Close><Button disabled={editorPending} type="submit">{editorPending ? 'Saving…' : 'Save'}</Button></div></form></Dialog.Content></Dialog.Portal></Dialog.Root>
      <ConfirmationDialog confirmLabel={statusBrand?.isActive ? 'Deactivate brand' : 'Activate brand'} description={`${statusBrand?.displayName ?? 'This brand'} will be ${statusBrand?.isActive ? 'unavailable for new catalog use' : 'available for catalog use'}. Existing product records are not rewritten.`} destructive={Boolean(statusBrand?.isActive)} onConfirm={() => statusBrand && setActive.mutate(statusBrand)} onOpenChange={(open) => !open && setStatusBrand(null)} open={Boolean(statusBrand)} pending={setActive.isPending} title={`${statusBrand?.isActive ? 'Deactivate' : 'Activate'} this brand?`} />
      {setActive.error && <div className="dialog-error floating-error" role="alert">{toAppError(setActive.error).message}</div>}
    </section>
  );
}

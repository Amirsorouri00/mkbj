import * as Dialog from '@radix-ui/react-dialog';
import {
  AssignAdminCategoryAttributeDocument,
  CategoryAttributeManagementDocument,
  type CategoryAttributeManagementQuery,
  type CategoryAttributeManagementQueryVariables,
  ExcludeAdminInheritedCategoryAttributeDocument,
  IncludeAdminInheritedCategoryAttributeDocument,
  SetAdminCategoryAttributeFilterableDocument,
  SetAdminCategoryAttributePrimaryDocument,
  SetAdminCategoryAttributeRequiredDocument,
  SetAdminCategoryAttributeSearchableDocument,
  SetAdminCategoryAttributeTitleDocument,
  SetAdminCategoryAttributeVariantDocument,
  UnassignAdminCategoryAttributeDocument,
} from '@mkbj/api-graphql';
import { Button } from '@mkbj/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, Plus, Search, X } from 'lucide-react';
import { type FormEvent, useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { graphQlRequest } from '../../api/graphql';
import { toAppError } from '../../api/errors';
import { useAuth } from '../../auth/auth-context';
import { ConfirmationDialog } from '../../components/confirmation-dialog';
import { EmptyState, ErrorState, PageLoading } from '../../components/feedback';
import { StatusBadge } from '../../components/status-badge';

type EffectiveRow = CategoryAttributeManagementQuery['effectiveCategoryAttributes'][number];
type Confirmation = { kind: 'unassign' | 'inheritance'; row: EffectiveRow };
type ToggleKind = 'required' | 'filterable' | 'searchable' | 'primary' | 'variant' | 'title' | 'titleOrder';

export function CategoryAttributePage() {
  const { id = '' } = useParams();
  const { token } = useAuth();
  const queryClient = useQueryClient();
  const [assignOpen, setAssignOpen] = useState(false);
  const [attributeSearch, setAttributeSearch] = useState('');
  const [searchFilter, setSearchFilter] = useState('');
  const [attributeId, setAttributeId] = useState('');
  const [required, setRequired] = useState(false);
  const [filterable, setFilterable] = useState(false);
  const [searchable, setSearchable] = useState(false);
  const [inTitle, setInTitle] = useState(false);
  const [titleOrder, setTitleOrder] = useState('0');
  const [primary, setPrimary] = useState(false);
  const [variant, setVariant] = useState(false);
  const [confirmation, setConfirmation] = useState<Confirmation | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const timeout = window.setTimeout(() => setSearchFilter(attributeSearch.trim()), 300);
    return () => window.clearTimeout(timeout);
  }, [attributeSearch]);
  const variables: CategoryAttributeManagementQueryVariables = {
    categoryId: id,
    categoryGraphId: id,
    attributePage: 1,
    attributePageSize: 100,
    attributeFilter: { status: 'ACTIVE', ...(searchFilter ? { nameContains: searchFilter } : {}) },
  };
  const query = useQuery({ queryKey: ['category-attribute-management', variables], queryFn: ({ signal }) => graphQlRequest<CategoryAttributeManagementQuery, CategoryAttributeManagementQueryVariables>(CategoryAttributeManagementDocument, variables, token!, signal), enabled: Boolean(token && id) });
  const refresh = async (message: string) => {
    await queryClient.invalidateQueries({ queryKey: ['category-attribute-management'] });
    setNotice(message);
    setError(null);
  };
  const fail = (caught: unknown) => setError(toAppError(caught).message);
  const assign = useMutation({ mutationFn: () => graphQlRequest(AssignAdminCategoryAttributeDocument, { categoryId: id, attributeBlueprintId: attributeId, isRequired: required, isFilterable: filterable, isSearchable: searchable, isInTitle: inTitle, titleOrder: inTitle ? Number(titleOrder) || 0 : null, isPrimary: primary, isVariantGenerating: variant }, token!), onSuccess: async () => { await refresh('Attribute assigned.'); setAssignOpen(false); }, onError: fail });
  const toggle = useMutation({
    mutationFn: async ({ kind, row, value, order }: { kind: ToggleKind; row: EffectiveRow; value: boolean; order?: number }) => {
      if (kind === 'required') return graphQlRequest(SetAdminCategoryAttributeRequiredDocument, { assignmentId: row.assignmentId, value }, token!);
      if (kind === 'filterable') return graphQlRequest(SetAdminCategoryAttributeFilterableDocument, { assignmentId: row.assignmentId, value }, token!);
      if (kind === 'searchable') return graphQlRequest(SetAdminCategoryAttributeSearchableDocument, { assignmentId: row.assignmentId, value }, token!);
      if (kind === 'primary') return graphQlRequest(SetAdminCategoryAttributePrimaryDocument, { assignmentId: row.assignmentId, value }, token!);
      if (kind === 'variant') return graphQlRequest(SetAdminCategoryAttributeVariantDocument, { assignmentId: row.assignmentId, value }, token!);
      return graphQlRequest(SetAdminCategoryAttributeTitleDocument, { assignmentId: row.assignmentId, isInTitle: kind === 'title' ? value : row.isInTitle, titleOrder: (kind === 'title' ? value : row.isInTitle) ? order ?? row.titleOrder ?? 0 : null }, token!);
    },
    onSuccess: () => refresh('Attribute setting saved.'),
    onError: fail,
  });
  const unassign = useMutation({ mutationFn: (row: EffectiveRow) => graphQlRequest(UnassignAdminCategoryAttributeDocument, { assignmentId: row.assignmentId }, token!), onSuccess: async () => { await refresh('Attribute unassigned.'); setConfirmation(null); }, onError: fail });
  const inheritance = useMutation<unknown, Error, EffectiveRow>({ mutationFn: (row) => row.isExcluded ? graphQlRequest(IncludeAdminInheritedCategoryAttributeDocument, { categoryId: id, sourceAssignmentId: row.assignmentId }, token!) : graphQlRequest(ExcludeAdminInheritedCategoryAttributeDocument, { categoryId: id, sourceAssignmentId: row.assignmentId }, token!), onSuccess: async () => { await refresh(confirmation?.row.isExcluded ? 'Inherited attribute included.' : 'Inherited attribute excluded.'); setConfirmation(null); }, onError: fail });

  const submit = (event: FormEvent) => { event.preventDefault(); setError(null); if (!attributeId) return setError('Choose an attribute blueprint.'); assign.mutate(); };
  if (query.isPending) return <PageLoading label="Loading category attributes" />;
  if (query.isError) return <ErrorState error={query.error} retry={() => void query.refetch()} />;
  const category = query.data.category;
  const rows = query.data.effectiveCategoryAttributes;
  const assigned = new Set(rows.map((row) => row.attributeBlueprintId));
  const available = query.data.attributeBlueprints.items.filter((item) => !assigned.has(item.id) && (item.scope.toUpperCase() === 'GLOBAL' || item.scopeCategoryId === id));
  const pending = assign.isPending || toggle.isPending || unassign.isPending || inheritance.isPending;

  return (
    <section aria-labelledby="category-attributes-title" className="page-stack">
      <Button asChild className="back-link" size="compact" variant="ghost"><Link to="/catalog/categories"><ArrowLeft size={16} />Categories</Link></Button>
      <div className="page-heading"><div><p className="eyebrow">Category attributes</p><h1 id="category-attributes-title">{category.name}</h1><p>Manage direct assignments and inherited catalog semantics. Every setting is saved independently.</p></div><Button onClick={() => { setAssignOpen(true); setError(null); }}><Plus size={16} />Assign attribute</Button></div>
      {notice && <div className="success-panel" role="status"><span>{notice}</span><Button onClick={() => setNotice(null)} size="compact" variant="ghost">Dismiss</Button></div>}
      {error && <div className="error-panel" role="alert"><div><strong>The change was not saved.</strong><p>{error}</p></div></div>}
      {!rows.length ? <EmptyState title="No effective attributes">Assign the first attribute blueprint to this category.</EmptyState> : <div className="panel table-panel" aria-busy={pending}><div className="table-summary"><strong>{rows.length} effective attributes</strong><span>Inherited settings are read-only here</span></div><div className="table-scroll" role="region" aria-label="Category attributes table" tabIndex={0}><table className="attribute-assignment-table"><thead><tr><th scope="col">Attribute</th><th scope="col">Source</th><th scope="col">Rules</th><th scope="col">Title</th><th scope="col"><span className="sr-only">Actions</span></th></tr></thead><tbody>{rows.map((row) => <tr key={`${row.assignmentId}-${row.sourceCategoryId}`}><th scope="row"><strong>{row.attributeBlueprintName}</strong><span className="cell-subtitle">Blueprint {row.attributeBlueprintId}</span></th><td><StatusBadge status={row.isInherited ? row.isExcluded ? 'Excluded' : 'Inherited' : 'Direct'} /></td><td>{row.isInherited ? <span className="rule-summary">{[row.isRequired && 'Required', row.isFilterable && 'Filterable', row.isSearchable && 'Searchable', row.isPrimary && 'Primary', row.isVariantGenerating && 'Variant'].filter(Boolean).join(' · ') || 'No flags'}</span> : <div className="toggle-cluster"><Button aria-pressed={row.isRequired} disabled={pending} onClick={() => toggle.mutate({ kind: 'required', row, value: !row.isRequired })} size="compact" variant={row.isRequired ? 'secondary' : 'ghost'}>Required</Button><Button aria-pressed={row.isFilterable} disabled={pending} onClick={() => toggle.mutate({ kind: 'filterable', row, value: !row.isFilterable })} size="compact" variant={row.isFilterable ? 'secondary' : 'ghost'}>Filter</Button><Button aria-pressed={row.isSearchable} disabled={pending} onClick={() => toggle.mutate({ kind: 'searchable', row, value: !row.isSearchable })} size="compact" variant={row.isSearchable ? 'secondary' : 'ghost'}>Search</Button><Button aria-pressed={row.isPrimary} disabled={pending} onClick={() => toggle.mutate({ kind: 'primary', row, value: !row.isPrimary })} size="compact" variant={row.isPrimary ? 'secondary' : 'ghost'}>Primary</Button><Button aria-pressed={row.isVariantGenerating} disabled={pending} onClick={() => toggle.mutate({ kind: 'variant', row, value: !row.isVariantGenerating })} size="compact" variant={row.isVariantGenerating ? 'secondary' : 'ghost'}>Variant</Button></div>}</td><td>{row.isInherited ? row.isInTitle ? `Order ${row.titleOrder ?? 0}` : 'Not used' : <div className="title-setting"><Button aria-pressed={row.isInTitle} disabled={pending} onClick={() => toggle.mutate({ kind: 'title', row, value: !row.isInTitle, order: row.titleOrder ?? 0 })} size="compact" variant={row.isInTitle ? 'secondary' : 'ghost'}>In title</Button>{row.isInTitle && <input aria-label={`Title order for ${row.attributeBlueprintName}`} defaultValue={row.titleOrder ?? 0} min={0} onBlur={(event) => { const order = Number(event.target.value); if (Number.isInteger(order) && order >= 0 && order !== row.titleOrder) toggle.mutate({ kind: 'titleOrder', row, value: true, order }); }} type="number" />}</div>}</td><td><div className="row-actions"><Button disabled={pending} onClick={() => setConfirmation({ kind: row.isInherited ? 'inheritance' : 'unassign', row })} size="compact" variant="ghost">{row.isInherited ? row.isExcluded ? 'Include' : 'Exclude' : 'Unassign'}</Button></div></td></tr>)}</tbody></table></div></div>}

      <Dialog.Root onOpenChange={setAssignOpen} open={assignOpen}><Dialog.Portal><Dialog.Overlay className="dialog-overlay" /><Dialog.Content aria-describedby="assign-attribute-description" className="dialog-panel dialog-panel--wide"><div className="dialog-heading"><Dialog.Title>Assign attribute</Dialog.Title><Dialog.Close asChild><Button aria-label="Close dialog" disabled={assign.isPending} size="icon" variant="ghost"><X /></Button></Dialog.Close></div><Dialog.Description id="assign-attribute-description">Choose an active global attribute or one scoped to this category.</Dialog.Description><form className="dialog-form" onSubmit={submit}><label className="field"><span>Find attribute</span><span className="input-with-icon"><Search aria-hidden="true" size={17} /><input onChange={(event) => setAttributeSearch(event.target.value)} placeholder="Search blueprint names" value={attributeSearch} /></span></label><label className="field"><span>Attribute *</span><select onChange={(event) => setAttributeId(event.target.value)} value={attributeId}><option value="">Select an attribute</option>{available.map((item) => <option key={item.id} value={item.id}>{item.name} · {item.dataType}</option>)}</select><span className="field-meta">The API returns at most 100 matching blueprints; narrow the search when needed.</span></label><fieldset className="choice-grid"><legend>Assignment rules</legend><label><input checked={required} onChange={(event) => setRequired(event.target.checked)} type="checkbox" />Required</label><label><input checked={filterable} onChange={(event) => setFilterable(event.target.checked)} type="checkbox" />Filterable</label><label><input checked={searchable} onChange={(event) => setSearchable(event.target.checked)} type="checkbox" />Searchable</label><label><input checked={primary} onChange={(event) => setPrimary(event.target.checked)} type="checkbox" />Primary</label><label><input checked={variant} onChange={(event) => setVariant(event.target.checked)} type="checkbox" />Generates variants</label><label><input checked={inTitle} onChange={(event) => setInTitle(event.target.checked)} type="checkbox" />Use in title</label></fieldset>{inTitle && <label className="field"><span>Title order</span><input min={0} onChange={(event) => setTitleOrder(event.target.value)} type="number" value={titleOrder} /></label>}{error && <div className="dialog-error" role="alert">{error}</div>}<div className="dialog-actions"><Dialog.Close asChild><Button disabled={assign.isPending} type="button" variant="secondary">Cancel</Button></Dialog.Close><Button disabled={assign.isPending} type="submit">{assign.isPending ? 'Assigning…' : 'Assign attribute'}</Button></div></form></Dialog.Content></Dialog.Portal></Dialog.Root>
      <ConfirmationDialog confirmLabel={confirmation?.kind === 'unassign' ? 'Unassign attribute' : confirmation?.row.isExcluded ? 'Include attribute' : 'Exclude attribute'} description={confirmation?.kind === 'unassign' ? 'The direct assignment will be removed. Existing product values are not rewritten by this action.' : `This changes whether the inherited ${confirmation?.row.attributeBlueprintName ?? 'attribute'} applies to this category.`} destructive={confirmation?.kind === 'unassign' || !confirmation?.row.isExcluded} onConfirm={() => confirmation && (confirmation.kind === 'unassign' ? unassign.mutate(confirmation.row) : inheritance.mutate(confirmation.row))} onOpenChange={(open) => !open && setConfirmation(null)} open={Boolean(confirmation)} pending={unassign.isPending || inheritance.isPending} title={confirmation?.kind === 'unassign' ? 'Unassign this attribute?' : 'Change inherited attribute?'} />
    </section>
  );
}

import * as Dialog from '@radix-ui/react-dialog';
import * as DropdownMenu from '@radix-ui/react-dropdown-menu';
import {
  AdminAttributeBlueprintsDocument,
  type AdminAttributeBlueprintsQuery,
  type AdminAttributeBlueprintsQueryVariables,
  AdminAttributeBlueprintUsageDocument,
  type AdminAttributeBlueprintUsageQuery,
  type AdminAttributeBlueprintUsageQueryVariables,
  AttributeBlueprintEditorOptionsDocument,
  type AttributeBlueprintEditorOptionsQuery,
  type AttributeBlueprintEditorOptionsQueryVariables,
  type AttributeBlueprintDataType,
  type AttributeBlueprintDataTypeFilter,
  type AttributeBlueprintStatusFilter,
  CreateAdminAttributeBlueprintDocument,
  type CreateAdminAttributeBlueprintMutation,
  type CreateAdminAttributeBlueprintMutationVariables,
  DeleteAdminAttributeBlueprintDocument,
  type DeleteAdminAttributeBlueprintMutation,
  type DeleteAdminAttributeBlueprintMutationVariables,
  SetAdminAttributeBlueprintAliasesDocument,
  type SetAdminAttributeBlueprintAliasesMutation,
  type SetAdminAttributeBlueprintAliasesMutationVariables,
  SetAdminAttributeBlueprintArchiveStatusDocument,
  type SetAdminAttributeBlueprintArchiveStatusMutation,
  type SetAdminAttributeBlueprintArchiveStatusMutationVariables,
  SetAdminAttributeBlueprintScopeDocument,
  type SetAdminAttributeBlueprintScopeMutation,
  type SetAdminAttributeBlueprintScopeMutationVariables,
  SetAdminAttributeBlueprintStatusDocument,
  type SetAdminAttributeBlueprintStatusMutation,
  type SetAdminAttributeBlueprintStatusMutationVariables,
  SetAdminAttributeBlueprintUnitDocument,
  type SetAdminAttributeBlueprintUnitMutation,
  type SetAdminAttributeBlueprintUnitMutationVariables,
  UpdateAdminAttributeBlueprintDocument,
  type UpdateAdminAttributeBlueprintMutation,
  type UpdateAdminAttributeBlueprintMutationVariables,
} from '@mkbj/api-graphql';
import { Button } from '@mkbj/ui';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { MoreHorizontal, Plus, Search, X } from 'lucide-react';
import { type FormEvent, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { graphQlRequest } from '../../api/graphql';
import { toAppError } from '../../api/errors';
import { useAuth } from '../../auth/auth-context';
import { ConfirmationDialog } from '../../components/confirmation-dialog';
import { EmptyState, ErrorState, PageLoading } from '../../components/feedback';
import { OffsetPagination } from '../../components/pagination';
import { StatusBadge } from '../../components/status-badge';
import { flattenCategories } from '../products/product-editor-model';
import {
  createOptionDraft,
  displayAttributeValue,
  parseAliases,
  readAttributeListState,
  validateAttributeDraft,
  writeAttributeListState,
  type AttributeListState,
  type AttributeOptionDraft,
} from './attribute-list-model';

type BlueprintRow = AdminAttributeBlueprintsQuery['attributeBlueprints']['items'][number];
type Editor = { mode: 'create' | 'edit' | 'scope' | 'aliases' | 'unit'; row?: BlueprintRow };
type Confirmation = { kind: 'status' | 'archive' | 'delete'; row: BlueprintRow };

const dataTypes: ReadonlyArray<{ value: AttributeBlueprintDataType; label: string }> = [
  { value: 'TEXT', label: 'Text' },
  { value: 'NUMBER', label: 'Number' },
  { value: 'BOOLEAN', label: 'Yes / No' },
  { value: 'DATE', label: 'Date' },
  { value: 'OPTION', label: 'Option list' },
];

function graphDataType(value: string): AttributeBlueprintDataType {
  return value.toUpperCase() as AttributeBlueprintDataType;
}

function graphStatus(value: string): 'ACTIVE' | 'INACTIVE' | 'ARCHIVED' {
  return value.toUpperCase() as 'ACTIVE' | 'INACTIVE' | 'ARCHIVED';
}

function OptionEditor({
  dataType,
  options,
  setOptions,
  units,
}: {
  dataType: AttributeBlueprintDataType;
  options: AttributeOptionDraft[];
  setOptions: React.Dispatch<React.SetStateAction<AttributeOptionDraft[]>>;
  units: AdminAttributeBlueprintsQuery['measurementUnits'];
}) {
  if (dataType === 'BOOLEAN') {
    return <div className="attribute-note">Boolean attributes use the built-in Yes and No values.</div>;
  }

  return (
    <fieldset className="option-editor">
      <legend>Predefined values</legend>
      <p>{dataType === 'OPTION' ? 'Add the choices staff can select.' : 'Optional values that can be reused consistently.'}</p>
      {options.map((option, index) => (
        <div className="option-editor__row" key={option.key}>
          <label className="field">
            <span>Value {index + 1}</span>
            <input
              maxLength={1000}
              onChange={(event) => setOptions((current) => current.map((item) => item.key === option.key ? { ...item, value: event.target.value } : item))}
              type={dataType === 'DATE' ? 'date' : 'text'}
              value={option.value}
            />
          </label>
          {dataType === 'NUMBER' && <label className="field"><span>Unit *</span><select onChange={(event) => setOptions((current) => current.map((item) => item.key === option.key ? { ...item, measurementUnitId: event.target.value } : item))} value={option.measurementUnitId}><option value="">Select a unit</option>{units.map((unit) => <option key={unit.id} value={unit.id}>{unit.name} ({unit.symbol})</option>)}</select></label>}
          <Button aria-label={`Remove value ${index + 1}`} onClick={() => setOptions((current) => current.filter((item) => item.key !== option.key))} size="icon" type="button" variant="ghost"><X size={16} /></Button>
        </div>
      ))}
      <Button onClick={() => setOptions((current) => [...current, createOptionDraft(Date.now())])} size="compact" type="button" variant="ghost"><Plus size={15} />Add value</Button>
    </fieldset>
  );
}

export function AttributeListPage() {
  const { token } = useAuth();
  const queryClient = useQueryClient();
  const [searchParams, setSearchParams] = useSearchParams();
  const state = readAttributeListState(searchParams);
  const [searchDraft, setSearchDraft] = useState(state.q);
  const [editor, setEditor] = useState<Editor | null>(null);
  const [confirmation, setConfirmation] = useState<Confirmation | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [lifecycleError, setLifecycleError] = useState<string | null>(null);
  const [formErrors, setFormErrors] = useState<string[]>([]);
  const [name, setName] = useState('');
  const [dataType, setDataType] = useState<AttributeBlueprintDataType>('TEXT');
  const [options, setOptions] = useState<AttributeOptionDraft[]>([]);
  const [aliases, setAliases] = useState('');
  const [scope, setScope] = useState<'GLOBAL' | 'CATEGORY'>('GLOBAL');
  const [scopeCategoryId, setScopeCategoryId] = useState('');
  const [unitId, setUnitId] = useState('');
  const [confirmOptionRemoval, setConfirmOptionRemoval] = useState(false);
  const errorSummaryRef = useRef<HTMLDivElement>(null);

  useEffect(() => setSearchDraft(state.q), [state.q]);
  const updateState = useCallback((next: Partial<AttributeListState>) => {
    setSearchParams((current) => writeAttributeListState({ ...readAttributeListState(current), ...next }), { replace: true });
  }, [setSearchParams]);
  const updateStatusFilter = (value: string) => {
    const next = { ...state, page: 1 };
    if (value) next.status = value as AttributeBlueprintStatusFilter;
    else delete next.status;
    setSearchParams(writeAttributeListState(next), { replace: true });
  };
  const updateDataTypeFilter = (value: string) => {
    const next = { ...state, page: 1 };
    if (value) next.dataType = value as AttributeBlueprintDataTypeFilter;
    else delete next.dataType;
    setSearchParams(writeAttributeListState(next), { replace: true });
  };
  useEffect(() => {
    if (searchDraft.trim() === state.q) return;
    const timeout = window.setTimeout(() => updateState({ q: searchDraft.trim(), page: 1 }), 300);
    return () => window.clearTimeout(timeout);
  }, [searchDraft, state.q, updateState]);
  useEffect(() => {
    if (formErrors.length) errorSummaryRef.current?.focus();
  }, [formErrors]);

  const filter = {
    ...(state.q ? { nameContains: state.q } : {}),
    ...(state.status ? { status: state.status } : {}),
    ...(state.dataType ? { dataType: state.dataType } : {}),
  };
  const variables: AdminAttributeBlueprintsQueryVariables = {
    page: state.page,
    pageSize: state.pageSize,
    includeDeleted: state.includeDeleted,
    filter,
  };
  const blueprints = useQuery({
    queryKey: ['admin-attribute-blueprints', variables],
    queryFn: ({ signal }) => graphQlRequest<AdminAttributeBlueprintsQuery, AdminAttributeBlueprintsQueryVariables>(AdminAttributeBlueprintsDocument, variables, token!, signal),
    enabled: Boolean(token),
    placeholderData: keepPreviousData,
  });
  const editorOptions = useQuery({
    queryKey: ['attribute-blueprint-editor-options'],
    queryFn: ({ signal }) => graphQlRequest<AttributeBlueprintEditorOptionsQuery, AttributeBlueprintEditorOptionsQueryVariables>(AttributeBlueprintEditorOptionsDocument, {}, token!, signal),
    enabled: Boolean(token),
    staleTime: 5 * 60_000,
  });
  const usageTargetId = editor?.mode === 'edit' ? editor.row?.id : confirmation?.row.id;
  const usage = useQuery({
    queryKey: ['admin-attribute-blueprint-usage', usageTargetId],
    queryFn: ({ signal }) => graphQlRequest<AdminAttributeBlueprintUsageQuery, AdminAttributeBlueprintUsageQueryVariables>(AdminAttributeBlueprintUsageDocument, { id: usageTargetId! }, token!, signal),
    enabled: Boolean(token && usageTargetId),
  });

  const completed = async (message: string) => {
    await queryClient.invalidateQueries({ queryKey: ['admin-attribute-blueprints'] });
    setEditor(null);
    setConfirmation(null);
    setFormErrors([]);
    setLifecycleError(null);
    setNotice(message);
  };
  const formFailed = (error: unknown) => setFormErrors([toAppError(error).message]);
  const lifecycleFailed = (error: unknown) => setLifecycleError(toAppError(error).message);

  const create = useMutation({ mutationFn: () => graphQlRequest<CreateAdminAttributeBlueprintMutation, CreateAdminAttributeBlueprintMutationVariables>(CreateAdminAttributeBlueprintDocument, { input: { name: name.trim(), dataType, options: options.map((option) => ({ value: option.value.trim(), measurementUnitId: option.measurementUnitId || null })) } }, token!), onSuccess: () => completed('Attribute blueprint created.'), onError: formFailed });
  const update = useMutation({ mutationFn: () => graphQlRequest<UpdateAdminAttributeBlueprintMutation, UpdateAdminAttributeBlueprintMutationVariables>(UpdateAdminAttributeBlueprintDocument, { input: { attributeBlueprintId: editor!.row!.id, name: name.trim(), dataType, isActive: graphStatus(editor!.row!.status) === 'ACTIVE', options: options.map((option) => ({ ...(option.optionId ? { optionId: option.optionId } : {}), value: option.value.trim(), measurementUnitId: option.measurementUnitId || null })) } }, token!), onSuccess: () => completed('Attribute blueprint updated.'), onError: formFailed });
  const saveScope = useMutation({ mutationFn: () => graphQlRequest<SetAdminAttributeBlueprintScopeMutation, SetAdminAttributeBlueprintScopeMutationVariables>(SetAdminAttributeBlueprintScopeDocument, { input: { attributeBlueprintId: editor!.row!.id, scope, categoryId: scope === 'CATEGORY' ? scopeCategoryId : null } }, token!), onSuccess: () => completed('Attribute scope saved.'), onError: formFailed });
  const saveAliases = useMutation({ mutationFn: () => graphQlRequest<SetAdminAttributeBlueprintAliasesMutation, SetAdminAttributeBlueprintAliasesMutationVariables>(SetAdminAttributeBlueprintAliasesDocument, { input: { attributeBlueprintId: editor!.row!.id, aliases: parseAliases(aliases) } }, token!), onSuccess: () => completed('Attribute aliases saved.'), onError: formFailed });
  const saveUnit = useMutation({ mutationFn: () => graphQlRequest<SetAdminAttributeBlueprintUnitMutation, SetAdminAttributeBlueprintUnitMutationVariables>(SetAdminAttributeBlueprintUnitDocument, { input: { attributeBlueprintId: editor!.row!.id, unitId: unitId || null } }, token!), onSuccess: () => completed('Default measurement unit saved.'), onError: formFailed });
  const setStatus = useMutation({ mutationFn: (row: BlueprintRow) => graphQlRequest<SetAdminAttributeBlueprintStatusMutation, SetAdminAttributeBlueprintStatusMutationVariables>(SetAdminAttributeBlueprintStatusDocument, { attributeBlueprintId: row.id, isActive: graphStatus(row.status) !== 'ACTIVE' }, token!), onSuccess: () => completed(graphStatus(confirmation!.row.status) === 'ACTIVE' ? 'Attribute deactivated.' : 'Attribute activated.'), onError: lifecycleFailed });
  const setArchive = useMutation({ mutationFn: (row: BlueprintRow) => graphQlRequest<SetAdminAttributeBlueprintArchiveStatusMutation, SetAdminAttributeBlueprintArchiveStatusMutationVariables>(SetAdminAttributeBlueprintArchiveStatusDocument, { attributeBlueprintId: row.id, isArchived: graphStatus(row.status) !== 'ARCHIVED' }, token!), onSuccess: () => completed(graphStatus(confirmation!.row.status) === 'ARCHIVED' ? 'Attribute restored from archive.' : 'Attribute archived.'), onError: lifecycleFailed });
  const remove = useMutation({ mutationFn: (row: BlueprintRow) => graphQlRequest<DeleteAdminAttributeBlueprintMutation, DeleteAdminAttributeBlueprintMutationVariables>(DeleteAdminAttributeBlueprintDocument, { attributeBlueprintId: row.id }, token!), onSuccess: () => completed('Attribute deleted.'), onError: lifecycleFailed });

  const openEditor = (next: Editor) => {
    const row = next.row;
    setEditor(next);
    setFormErrors([]);
    setName(row?.name ?? '');
    setDataType(row ? graphDataType(row.dataType) : 'TEXT');
    setOptions(row?.options.map((option) => ({ key: option.id, optionId: option.id, value: option.value, measurementUnitId: option.measurementUnitId ?? '' })) ?? []);
    setAliases(row?.aliases.join('\n') ?? '');
    setScope(row?.scope.toUpperCase() === 'CATEGORY' ? 'CATEGORY' : 'GLOBAL');
    setScopeCategoryId(row?.scopeCategoryId ?? '');
    setUnitId(row?.unitId ?? '');
    setConfirmOptionRemoval(false);
  };
  const removedExistingOption = Boolean(editor?.row?.options.some((existing) => !options.some((option) => option.optionId === existing.id)));
  const submit = (event: FormEvent) => {
    event.preventDefault();
    setFormErrors([]);
    if (editor?.mode === 'create' || editor?.mode === 'edit') {
      const errors = validateAttributeDraft(name, dataType, options);
      if (removedExistingOption && !confirmOptionRemoval) errors.push('Confirm removal of existing options before saving.');
      if (errors.length) return setFormErrors(errors);
      if (editor.mode === 'create') create.mutate(); else update.mutate();
    }
    if (editor?.mode === 'scope') {
      if (scope === 'CATEGORY' && !scopeCategoryId) return setFormErrors(['Choose the category this attribute belongs to.']);
      saveScope.mutate();
    }
    if (editor?.mode === 'aliases') {
      if (parseAliases(aliases).length > 20) return setFormErrors(['An attribute can have at most 20 aliases.']);
      saveAliases.mutate();
    }
    if (editor?.mode === 'unit') saveUnit.mutate();
  };

  const page = blueprints.data?.attributeBlueprints;
  const units = editorOptions.data?.measurementUnits ?? blueprints.data?.measurementUnits ?? [];
  const unitNames = useMemo(() => new Map((blueprints.data?.measurementUnits ?? []).map((unit) => [unit.id, `${unit.name} (${unit.symbol})`])), [blueprints.data?.measurementUnits]);
  const categories = editorOptions.data ? flattenCategories(editorOptions.data.categoryHierarchy) : [];
  const usageSnapshot = usage.data?.attributeBlueprintUsage;
  const formPending = create.isPending || update.isPending || saveScope.isPending || saveAliases.isPending || saveUnit.isPending;
  const lifecyclePending = setStatus.isPending || setArchive.isPending || remove.isPending;
  useEffect(() => { if (page && page.pageCount > 0 && state.page > page.pageCount) updateState({ page: page.pageCount }); }, [page, state.page, updateState]);

  const editorTitle = editor?.mode === 'create' ? 'Create attribute' : editor?.mode === 'edit' ? 'Edit attribute' : editor?.mode === 'scope' ? 'Set attribute scope' : editor?.mode === 'aliases' ? 'Edit import aliases' : 'Set default unit';
  const confirmationDescription = usage.isPending ? 'Checking current product and category usage…' : confirmation?.kind === 'delete'
    ? usageSnapshot?.productValueCount ? `This attribute is used by ${usageSnapshot.productValueCount} product values and cannot be deleted until those values are removed.` : `This permanently removes the blueprint. ${usageSnapshot?.categoryMappingCount ?? 0} category assignments will be removed automatically.`
    : confirmation?.kind === 'archive' ? `Archiving hides this attribute from active catalog work. It currently has ${usageSnapshot?.categoryMappingCount ?? 0} category assignments; active assignments may block this action.`
      : `This changes whether the attribute is available for catalog work. It currently has ${usageSnapshot?.categoryMappingCount ?? 0} category assignments; active assignments may block deactivation.`;

  return (
    <section aria-labelledby="attributes-title" className="page-stack">
      <div className="page-heading"><div><p className="eyebrow">Catalog</p><h1 id="attributes-title">Attributes</h1><p>Define reusable product data, controlled values, scope and measurement semantics.</p></div><Button onClick={() => openEditor({ mode: 'create' })}><Plus size={16} />Create attribute</Button></div>
      {notice && <div className="success-panel" role="status"><span>{notice}</span><Button onClick={() => setNotice(null)} size="compact" variant="ghost">Dismiss</Button></div>}
      <div className="panel filter-panel"><div className="filters" role="search"><label className="search-field"><span>Search attributes</span><span className="input-with-icon"><Search aria-hidden="true" size={17} /><input onChange={(event) => setSearchDraft(event.target.value)} placeholder="Name or canonical key" value={searchDraft} /></span></label><label><span>Status</span><select onChange={(event) => updateStatusFilter(event.target.value)} value={state.status ?? ''}><option value="">All statuses</option><option value="ACTIVE">Active</option><option value="INACTIVE">Inactive</option><option value="ARCHIVED">Archived</option></select></label><label><span>Data type</span><select onChange={(event) => updateDataTypeFilter(event.target.value)} value={state.dataType ?? ''}><option value="">All types</option>{dataTypes.map((type) => <option key={type.value} value={type.value}>{type.label}</option>)}</select></label><label className="checkbox-field"><input checked={state.includeDeleted} onChange={(event) => updateState({ includeDeleted: event.target.checked, page: 1 })} type="checkbox" /><span>Include deleted</span></label>{(state.q || state.status || state.dataType || state.includeDeleted) && <Button onClick={() => { setSearchDraft(''); setSearchParams({}); }} variant="ghost">Clear</Button>}</div></div>

      {blueprints.isPending ? <PageLoading label="Loading attributes" /> : blueprints.isError ? <ErrorState error={blueprints.error} retry={() => void blueprints.refetch()} /> : !page?.items.length ? <EmptyState title="No attributes found">Try broader filters or create an attribute blueprint.</EmptyState> : <div className="panel table-panel" aria-busy={blueprints.isFetching}><div className="table-summary"><strong>{page.totalCount.toLocaleString()} attributes</strong>{blueprints.isFetching && <span>Updating…</span>}</div><div className="table-scroll" role="region" aria-label="Attributes table" tabIndex={0}><table><thead><tr><th scope="col">Attribute</th><th scope="col">Type</th><th scope="col">Scope</th><th scope="col">Default unit</th><th scope="col">Status</th><th scope="col">Values</th><th scope="col"><span className="sr-only">Actions</span></th></tr></thead><tbody>{page.items.map((row) => {
        const status = row.deletedAt ? 'Deleted' : displayAttributeValue(row.status);
        const archived = graphStatus(row.status) === 'ARCHIVED';
        return <tr key={row.id}><th scope="row"><strong>{row.name}</strong><span className="cell-subtitle">{row.canonicalKey}</span></th><td>{displayAttributeValue(row.dataType)}</td><td>{displayAttributeValue(row.scope)}{row.scopeCategoryId && <span className="cell-subtitle">Category scoped</span>}</td><td>{row.unitId ? unitNames.get(row.unitId) ?? `Unit ${row.unitId}` : '—'}</td><td><StatusBadge status={status} /></td><td>{row.options.length}</td><td><div className="row-actions">{!row.deletedAt && <><Button disabled={archived} onClick={() => openEditor({ mode: 'edit', row })} size="compact" title={archived ? 'Restore this attribute before editing its core definition' : undefined} variant="ghost">Edit</Button><DropdownMenu.Root><DropdownMenu.Trigger asChild><Button aria-label={`More actions for ${row.name}`} size="icon" variant="ghost"><MoreHorizontal size={17} /></Button></DropdownMenu.Trigger><DropdownMenu.Portal><DropdownMenu.Content align="end" className="action-menu" sideOffset={6}><DropdownMenu.Item onSelect={() => openEditor({ mode: 'scope', row })}>Scope</DropdownMenu.Item><DropdownMenu.Item onSelect={() => openEditor({ mode: 'aliases', row })}>Import aliases</DropdownMenu.Item><DropdownMenu.Item onSelect={() => openEditor({ mode: 'unit', row })}>Default unit</DropdownMenu.Item><DropdownMenu.Separator /><DropdownMenu.Item disabled={archived} onSelect={() => { setLifecycleError(null); setConfirmation({ kind: 'status', row }); }}>{graphStatus(row.status) === 'ACTIVE' ? 'Deactivate' : 'Activate'}</DropdownMenu.Item><DropdownMenu.Item onSelect={() => { setLifecycleError(null); setConfirmation({ kind: 'archive', row }); }}>{archived ? 'Restore from archive' : 'Archive'}</DropdownMenu.Item><DropdownMenu.Item className="action-menu__danger" onSelect={() => { setLifecycleError(null); setConfirmation({ kind: 'delete', row }); }}>Delete</DropdownMenu.Item></DropdownMenu.Content></DropdownMenu.Portal></DropdownMenu.Root></>}</div></td></tr>;
      })}</tbody></table></div><OffsetPagination itemLabel="Attributes" onPageChange={(nextPage) => updateState({ page: nextPage })} onPageSizeChange={(nextPageSize) => updateState({ pageSize: nextPageSize, page: 1 })} page={page.page} pageCount={page.pageCount} pageSize={state.pageSize} /></div>}

      <Dialog.Root onOpenChange={(open) => !open && setEditor(null)} open={Boolean(editor)}><Dialog.Portal><Dialog.Overlay className="dialog-overlay" /><Dialog.Content aria-describedby="attribute-dialog-description" className="dialog-panel dialog-panel--large"><div className="dialog-heading"><Dialog.Title>{editorTitle}</Dialog.Title><Dialog.Close asChild><Button aria-label="Close dialog" disabled={formPending} size="icon" variant="ghost"><X /></Button></Dialog.Close></div><Dialog.Description id="attribute-dialog-description">Changes are validated against existing product values and category assignments.</Dialog.Description><form className="dialog-form dialog-form--scroll" onSubmit={submit}>
        {(editor?.mode === 'create' || editor?.mode === 'edit') && <><div className="form-grid"><label className="field"><span>Name *</span><input aria-invalid={formErrors.some((error) => error.toLowerCase().includes('name')) || undefined} autoFocus maxLength={200} onChange={(event) => setName(event.target.value)} value={name} /></label><label className="field"><span>Data type *</span><select disabled={editor.mode === 'edit' && (usage.isPending || usageSnapshot?.canChangeDataType === false)} onChange={(event) => { setDataType(event.target.value as AttributeBlueprintDataType); if (event.target.value === 'BOOLEAN') setOptions([]); }} value={dataType}>{dataTypes.map((type) => <option key={type.value} value={type.value}>{type.label}</option>)}</select>{editor.mode === 'edit' && usageSnapshot?.canChangeDataType === false && <span className="field-meta">The type is locked because product values exist.</span>}</label></div><OptionEditor dataType={dataType} options={options} setOptions={setOptions} units={units} />{removedExistingOption && <label className="removal-confirmation"><input checked={confirmOptionRemoval} onChange={(event) => setConfirmOptionRemoval(event.target.checked)} type="checkbox" /><span><strong>Confirm option removal</strong>Removing a predefined value also removes product values that use it.</span></label>}</>}
        {editor?.mode === 'scope' && <><label className="field"><span>Scope *</span><select autoFocus onChange={(event) => setScope(event.target.value as 'GLOBAL' | 'CATEGORY')} value={scope}><option value="GLOBAL">Global catalogue</option><option value="CATEGORY">One category</option></select><span className="field-meta">Category scope prevents one attribute name from carrying different meanings across branches.</span></label>{scope === 'CATEGORY' && <label className="field"><span>Category *</span><select onChange={(event) => setScopeCategoryId(event.target.value)} value={scopeCategoryId}><option value="">Select a category</option>{categories.map((category) => <option key={category.id} value={category.id}>{category.label}</option>)}</select></label>}</>}
        {editor?.mode === 'aliases' && <label className="field"><span>Import aliases</span><textarea autoFocus onChange={(event) => setAliases(event.target.value)} placeholder="One supplier column name per line" rows={8} value={aliases} /><span className="field-meta">Up to 20 aliases. The backend canonicalizes and de-duplicates them.</span></label>}
        {editor?.mode === 'unit' && <label className="field"><span>Default measurement unit</span><select autoFocus onChange={(event) => setUnitId(event.target.value)} value={unitId}><option value="">No default unit</option>{units.map((unit) => <option key={unit.id} value={unit.id}>{unit.name} ({unit.symbol})</option>)}</select><span className="field-meta">This is the default semantic unit for values; numeric predefined values still carry their own unit.</span></label>}
        {formErrors.length > 0 && <div className="form-error-summary" ref={errorSummaryRef} role="alert" tabIndex={-1}><strong>Review the following</strong><ul>{formErrors.map((error) => <li key={error}>{error}</li>)}</ul></div>}
        <div className="dialog-actions"><Dialog.Close asChild><Button disabled={formPending} type="button" variant="secondary">Cancel</Button></Dialog.Close><Button disabled={formPending || editorOptions.isPending} type="submit">{formPending ? 'Saving…' : 'Save'}</Button></div>
      </form></Dialog.Content></Dialog.Portal></Dialog.Root>

      <ConfirmationDialog confirmDisabled={confirmation?.kind === 'delete' && (usage.isPending || Boolean(usageSnapshot && !usageSnapshot.canDelete))} confirmLabel={confirmation?.kind === 'delete' ? 'Delete attribute' : confirmation?.kind === 'archive' ? graphStatus(confirmation.row.status) === 'ARCHIVED' ? 'Restore attribute' : 'Archive attribute' : graphStatus(confirmation?.row.status ?? 'INACTIVE') === 'ACTIVE' ? 'Deactivate attribute' : 'Activate attribute'} description={confirmationDescription} destructive={confirmation?.kind === 'delete' || (confirmation?.kind === 'archive' && graphStatus(confirmation.row.status) !== 'ARCHIVED')} onConfirm={() => { if (!confirmation) return; if (confirmation.kind === 'delete') remove.mutate(confirmation.row); if (confirmation.kind === 'archive') setArchive.mutate(confirmation.row); if (confirmation.kind === 'status') setStatus.mutate(confirmation.row); }} onOpenChange={(open) => { if (!open && !lifecyclePending) setConfirmation(null); }} open={Boolean(confirmation)} pending={lifecyclePending} title={confirmation?.kind === 'delete' ? 'Delete this attribute?' : confirmation?.kind === 'archive' ? graphStatus(confirmation.row.status) === 'ARCHIVED' ? 'Restore this attribute?' : 'Archive this attribute?' : `${graphStatus(confirmation?.row.status ?? 'INACTIVE') === 'ACTIVE' ? 'Deactivate' : 'Activate'} this attribute?`} />
      {lifecycleError && <div className="dialog-error floating-error" role="alert">{lifecycleError}</div>}
    </section>
  );
}

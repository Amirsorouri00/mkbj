import * as Dialog from '@radix-ui/react-dialog';
import {
  AdminProductsDocument,
  type AdminProductsQuery,
  type AdminProductsQueryVariables,
  type ProductStatus,
  ProductEditorOptionsDocument,
  type ProductEditorOptionsQuery,
  type ProductEditorOptionsQueryVariables,
  BulkEditAdminProductsDocument,
  type BulkEditAdminProductsMutation,
  type BulkEditAdminProductsMutationVariables,
  type BulkStatusChange,
} from '@mkbj/api-graphql';
import { Button } from '@mkbj/ui';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { flexRender } from '@tanstack/react-table';
import { getCoreRowModel, legacyCreateColumnHelper, useLegacyTable } from '@tanstack/react-table/legacy';
import { Image, ImageOff, Search, X } from 'lucide-react';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { graphQlRequest } from '../../api/graphql';
import { toAppError } from '../../api/errors';
import { useAuth } from '../../auth/auth-context';
import { EmptyState, ErrorState, PageLoading } from '../../components/feedback';
import { OffsetPagination } from '../../components/pagination';
import { StatusBadge } from '../../components/status-badge';
import { flattenCategories } from './product-editor-model';
import { productListCapabilities, toAdminProductsVariables, toProductListItem, type ProductListItem } from './product-list-contract';
import { readProductListState, writeProductListState, type ProductListState } from './url-state';

function humanizeStatus(status: string): string {
  return status.charAt(0) + status.slice(1).toLowerCase();
}

type ProductRow = ProductListItem;
const columnHelper = legacyCreateColumnHelper<ProductRow>();
function createProductColumns(selected: ReadonlySet<string>, toggle: (id: string) => void) { return [
  columnHelper.display({ id: 'selection', header: () => <span className="sr-only">Select</span>, cell: ({ row }) => <input aria-label={`Select ${row.original.title}`} checked={selected.has(row.original.id)} className="row-checkbox" onChange={() => toggle(row.original.id)} type="checkbox" /> }),
  columnHelper.accessor((product): unknown => product.title, {
    id: 'title',
    header: 'Product',
    cell: ({ row }) => {
      const product = row.original;
      return <div className="product-cell"><span className="product-thumb" title={product.thumbnail.kind === 'missing' ? 'No usable image' : 'Usable image available'}>{product.thumbnail.kind === 'image' ? <img alt={product.thumbnail.alt} src={product.thumbnail.url} /> : product.thumbnail.kind === 'available' ? <Image aria-hidden="true" size={17} /> : <ImageOff aria-hidden="true" size={17} />}</span><span><Link className="product-link" to={`/catalog/products/${product.id}`}>{product.title}</Link><span className="cell-subtitle">{product.slug}{product.mpn ? ` · ${product.mpn}` : ''}</span></span></div>;
    },
  }),
  columnHelper.accessor((product): unknown => product.status, { id: 'status', header: 'Status', cell: ({ row }) => <StatusBadge status={humanizeStatus(row.original.status)} /> }),
  columnHelper.accessor((product): unknown => product.brandName, { id: 'brand', header: 'Brand', cell: ({ row }) => row.original.brandName }),
  columnHelper.accessor((product): unknown => product.primaryCategoryName, { id: 'category', header: 'Category', cell: ({ row }) => row.original.primaryCategoryName }),
  columnHelper.accessor((product): unknown => product.variantCount, { id: 'variants', header: 'Variants', cell: ({ row }) => <><strong>{row.original.activeVariantCount}</strong><span className="cell-subtitle">of {row.original.variantCount} active</span></> }),
  columnHelper.accessor((product): unknown => product.effectiveUpdatedAt, { id: 'updated', header: 'Updated', cell: ({ row }) => new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(new Date(row.original.effectiveUpdatedAt)) }),
  columnHelper.display({ id: 'actions', header: () => <span className="sr-only">Actions</span>, cell: ({ row }) => <Button asChild size="compact" variant="ghost"><Link aria-label={`View ${row.original.title}`} to={`/catalog/products/${row.original.id}`}>View</Link></Button> }),
]; }

export function ProductListPage() {
  const { token } = useAuth();
  const queryClient = useQueryClient();
  const [searchParams, setSearchParams] = useSearchParams();
  const state = readProductListState(searchParams);
  const [queryDraft, setQueryDraft] = useState(state.q);
  const [selected, setSelected] = useState<Set<string>>(() => new Set());
  const [bulkOpen, setBulkOpen] = useState(false);
  const [bulkStatus, setBulkStatus] = useState<BulkStatusChange>('NONE');
  const [bulkBrandId, setBulkBrandId] = useState('');
  const [bulkCategoryId, setBulkCategoryId] = useState('');
  const [bulkError, setBulkError] = useState<string | null>(null);
  const [bulkNotice, setBulkNotice] = useState<string | null>(null);
  useEffect(() => setQueryDraft(state.q), [state.q]);

  const variables: AdminProductsQueryVariables = toAdminProductsVariables(state);
  const products = useQuery({
    queryKey: ['admin-products', variables],
    queryFn: ({ signal }) =>
      graphQlRequest<AdminProductsQuery, AdminProductsQueryVariables>(AdminProductsDocument, variables, token!, signal),
    enabled: Boolean(token),
    placeholderData: keepPreviousData,
  });
  const options = useQuery({
    queryKey: ['product-editor-options'],
    queryFn: ({ signal }) => graphQlRequest<ProductEditorOptionsQuery, ProductEditorOptionsQueryVariables>(ProductEditorOptionsDocument, { brandPage: 1, brandPageSize: 100 }, token!, signal),
    enabled: Boolean(token),
    staleTime: 5 * 60_000,
  });

  const update = useCallback((next: Partial<ProductListState>) => {
    setSearchParams((current) => writeProductListState({ ...readProductListState(current), ...next }), { replace: true });
  }, [setSearchParams]);
  useEffect(() => {
    if (queryDraft.trim() === state.q) return;
    const timeout = window.setTimeout(() => update({ q: queryDraft.trim(), page: 1 }), 300);
    return () => window.clearTimeout(timeout);
  }, [queryDraft, state.q, update]);
  const page = products.data?.adminProducts;
  const rows = useMemo(() => page?.items.map(toProductListItem) ?? [], [page?.items]);
  const categories = options.data ? flattenCategories(options.data.categoryHierarchy) : [];
  const bulk = useMutation({ mutationFn: () => graphQlRequest<BulkEditAdminProductsMutation, BulkEditAdminProductsMutationVariables>(BulkEditAdminProductsDocument, { input: { productIds: [...selected], status: bulkStatus, brandId: bulkBrandId || null, primaryCategoryId: bulkCategoryId || null } }, token!), onSuccess: async (result) => { const succeeded = result.bulkEditProducts.items.filter((item) => item.succeeded); const failed = result.bulkEditProducts.items.filter((item) => !item.succeeded); setSelected(new Set(failed.map((item) => item.productId))); setBulkNotice(`${succeeded.length} products updated${failed.length ? `; ${failed.length} failed: ${[...new Set(failed.map((item) => item.errorCode ?? 'UNKNOWN'))].join(', ')}` : '.'}`); setBulkOpen(false); setBulkError(null); await queryClient.invalidateQueries({ queryKey: ['admin-products'] }); }, onError: (error) => setBulkError(toAppError(error).message) });
  useEffect(() => {
    if (page && page.pageCount > 0 && state.page > page.pageCount) update({ page: page.pageCount });
  }, [page, state.page, update]);
  const columns = useMemo(() => createProductColumns(selected, (productId) => setSelected((current) => { const next = new Set(current); if (next.has(productId)) next.delete(productId); else next.add(productId); return next; })), [selected]);
  const table = useLegacyTable({
    data: rows,
    columns,
    getCoreRowModel: getCoreRowModel(),
  });

  return (
    <section aria-labelledby="products-title" className="page-stack">
      <div className="page-heading">
        <div><p className="eyebrow">Catalog</p><h1 id="products-title">Products</h1><p>Review product readiness, variants and publishing status.</p></div>
        <Button asChild><Link to="/catalog/products/new">Create product</Link></Button>
      </div>

      {bulkNotice && <div className="success-panel" role="status"><span>{bulkNotice}</span><Button onClick={() => setBulkNotice(null)} size="compact" variant="ghost">Dismiss</Button></div>}
      {selected.size > 0 && <div className="panel selection-toolbar" role="region" aria-label="Product selection"><strong>{selected.size} selected across pages</strong><div><Button onClick={() => { setBulkError(null); setBulkOpen(true); }} size="compact">Bulk edit</Button><Button onClick={() => setSelected(new Set())} size="compact" variant="ghost">Clear selection</Button></div></div>}

      <div className="panel filter-panel">
        <div className="filters" role="search">
          <label className="search-field">
            <span>Search products</span>
            <span className="input-with-icon"><Search aria-hidden="true" size={17} /><input onChange={(event) => setQueryDraft(event.target.value)} placeholder="Title, slug or MPN" value={queryDraft} /></span>
          </label>
          <label>
            <span>Status</span>
            <select onChange={(event) => {
              const next = { ...state, page: 1 };
              if (event.target.value) next.status = event.target.value as ProductStatus;
              else delete next.status;
              setSearchParams(writeProductListState(next), { replace: true });
            }} value={state.status ?? ''}>
              <option value="">All statuses</option>
              <option value="ACTIVE">Active</option>
              <option value="DRAFT">Draft</option>
              <option value="DEACTIVATED">Deactivated</option>
            </select>
          </label>
          <label><span>Brand</span><select disabled={options.isPending || options.isError} onChange={(event) => {
            const next = { ...state, page: 1 };
            if (event.target.value) next.brandId = event.target.value; else delete next.brandId;
            setSearchParams(writeProductListState(next), { replace: true });
          }} value={state.brandId ?? ''}><option value="">All brands</option>{options.data?.adminBrands.items.map((brand) => <option key={brand.id} value={brand.id}>{brand.displayName}</option>)}</select></label>
          <label><span>Category</span><select disabled={options.isPending || options.isError} onChange={(event) => {
            const next = { ...state, page: 1 };
            if (event.target.value) next.categoryId = event.target.value; else delete next.categoryId;
            setSearchParams(writeProductListState(next), { replace: true });
          }} value={state.categoryId ?? ''}><option value="">All categories</option>{categories.map((category) => <option key={category.id} value={category.id}>{category.label}</option>)}</select></label>
          {(state.q || state.status || state.brandId || state.categoryId) && <Button onClick={() => { setQueryDraft(''); setSearchParams({}); }} type="button" variant="ghost">Clear</Button>}
        </div>
      </div>

      {products.isPending ? <PageLoading label="Loading products" /> : products.isError ? <ErrorState error={products.error} retry={() => void products.refetch()} /> : !page?.items.length ? (
        <EmptyState title="No products found">Try a broader search or clear the current filters.</EmptyState>
      ) : (
        <div className="panel table-panel" aria-busy={products.isFetching}>
          <div className="table-summary"><div><strong>{page.totalCount.toLocaleString()} products</strong><Button onClick={() => setSelected((current) => { const next = new Set(current); const allSelected = rows.every((row) => next.has(row.id)); rows.forEach((row) => allSelected ? next.delete(row.id) : next.add(row.id)); return next; })} size="compact" variant="ghost">{rows.every((row) => selected.has(row.id)) ? 'Clear visible' : 'Select visible'}</Button></div><span title={`Server order: ${productListCapabilities.sorting.fixedOrder}`}>{products.isFetching ? 'Updating…' : 'Newest created first'}</span></div>
          <div className="table-scroll" role="region" aria-label="Products table" tabIndex={0}>
            <table>
              <thead>{table.getHeaderGroups().map((group) => <tr key={group.id}>{group.headers.map((header) => <th key={header.id} scope="col">{header.isPlaceholder ? null : flexRender(header.column.columnDef.header, header.getContext())}</th>)}</tr>)}</thead>
              <tbody>{table.getRowModel().rows.map((row) => <tr key={row.id}>{row.getVisibleCells().map((cell, index) => index === 0 ? <th key={cell.id} scope="row">{flexRender(cell.column.columnDef.cell, cell.getContext())}</th> : <td key={cell.id}>{flexRender(cell.column.columnDef.cell, cell.getContext())}</td>)}</tr>)}</tbody>
            </table>
          </div>
          <OffsetPagination itemLabel="Products" onPageChange={(nextPage) => update({ page: nextPage })} onPageSizeChange={(nextPageSize) => update({ pageSize: nextPageSize, page: 1 })} page={page.page} pageCount={page.pageCount} pageSize={state.pageSize} />
        </div>
      )}
      <Dialog.Root onOpenChange={setBulkOpen} open={bulkOpen}><Dialog.Portal><Dialog.Overlay className="dialog-overlay" /><Dialog.Content aria-describedby="bulk-edit-description" className="dialog-panel dialog-panel--wide"><div className="dialog-heading"><Dialog.Title>Bulk edit {selected.size} products</Dialog.Title><Dialog.Close asChild><Button aria-label="Close dialog" disabled={bulk.isPending} size="icon" variant="ghost"><X /></Button></Dialog.Close></div><Dialog.Description id="bulk-edit-description">The backend processes each product independently and returns a result for every selected ID. Failed products remain selected.</Dialog.Description><form className="dialog-form" onSubmit={(event) => { event.preventDefault(); setBulkError(null); if (bulkStatus === 'NONE' && !bulkBrandId && !bulkCategoryId) return setBulkError('Choose at least one change.'); bulk.mutate(); }}><label className="field"><span>Status change</span><select onChange={(event) => setBulkStatus(event.target.value as BulkStatusChange)} value={bulkStatus}><option value="NONE">No status change</option><option value="ACTIVATE">Activate</option><option value="DEACTIVATE">Deactivate</option></select></label><label className="field"><span>Replace brand</span><select onChange={(event) => setBulkBrandId(event.target.value)} value={bulkBrandId}><option value="">Keep current brands</option>{options.data?.adminBrands.items.filter((brand) => brand.isActive).map((brand) => <option key={brand.id} value={brand.id}>{brand.displayName}</option>)}</select></label><label className="field"><span>Replace primary category</span><select onChange={(event) => setBulkCategoryId(event.target.value)} value={bulkCategoryId}><option value="">Keep current categories</option>{categories.filter((category) => category.status === 'ACTIVE').map((category) => <option key={category.id} value={category.id}>{category.label}</option>)}</select></label>{bulkError && <div className="dialog-error" role="alert">{bulkError}</div>}<div className="warning-panel"><strong>Confirm the selected-set change</strong><p>This action affects only the {selected.size} explicit product IDs selected across pages. It does not mean “all matching filters.”</p></div><div className="dialog-actions"><Dialog.Close asChild><Button disabled={bulk.isPending} type="button" variant="secondary">Cancel</Button></Dialog.Close><Button disabled={bulk.isPending} type="submit">{bulk.isPending ? 'Updating…' : 'Apply changes'}</Button></div></form></Dialog.Content></Dialog.Portal></Dialog.Root>
    </section>
  );
}

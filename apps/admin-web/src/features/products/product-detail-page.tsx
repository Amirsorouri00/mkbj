import * as Dialog from '@radix-ui/react-dialog';
import {
  AdminProductDocument,
  type AdminProductQuery,
  type AdminProductQueryVariables,
  type ProductDetailFieldsFragment,
  SetAdminProductActiveDocument,
  type SetAdminProductActiveMutation,
  type SetAdminProductActiveMutationVariables,
  MergeAdminDuplicateProductDocument,
  type MergeAdminDuplicateProductMutation,
  type MergeAdminDuplicateProductMutationVariables,
} from '@mkbj/api-graphql';
import { Button } from '@mkbj/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft } from 'lucide-react';
import { type FormEvent, useEffect, useState } from 'react';
import { Link, useLocation, useParams } from 'react-router-dom';
import { graphQlRequest } from '../../api/graphql';
import { toAppError } from '../../api/errors';
import { useAuth } from '../../auth/auth-context';
import { ConfirmationDialog } from '../../components/confirmation-dialog';
import { ErrorState, PageLoading } from '../../components/feedback';
import { StatusBadge } from '../../components/status-badge';
import { ProductResources } from './product-resources';
import { ProductPricingSection } from '../pricing/product-pricing-section';
import { normalizeProductDetail } from './product-detail-model';

function dateTime(value: string | null): string {
  if (!value) return 'Not yet';
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return 'Not yet';
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(parsed);
}

const activationProblemLabels: Record<string, string> = {
  NO_ACTIVE_VARIANT: 'Add or activate at least one variant.',
  NO_BRAND: 'Choose a brand.',
  NO_PRIMARY_CATEGORY: 'Choose a primary category.',
  NO_TITLE: 'Add a product title.',
  NO_USABLE_IMAGE: 'Upload an image with publication rights allowed.',
};

export function ProductDetailPage() {
  const { id = '' } = useParams();
  const { token } = useAuth();
  const location = useLocation();
  const queryClient = useQueryClient();
  const [confirmActivation, setConfirmActivation] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [notice, setNotice] = useState((location.state as { notice?: string } | null)?.notice ?? null);
  const [mergeOpen, setMergeOpen] = useState(false);
  const [survivorDraft, setSurvivorDraft] = useState('');
  const [survivorId, setSurvivorId] = useState('');
  const variables = { id } satisfies AdminProductQueryVariables;
  const query = useQuery({
    queryKey: ['admin-product', id],
    queryFn: ({ signal }) => graphQlRequest<AdminProductQuery, AdminProductQueryVariables>(AdminProductDocument, variables, token!, signal),
    enabled: Boolean(id && token),
  });
  const activation = useMutation({
    mutationFn: (active: boolean) => graphQlRequest<SetAdminProductActiveMutation, SetAdminProductActiveMutationVariables>(SetAdminProductActiveDocument, { input: { productId: id, active } }, token!),
    onSuccess: async (result) => {
      queryClient.setQueryData(['admin-product', id], { adminProduct: result.setProductActive });
      await queryClient.invalidateQueries({ queryKey: ['admin-products'] });
      setNotice(result.setProductActive.status === 'ACTIVE' ? 'Product activated.' : 'Product deactivated.');
      setConfirmActivation(false);
      setActionError(null);
    },
    onError: (error) => setActionError(toAppError(error).message),
  });
  useEffect(() => { const timeout = window.setTimeout(() => setSurvivorId(survivorDraft.trim()), 300); return () => window.clearTimeout(timeout); }, [survivorDraft]);
  const survivor = useQuery({ queryKey: ['admin-product', 'merge-survivor', survivorId], queryFn: ({ signal }) => graphQlRequest<AdminProductQuery, AdminProductQueryVariables>(AdminProductDocument, { id: survivorId }, token!, signal), enabled: Boolean(mergeOpen && survivorId && survivorId !== id), retry: false });
  const merge = useMutation({ mutationFn: () => graphQlRequest<MergeAdminDuplicateProductMutation, MergeAdminDuplicateProductMutationVariables>(MergeAdminDuplicateProductDocument, { input: { duplicateProductId: id, survivorProductId: survivorId } }, token!), onSuccess: async (result) => { queryClient.setQueryData(['admin-product', id], { adminProduct: result.mergeDuplicateProduct }); await queryClient.invalidateQueries({ queryKey: ['admin-products'] }); setMergeOpen(false); setNotice(`Duplicate merged into product ${survivorId}.`); setActionError(null); }, onError: (error) => setActionError(toAppError(error).message) });

  if (query.isPending) return <PageLoading label="Loading product" />;
  if (query.isError) return <ErrorState error={query.error} retry={() => void query.refetch()} />;
  if (!query.data?.adminProduct) return <ErrorState error={new Error('The product response was incomplete. Try again.')} retry={() => void query.refetch()} />;
  const product = normalizeProductDetail(query.data.adminProduct);
  const isActive = product.status === 'ACTIVE';
  const applyUpdated = (updated: ProductDetailFieldsFragment, message: string) => {
    queryClient.setQueryData(['admin-product', id], { adminProduct: updated });
    void queryClient.invalidateQueries({ queryKey: ['admin-products'] });
    setNotice(message);
    setActionError(null);
  };
  const refresh = async (message: string) => {
    await query.refetch();
    await queryClient.invalidateQueries({ queryKey: ['admin-products'] });
    setNotice(message);
  };

  return (
    <section aria-labelledby="product-title" className="page-stack">
      <Button asChild className="back-link" size="compact" variant="ghost"><Link to="/catalog/products"><ArrowLeft size={16} />Products</Link></Button>
      <div className="page-heading">
        <div><p className="eyebrow">Product</p><h1 id="product-title">{product.title}</h1><p>{product.shortDescription || 'No short description has been added.'}</p></div>
        <div className="heading-actions"><StatusBadge status={product.status.charAt(0) + product.status.slice(1).toLowerCase()} /><Button asChild variant="secondary"><Link to={`/catalog/products/${product.id}/edit`}>Edit product</Link></Button><Button disabled={Boolean(product.mergedIntoProductId)} onClick={() => { setMergeOpen(true); setActionError(null); }} variant="secondary">Merge duplicate</Button><Button disabled={Boolean(product.mergedIntoProductId)} onClick={() => setConfirmActivation(true)} variant={isActive ? 'destructive' : 'primary'}>{isActive ? 'Deactivate' : 'Activate'}</Button></div>
      </div>

      {notice && <div className="success-panel" role="status"><span>{notice}</span><Button aria-label="Dismiss message" onClick={() => setNotice(null)} size="compact" variant="ghost">Dismiss</Button></div>}
      {actionError && <div className="error-panel" role="alert"><div><strong>Product status was not changed.</strong><p>{actionError}</p></div></div>}
      {product.activationProblems.length > 0 && <div className="warning-panel" role="status"><strong>Not ready to activate</strong><ul>{product.activationProblems.map((problem) => <li key={problem}>{activationProblemLabels[problem] ?? problem}</li>)}</ul></div>}
      {product.mergedIntoProductId && <div className="warning-panel" role="status"><strong>Merged duplicate</strong><p>This record has been merged into product <Link to={`/catalog/products/${product.mergedIntoProductId}`}>{product.mergedIntoProductId}</Link> and is no longer independently editable.</p></div>}

      <div className="detail-grid">
        <div className="detail-main page-stack">
          <section className="panel detail-section"><h2>Product information</h2><dl className="definition-grid"><div><dt>Slug</dt><dd>{product.slug}</dd></div><div><dt>MPN</dt><dd>{product.mpn || '—'}</dd></div><div><dt>Brand</dt><dd>{product.brandName}</dd></div><div><dt>Primary category</dt><dd>{product.primaryCategoryName}</dd></div><div className="definition-wide"><dt>Description</dt><dd>{product.description || 'No description has been added.'}</dd></div></dl></section>
          <ProductResources onRefresh={refresh} onUpdated={applyUpdated} product={product} />
          <ProductPricingSection variants={product.variants} />
        </div>
        <aside className="detail-side page-stack">
          <section className="panel detail-section"><h2>Record</h2><dl className="record-list"><div><dt>Product ID</dt><dd>{product.id}</dd></div><div><dt>Created</dt><dd>{dateTime(product.createdAt)}</dd></div><div><dt>Last updated</dt><dd>{dateTime(product.updatedAt)}</dd></div></dl></section>
        </aside>
      </div>
      <ConfirmationDialog
        confirmLabel={isActive ? 'Deactivate product' : 'Activate product'}
        description={isActive ? 'This product will no longer be available for sale, and imports will not reactivate it.' : 'The backend will check variants, brand, category and usable media before activation.'}
        destructive={isActive}
        onConfirm={() => activation.mutate(!isActive)}
        onOpenChange={setConfirmActivation}
        open={confirmActivation}
        pending={activation.isPending}
        title={isActive ? 'Deactivate this product?' : 'Activate this product?'}
      />
      <Dialog.Root onOpenChange={setMergeOpen} open={mergeOpen}><Dialog.Portal><Dialog.Overlay className="dialog-overlay" /><Dialog.Content aria-describedby="merge-description" className="dialog-panel dialog-panel--large"><div className="dialog-heading"><Dialog.Title>Merge duplicate product</Dialog.Title><Dialog.Close asChild><Button aria-label="Close dialog" disabled={merge.isPending} size="icon" variant="ghost">×</Button></Dialog.Close></div><Dialog.Description id="merge-description">This product becomes the duplicate. Enter and verify the product ID that should survive.</Dialog.Description><form className="dialog-form" onSubmit={(event: FormEvent) => { event.preventDefault(); if (!survivorId || survivorId === id || !survivor.data) return setActionError('Enter a different, valid survivor product ID.'); merge.mutate(); }}><label className="field"><span>Survivor product ID *</span><input autoFocus onChange={(event) => setSurvivorDraft(event.target.value)} value={survivorDraft} /></label>{survivorDraft.trim() === id && <div className="dialog-error" role="alert">The survivor must be a different product.</div>}{survivor.isPending && <p className="muted">Loading survivor product…</p>}{survivor.isError && <div className="dialog-error" role="alert">{toAppError(survivor.error).message}</div>}{merge.isError && <div className="dialog-error" role="alert">{toAppError(merge.error).message}</div>}{survivor.data && <div className="merge-comparison"><article><span>Duplicate</span><strong>{product.title}</strong><small>{product.brandName} · {product.primaryCategoryName}</small><small>{product.variants.length} variants · {product.status}</small></article><article><span>Survivor</span><strong>{survivor.data.adminProduct.title}</strong><small>{survivor.data.adminProduct.brandName} · {survivor.data.adminProduct.primaryCategoryName}</small><small>{survivor.data.adminProduct.variants.length} variants · {survivor.data.adminProduct.status}</small></article></div>}<div className="warning-panel"><strong>This cannot be undone from the Backoffice.</strong><p>The backend preserves the survivor and marks the current product as merged.</p></div><div className="dialog-actions"><Dialog.Close asChild><Button disabled={merge.isPending} type="button" variant="secondary">Cancel</Button></Dialog.Close><Button disabled={!survivor.data || merge.isPending} type="submit" variant="destructive">{merge.isPending ? 'Merging…' : 'Merge into survivor'}</Button></div></form></Dialog.Content></Dialog.Portal></Dialog.Root>
    </section>
  );
}

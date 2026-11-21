import {
  AdminProductDocument,
  CreateAdminProductDocument,
  type CreateAdminProductMutation,
  type CreateAdminProductMutationVariables,
  ProductEditorOptionsDocument,
  type ProductEditorOptionsQuery,
  type ProductEditorOptionsQueryVariables,
  UpdateAdminProductDocument,
  type UpdateAdminProductMutation,
  type UpdateAdminProductMutationVariables,
  type AdminProductQuery,
  type AdminProductQueryVariables,
} from '@mkbj/api-graphql';
import { zodResolver } from '@hookform/resolvers/zod';
import { Button } from '@mkbj/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { AlertCircle, ArrowLeft } from 'lucide-react';
import { useCallback, useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { Link, useBeforeUnload, useNavigate, useParams } from 'react-router-dom';
import { graphQlRequest } from '../../api/graphql';
import { toAppError } from '../../api/errors';
import { useAuth } from '../../auth/auth-context';
import { ErrorState, PageLoading } from '../../components/feedback';
import { emptyProductValues, flattenCategories, optionalText, productSchema, type ProductFormValues } from './product-editor-model';

function FieldError({ message }: { message: string | undefined }) {
  return message ? <span className="field-error"><AlertCircle aria-hidden="true" size={14} />{message}</span> : null;
}

export function ProductEditorPage() {
  const { id } = useParams();
  const editing = Boolean(id);
  const { token } = useAuth();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const form = useForm<ProductFormValues>({ resolver: zodResolver(productSchema), defaultValues: emptyProductValues, mode: 'onBlur' });

  const productQuery = useQuery({
    queryKey: ['admin-product', id],
    queryFn: ({ signal }) => graphQlRequest<AdminProductQuery, AdminProductQueryVariables>(AdminProductDocument, { id: id! }, token!, signal),
    enabled: Boolean(id && token),
  });
  const optionsQuery = useQuery({
    queryKey: ['product-editor-options'],
    queryFn: ({ signal }) => graphQlRequest<ProductEditorOptionsQuery, ProductEditorOptionsQueryVariables>(ProductEditorOptionsDocument, { brandPage: 1, brandPageSize: 100 }, token!, signal),
    enabled: Boolean(token),
    staleTime: 5 * 60_000,
  });

  useEffect(() => {
    const product = productQuery.data?.adminProduct;
    if (!product) return;
    form.reset({
      title: product.title,
      slug: product.slug,
      brandId: product.brandId,
      primaryCategoryId: product.primaryCategoryId,
      shortDescription: product.shortDescription ?? '',
      description: product.description ?? '',
    });
  }, [form, productQuery.data]);

  useBeforeUnload(useCallback((event) => {
    if (!form.formState.isDirty) return;
    event.preventDefault();
  }, [form.formState.isDirty]));

  const createMutation = useMutation({
    mutationFn: (values: ProductFormValues) => graphQlRequest<CreateAdminProductMutation, CreateAdminProductMutationVariables>(CreateAdminProductDocument, {
      input: {
        title: values.title.trim(),
        brandId: values.brandId,
        primaryCategoryId: values.primaryCategoryId,
        slug: optionalText(values.slug),
        shortDescription: optionalText(values.shortDescription),
        description: optionalText(values.description),
      },
    }, token!),
  });
  const updateMutation = useMutation({
    mutationFn: (values: ProductFormValues) => graphQlRequest<UpdateAdminProductMutation, UpdateAdminProductMutationVariables>(UpdateAdminProductDocument, {
      input: {
        productId: id!,
        title: values.title.trim(),
        brandId: values.brandId,
        primaryCategoryId: values.primaryCategoryId,
        shortDescription: optionalText(values.shortDescription),
        description: optionalText(values.description),
      },
    }, token!),
  });

  const submit = form.handleSubmit(async (values) => {
    form.clearErrors('root');
    try {
      const result = editing ? (await updateMutation.mutateAsync(values)).updateProduct : (await createMutation.mutateAsync(values)).createProduct;
      await queryClient.invalidateQueries({ queryKey: ['admin-products'] });
      queryClient.setQueryData(['admin-product', result.id], { adminProduct: result });
      form.reset(values);
      void navigate(`/catalog/products/${result.id}`, { replace: true, state: { notice: editing ? 'Product changes saved.' : 'Product created as a draft.' } });
    } catch (error) {
      form.setError('root.server', { message: toAppError(error).message });
    }
  });

  if (editing && productQuery.isPending) return <PageLoading label="Loading product editor" />;
  if (productQuery.isError) return <ErrorState error={productQuery.error} retry={() => void productQuery.refetch()} />;
  if (optionsQuery.isPending) return <PageLoading label="Loading editor choices" />;
  if (optionsQuery.isError) return <ErrorState error={optionsQuery.error} retry={() => void optionsQuery.refetch()} />;

  const categories = flattenCategories(optionsQuery.data.categoryHierarchy);
  const pending = createMutation.isPending || updateMutation.isPending;
  const errors = [
    form.formState.errors.title?.message,
    form.formState.errors.slug?.message,
    form.formState.errors.brandId?.message,
    form.formState.errors.primaryCategoryId?.message,
    form.formState.errors.shortDescription?.message,
    form.formState.errors.description?.message,
    form.formState.errors.root?.server?.message,
  ].filter((message): message is string => Boolean(message));

  return (
    <section aria-labelledby="product-editor-title" className="page-stack">
      <Button asChild className="back-link" size="compact" variant="ghost"><Link to={editing ? `/catalog/products/${id}` : '/catalog/products'}><ArrowLeft size={16} />{editing ? 'Product' : 'Products'}</Link></Button>
      <div className="page-heading"><div><p className="eyebrow">Catalog</p><h1 id="product-editor-title">{editing ? 'Edit product' : 'Create product'}</h1><p>{editing ? 'Update the supported product fields. The existing slug remains permanent.' : 'Create a draft product. Variants, identifiers and media can be added after creation.'}</p></div></div>

      {errors.length > 0 && (
        <div className="form-error-summary" role="alert" tabIndex={-1}>
          <strong>Check the highlighted fields</strong>
          <ul>{errors.map((message, index) => <li key={`${message}-${index}`}>{message}</li>)}</ul>
        </div>
      )}

      <form className="editor-layout" noValidate onSubmit={(event) => void submit(event)}>
        <div className="page-stack">
          <section className="panel form-section">
            <div className="section-copy"><h2>General information</h2><p>Customer-facing product identity and description.</p></div>
            <div className="form-grid">
              <label className="field field--wide"><span>Product title <b aria-hidden="true">*</b></span><input aria-invalid={Boolean(form.formState.errors.title)} maxLength={200} {...form.register('title')} /><FieldError message={form.formState.errors.title?.message} /></label>
              <label className="field field--wide"><span>Slug {editing && <small>Permanent</small>}</span><input aria-invalid={Boolean(form.formState.errors.slug)} disabled={editing} placeholder="Generated from title when empty" {...form.register('slug')} /><FieldError message={form.formState.errors.slug?.message} /></label>
              <label className="field field--wide"><span>Short description</span><textarea aria-invalid={Boolean(form.formState.errors.shortDescription)} maxLength={500} rows={3} {...form.register('shortDescription')} /><span className="field-meta">Up to 500 characters</span><FieldError message={form.formState.errors.shortDescription?.message} /></label>
              <label className="field field--wide"><span>Description</span><textarea aria-invalid={Boolean(form.formState.errors.description)} maxLength={20000} rows={9} {...form.register('description')} /><FieldError message={form.formState.errors.description?.message} /></label>
            </div>
          </section>
          <section className="panel form-section">
            <div className="section-copy"><h2>Classification</h2><p>Brand and primary catalog placement are required by the backend.</p></div>
            <div className="form-grid">
              <label className="field"><span>Brand <b aria-hidden="true">*</b></span><select aria-invalid={Boolean(form.formState.errors.brandId)} {...form.register('brandId')}><option value="">Choose a brand</option>{optionsQuery.data.adminBrands.items.map((brand) => <option disabled={!brand.isActive && brand.id !== form.getValues('brandId')} key={brand.id} value={brand.id}>{brand.displayName}{!brand.isActive ? ' (inactive)' : ''}</option>)}</select><FieldError message={form.formState.errors.brandId?.message} /></label>
              <label className="field"><span>Primary category <b aria-hidden="true">*</b></span><select aria-invalid={Boolean(form.formState.errors.primaryCategoryId)} {...form.register('primaryCategoryId')}><option value="">Choose a category</option>{categories.map((category) => <option disabled={category.status !== 'ACTIVE' && category.id !== form.getValues('primaryCategoryId')} key={category.id} value={category.id}>{category.label}{category.status !== 'ACTIVE' ? ' (inactive)' : ''}</option>)}</select><FieldError message={form.formState.errors.primaryCategoryId?.message} /></label>
            </div>
          </section>
        </div>
        <aside className="panel editor-actions">
          <h2>{editing ? 'Save changes' : 'Create draft'}</h2>
          <p>The backend validates and normalizes this product when you save.</p>
          <Button disabled={pending || !form.formState.isDirty} type="submit">{pending ? 'Saving…' : editing ? 'Save product' : 'Create product'}</Button>
          <Button asChild variant="secondary"><Link to={editing ? `/catalog/products/${id}` : '/catalog/products'}>Cancel</Link></Button>
        </aside>
      </form>
    </section>
  );
}

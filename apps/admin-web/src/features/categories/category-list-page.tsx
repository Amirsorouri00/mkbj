import * as Dialog from '@radix-ui/react-dialog';
import {
    AdminCategoriesDocument,
    type AdminCategoriesQuery,
    type AdminCategoriesQueryVariables,
    type CategoryStatus,
    CreateAdminCategoryDocument,
    type CreateAdminCategoryMutation,
    type CreateAdminCategoryMutationVariables,
    DeleteAdminCategoryDocument,
    type DeleteAdminCategoryMutation,
    type DeleteAdminCategoryMutationVariables,
    MoveAdminCategoryDocument,
    type MoveAdminCategoryMutation,
    type MoveAdminCategoryMutationVariables,
    ProductEditorOptionsDocument,
    type ProductEditorOptionsQuery,
    type ProductEditorOptionsQueryVariables,
    SetAdminCategoryStatusDocument,
    type SetAdminCategoryStatusMutation,
    type SetAdminCategoryStatusMutationVariables,
    UpdateAdminCategoryDocument,
    type UpdateAdminCategoryMutation,
    type UpdateAdminCategoryMutationVariables,
} from '@mkbj/api-graphql';
import { Button } from '@mkbj/ui';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ListChecks, Move, Pencil, Plus, Search, Trash2, X } from 'lucide-react';
import { type FormEvent, useCallback, useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { graphQlRequest } from '../../api/graphql';
import { toAppError } from '../../api/errors';
import { useAuth } from '../../auth/auth-context';
import { ConfirmationDialog } from '../../components/confirmation-dialog';
import { EmptyState, ErrorState, PageLoading } from '../../components/feedback';
import { CursorPagination } from '../../components/pagination';
import { StatusBadge } from '../../components/status-badge';
import { flattenCategories, type CategoryNode } from '../products/product-editor-model';
import {
    readCategoryListState,
    writeCategoryListState,
    type CategoryListState,
} from './category-list-model';

type CategoryRow = NonNullable<NonNullable<AdminCategoriesQuery['categories']>['nodes']>[number];
type Editor = { mode: 'create' | 'edit' | 'move'; category?: CategoryRow };
type Confirmation = { kind: 'status' | 'delete'; category: CategoryRow };

function Tree({ nodes }: { nodes: ReadonlyArray<CategoryNode> }) {
    return (
        <ul className="category-tree">
            {nodes.map((node) => (
                <li key={node.id}>
                    <span>
                        <StatusBadge
                            status={node.status.charAt(0) + node.status.slice(1).toLowerCase()}
                        />
                        {node.name}
                    </span>
                    {node.children?.length ? <Tree nodes={node.children} /> : null}
                </li>
            ))}
        </ul>
    );
}

export function CategoryListPage() {
    const { token } = useAuth();
    const queryClient = useQueryClient();
    const [searchParams, setSearchParams] = useSearchParams();
    const state = readCategoryListState(searchParams);
    const [draft, setDraft] = useState(state.q);
    const [editor, setEditor] = useState<Editor | null>(null);
    const [confirmation, setConfirmation] = useState<Confirmation | null>(null);
    const [name, setName] = useState('');
    const [slug, setSlug] = useState('');
    const [description, setDescription] = useState('');
    const [parentId, setParentId] = useState('');
    const [displayOrder, setDisplayOrder] = useState('0');
    const [formError, setFormError] = useState<string | null>(null);
    const [notice, setNotice] = useState<string | null>(null);
    const [createdCategoryId, setCreatedCategoryId] = useState<string | null>(null);

    useEffect(() => setDraft(state.q), [state.q]);
    const replaceState = useCallback(
        (next: CategoryListState) =>
            setSearchParams(writeCategoryListState(next), { replace: true }),
        [setSearchParams],
    );
    useEffect(() => {
        if (draft.trim() === state.q) return;
        const timeout = window.setTimeout(() => {
            const next = { ...state, q: draft.trim(), direction: 'forward' as const };
            delete next.cursor;
            replaceState(next);
        }, 300);
        return () => window.clearTimeout(timeout);
    }, [draft, replaceState, state]);

    const filter = {
        ...(state.q ? { name: state.q } : {}),
        ...(state.status ? { status: state.status } : {}),
    };
    const variables: AdminCategoriesQueryVariables =
        state.direction === 'backward' && state.cursor
            ? { filter, last: state.pageSize, before: state.cursor }
            : { filter, first: state.pageSize, ...(state.cursor ? { after: state.cursor } : {}) };
    const query = useQuery({
        queryKey: ['admin-categories', variables],
        queryFn: ({ signal }) =>
            graphQlRequest<AdminCategoriesQuery, AdminCategoriesQueryVariables>(
                AdminCategoriesDocument,
                variables,
                token!,
                signal,
            ),
        enabled: Boolean(token),
        placeholderData: keepPreviousData,
    });
    const hierarchy = useQuery({
        queryKey: ['product-editor-options'],
        queryFn: ({ signal }) =>
            graphQlRequest<ProductEditorOptionsQuery, ProductEditorOptionsQueryVariables>(
                ProductEditorOptionsDocument,
                { brandPage: 1, brandPageSize: 100 },
                token!,
                signal,
            ),
        enabled: Boolean(token),
        staleTime: 5 * 60_000,
    });

    const completed = async (message: string) => {
        await queryClient.invalidateQueries({ queryKey: ['admin-categories'] });
        await queryClient.invalidateQueries({ queryKey: ['product-editor-options'] });
        setEditor(null);
        setConfirmation(null);
        setFormError(null);
        setNotice(message);
    };
    const create = useMutation({
        mutationFn: () =>
            graphQlRequest<CreateAdminCategoryMutation, CreateAdminCategoryMutationVariables>(
                CreateAdminCategoryDocument,
                {
                    input: {
                        name: name.trim(),
                        slug: slug.trim(),
                        description: description.trim() || null,
                        parentId: parentId || null,
                        displayOrder: Number(displayOrder) || 0,
                    },
                },
                token!,
            ),
        onSuccess: async (result) => {
            setCreatedCategoryId(result.createCategory);
            await completed('Category created. Assign its product attributes next.');
        },
        onError: (error) => setFormError(toAppError(error).message),
    });
    const update = useMutation({
        mutationFn: () =>
            graphQlRequest<UpdateAdminCategoryMutation, UpdateAdminCategoryMutationVariables>(
                UpdateAdminCategoryDocument,
                {
                    input: {
                        categoryId: editor!.category!.id,
                        name: name.trim(),
                        slug: slug.trim(),
                        description: description.trim() || null,
                        displayOrder: Number(displayOrder) || 0,
                        status: editor!.category!.status,
                    },
                },
                token!,
            ),
        onSuccess: () => completed('Category updated.'),
        onError: (error) => setFormError(toAppError(error).message),
    });
    const move = useMutation({
        mutationFn: () =>
            graphQlRequest<MoveAdminCategoryMutation, MoveAdminCategoryMutationVariables>(
                MoveAdminCategoryDocument,
                {
                    input: {
                        categoryId: editor!.category!.id,
                        parentId: parentId || null,
                        displayOrder: Number(displayOrder) || 0,
                    },
                },
                token!,
            ),
        onSuccess: () => completed('Category moved.'),
        onError: (error) => setFormError(toAppError(error).message),
    });
    const setStatus = useMutation({
        mutationFn: (category: CategoryRow) =>
            graphQlRequest<SetAdminCategoryStatusMutation, SetAdminCategoryStatusMutationVariables>(
                SetAdminCategoryStatusDocument,
                {
                    input: {
                        categoryId: category.id,
                        status: category.status === 'ACTIVE' ? 'INACTIVE' : 'ACTIVE',
                    },
                },
                token!,
            ),
        onSuccess: () =>
            completed(
                confirmation?.category.status === 'ACTIVE'
                    ? 'Category deactivated.'
                    : 'Category activated.',
            ),
    });
    const remove = useMutation({
        mutationFn: (category: CategoryRow) =>
            graphQlRequest<DeleteAdminCategoryMutation, DeleteAdminCategoryMutationVariables>(
                DeleteAdminCategoryDocument,
                { input: { categoryId: category.id } },
                token!,
            ),
        onSuccess: () => completed('Category deleted.'),
    });

    const openEditor = (next: Editor) => {
        setEditor(next);
        setFormError(null);
        setName(next.category?.name ?? '');
        setSlug(next.category?.slug ?? '');
        setDescription(next.category?.description ?? '');
        setParentId(next.category?.parentId ?? '');
        setDisplayOrder(String(next.category?.displayOrder ?? 0));
    };
    const submit = (event: FormEvent) => {
        event.preventDefault();
        setFormError(null);
        if (editor?.mode !== 'move' && (!name.trim() || !slug.trim()))
            return setFormError('Name and slug are required.');
        if (editor?.mode === 'create') create.mutate();
        if (editor?.mode === 'edit') update.mutate();
        if (editor?.mode === 'move') move.mutate();
    };
    const connection = query.data?.categories;
    const categories = connection?.nodes ?? [];
    const pageInfo = connection?.pageInfo;
    const options = hierarchy.data ? flattenCategories(hierarchy.data.categoryHierarchy) : [];
    const editorPending = create.isPending || update.isPending || move.isPending;
    const confirmationPending = setStatus.isPending || remove.isPending;

    return (
        <section aria-labelledby="categories-title" className="page-stack">
            <div className="page-heading">
                <div>
                    <p className="eyebrow">Catalog</p>
                    <h1 id="categories-title">Categories</h1>
                    <p>Browse the hierarchy and manage category placement and lifecycle.</p>
                </div>
                <Button onClick={() => openEditor({ mode: 'create' })}>
                    <Plus size={16} />
                    Create category
                </Button>
            </div>
            {notice && (
                <div className="success-panel" role="status">
                    <span>{notice}</span>
                    <div>
                        {createdCategoryId && (
                            <Button asChild size="compact" variant="secondary">
                                <Link to={`/catalog/categories/${createdCategoryId}/attributes`}>
                                    Assign attributes
                                </Link>
                            </Button>
                        )}
                        <Button
                            onClick={() => {
                                setNotice(null);
                                setCreatedCategoryId(null);
                            }}
                            size="compact"
                            variant="ghost"
                        >
                            Dismiss
                        </Button>
                    </div>
                </div>
            )}
            {hierarchy.data && (
                <details className="panel hierarchy-panel">
                    <summary>Category hierarchy</summary>
                    <Tree nodes={hierarchy.data.categoryHierarchy} />
                </details>
            )}
            <div className="panel filter-panel">
                <div className="filters" role="search">
                    <label className="search-field">
                        <span>Search categories</span>
                        <span className="input-with-icon">
                            <Search aria-hidden="true" size={17} />
                            <input
                                onChange={(event) => setDraft(event.target.value)}
                                placeholder="Category name"
                                value={draft}
                            />
                        </span>
                    </label>
                    <label>
                        <span>Status</span>
                        <select
                            onChange={(event) => {
                                const next = { ...state, direction: 'forward' as const };
                                if (event.target.value)
                                    next.status = event.target.value as CategoryStatus;
                                else delete next.status;
                                delete next.cursor;
                                replaceState(next);
                            }}
                            value={state.status ?? ''}
                        >
                            <option value="">All statuses</option>
                            <option value="ACTIVE">Active</option>
                            <option value="INACTIVE">Inactive</option>
                            <option value="DELETED">Deleted</option>
                        </select>
                    </label>
                    {(state.q || state.status) && (
                        <Button
                            onClick={() => {
                                setDraft('');
                                setSearchParams({});
                            }}
                            variant="ghost"
                        >
                            Clear
                        </Button>
                    )}
                </div>
            </div>
            {query.isPending ? (
                <PageLoading label="Loading categories" />
            ) : query.isError ? (
                <ErrorState error={query.error} retry={() => void query.refetch()} />
            ) : !categories.length ? (
                <EmptyState title="No categories found">
                    Try a broader search or create a category.
                </EmptyState>
            ) : (
                <div className="panel table-panel" aria-busy={query.isFetching}>
                    <div className="table-summary">
                        <strong>{connection?.totalCount.toLocaleString()} categories</strong>
                        {query.isFetching && <span>Updating…</span>}
                    </div>
                    <div
                        className="table-scroll"
                        role="region"
                        aria-label="Categories table"
                        tabIndex={0}
                    >
                        <table>
                            <thead>
                                <tr>
                                    <th scope="col">Category</th>
                                    <th scope="col">Parent</th>
                                    <th scope="col">Status</th>
                                    <th scope="col">Children</th>
                                    <th scope="col">Order</th>
                                    <th scope="col">
                                        <span className="sr-only">Actions</span>
                                    </th>
                                </tr>
                            </thead>
                            <tbody>
                                {categories.map((category) => (
                                    <tr key={category.id}>
                                        <th scope="row">
                                            <strong>{category.name}</strong>
                                            <span className="cell-subtitle">/{category.slug}</span>
                                        </th>
                                        <td>{category.parent?.name ?? 'Root'}</td>
                                        <td>
                                            <StatusBadge
                                                status={
                                                    category.status.charAt(0) +
                                                    category.status.slice(1).toLowerCase()
                                                }
                                            />
                                        </td>
                                        <td>{category.childCount}</td>
                                        <td>{category.displayOrder}</td>
                                        <td>
                                            <div className="row-actions">
                                                <Button
                                                    asChild
                                                    size="compact"
                                                    title="Manage attributes"
                                                    variant="ghost"
                                                >
                                                    <Link
                                                        to={`/catalog/categories/${category.id}/attributes`}
                                                    >
                                                        <ListChecks aria-hidden="true" size={15} />
                                                        Attributes
                                                    </Link>
                                                </Button>
                                                <Button
                                                    aria-label={`Edit ${category.name}`}
                                                    onClick={() =>
                                                        openEditor({ mode: 'edit', category })
                                                    }
                                                    size="icon"
                                                    variant="ghost"
                                                >
                                                    <Pencil size={15} />
                                                </Button>
                                                <Button
                                                    aria-label={`Move ${category.name}`}
                                                    onClick={() =>
                                                        openEditor({ mode: 'move', category })
                                                    }
                                                    size="icon"
                                                    variant="ghost"
                                                >
                                                    <Move size={15} />
                                                </Button>
                                                {category.status !== 'DELETED' && (
                                                    <>
                                                        <Button
                                                            onClick={() =>
                                                                setConfirmation({
                                                                    kind: 'status',
                                                                    category,
                                                                })
                                                            }
                                                            size="compact"
                                                            variant="ghost"
                                                        >
                                                            {category.status === 'ACTIVE'
                                                                ? 'Deactivate'
                                                                : 'Activate'}
                                                        </Button>
                                                        <Button
                                                            aria-label={`Delete ${category.name}`}
                                                            onClick={() =>
                                                                setConfirmation({
                                                                    kind: 'delete',
                                                                    category,
                                                                })
                                                            }
                                                            size="icon"
                                                            variant="ghost"
                                                        >
                                                            <Trash2 size={15} />
                                                        </Button>
                                                    </>
                                                )}
                                            </div>
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                    <CursorPagination
                        hasNextPage={Boolean(pageInfo?.hasNextPage && pageInfo.endCursor)}
                        hasPreviousPage={Boolean(pageInfo?.hasPreviousPage && pageInfo.startCursor)}
                        itemLabel="Categories"
                        onNextPage={() =>
                            pageInfo?.endCursor &&
                            replaceState({
                                ...state,
                                cursor: pageInfo.endCursor,
                                direction: 'forward',
                            })
                        }
                        onPageSizeChange={(nextPageSize) => {
                            const next = {
                                ...state,
                                pageSize: nextPageSize,
                                direction: 'forward' as const,
                            };
                            delete next.cursor;
                            replaceState(next);
                        }}
                        onPreviousPage={() =>
                            pageInfo?.startCursor &&
                            replaceState({
                                ...state,
                                cursor: pageInfo.startCursor,
                                direction: 'backward',
                            })
                        }
                        pageSize={state.pageSize}
                    />
                </div>
            )}

            <Dialog.Root onOpenChange={(open) => !open && setEditor(null)} open={Boolean(editor)}>
                <Dialog.Portal>
                    <Dialog.Overlay className="dialog-overlay" />
                    <Dialog.Content
                        aria-describedby="category-dialog-description"
                        className="dialog-panel dialog-panel--wide"
                    >
                        <div className="dialog-heading">
                            <Dialog.Title>
                                {editor?.mode === 'create'
                                    ? 'Create category'
                                    : editor?.mode === 'edit'
                                      ? 'Edit category'
                                      : 'Move category'}
                            </Dialog.Title>
                            <Dialog.Close asChild>
                                <Button aria-label="Close dialog" size="icon" variant="ghost">
                                    <X />
                                </Button>
                            </Dialog.Close>
                        </div>
                        <Dialog.Description id="category-dialog-description">
                            Hierarchy rules and cycle prevention are enforced by the backend.
                        </Dialog.Description>
                        <form className="dialog-form" onSubmit={submit}>
                            {editor?.mode !== 'move' && (
                                <>
                                    <label className="field">
                                        <span>Name *</span>
                                        <input
                                            autoFocus
                                            onChange={(event) => setName(event.target.value)}
                                            value={name}
                                        />
                                    </label>
                                    <label className="field">
                                        <span>Slug *</span>
                                        <input
                                            onChange={(event) => setSlug(event.target.value)}
                                            value={slug}
                                        />
                                    </label>
                                    <label className="field">
                                        <span>Description</span>
                                        <textarea
                                            onChange={(event) => setDescription(event.target.value)}
                                            rows={4}
                                            value={description}
                                        />
                                    </label>
                                </>
                            )}
                            {editor?.mode !== 'edit' && (
                                <label className="field">
                                    <span>Parent category</span>
                                    <select
                                        onChange={(event) => setParentId(event.target.value)}
                                        value={parentId}
                                    >
                                        <option value="">Root category</option>
                                        {options
                                            .filter((option) => option.id !== editor?.category?.id)
                                            .map((option) => (
                                                <option key={option.id} value={option.id}>
                                                    {option.label}
                                                </option>
                                            ))}
                                    </select>
                                </label>
                            )}
                            <label className="field">
                                <span>Display order</span>
                                <input
                                    min={0}
                                    onChange={(event) => setDisplayOrder(event.target.value)}
                                    type="number"
                                    value={displayOrder}
                                />
                            </label>
                            {formError && (
                                <div className="dialog-error" role="alert">
                                    {formError}
                                </div>
                            )}
                            <div className="dialog-actions">
                                <Dialog.Close asChild>
                                    <Button type="button" variant="secondary">
                                        Cancel
                                    </Button>
                                </Dialog.Close>
                                <Button disabled={editorPending} type="submit">
                                    {editorPending ? 'Saving…' : 'Save'}
                                </Button>
                            </div>
                        </form>
                    </Dialog.Content>
                </Dialog.Portal>
            </Dialog.Root>
            <ConfirmationDialog
                confirmLabel={
                    confirmation?.kind === 'delete'
                        ? 'Delete category'
                        : confirmation?.category.status === 'ACTIVE'
                          ? 'Deactivate category'
                          : 'Activate category'
                }
                description={
                    confirmation?.kind === 'delete'
                        ? `Delete ${confirmation.category.name}? The backend will refuse deletion when protected relationships still exist.`
                        : `Change the status of ${confirmation?.category.name ?? 'this category'}?`
                }
                destructive={
                    confirmation?.kind === 'delete' || confirmation?.category.status === 'ACTIVE'
                }
                onConfirm={() => {
                    if (!confirmation) return;
                    if (confirmation.kind === 'delete') remove.mutate(confirmation.category);
                    else setStatus.mutate(confirmation.category);
                }}
                onOpenChange={(open) => !open && setConfirmation(null)}
                open={Boolean(confirmation)}
                pending={confirmationPending}
                title={
                    confirmation?.kind === 'delete'
                        ? 'Delete this category?'
                        : 'Change category status?'
                }
            />
            {(setStatus.error || remove.error) && (
                <div className="dialog-error floating-error" role="alert">
                    {toAppError(setStatus.error || remove.error).message}
                </div>
            )}
        </section>
    );
}

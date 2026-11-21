import * as Dialog from '@radix-ui/react-dialog';
import {
    AddAdminProductIdentifierDocument,
    type AddAdminProductIdentifierMutation,
    type AddAdminProductIdentifierMutationVariables,
    AddAdminVariantDocument,
    type AddAdminVariantMutation,
    type AddAdminVariantMutationVariables,
    type ProductDetailFieldsFragment,
    type ProductIdentifierType,
    RemoveAdminProductIdentifierDocument,
    type RemoveAdminProductIdentifierMutation,
    type RemoveAdminProductIdentifierMutationVariables,
    RemoveAdminProductMediaDocument,
    type RemoveAdminProductMediaMutation,
    type RemoveAdminProductMediaMutationVariables,
    SetAdminVariantActiveDocument,
    type SetAdminVariantActiveMutation,
    type SetAdminVariantActiveMutationVariables,
    ProductCategoryAttributeRulesDocument,
    type ProductCategoryAttributeRulesQuery,
    type ProductCategoryAttributeRulesQueryVariables,
} from '@mkbj/api-graphql';
import { Button } from '@mkbj/ui';
import { useMutation, useQuery } from '@tanstack/react-query';
import { ImageOff, Plus, Trash2, X } from 'lucide-react';
import { type FormEvent, type ReactNode, useState } from 'react';
import { graphQlRequest } from '../../api/graphql';
import { toAppError } from '../../api/errors';
import { uploadProductMedia, type ProductMediaUpload } from '../../api/rest';
import { useAuth } from '../../auth/auth-context';
import { ConfirmationDialog } from '../../components/confirmation-dialog';
import { StatusBadge } from '../../components/status-badge';

interface ProductResourcesProps {
    product: ProductDetailFieldsFragment;
    onUpdated: (product: ProductDetailFieldsFragment, notice: string) => void;
    onRefresh: (notice: string) => Promise<void>;
}

type AttributeRow = { id: number; key: string; value: string };
type Confirmation =
    | { kind: 'variant'; id: string; label: string; active: boolean }
    | { kind: 'identifier'; id: string; label: string }
    | { kind: 'media'; id: string; label: string };

function ResourceDialog({
    open,
    onOpenChange,
    title,
    description,
    children,
}: {
    open: boolean;
    onOpenChange: (open: boolean) => void;
    title: string;
    description: string;
    children: ReactNode;
}) {
    return (
        <Dialog.Root onOpenChange={onOpenChange} open={open}>
            <Dialog.Portal>
                <Dialog.Overlay className="dialog-overlay" />
                <Dialog.Content
                    aria-describedby="resource-dialog-description"
                    className="dialog-panel dialog-panel--wide"
                >
                    <div className="dialog-heading">
                        <Dialog.Title>{title}</Dialog.Title>
                        <Dialog.Close asChild>
                            <Button aria-label="Close dialog" size="icon" variant="ghost">
                                <X size={18} />
                            </Button>
                        </Dialog.Close>
                    </div>
                    <Dialog.Description id="resource-dialog-description">
                        {description}
                    </Dialog.Description>
                    {children}
                </Dialog.Content>
            </Dialog.Portal>
        </Dialog.Root>
    );
}

export function ProductResources({ product, onUpdated, onRefresh }: ProductResourcesProps) {
    const { token } = useAuth();
    const [variantOpen, setVariantOpen] = useState(false);
    const [identifierOpen, setIdentifierOpen] = useState(false);
    const [mediaOpen, setMediaOpen] = useState(false);
    const [sku, setSku] = useState('');
    const [attributes, setAttributes] = useState<AttributeRow[]>([]);
    const [identifierType, setIdentifierType] = useState<ProductIdentifierType>('GTIN');
    const [identifierValue, setIdentifierValue] = useState('');
    const [sourceDomain, setSourceDomain] = useState('');
    const [mediaFile, setMediaFile] = useState<File | null>(null);
    const [mediaAlt, setMediaAlt] = useState('');
    const [mediaRights, setMediaRights] = useState<ProductMediaUpload['rights']>('RightsUnknown');
    const [confirmation, setConfirmation] = useState<Confirmation | null>(null);
    const [formError, setFormError] = useState<string | null>(null);
    const categoryAttributes = useQuery({
        queryKey: ['product-category-attribute-rules', product.primaryCategoryId],
        queryFn: ({ signal }) =>
            graphQlRequest<
                ProductCategoryAttributeRulesQuery,
                ProductCategoryAttributeRulesQueryVariables
            >(
                ProductCategoryAttributeRulesDocument,
                { categoryId: product.primaryCategoryId, attributePage: 1, attributePageSize: 200 },
                token!,
                signal,
            ),
        enabled: Boolean(token && product.primaryCategoryId),
        staleTime: 60_000,
    });
    const blueprintById = new Map(
        categoryAttributes.data?.attributeBlueprints.items.map((blueprint) => [
            blueprint.id,
            blueprint,
        ]) ?? [],
    );
    const variantRules =
        categoryAttributes.data?.effectiveCategoryAttributes
            .filter((rule) => rule.isVariantGenerating && !rule.isExcluded)
            .map((rule) => ({ ...rule, blueprint: blueprintById.get(rule.attributeBlueprintId) }))
            .filter((rule) => rule.blueprint) ?? [];
    const openVariant = () => {
        setFormError(null);
        setAttributes(
            variantRules.map((rule, index) => ({
                id: index + 1,
                key: rule.blueprint!.canonicalKey,
                value: '',
            })),
        );
        setVariantOpen(true);
    };

    const variantAdd = useMutation({
        mutationFn: () =>
            graphQlRequest<AddAdminVariantMutation, AddAdminVariantMutationVariables>(
                AddAdminVariantDocument,
                {
                    input: {
                        productId: product.id,
                        sku: sku.trim(),
                        attributes: attributes
                            .filter((row) => row.value.trim())
                            .map((row) => ({ key: row.key.trim(), value: row.value.trim() })),
                    },
                },
                token!,
            ),
        onSuccess: (result) => {
            onUpdated(result.addVariant, 'Variant added.');
            setVariantOpen(false);
            setSku('');
            setAttributes([]);
            setFormError(null);
        },
        onError: (error) => setFormError(toAppError(error).message),
    });
    const variantStatus = useMutation({
        mutationFn: (item: Extract<Confirmation, { kind: 'variant' }>) =>
            graphQlRequest<SetAdminVariantActiveMutation, SetAdminVariantActiveMutationVariables>(
                SetAdminVariantActiveDocument,
                { input: { productId: product.id, variantId: item.id, active: !item.active } },
                token!,
            ),
        onSuccess: (result) => {
            onUpdated(result.setVariantActive, 'Variant status changed.');
            setConfirmation(null);
        },
    });
    const identifierAdd = useMutation({
        mutationFn: () =>
            graphQlRequest<
                AddAdminProductIdentifierMutation,
                AddAdminProductIdentifierMutationVariables
            >(
                AddAdminProductIdentifierDocument,
                {
                    input: {
                        productId: product.id,
                        type: identifierType,
                        value: identifierValue.trim(),
                        sourceDomain: identifierType === 'SOURCE_ID' ? sourceDomain.trim() : null,
                    },
                },
                token!,
            ),
        onSuccess: (result) => {
            onUpdated(result.addProductIdentifier, 'Identifier added.');
            setIdentifierOpen(false);
            setIdentifierValue('');
            setSourceDomain('');
            setFormError(null);
        },
        onError: (error) => setFormError(toAppError(error).message),
    });
    const identifierRemove = useMutation({
        mutationFn: (item: Extract<Confirmation, { kind: 'identifier' }>) =>
            graphQlRequest<
                RemoveAdminProductIdentifierMutation,
                RemoveAdminProductIdentifierMutationVariables
            >(
                RemoveAdminProductIdentifierDocument,
                { input: { productId: product.id, identifierId: item.id } },
                token!,
            ),
        onSuccess: (result) => {
            onUpdated(result.removeProductIdentifier, 'Identifier removed.');
            setConfirmation(null);
        },
    });
    const mediaRemove = useMutation({
        mutationFn: (item: Extract<Confirmation, { kind: 'media' }>) =>
            graphQlRequest<
                RemoveAdminProductMediaMutation,
                RemoveAdminProductMediaMutationVariables
            >(
                RemoveAdminProductMediaDocument,
                { input: { productId: product.id, mediaId: item.id } },
                token!,
            ),
        onSuccess: (result) => {
            onUpdated(result.removeProductMedia, 'Image removed.');
            setConfirmation(null);
        },
    });
    const mediaUpload = useMutation({
        mutationFn: () =>
            uploadProductMedia(
                product.id,
                { file: mediaFile!, alt: mediaAlt, rights: mediaRights },
                token!,
            ),
        onSuccess: async () => {
            await onRefresh('Image uploaded.');
            setMediaOpen(false);
            setMediaFile(null);
            setMediaAlt('');
            setMediaRights('RightsUnknown');
            setFormError(null);
        },
        onError: (error) => setFormError(toAppError(error).message),
    });

    const submitVariant = (event: FormEvent) => {
        event.preventDefault();
        const normalizedSku = sku.trim().toUpperCase();
        if (!/^[A-Z0-9-]{3,32}$/.test(normalizedSku))
            return setFormError('SKU must be 3–32 letters, numbers or hyphens.');
        const missingRequired = variantRules.find(
            (rule) =>
                rule.isRequired &&
                !attributes.find((row) => row.key === rule.blueprint!.canonicalKey)?.value.trim(),
        );
        if (missingRequired)
            return setFormError(
                `${missingRequired.attributeBlueprintName} is required by this category.`,
            );
        const completedAttributes = attributes.filter((row) => row.value.trim());
        const keys = completedAttributes.map((row) => row.key.trim().toLowerCase());
        if (new Set(keys).size !== keys.length)
            return setFormError('Each attribute key can be used only once.');
        setSku(normalizedSku);
        setFormError(null);
        variantAdd.mutate();
    };
    const submitIdentifier = (event: FormEvent) => {
        event.preventDefault();
        if (!identifierValue.trim()) return setFormError('Enter an identifier value.');
        if (identifierType === 'SOURCE_ID' && !sourceDomain.trim())
            return setFormError('A source ID requires its source domain.');
        setFormError(null);
        identifierAdd.mutate();
    };
    const submitMedia = (event: FormEvent) => {
        event.preventDefault();
        if (!mediaFile) return setFormError('Choose a JPEG, PNG or WebP image.');
        setFormError(null);
        mediaUpload.mutate();
    };
    const confirmAction = () => {
        if (!confirmation) return;
        if (confirmation.kind === 'variant') variantStatus.mutate(confirmation);
        if (confirmation.kind === 'identifier') identifierRemove.mutate(confirmation);
        if (confirmation.kind === 'media') mediaRemove.mutate(confirmation);
    };
    const confirmationPending =
        variantStatus.isPending || identifierRemove.isPending || mediaRemove.isPending;
    const confirmationError = variantStatus.error || identifierRemove.error || mediaRemove.error;

    return (
        <>
            <section className="panel detail-section">
                <div className="section-heading">
                    <div>
                        <h2>Variants</h2>
                        <span>{product.variants.length}</span>
                    </div>
                    <Button
                        disabled={categoryAttributes.isPending}
                        onClick={openVariant}
                        size="compact"
                        variant="secondary"
                    >
                        <Plus size={15} />
                        Add variant
                    </Button>
                </div>
                {categoryAttributes.isError && (
                    <div className="dialog-error" role="alert">
                        Category attribute rules could not be loaded. Retry before creating a
                        variant.
                    </div>
                )}
                {variantRules.length > 0 && (
                    <p className="attribute-note">
                        This category defines {variantRules.length} variant-generating{' '}
                        {variantRules.length === 1 ? 'attribute' : 'attributes'}. New variants must
                        use those canonical keys.
                    </p>
                )}
                {product.variants.length === 0 ? (
                    <p className="muted">No variants have been added.</p>
                ) : (
                    <div
                        className="table-scroll"
                        role="region"
                        aria-label="Product variants"
                        tabIndex={0}
                    >
                        <table>
                            <thead>
                                <tr>
                                    <th scope="col">SKU</th>
                                    <th scope="col">Attributes</th>
                                    <th scope="col">Status</th>
                                    <th scope="col">
                                        <span className="sr-only">Actions</span>
                                    </th>
                                </tr>
                            </thead>
                            <tbody>
                                {product.variants.map((variant) => (
                                    <tr key={variant.id}>
                                        <th scope="row">{variant.sku}</th>
                                        <td>
                                            {variant.attributeValues.length
                                                ? variant.attributeValues
                                                      .map(({ key, value }) => `${key}: ${value}`)
                                                      .join(' · ')
                                                : variant.attributeSignature || '—'}
                                        </td>
                                        <td>
                                            <StatusBadge
                                                status={variant.isActive ? 'Active' : 'Inactive'}
                                            />
                                        </td>
                                        <td>
                                            <Button
                                                onClick={() =>
                                                    setConfirmation({
                                                        kind: 'variant',
                                                        id: variant.id,
                                                        label: variant.sku,
                                                        active: variant.isActive,
                                                    })
                                                }
                                                size="compact"
                                                variant="ghost"
                                            >
                                                {variant.isActive ? 'Deactivate' : 'Activate'}
                                            </Button>
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                )}
            </section>

            <section className="panel detail-section">
                <div className="section-heading">
                    <div>
                        <h2>Identifiers</h2>
                        <span>{product.identifiers.length}</span>
                    </div>
                    <Button
                        onClick={() => {
                            setFormError(null);
                            setIdentifierOpen(true);
                        }}
                        size="compact"
                        variant="secondary"
                    >
                        <Plus size={15} />
                        Add identifier
                    </Button>
                </div>
                {product.identifiers.length === 0 ? (
                    <p className="muted">No identifiers have been added.</p>
                ) : (
                    <ul className="plain-list">
                        {product.identifiers.map((identifier) => (
                            <li key={identifier.id}>
                                <strong>{identifier.type}</strong>
                                <span>{identifier.value}</span>
                                {identifier.sourceDomain && (
                                    <small>{identifier.sourceDomain}</small>
                                )}
                                <Button
                                    aria-label={`Remove ${identifier.type} ${identifier.value}`}
                                    onClick={() =>
                                        setConfirmation({
                                            kind: 'identifier',
                                            id: identifier.id,
                                            label: `${identifier.type} ${identifier.value}`,
                                        })
                                    }
                                    size="compact"
                                    variant="ghost"
                                >
                                    <Trash2 size={15} />
                                </Button>
                            </li>
                        ))}
                    </ul>
                )}
            </section>

            <section className="panel detail-section">
                <div className="section-heading">
                    <div>
                        <h2>Media</h2>
                        <span>{product.media.length}</span>
                    </div>
                    <Button
                        onClick={() => {
                            setFormError(null);
                            setMediaOpen(true);
                        }}
                        size="compact"
                        variant="secondary"
                    >
                        <Plus size={15} />
                        Upload
                    </Button>
                </div>
                {product.media.length === 0 ? (
                    <div className="media-empty">
                        <ImageOff aria-hidden="true" />
                        <span>No product media</span>
                    </div>
                ) : (
                    <div className="media-grid">
                        {product.media.map((media) => (
                            <figure key={media.id}>
                                <div className="media-image">
                                    <img alt={media.alt || ''} src={media.url} />
                                    <Button
                                        aria-label={`Remove ${media.alt || 'product image'}`}
                                        onClick={() =>
                                            setConfirmation({
                                                kind: 'media',
                                                id: media.id,
                                                label: media.alt || 'this image',
                                            })
                                        }
                                        size="compact"
                                        variant="destructive"
                                    >
                                        <Trash2 size={14} />
                                    </Button>
                                </div>
                                <figcaption>
                                    {media.alt || 'Product image'} ·{' '}
                                    {media.usageRights.replaceAll('_', ' ').toLowerCase()}
                                </figcaption>
                            </figure>
                        ))}
                    </div>
                )}
            </section>

            <ResourceDialog
                description="A variant SKU is permanent after creation. Category rules supply the attribute keys that form its unique signature."
                onOpenChange={setVariantOpen}
                open={variantOpen}
                title="Add variant"
            >
                <form className="dialog-form" onSubmit={submitVariant}>
                    <label className="field">
                        <span>SKU *</span>
                        <input
                            autoFocus
                            onChange={(event) => setSku(event.target.value)}
                            placeholder="ABC-123"
                            value={sku}
                        />
                    </label>
                    {variantRules.length === 0 ? (
                        <p className="attribute-note">
                            This category has no variant-generating attributes. Create one default
                            variant with no attributes.
                        </p>
                    ) : (
                        variantRules.map((rule) => {
                            const row = attributes.find(
                                (item) => item.key === rule.blueprint!.canonicalKey,
                            );
                            const options = rule.blueprint!.options;
                            return (
                                <label className="field" key={rule.attributeBlueprintId}>
                                    <span>
                                        {rule.attributeBlueprintName}
                                        {rule.isRequired ? ' *' : ''}
                                        <small>{rule.isInherited ? 'Inherited' : 'Category'}</small>
                                    </span>
                                    {options.length ? (
                                        <select
                                            onChange={(event) =>
                                                setAttributes((current) =>
                                                    current.map((item) =>
                                                        item.key === rule.blueprint!.canonicalKey
                                                            ? { ...item, value: event.target.value }
                                                            : item,
                                                    ),
                                                )
                                            }
                                            required={rule.isRequired}
                                            value={row?.value ?? ''}
                                        >
                                            <option value="">
                                                Select {rule.attributeBlueprintName.toLowerCase()}
                                            </option>
                                            {options.map((option) => (
                                                <option key={option.id} value={option.value}>
                                                    {option.value}
                                                </option>
                                            ))}
                                        </select>
                                    ) : (
                                        <input
                                            onChange={(event) =>
                                                setAttributes((current) =>
                                                    current.map((item) =>
                                                        item.key === rule.blueprint!.canonicalKey
                                                            ? { ...item, value: event.target.value }
                                                            : item,
                                                    ),
                                                )
                                            }
                                            placeholder={rule.blueprint!.dataType}
                                            required={rule.isRequired}
                                            value={row?.value ?? ''}
                                        />
                                    )}
                                    <span className="field-meta">
                                        Stored as {rule.blueprint!.canonicalKey}
                                    </span>
                                </label>
                            );
                        })
                    )}
                    {formError && (
                        <div className="dialog-error" role="alert">
                            {formError}
                        </div>
                    )}
                    <div className="dialog-actions">
                        <Button
                            onClick={() => setVariantOpen(false)}
                            type="button"
                            variant="secondary"
                        >
                            Cancel
                        </Button>
                        <Button disabled={variantAdd.isPending} type="submit">
                            {variantAdd.isPending ? 'Adding…' : 'Add variant'}
                        </Button>
                    </div>
                </form>
            </ResourceDialog>

            <ResourceDialog
                description="The backend validates formats and check digits, and normalizes the stored value."
                onOpenChange={setIdentifierOpen}
                open={identifierOpen}
                title="Add identifier"
            >
                <form className="dialog-form" onSubmit={submitIdentifier}>
                    <label className="field">
                        <span>Type *</span>
                        <select
                            onChange={(event) =>
                                setIdentifierType(event.target.value as ProductIdentifierType)
                            }
                            value={identifierType}
                        >
                            <option value="GTIN">GTIN</option>
                            <option value="UPC">UPC</option>
                            <option value="EAN">EAN</option>
                            <option value="MPN">MPN</option>
                            <option value="SOURCE_ID">Source ID</option>
                        </select>
                    </label>
                    <label className="field">
                        <span>Value *</span>
                        <input
                            onChange={(event) => setIdentifierValue(event.target.value)}
                            value={identifierValue}
                        />
                    </label>
                    {identifierType === 'SOURCE_ID' && (
                        <label className="field">
                            <span>Source domain *</span>
                            <input
                                onChange={(event) => setSourceDomain(event.target.value)}
                                placeholder="supplier.example"
                                value={sourceDomain}
                            />
                        </label>
                    )}
                    {formError && (
                        <div className="dialog-error" role="alert">
                            {formError}
                        </div>
                    )}
                    <div className="dialog-actions">
                        <Button
                            onClick={() => setIdentifierOpen(false)}
                            type="button"
                            variant="secondary"
                        >
                            Cancel
                        </Button>
                        <Button disabled={identifierAdd.isPending} type="submit">
                            {identifierAdd.isPending ? 'Adding…' : 'Add identifier'}
                        </Button>
                    </div>
                </form>
            </ResourceDialog>

            <ResourceDialog
                description="The backend accepts JPEG, PNG and WebP files and verifies the actual file bytes."
                onOpenChange={setMediaOpen}
                open={mediaOpen}
                title="Upload product image"
            >
                <form className="dialog-form" onSubmit={submitMedia}>
                    <label className="field">
                        <span>Image file *</span>
                        <input
                            accept="image/jpeg,image/png,image/webp"
                            onChange={(event) => setMediaFile(event.target.files?.[0] ?? null)}
                            type="file"
                        />
                    </label>
                    <label className="field">
                        <span>Alternative text</span>
                        <input
                            onChange={(event) => setMediaAlt(event.target.value)}
                            placeholder="Describe the image"
                            value={mediaAlt}
                        />
                    </label>
                    <label className="field">
                        <span>Usage rights *</span>
                        <select
                            onChange={(event) =>
                                setMediaRights(event.target.value as ProductMediaUpload['rights'])
                            }
                            value={mediaRights}
                        >
                            <option value="RightsUnknown">Not confirmed</option>
                            <option value="Allowed">Allowed for publication</option>
                            <option value="Denied">Publication denied</option>
                        </select>
                        <span className="field-meta">
                            Only an allowed image can satisfy product activation.
                        </span>
                    </label>
                    {formError && (
                        <div className="dialog-error" role="alert">
                            {formError}
                        </div>
                    )}
                    <div className="dialog-actions">
                        <Button
                            onClick={() => setMediaOpen(false)}
                            type="button"
                            variant="secondary"
                        >
                            Cancel
                        </Button>
                        <Button disabled={mediaUpload.isPending} type="submit">
                            {mediaUpload.isPending ? 'Uploading…' : 'Upload image'}
                        </Button>
                    </div>
                </form>
            </ResourceDialog>

            <ConfirmationDialog
                confirmLabel={
                    confirmation?.kind === 'variant'
                        ? confirmation.active
                            ? 'Deactivate variant'
                            : 'Activate variant'
                        : 'Remove'
                }
                description={
                    confirmation?.kind === 'variant'
                        ? `Change the status of ${confirmation.label}? Product status may also change when its last active variant is deactivated.`
                        : `Remove ${confirmation?.label ?? 'this item'} from the product? This action takes effect immediately.`
                }
                destructive={confirmation?.kind !== 'variant' || confirmation.active}
                onConfirm={confirmAction}
                onOpenChange={(open) => !open && setConfirmation(null)}
                open={Boolean(confirmation)}
                pending={confirmationPending}
                title={
                    confirmation?.kind === 'variant'
                        ? 'Change variant status?'
                        : 'Remove this item?'
                }
            />
            {confirmationError && (
                <div className="dialog-error floating-error" role="alert">
                    {toAppError(confirmationError).message}
                </div>
            )}
        </>
    );
}

import * as Dialog from '@radix-ui/react-dialog';
import {
    AdminVariantLookupDocument,
    type AdminVariantLookupQuery,
    type AdminVariantLookupQueryVariables,
} from '@mkbj/api-graphql';
import { Button } from '@mkbj/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { RefreshCw, Search, X } from 'lucide-react';
import { type FormEvent, type KeyboardEvent, useEffect, useId, useState } from 'react';
import { graphQlRequest } from '../../api/graphql';
import { restGet, restSend } from '../../api/rest';
import { toAppError } from '../../api/errors';
import { useAuth } from '../../auth/auth-context';
import { EmptyState, ErrorState, PageLoading } from '../../components/feedback';
import { StatusBadge } from '../../components/status-badge';

interface Currency {
    id: string;
    code: string;
    minorUnits: number;
    isEnabled: boolean;
}
interface Override {
    id: string;
    base: string;
    quote: string;
    rate: number;
    setAt: string;
    validUntil: string | null;
    isActive: boolean;
}
interface Rate {
    id: string;
    base: string;
    quote: string;
    rate: number;
    provider: string;
    rateDate: string;
    fetchedAt: string;
}
interface RatePage {
    page: number;
    pageSize: number;
    pageCount: number;
    totalCount: number;
    items: Rate[];
}
interface VariantPrice {
    variantId: string;
    baseAmountMinor: number;
    baseCurrency: string;
    overrides: Array<{ currency: string; amountMinor: number; priceList: string }>;
    updatedAt: string;
}

export function PricingPage() {
    const { token, roles } = useAuth();
    const client = useQueryClient();
    const superAdmin = roles.includes('super_admin');
    const [variantDraft, setVariantDraft] = useState('');
    const [variantSearch, setVariantSearch] = useState('');
    const [variantLookupOpen, setVariantLookupOpen] = useState(false);
    const [activeSuggestion, setActiveSuggestion] = useState(0);
    const suggestionListId = useId();
    const [variantId, setVariantId] = useState('');
    const [priceOpen, setPriceOpen] = useState(false);
    const [overrideOpen, setOverrideOpen] = useState(false);
    const [fxOpen, setFxOpen] = useState(false);
    const [amount, setAmount] = useState('');
    const [currency, setCurrency] = useState('SGD');
    const [base, setBase] = useState('USD');
    const [quote, setQuote] = useState('SGD');
    const [rate, setRate] = useState('');
    const [validUntil, setValidUntil] = useState('');
    const [error, setError] = useState<string | null>(null);
    useEffect(() => {
        const timeout = window.setTimeout(() => setVariantSearch(variantDraft.trim()), 250);
        return () => window.clearTimeout(timeout);
    }, [variantDraft]);
    const variantLookup = useQuery({
        queryKey: ['admin-variant-lookup', variantSearch],
        queryFn: ({ signal }) => graphQlRequest<AdminVariantLookupQuery, AdminVariantLookupQueryVariables>(
            AdminVariantLookupDocument, { query: variantSearch, take: 10 }, token!, signal,
        ),
        enabled: Boolean(token && variantSearch.length >= 2),
        staleTime: 30_000,
    });
    const variantSuggestions = variantLookup.data?.adminVariantLookup ?? [];
    const chooseVariant = (item: (typeof variantSuggestions)[number]) => {
        setVariantDraft(`${item.sku} · ${item.productTitle}`);
        setVariantId(item.variantId);
        setVariantLookupOpen(false);
        setActiveSuggestion(0);
    };
    const handleLookupKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
        if (!variantLookupOpen || !variantSuggestions.length) return;
        if (event.key === 'ArrowDown') {
            event.preventDefault();
            setActiveSuggestion((current) => (current + 1) % variantSuggestions.length);
        } else if (event.key === 'ArrowUp') {
            event.preventDefault();
            setActiveSuggestion((current) => (current - 1 + variantSuggestions.length) % variantSuggestions.length);
        } else if (event.key === 'Enter') {
            event.preventDefault();
            chooseVariant(variantSuggestions[activeSuggestion]!);
        } else if (event.key === 'Escape') {
            setVariantLookupOpen(false);
        }
    };
    const currencies = useQuery({
        queryKey: ['admin-pricing-currencies'],
        queryFn: ({ signal }) =>
            restGet<Currency[]>('/api/v1/admin/pricing/currencies', token!, signal),
        enabled: Boolean(token),
    });
    const rates = useQuery({
        queryKey: ['admin-fx-rates'],
        queryFn: ({ signal }) =>
            restGet<RatePage>('/api/v1/admin/pricing/fx/rates?page=1&pageSize=50', token!, signal),
        enabled: Boolean(token),
    });
    const overrides = useQuery({
        queryKey: ['admin-fx-overrides'],
        queryFn: ({ signal }) =>
            restGet<Override[]>(
                '/api/v1/admin/pricing/fx/overrides?activeOnly=false',
                token!,
                signal,
            ),
        enabled: Boolean(token),
    });
    const variant = useQuery({
        queryKey: ['admin-variant-price', variantId],
        queryFn: ({ signal }) =>
            restGet<VariantPrice>(`/api/v1/admin/pricing/variants/${variantId}`, token!, signal),
        enabled: Boolean(token && variantId),
        retry: false,
    });
    const write = useMutation({
        mutationFn: ({
            path,
            method,
            body,
        }: {
            path: string;
            method: 'POST' | 'PUT' | 'DELETE';
            body?: unknown;
        }) => restSend(path, token!, method, body),
        onSuccess: async () => {
            setPriceOpen(false);
            setOverrideOpen(false);
            setFxOpen(false);
            setError(null);
            await client.invalidateQueries({ queryKey: ['admin-pricing'] });
            await Promise.all([
                client.invalidateQueries({ queryKey: ['admin-pricing-currencies'] }),
                client.invalidateQueries({ queryKey: ['admin-fx-rates'] }),
                client.invalidateQueries({ queryKey: ['admin-fx-overrides'] }),
                client.invalidateQueries({ queryKey: ['admin-variant-price'] }),
            ]);
        },
        onError: (caught) => setError(toAppError(caught).message),
    });
    if (currencies.isPending || rates.isPending || overrides.isPending)
        return <PageLoading label="Loading pricing" />;
    const loadingError = currencies.error || rates.error || overrides.error;
    if (loadingError)
        return (
            <ErrorState
                error={loadingError}
                retry={() => {
                    void currencies.refetch();
                    void rates.refetch();
                    void overrides.refetch();
                }}
            />
        );
    const submitPrice = (event: FormEvent) => {
        event.preventDefault();
        write.mutate({
            path: `/api/v1/admin/pricing/variants/${variantId}/price`,
            method: 'PUT',
            body: { amount, currency },
        });
    };
    return (
        <section aria-labelledby="pricing-title" className="page-stack">
            <div className="page-heading">
                <div>
                    <p className="eyebrow">Commerce</p>
                    <h1 id="pricing-title">Pricing</h1>
                    <p>Manage variant prices, selling currencies and exchange-rate controls.</p>
                </div>
                {superAdmin && (
                    <Button
                        disabled={write.isPending}
                        onClick={() =>
                            write.mutate({
                                path: '/api/v1/admin/pricing/fx/refresh',
                                method: 'POST',
                            })
                        }
                        variant="secondary"
                    >
                        <RefreshCw size={16} />
                        Request FX refresh
                    </Button>
                )}
            </div>
            {error && (
                <div className="dialog-error" role="alert">
                    {error}
                </div>
            )}
            <section className="panel detail-section">
                <div className="section-heading">
                    <div>
                        <h2>Variant pricing</h2>
                        <span>Catalog staff</span>
                    </div>
                </div>
                <form
                    className="filters variant-lookup-form"
                    onSubmit={(event) => {
                        event.preventDefault();
                        if (!variantId) setVariantId(variantDraft.trim());
                        setVariantLookupOpen(false);
                    }}
                    role="search"
                >
                    <label className="search-field variant-combobox">
                        <span>Find variant</span>
                        <span className="input-with-icon">
                            <Search aria-hidden="true" size={17} />
                            <input
                                aria-activedescendant={variantLookupOpen && variantSuggestions.length ? `${suggestionListId}-${activeSuggestion}` : undefined}
                                aria-autocomplete="list"
                                aria-controls={suggestionListId}
                                aria-expanded={variantLookupOpen}
                                autoComplete="off"
                                onBlur={() => window.setTimeout(() => setVariantLookupOpen(false), 100)}
                                onChange={(event) => { setVariantDraft(event.target.value); setVariantId(''); setVariantLookupOpen(true); setActiveSuggestion(0); }}
                                onFocus={() => setVariantLookupOpen(true)}
                                onKeyDown={handleLookupKeyDown}
                                placeholder="Search by SKU, product, MPN, or variant ID"
                                role="combobox"
                                value={variantDraft}
                            />
                        </span>
                        {variantLookupOpen && variantSearch.length >= 2 && (
                            <div className="variant-suggestions" id={suggestionListId} role="listbox">
                                {variantLookup.isPending ? (
                                    <div aria-live="polite" className="variant-suggestion-state">Searching variants…</div>
                                ) : variantLookup.isError ? (
                                    <div className="variant-suggestion-state" role="alert">Variants could not be loaded.</div>
                                ) : variantSuggestions.length === 0 ? (
                                    <div className="variant-suggestion-state">No matching variants. Try a SKU, product title, MPN, or full ID.</div>
                                ) : variantSuggestions.map((item, index) => (
                                    <button aria-selected={index === activeSuggestion} className="variant-suggestion" id={`${suggestionListId}-${index}`} key={item.variantId} onMouseDown={(event) => event.preventDefault()} onMouseEnter={() => setActiveSuggestion(index)} onClick={() => chooseVariant(item)} role="option" type="button">
                                        <span><strong>{item.sku}</strong><small>{item.productTitle}</small></span>
                                        <span><small>{item.brandName} · {item.categoryName}</small><small>{item.attributeValues.map(({ key, value }) => `${key}: ${value}`).join(' · ') || 'Base variant'} · {item.isActive ? 'Active' : 'Inactive'}</small></span>
                                    </button>
                                ))}
                            </div>
                        )}
                    </label>
                    <Button disabled={!variantId && !/^\d+$/.test(variantDraft.trim())} type="submit">Load price</Button>
                </form>
                {variantId &&
                    (variant.isPending ? (
                        <PageLoading label="Loading variant price" />
                    ) : variant.isError ? (
                        <ErrorState error={variant.error} />
                    ) : (
                        <div className="pricing-grid">
                            <article className="pricing-card">
                                <div>
                                    <strong>Base price</strong>
                                    <span>
                                        Updated{' '}
                                        {new Intl.DateTimeFormat(undefined, {
                                            dateStyle: 'medium',
                                        }).format(new Date(variant.data.updatedAt))}
                                    </span>
                                </div>
                                <strong>
                                    {variant.data.baseCurrency}{' '}
                                    {(variant.data.baseAmountMinor / 100).toFixed(2)}
                                </strong>
                                <Button
                                    onClick={() => {
                                        setCurrency(variant.data.baseCurrency);
                                        setAmount((variant.data.baseAmountMinor / 100).toFixed(2));
                                        setPriceOpen(true);
                                    }}
                                    variant="secondary"
                                >
                                    Edit base price
                                </Button>
                            </article>
                            <article className="pricing-card">
                                <div>
                                    <strong>Currency overrides</strong>
                                    <span>{variant.data.overrides.length}</span>
                                </div>
                                {variant.data.overrides.length ? (
                                    <ul className="override-list">
                                        {variant.data.overrides.map((item) => (
                                            <li key={item.currency}>
                                                <span>
                                                    <strong>
                                                        {item.currency}{' '}
                                                        {(item.amountMinor / 100).toFixed(2)}
                                                    </strong>
                                                    <small>{item.priceList}</small>
                                                </span>
                                                <Button
                                                    onClick={() =>
                                                        write.mutate({
                                                            path: `/api/v1/admin/pricing/variants/${variantId}/overrides/${item.currency}`,
                                                            method: 'DELETE',
                                                        })
                                                    }
                                                    size="compact"
                                                    variant="ghost"
                                                >
                                                    Remove
                                                </Button>
                                            </li>
                                        ))}
                                    </ul>
                                ) : (
                                    <p className="muted">No currency overrides.</p>
                                )}
                                <Button
                                    onClick={() => {
                                        setAmount('');
                                        setOverrideOpen(true);
                                    }}
                                    variant="secondary"
                                >
                                    Add override
                                </Button>
                            </article>
                        </div>
                    ))}
            </section>
            <div className="detail-grid">
                <section className="panel detail-section">
                    <div className="section-heading">
                        <h2>Selling currencies</h2>
                        <span>{currencies.data.length}</span>
                    </div>
                    <ul className="plain-list">
                        {currencies.data.map((item) => (
                            <li key={item.code}>
                                <strong>{item.code}</strong>
                                <span>{item.minorUnits} minor units</span>
                                <StatusBadge status={item.isEnabled ? 'Enabled' : 'Disabled'} />
                                {superAdmin && (
                                    <Button
                                        onClick={() =>
                                            write.mutate({
                                                path: `/api/v1/admin/pricing/currencies/${item.code}`,
                                                method: 'PUT',
                                                body: { enabled: !item.isEnabled },
                                            })
                                        }
                                        size="compact"
                                        variant="ghost"
                                    >
                                        {item.isEnabled ? 'Disable' : 'Enable'}
                                    </Button>
                                )}
                            </li>
                        ))}
                    </ul>
                </section>
                <section className="panel detail-section">
                    <div className="section-heading">
                        <h2>FX overrides</h2>
                        <span>{overrides.data.length}</span>
                    </div>
                    {overrides.data.length ? (
                        <ul className="plain-list">
                            {overrides.data.map((item) => (
                                <li key={item.id}>
                                    <strong>
                                        {item.base}/{item.quote}
                                    </strong>
                                    <span>{item.rate}</span>
                                    <StatusBadge status={item.isActive ? 'Active' : 'Expired'} />
                                    {superAdmin && item.isActive && (
                                        <Button
                                            onClick={() =>
                                                write.mutate({
                                                    path: `/api/v1/admin/pricing/fx/overrides/${item.base}/${item.quote}`,
                                                    method: 'DELETE',
                                                })
                                            }
                                            size="compact"
                                            variant="ghost"
                                        >
                                            Clear
                                        </Button>
                                    )}
                                </li>
                            ))}
                        </ul>
                    ) : (
                        <p className="muted">No FX overrides.</p>
                    )}
                    {superAdmin && (
                        <Button onClick={() => setFxOpen(true)} variant="secondary">
                            Set FX override
                        </Button>
                    )}
                </section>
            </div>
            <section className="panel table-panel">
                <div className="table-summary">
                    <strong>Provider rates</strong>
                    <span>{rates.data.totalCount} rates</span>
                </div>
                {rates.data.items.length ? (
                    <div className="table-scroll">
                        <table>
                            <thead>
                                <tr>
                                    <th scope="col">Pair</th>
                                    <th scope="col">Rate</th>
                                    <th scope="col">Provider</th>
                                    <th scope="col">Rate date</th>
                                    <th scope="col">Fetched</th>
                                </tr>
                            </thead>
                            <tbody>
                                {rates.data.items.map((item) => (
                                    <tr key={item.id}>
                                        <th scope="row">
                                            {item.base}/{item.quote}
                                        </th>
                                        <td>{item.rate}</td>
                                        <td>{item.provider}</td>
                                        <td>{item.rateDate}</td>
                                        <td>
                                            {new Intl.DateTimeFormat(undefined, {
                                                dateStyle: 'medium',
                                                timeStyle: 'short',
                                            }).format(new Date(item.fetchedAt))}
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                ) : (
                    <EmptyState title="No FX rates">
                        Request an FX refresh after configuring the provider.
                    </EmptyState>
                )}
            </section>
            <Dialog.Root onOpenChange={setPriceOpen} open={priceOpen}>
                <Dialog.Portal>
                    <Dialog.Overlay className="dialog-overlay" />
                    <Dialog.Content className="dialog-panel">
                        <div className="dialog-heading">
                            <Dialog.Title>Edit base price</Dialog.Title>
                            <Dialog.Close asChild>
                                <Button aria-label="Close" size="icon" variant="ghost">
                                    <X />
                                </Button>
                            </Dialog.Close>
                        </div>
                        <form className="dialog-form" onSubmit={submitPrice}>
                            <label className="field">
                                <span>Amount *</span>
                                <input
                                    inputMode="decimal"
                                    onChange={(event) => setAmount(event.target.value)}
                                    pattern="\d+\.\d{2}"
                                    required
                                    value={amount}
                                />
                            </label>
                            <label className="field">
                                <span>Currency *</span>
                                <select
                                    onChange={(event) => setCurrency(event.target.value)}
                                    value={currency}
                                >
                                    {currencies.data.map((item) => (
                                        <option key={item.code}>{item.code}</option>
                                    ))}
                                </select>
                            </label>
                            <div className="dialog-actions">
                                <Dialog.Close asChild>
                                    <Button variant="secondary">Cancel</Button>
                                </Dialog.Close>
                                <Button disabled={write.isPending}>Save price</Button>
                            </div>
                        </form>
                    </Dialog.Content>
                </Dialog.Portal>
            </Dialog.Root>
            <Dialog.Root onOpenChange={setOverrideOpen} open={overrideOpen}>
                <Dialog.Portal>
                    <Dialog.Overlay className="dialog-overlay" />
                    <Dialog.Content className="dialog-panel">
                        <div className="dialog-heading">
                            <Dialog.Title>Add currency override</Dialog.Title>
                            <Dialog.Close asChild>
                                <Button aria-label="Close" size="icon" variant="ghost">
                                    <X />
                                </Button>
                            </Dialog.Close>
                        </div>
                        <form
                            className="dialog-form"
                            onSubmit={(event) => {
                                event.preventDefault();
                                write.mutate({
                                    path: `/api/v1/admin/pricing/variants/${variantId}/overrides/${currency}`,
                                    method: 'PUT',
                                    body: { amount },
                                });
                            }}
                        >
                            <label className="field">
                                <span>Currency *</span>
                                <select
                                    onChange={(event) => setCurrency(event.target.value)}
                                    value={currency}
                                >
                                    {currencies.data.map((item) => (
                                        <option key={item.code}>{item.code}</option>
                                    ))}
                                </select>
                            </label>
                            <label className="field">
                                <span>Amount *</span>
                                <input
                                    inputMode="decimal"
                                    onChange={(event) => setAmount(event.target.value)}
                                    pattern="\d+\.\d{2}"
                                    required
                                    value={amount}
                                />
                            </label>
                            <div className="dialog-actions">
                                <Dialog.Close asChild>
                                    <Button variant="secondary">Cancel</Button>
                                </Dialog.Close>
                                <Button disabled={write.isPending}>Save override</Button>
                            </div>
                        </form>
                    </Dialog.Content>
                </Dialog.Portal>
            </Dialog.Root>
            <Dialog.Root onOpenChange={setFxOpen} open={fxOpen}>
                <Dialog.Portal>
                    <Dialog.Overlay className="dialog-overlay" />
                    <Dialog.Content className="dialog-panel">
                        <div className="dialog-heading">
                            <Dialog.Title>Set FX override</Dialog.Title>
                            <Dialog.Close asChild>
                                <Button aria-label="Close" size="icon" variant="ghost">
                                    <X />
                                </Button>
                            </Dialog.Close>
                        </div>
                        <form
                            className="dialog-form"
                            onSubmit={(event) => {
                                event.preventDefault();
                                write.mutate({
                                    path: '/api/v1/admin/pricing/fx/overrides',
                                    method: 'POST',
                                    body: {
                                        base,
                                        quote,
                                        rate,
                                        validUntil: validUntil
                                            ? new Date(validUntil).toISOString()
                                            : null,
                                    },
                                });
                            }}
                        >
                            <div className="form-grid">
                                <label className="field">
                                    <span>Base *</span>
                                    <select
                                        onChange={(event) => setBase(event.target.value)}
                                        value={base}
                                    >
                                        {currencies.data.map((item) => (
                                            <option key={item.code}>{item.code}</option>
                                        ))}
                                    </select>
                                </label>
                                <label className="field">
                                    <span>Quote *</span>
                                    <select
                                        onChange={(event) => setQuote(event.target.value)}
                                        value={quote}
                                    >
                                        {currencies.data.map((item) => (
                                            <option key={item.code}>{item.code}</option>
                                        ))}
                                    </select>
                                </label>
                                <label className="field">
                                    <span>Rate *</span>
                                    <input
                                        inputMode="decimal"
                                        onChange={(event) => setRate(event.target.value)}
                                        placeholder="1.2000000000"
                                        required
                                        value={rate}
                                    />
                                </label>
                                <label className="field">
                                    <span>Valid until</span>
                                    <input
                                        onChange={(event) => setValidUntil(event.target.value)}
                                        type="datetime-local"
                                        value={validUntil}
                                    />
                                </label>
                            </div>
                            <div className="dialog-actions">
                                <Dialog.Close asChild>
                                    <Button variant="secondary">Cancel</Button>
                                </Dialog.Close>
                                <Button disabled={write.isPending}>Set override</Button>
                            </div>
                        </form>
                    </Dialog.Content>
                </Dialog.Portal>
            </Dialog.Root>
        </section>
    );
}

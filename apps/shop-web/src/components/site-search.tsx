'use client';

import { Search } from 'lucide-react';
import { useRouter } from 'next/navigation';
import { useEffect, useId, useRef, useState } from 'react';
import { publicConfig } from '@/lib/config';

interface SearchSuggestion {
    productId: string;
    slug: string;
    title: string;
    brandName: string;
    minPrice: { amount: string; currency: string };
}

interface SearchEnvelope {
    data?: { shopProducts?: { items?: SearchSuggestion[] } };
}

const query = `
  query ShopSearchSuggestions($currency: String!, $filter: ShopProductFilterInput) {
    shopProducts(currency: $currency, filter: $filter, sort: RELEVANCE, first: 6) {
      items { productId slug title brandName minPrice { amount currency } }
    }
  }
`;

export function SiteSearch({ currency }: { currency: string }) {
    const router = useRouter();
    const listboxId = useId();
    const [value, setValue] = useState('');
    const [suggestions, setSuggestions] = useState<SearchSuggestion[]>([]);
    const [activeIndex, setActiveIndex] = useState(-1);
    const [loading, setLoading] = useState(false);
    const [focused, setFocused] = useState(false);
    const request = useRef<AbortController | null>(null);
    const open = focused && value.trim().length >= 2;

    useEffect(() => {
        const term = value.trim();
        if (term.length < 2) {
            setSuggestions([]);
            setActiveIndex(-1);
            setLoading(false);
            request.current?.abort();
            return;
        }

        const loadSuggestions = async () => {
            request.current?.abort();
            const controller = new AbortController();
            request.current = controller;
            setLoading(true);

            try {
                const response = await fetch(publicConfig.graphqlUrl, {
                    method: 'POST',
                    headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
                    body: JSON.stringify({ query, variables: { currency, filter: { q: term } } }),
                    signal: controller.signal,
                });
                const envelope = (await response.json()) as SearchEnvelope;
                if (!controller.signal.aborted) {
                    setSuggestions(envelope.data?.shopProducts?.items ?? []);
                    setActiveIndex(-1);
                }
            } catch (error) {
                if (error instanceof DOMException && error.name === 'AbortError') return;
                setSuggestions([]);
            } finally {
                if (!controller.signal.aborted) setLoading(false);
            }
        };
        const timer = window.setTimeout(() => {
            void loadSuggestions();
        }, 180);

        return () => window.clearTimeout(timer);
    }, [currency, value]);

    const submit = (term = value) => {
        const normalized = term.trim();
        if (!normalized) return;
        setFocused(false);
        router.push(`/products?currency=${encodeURIComponent(currency)}&q=${encodeURIComponent(normalized)}`);
    };

    return (
        <form
            action="/products"
            className="global-search"
            onSubmit={(event) => {
                event.preventDefault();
                submit();
            }}
            role="search"
        >
            <label className="sr-only" htmlFor="site-search">Search products</label>
            <input name="currency" type="hidden" value={currency} />
            <input
                aria-activedescendant={activeIndex >= 0 ? `${listboxId}-${activeIndex}` : undefined}
                aria-autocomplete="list"
                aria-controls={listboxId}
                aria-expanded={open}
                autoComplete="off"
                id="site-search"
                name="q"
                onBlur={(event) => {
                    if (!event.currentTarget.form?.contains(event.relatedTarget)) setFocused(false);
                }}
                onChange={(event) => setValue(event.target.value)}
                onFocus={() => setFocused(true)}
                onKeyDown={(event) => {
                    if (!open) return;
                    if (event.key === 'ArrowDown') {
                        event.preventDefault();
                        setActiveIndex((index) => Math.min(index + 1, suggestions.length - 1));
                    } else if (event.key === 'ArrowUp') {
                        event.preventDefault();
                        setActiveIndex((index) => Math.max(index - 1, -1));
                    } else if (event.key === 'Enter' && activeIndex >= 0) {
                        event.preventDefault();
                        const selected = suggestions[activeIndex];
                        if (!selected) return;
                        setFocused(false);
                        router.push(`/products/${selected.slug}?currency=${encodeURIComponent(currency)}`);
                    } else if (event.key === 'Escape') {
                        event.preventDefault();
                        setFocused(false);
                    }
                }}
                placeholder="Search products, brands and categories"
                role="combobox"
                type="search"
                value={value}
            />
            <button aria-label="Search" type="submit"><Search aria-hidden="true" /></button>
            {open && (
                <div className="search-suggestions" id={listboxId} role="listbox">
                    {loading ? (
                        <p className="search-suggestions__status" role="status">Finding products…</p>
                    ) : suggestions.length ? (
                        suggestions.map((suggestion, index) => (
                            <button
                                aria-selected={index === activeIndex}
                                className="search-suggestion"
                                id={`${listboxId}-${index}`}
                                key={suggestion.productId}
                                onClick={() => {
                                    setFocused(false);
                                    router.push(`/products/${suggestion.slug}?currency=${encodeURIComponent(currency)}`);
                                }}
                                onMouseDown={(event) => event.preventDefault()}
                                role="option"
                                type="button"
                            >
                                <span>
                                    <strong>{suggestion.title}</strong>
                                    <small>{suggestion.brandName}</small>
                                </span>
                                <b>{suggestion.minPrice.currency} {suggestion.minPrice.amount}</b>
                            </button>
                        ))
                    ) : (
                        <div className="search-suggestions__empty">
                            <strong>No close matches yet</strong>
                            <span>Press Enter to search the full catalogue for “{value.trim()}”.</span>
                        </div>
                    )}
                    <button className="search-suggestions__all" onClick={() => submit()} type="button">
                        <Search aria-hidden="true" /> See all results for “{value.trim()}”
                    </button>
                </div>
            )}
        </form>
    );
}

import {
    ShopFacetsDocument,
    ShopProductsDocument,
    type ShopProductFilterInput,
    type StorefrontSort,
} from '@mkbj/api-graphql';
import { X } from 'lucide-react';
import Link from 'next/link';
import { FilterPanel } from '@/components/filter-panel';
import { ProductCard } from '@/components/product-card';
import { ShopError } from '@/components/shop-error';
import { shopQuery } from '@/lib/graphql';

export const dynamic = 'force-dynamic';

type QueryValues = Record<string, string | string[] | undefined>;
const shopSort = {
    relevance: 'RELEVANCE',
    newest: 'NEWEST',
    popular: 'POPULAR',
    priceAsc: 'PRICE_ASC',
    priceDesc: 'PRICE_DESC',
} as const satisfies Record<string, StorefrontSort>;
const validSorts: ReadonlyArray<StorefrontSort> = Object.values(shopSort);
const one = (value: string | string[] | undefined) => (Array.isArray(value) ? value[0] : value);
const many = (value: string | string[] | undefined) =>
    value ? (Array.isArray(value) ? value : [value]) : [];

function buildHref(values: QueryValues, changes: Record<string, string | undefined>): string {
    const query = new URLSearchParams();
    for (const [key, value] of Object.entries(values)) {
        for (const item of many(value)) query.append(key, item);
    }
    for (const [key, value] of Object.entries(changes)) {
        query.delete(key);
        if (value) query.set(key, value);
    }
    return `/products?${query.toString()}`;
}

export default async function ProductsPage({
    searchParams,
}: {
    searchParams: Promise<QueryValues>;
}) {
    const values = await searchParams;
    const currency = /^[A-Z]{3}$/i.test(one(values.currency) ?? '')
        ? one(values.currency)!.toUpperCase()
        : 'USD';
    const q = one(values.q)?.trim() || undefined;
    const categoryId = one(values.categoryId);
    const selectedBrands = many(values.brand);
    const sortValue = one(values.sort);
    const sort = validSorts.includes(sortValue as StorefrontSort)
        ? (sortValue as StorefrontSort)
        : q
          ? shopSort.relevance
          : shopSort.newest;
    const attributes = Object.entries(values)
        .filter(([key]) => key.startsWith('attr.'))
        .map(([key, value]) => ({ key: key.slice(5), values: many(value) }))
        .filter(({ values: selected }) => selected.length > 0);
    const filter: ShopProductFilterInput = {
        ...(q ? { q } : {}),
        ...(categoryId ? { categoryId } : {}),
        ...(selectedBrands.length ? { brandIds: selectedBrands } : {}),
        ...(attributes.length ? { attributes } : {}),
        ...(one(values.priceMin) ? { priceMin: one(values.priceMin)! } : {}),
        ...(one(values.priceMax) ? { priceMax: one(values.priceMax)! } : {}),
    };

    try {
        const [{ shopProducts: page }, { shopFacets: facets }] = await Promise.all([
            shopQuery(ShopProductsDocument, {
                currency,
                filter,
                sort,
                after: one(values.after) ?? null,
                first: 24,
            }),
            shopQuery(ShopFacetsDocument, { currency, categoryId: categoryId ?? null }),
        ]);
        const activeFilterCount =
            selectedBrands.length +
            attributes.length +
            (filter.priceMin ? 1 : 0) +
            (filter.priceMax ? 1 : 0);
        return (
            <div className="site-container listing-page">
                <nav aria-label="Breadcrumb" className="breadcrumbs">
                    <Link href="/" prefetch={false}>
                        Home
                    </Link>
                    <span aria-hidden="true">/</span>
                    <span aria-current="page">Products</span>
                </nav>
                <header className="listing-header">
                    <div>
                        <p className="eyebrow">Marketplace</p>
                        <h1>{q ? `Results for “${q}”` : 'Explore all products'}</h1>
                        <p>
                            {page.items.length} products on this page · prices shown in {currency}
                        </p>
                    </div>
                    <form action="/products" className="sort-form">
                        {Object.entries(values)
                            .filter(([key]) => key !== 'sort' && key !== 'after')
                            .flatMap(([key, value]) =>
                                many(value).map((item) => (
                                    <input
                                        key={`${key}-${item}`}
                                        name={key}
                                        type="hidden"
                                        value={item}
                                    />
                                )),
                            )}
                        <label htmlFor="sort">Sort by</label>
                        <select defaultValue={sort} id="sort" name="sort">
                            {q && <option value={shopSort.relevance}>Most relevant</option>}
                            <option value={shopSort.newest}>Newest</option>
                            <option value={shopSort.popular}>Popular</option>
                            <option value={shopSort.priceAsc}>Price: low to high</option>
                            <option value={shopSort.priceDesc}>Price: high to low</option>
                        </select>
                        <button className="button button--secondary" type="submit">
                            Apply
                        </button>
                    </form>
                </header>

                {page.usedTypoFallback && (
                    <div className="typo-notice" role="status">
                        We broadened the search to find close matches.
                    </div>
                )}
                <div className="listing-layout">
                    <FilterPanel activeFilterCount={activeFilterCount}>
                        <form action="/products">
                            <input name="currency" type="hidden" value={currency} />
                            {q && <input name="q" type="hidden" value={q} />}
                            {categoryId && (
                                <input name="categoryId" type="hidden" value={categoryId} />
                            )}
                            <input name="sort" type="hidden" value={sort} />
                            <fieldset>
                                <legend>Brand</legend>
                                {facets.brands.slice(0, 12).map((brand) => (
                                    <label className="check-row" key={brand.value}>
                                        <input
                                            defaultChecked={selectedBrands.includes(brand.value)}
                                            name="brand"
                                            type="checkbox"
                                            value={brand.value}
                                        />
                                        <span>{brand.label}</span>
                                        <small>{brand.count}</small>
                                    </label>
                                ))}
                            </fieldset>
                            <fieldset>
                                <legend>Price</legend>
                                <div className="price-fields">
                                    <label>
                                        Minimum
                                        <input
                                            defaultValue={one(values.priceMin)}
                                            inputMode="decimal"
                                            name="priceMin"
                                            pattern="\d+\.\d{2}"
                                            placeholder="0.00"
                                        />
                                    </label>
                                    <label>
                                        Maximum
                                        <input
                                            defaultValue={one(values.priceMax)}
                                            inputMode="decimal"
                                            name="priceMax"
                                            pattern="\d+\.\d{2}"
                                            placeholder="500.00"
                                        />
                                    </label>
                                </div>
                            </fieldset>
                            {facets.attributes.slice(0, 5).map((group) => (
                                <fieldset key={group.key}>
                                    <legend>{group.key}</legend>
                                    {group.values.slice(0, 8).map((option) => (
                                        <label className="check-row" key={option.value}>
                                            <input
                                                defaultChecked={many(
                                                    values[`attr.${group.key}`],
                                                ).includes(option.value)}
                                                name={`attr.${group.key}`}
                                                type="checkbox"
                                                value={option.value}
                                            />
                                            <span>{option.label}</span>
                                            <small>{option.count}</small>
                                        </label>
                                    ))}
                                </fieldset>
                            ))}
                            <div className="filter-actions">
                                <button className="button button--primary" type="submit">
                                    Show products
                                </button>
                                {activeFilterCount > 0 && (
                                    <Link
                                        href={`/products?currency=${currency}${q ? `&q=${encodeURIComponent(q)}` : ''}`}
                                        prefetch={false}
                                    >
                                        <X aria-hidden="true" /> Clear
                                    </Link>
                                )}
                            </div>
                        </form>
                    </FilterPanel>

                    <section aria-label="Products" className="listing-results">
                        {page.items.length ? (
                            <div className="product-grid product-grid--listing">
                                {page.items.map((product, index) => (
                                    <ProductCard
                                        currency={currency}
                                        key={product.productId}
                                        priority={index < 4}
                                        product={product}
                                    />
                                ))}
                            </div>
                        ) : (
                            <div className="empty-results">
                                <h2>No products match those choices</h2>
                                <p>Try removing a filter or searching with fewer words.</p>
                                <Link
                                    className="button button--secondary"
                                    href={`/products?currency=${currency}`}
                                    prefetch={false}
                                >
                                    Clear filters
                                </Link>
                            </div>
                        )}
                        {page.nextCursor && (
                            <div className="cursor-pagination">
                                <span>Showing {page.items.length} results</span>
                                <Link
                                    className="button button--secondary"
                                    href={buildHref(values, { after: page.nextCursor })}
                                    prefetch={false}
                                >
                                    Next results <span aria-hidden="true">→</span>
                                </Link>
                            </div>
                        )}
                    </section>
                </div>
            </div>
        );
    } catch (error) {
        return (
            <div className="site-container page-space">
                <ShopError
                    message={error instanceof Error ? error.message : 'Please try again shortly.'}
                />
            </div>
        );
    }
}

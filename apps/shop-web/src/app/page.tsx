import { ShopHomeDocument } from '@mkbj/api-graphql';
import { ArrowRight, BadgeCheck, CreditCard, RefreshCcw, ShieldCheck } from 'lucide-react';
import Link from 'next/link';
import { CategoryCard } from '@/components/category-card';
import { CommerceHero } from '@/components/commerce-hero';
import { ProductRail } from '@/components/product-rail';
import { SectionHeading } from '@/components/section-heading';
import { ShopError } from '@/components/shop-error';
import { shopQuery } from '@/lib/graphql';

export const dynamic = 'force-dynamic';

export default async function HomePage({
    searchParams,
}: {
    searchParams: Promise<{ currency?: string }>;
}) {
    const { currency: rawCurrency } = await searchParams;
    const currency = /^[A-Z]{3}$/i.test(rawCurrency ?? '') ? rawCurrency!.toUpperCase() : 'USD';

    try {
        const { shopHome: home } = await shopQuery(ShopHomeDocument, { currency });
        return (
            <>
                <CommerceHero
                    categoryCount={home.featuredCategories.length}
                    currency={currency}
                    featured={home.newArrivals[0]}
                />

                <section className="store-benefits" aria-label="Why shop with MKBJ">
                    <div className="site-container store-benefits__grid">
                        <span>
                            <BadgeCheck aria-hidden="true" />
                            <b>Curated catalogue</b>
                            <small>Active, priced products only</small>
                        </span>
                        <span>
                            <RefreshCcw aria-hidden="true" />
                            <b>Live cart totals</b>
                            <small>Prices reconfirmed before checkout</small>
                        </span>
                        <span>
                            <CreditCard aria-hidden="true" />
                            <b>Clear payment flow</b>
                            <small>No surprise charges from the browser</small>
                        </span>
                        <span>
                            <ShieldCheck aria-hidden="true" />
                            <b>Secure account</b>
                            <small>Your orders and documents stay private</small>
                        </span>
                    </div>
                </section>

                <section className="site-container commerce-section" id="categories">
                    <SectionHeading
                        eyebrow="Departments"
                        href={`/products?currency=${currency}`}
                        subtitle="Only categories with currently available products are shown."
                        title="Shop by category"
                    />
                    <div className="category-card-grid">
                        {home.featuredCategories.map((category) => (
                            <CategoryCard
                                category={category}
                                currency={currency}
                                key={category.categoryId}
                            />
                        ))}
                    </div>
                </section>

                <ProductRail
                    currency={currency}
                    eyebrow="Just added"
                    href={`/products?currency=${currency}&sort=NEWEST`}
                    priority
                    products={home.newArrivals}
                    subtitle="Fresh catalogue additions, ready to compare."
                    title="New arrivals"
                />

                {home.bestSellers.length > 0 && (
                    <ProductRail
                        currency={currency}
                        eyebrow="Trending"
                        href={`/products?currency=${currency}&sort=POPULAR`}
                        products={home.bestSellers}
                        subtitle="Products customers are choosing now."
                        title="Popular right now"
                    />
                )}

                {home.categorySections.map((section) => (
                    <ProductRail
                        currency={currency}
                        eyebrow="Explore the department"
                        href={`/products?currency=${currency}&categoryId=${section.categoryId}`}
                        key={section.key}
                        products={section.products}
                        subtitle={section.subtitle}
                        title={section.title}
                    />
                ))}

                <section className="site-container closing-banner">
                    <div>
                        <p className="eyebrow">Ready when you are</p>
                        <h2>Find the right option without the noise.</h2>
                    </div>
                    <Link
                        className="button button--primary"
                        href={`/products?currency=${currency}`}
                        prefetch={false}
                    >
                        Browse the catalogue <ArrowRight aria-hidden="true" />
                    </Link>
                </section>
            </>
        );
    } catch (error) {
        return (
            <div className="site-container page-space">
                <ShopError
                    message={error instanceof Error ? error.message : 'Please try again shortly.'}
                    title="The shop is taking a moment"
                />
            </div>
        );
    }
}

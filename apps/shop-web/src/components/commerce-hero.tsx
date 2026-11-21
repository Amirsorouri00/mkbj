import type { ShopProductCardFieldsFragment } from '@mkbj/api-graphql';
import { ArrowRight, ShieldCheck, Truck } from 'lucide-react';
import Link from 'next/link';
import { formatMoney } from '@/lib/money';
import { ProductImage } from './product-image';

export function CommerceHero({
    categoryCount,
    currency,
    featured,
}: {
    categoryCount: number;
    currency: string;
    featured?: ShopProductCardFieldsFragment | undefined;
}) {
    const href = featured
        ? `/products/${featured.slug}?currency=${currency}`
        : `/products?currency=${currency}`;

    return (
        <section className="commerce-hero">
            <div className="site-container commerce-hero__grid">
                <div className="commerce-hero__copy">
                    <p className="eyebrow">MKBJ marketplace</p>
                    <h1>
                        Everything useful,
                        <br />
                        one clear place.
                    </h1>
                    <p>
                        Shop current products with transparent options, live pricing and a checkout
                        that confirms every total.
                    </p>
                    <div className="hero-actions">
                        <Link
                            className="button button--primary"
                            href={`/products?currency=${currency}`}
                            prefetch={false}
                        >
                            Shop products <ArrowRight aria-hidden="true" />
                        </Link>
                        <Link className="button button--dark-quiet" href="#categories">
                            Browse {categoryCount} categories
                        </Link>
                    </div>
                    <div className="commerce-hero__assurance" aria-label="Shopping benefits">
                        <span>
                            <ShieldCheck aria-hidden="true" /> Verified catalogue
                        </span>
                        <span>
                            <Truck aria-hidden="true" /> Clear order status
                        </span>
                    </div>
                </div>
                {featured && (
                    <Link
                        aria-label={`View featured product ${featured.title}`}
                        className="hero-product"
                        href={href}
                        prefetch={false}
                    >
                        <div className="hero-product__image">
                            <ProductImage
                                alt=""
                                priority
                                sizes="(max-width: 760px) 70vw, 36vw"
                                src={featured.imageUrl}
                            />
                        </div>
                        <div className="hero-product__copy">
                            <span>New this week</span>
                            <strong>{featured.title}</strong>
                            <small>{featured.brandName}</small>
                            <b>{formatMoney(featured.minPrice)}</b>
                        </div>
                    </Link>
                )}
            </div>
        </section>
    );
}

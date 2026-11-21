import type { ShopProductCardFieldsFragment } from '@mkbj/api-graphql';
import { ArrowRight } from 'lucide-react';
import Link from 'next/link';
import { formatMoney } from '@/lib/money';
import { ProductImage } from './product-image';

export function ProductCard({
    product,
    currency,
    priority = false,
}: {
    product: ShopProductCardFieldsFragment;
    currency: string;
    priority?: boolean;
}) {
    const productHref = `/products/${product.slug}?currency=${encodeURIComponent(currency)}`;
    const range =
        product.minPrice.amount === product.maxPrice.amount
            ? formatMoney(product.minPrice)
            : `${formatMoney(product.minPrice)} – ${formatMoney(product.maxPrice)}`;

    return (
        <article className="product-card">
            <Link
                aria-label={`View ${product.title}`}
                className="product-card__image"
                href={productHref}
                prefetch={false}
            >
                <ProductImage
                    alt=""
                    priority={priority}
                    sizes="(max-width: 640px) 50vw, (max-width: 1100px) 33vw, 18vw"
                    src={product.imageUrl}
                />
            </Link>
            <div className="product-card__content">
                <p className="product-card__brand">{product.brandName}</p>
                <h3>
                    <Link href={productHref} prefetch={false}>
                        {product.title}
                    </Link>
                </h3>
                <p className="product-card__price">{range}</p>
                <div className="product-card__footer">
                    <p className="product-card__meta">
                        {product.variantCount} {product.variantCount === 1 ? 'option' : 'options'}
                    </p>
                    <Link
                        aria-label={`View options for ${product.title}`}
                        href={productHref}
                        prefetch={false}
                    >
                        View <ArrowRight aria-hidden="true" />
                    </Link>
                </div>
            </div>
        </article>
    );
}

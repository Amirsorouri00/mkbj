import type { ShopProductCardFieldsFragment } from '@mkbj/api-graphql';
import { ProductCard } from './product-card';
import { SectionHeading } from './section-heading';

export function ProductRail({
    currency,
    eyebrow,
    href,
    products,
    subtitle,
    title,
    priority = false,
}: {
    currency: string;
    eyebrow: string;
    href: string;
    products: ReadonlyArray<ShopProductCardFieldsFragment>;
    subtitle?: string;
    title: string;
    priority?: boolean;
}) {
    if (!products.length) return null;

    return (
        <section className="site-container commerce-section">
            <SectionHeading eyebrow={eyebrow} href={href} subtitle={subtitle} title={title} />
            <div className="product-grid product-grid--rail">
                {products.map((product, index) => (
                    <ProductCard
                        currency={currency}
                        key={product.productId}
                        priority={priority && index < 5}
                        product={product}
                    />
                ))}
            </div>
        </section>
    );
}

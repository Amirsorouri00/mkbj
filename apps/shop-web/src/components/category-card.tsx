import { ArrowRight } from 'lucide-react';
import Link from 'next/link';

export function CategoryCard({
    category,
    currency,
}: {
    category: { categoryId: string; name: string; productCount: string | number };
    currency: string;
}) {
    return (
        <Link
            className="category-card"
            href={`/products?currency=${currency}&categoryId=${category.categoryId}`}
            prefetch={false}
        >
            <span className="category-card__mark" aria-hidden="true">
                {category.name.slice(0, 1).toUpperCase()}
            </span>
            <span>
                <strong>{category.name}</strong>
                <small>{category.productCount} products</small>
            </span>
            <ArrowRight aria-hidden="true" />
        </Link>
    );
}

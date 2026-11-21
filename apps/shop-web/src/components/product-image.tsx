'use client';

import { ImageIcon } from 'lucide-react';
import Image from 'next/image';
import { useState } from 'react';

export function ProductImage({
    src,
    alt,
    priority = false,
    sizes,
}: {
    src?: string | null;
    alt: string;
    priority?: boolean;
    sizes: string;
}) {
    const [failed, setFailed] = useState(false);

    if (!src || failed) {
        return (
            <span
                className="product-placeholder"
                role="img"
                aria-label={alt || 'Product image unavailable'}
            >
                <ImageIcon aria-hidden="true" />
                <span>Image coming soon</span>
            </span>
        );
    }

    return (
        <Image
            alt={alt}
            fill
            onError={() => setFailed(true)}
            priority={priority}
            sizes={sizes}
            src={src}
            unoptimized
        />
    );
}

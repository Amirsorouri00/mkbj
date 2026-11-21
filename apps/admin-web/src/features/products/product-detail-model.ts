import type { ProductDetailFieldsFragment } from '@mkbj/api-graphql';

export function normalizeProductDetail(product: ProductDetailFieldsFragment): ProductDetailFieldsFragment {
    return {
        ...product,
        activationProblems: product.activationProblems ?? [],
        secondaryCategoryIds: product.secondaryCategoryIds ?? [],
        sourceRefs: product.sourceRefs ?? [],
        identifiers: product.identifiers ?? [],
        media: product.media ?? [],
        variants: (product.variants ?? []).map((variant) => ({
            ...variant,
            attributeValues: variant.attributeValues ?? [],
        })),
    };
}

import { z } from 'zod';

export const productSchema = z.object({
  title: z.string().trim().min(1, 'Enter a product title.').max(200, 'Use 200 characters or fewer.'),
  slug: z.string().trim().max(240, 'Use 240 characters or fewer.'),
  brandId: z.string().min(1, 'Choose a brand.'),
  primaryCategoryId: z.string().min(1, 'Choose a primary category.'),
  shortDescription: z.string().trim().max(500, 'Use 500 characters or fewer.'),
  description: z.string().trim().max(20_000, 'Use 20,000 characters or fewer.'),
});

export type ProductFormValues = z.infer<typeof productSchema>;
export type CategoryNode = { id: string; name: string; status: string; children?: ReadonlyArray<CategoryNode> };
export type CategoryOption = { id: string; label: string; status: string };

export const emptyProductValues: ProductFormValues = {
  title: '',
  slug: '',
  brandId: '',
  primaryCategoryId: '',
  shortDescription: '',
  description: '',
};

export function flattenCategories(nodes: ReadonlyArray<CategoryNode>, depth = 0): CategoryOption[] {
  return nodes.flatMap((node) => [
    { id: node.id, label: `${'— '.repeat(depth)}${node.name}`, status: node.status },
    ...flattenCategories(node.children ?? [], depth + 1),
  ]);
}

export function optionalText(value: string): string | null {
  const normalized = value.trim();
  return normalized || null;
}

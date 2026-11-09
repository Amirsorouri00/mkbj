# Shop API Reference

The shop surface has two layers: **anonymous** (storefront browsing) and **authenticated** (customer account, cart, checkout, orders).

## Anonymous Endpoints (no auth required)

These power the public storefront. They read from materialized views, so data may be a few minutes behind admin changes.

### Home & Navigation

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/shop/home` | Homepage: featured products, categories |
| GET | `/api/v1/shop/categories` | Category tree |
| GET | `/api/v1/shop/categories/{slug}` | Single category by slug |
| GET | `/api/v1/shop/currencies` | Enabled currencies for the shop |

### Products & Search

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/shop/products` | Product listing (paginated, filterable) |
| GET | `/api/v1/shop/products/{slug}` | Product detail by slug |
| GET | `/api/v1/shop/products/{slug}/variants` | Variants for a product |
| GET | `/api/v1/shop/facets` | Available filter facets for the current category/search |
| GET | `/api/v1/shop/search` | Full-text product search |

**Query parameters for product listing:**
- `category` — category slug filter
- `brand` — brand slug filter
- `currency` — ISO code, prices returned in this currency
- `filter` — Gridify filter expression
- `orderBy` — Gridify sort expression
- `page`, `pageSize` — pagination

### Currencies

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/shop/currencies` | List enabled currencies with display info |

---

## Authenticated Endpoints (Shop realm token required)

Auth: `Authorization: Bearer <shop_access_token>` — policy `shop:customer`.

### My Profile

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/shop/me` | Get or create the customer record (first call creates it) |
| PUT | `/api/v1/shop/me` | Update profile (name, contact) |

### Cart

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/shop/cart` | Get the current cart |
| POST | `/api/v1/shop/cart/items` | Add item to cart |
| PUT | `/api/v1/shop/cart/items/{variantId}` | Update item quantity |
| DELETE | `/api/v1/shop/cart/items/{variantId}` | Remove item from cart |
| DELETE | `/api/v1/shop/cart` | Clear the entire cart |

**Add item body:**
```json
{
  "variantId": 123456789,
  "quantity": 2,
  "currency": "SGD"
}
```

### Checkout

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/v1/shop/checkout` | Submit order from cart |
| GET | `/api/v1/shop/checkout/preview` | Preview order totals before submitting |

**Checkout body:**
```json
{
  "expectedTotal": 15000,
  "currency": "SGD"
}
```

`expectedTotal` is the figure the customer was shown (minor units). The API compares it against a live quote and refuses the checkout if it no longer matches — it never silently uses a different price.

### My Orders

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/shop/orders` | List my orders (paginated) |
| GET | `/api/v1/shop/orders/{id}` | Order detail |

### My Documents

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/shop/documents` | List invoices/pro-formas for my orders |
| GET | `/api/v1/shop/documents/{id}` | Download document (PDF) |

---

## GraphQL (Shop surface)

The same data is available via GraphQL at `/graphql`. Shop queries and mutations are anonymous or `shop:customer`-gated per resolver. See [graphql-api.md](graphql-api.md).

Key queries: `shopHome`, `shopCategories`, `shopProducts`, `shopProduct`, `shopSearch`, `shopFacets`, `shopCurrencies`.

Key mutations: `shopUpdateProfile`, `shopAddToCart`, `shopUpdateCartItem`, `shopRemoveCartItem`, `shopClearCart`, `shopCheckout`.

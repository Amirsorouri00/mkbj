# MKBJ Shop Frontend Architecture

## Scope

`apps/shop-web` is the customer-facing MKBJ storefront. It is a Next.js App Router application with server-rendered anonymous discovery and client-side authenticated account flows. It is intentionally separate from `apps/admin-web`: the two applications share generated contracts and neutral tooling, but not visual identity or business components.

The .NET modular monolith remains authoritative for sellability, pricing, cart refreshes, checkout, customer identity, orders, and documents.

## Runtime boundaries

```text
apps/shop-web             Pages, shop components, Keycloak Shop session, orchestration
packages/api-graphql     Checked-in SDL, shop operations, generated TypeScript documents
packages/design-tokens   Independent shop and admin semantic token files
src/Modules/Catalog      Anonymous discovery projections and live sellability port
src/Modules/Pricing      Currency and live quote authority
src/Modules/Sales        Cart, checkout, order and document authority
src/Modules/Customer     Shop profile authority
```

Anonymous pages are server-rendered from GraphQL and remain indexable and shareable. Cart, profile, order history and documents run in the browser after Shop-realm Authorization Code + PKCE authentication. Tokens remain in memory; silent SSO restores an existing Keycloak session.

## Data flow

1. The URL owns currency, search, sort, filters, category and forward cursor state.
2. A server page sends a checked-in typed GraphQL document through a 30-second shared cache. The source is already a deliberately stale browse projection, and this prevents all server-rendered visitors from consuming one anonymous per-IP allowance. Authenticated cart and checkout requests remain uncached.
3. Generated types keep every ID opaque. GraphQL exposes Snowflake identifiers as `ID`, which serializes as exact JSON strings rather than unsafe JavaScript numbers.
4. Product discovery reads the materialized storefront views. A product card is display data, not proof that a variant can be purchased.
5. Add-to-cart and every cart read re-check live Catalog sellability and Pricing quotes.
6. Checkout sends only the displayed `expectedTotal`; the browser never supplies a unit price, subtotal, tax or payable amount.
7. Backend refusals and price changes are shown as recovery states instead of being replaced with frontend business rules.

The short cache is a browse-performance boundary, not a purchasing cache. Product detail may become unavailable between display and add-to-cart; the mutation remains authoritative and returns the recovery state.

## Design system

The canonical shop rules are in `design-system/mkbj-shop/MASTER.md`; runtime tokens are in `packages/design-tokens/src/shop.css`.

- Clean typographic `MKBJ` wordmark until an official brand asset exists.
- Blue-led marketplace identity, independent from the admin orange system.
- Minimum 44 px interactive targets, visible focus, semantic headings and form labels.
- Stable image aspect ratios and honest placeholders; no invented product imagery.
- Reduced-motion support and responsive layouts verified at desktop, 375 px, and phone landscape.
- Product and order prices use exact server strings or integer minor units; no floating-point arithmetic.

## Extensibility

Product-list request construction is centralized in the listing route. New sorting, filters and thumbnail metadata can be adopted by extending the generated input/fragment and mapping it into URL state; page components do not construct ad hoc backend requests. Forward cursor links preserve every active filter and currency parameter.

Catalog and Pricing remain separate in the UI vocabulary. “Published,” “priced,” and “available now” are never presented as synonyms.

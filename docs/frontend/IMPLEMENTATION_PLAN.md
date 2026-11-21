# MKBJ Web Applications Implementation Plan

## Decisions from the audit

- Build the admin as `apps/admin-web`: React, Vite and strict TypeScript.
- Use pnpm workspaces and Turborepo without changing the .NET solution layout.
- Use GraphQL for typed catalog/pricing reads and almost all supported mutations.
- Use REST only where the verified contract is materially better: multipart media upload and FX refresh.
- Check in generated contract snapshots and generated TypeScript so ordinary builds do not require a running backend.
- Keep TanStack Query as the only remote-state cache. No Redux or Apollo cache.
- Treat all Snowflake IDs and `Long` scalars as strings at the browser boundary.
- Implement only controls backed by verified operations. Missing variant editing, filtered-set bulk operations, and concurrency control remain visibly documented gaps.

## Workspace shape

```text
apps/
  admin-web/                 React/Vite application and admin-specific features
packages/
  api-graphql/               SDL snapshot, operation documents, generated types, fetcher
  api-rest/                  OpenAPI snapshot, generated request types, multipart/REST adapter
  design-tokens/             Theme tokens only; no admin business components
  ui/                        Theme-neutral primitives shared by future web applications
  config/                    TypeScript, ESLint and formatting configuration
docs/frontend/
  API_CONTRACT_MATRIX.md
  IMPLEMENTATION_PLAN.md
  DESIGN_SYSTEM.md
  PAGINATION_SPEC.md
  contracts/
```

`apps/shop-web` now contains the independent customer storefront. It shares generated contracts and neutral tooling with the admin, but has its own Next.js runtime, Shop-realm session provider and blue-led design tokens.

## Design direction

The proposed light, charcoal and orange direction is retained. UI/UX validation supports a minimal Swiss-style enterprise interface with high information density, low visual variance and subtle motion.

- Display `MKBJ` as a typographic wordmark until official artwork exists.
- Use orange for primary actions, selected navigation and focus—not for large surfaces.
- Keep dense rows readable and preserve 44 px interactive targets.
- Use semantic HTML tables on desktop; wrap wide tables in an explicit scroll region on small screens rather than replacing tabular semantics with arbitrary card grids.
- Provide a skip link, visible keyboard focus, logical tab order and reduced-motion behavior.
- Validation failures use both inline field errors and a focusable error summary; toast-only validation is prohibited.
- Mobile navigation uses a drawer; desktop uses an approximately 248 px sidebar.
- Use subtle CSS transitions only. GSAP is unnecessary for a backoffice application.

## Delivery milestones

### 1. Backend rename — complete

Repository-wide `MKBJ`/`Mkbj`/`mkbj` rename, build and test verification.

### 2. Contract baseline — complete

- Capture generated OpenAPI and GraphQL SDL.
- Record verified operations, permissions, wire formats and gaps.
- Add repeatable contract-generation and drift-check commands.

### 3. Workspace and quality gates — complete

- Add pnpm/Turbo workspace and shared strict configuration.
- Scaffold `admin-web`, generated API packages, design tokens and UI primitives.
- Add lint, typecheck, unit-test and build commands.

### 4. Authentication and application shell — complete

- Keycloak Backoffice Authorization Code + PKCE.
- Memory-only tokens with silent session restoration through Keycloak SSO.
- Session expiry, logout, 401/403 routes and permission-aware navigation.
- Responsive shell, breadcrumbs, error boundary and global request feedback.

### 5. Design system and data grid — complete

- Semantic tokens, typography, spacing, focus, state and feedback components.
- URL-owned page/search/filter/sort state.
- Page and cursor pagination adapters without fabricated totals or navigation.
- Accessible server-side table with cancellation and stable query keys.

### 6. Catalog vertical slice — complete for supported contracts

- Product list, filters, search, detail, create/update and activation.
- Variants, identifiers, media and category assignment.
- Brands, categories and attribute blueprints.
- Pricing shown as a distinct feature and query boundary.
- Explicit unavailable states for unsupported backend operations.

Implemented: product list/detail, debounced search, status/brand/category filters, page navigation, draft creation, general editing, product activation, variants, identifiers, multipart media upload/removal, brand administration, cursor-paginated hierarchical category administration, attribute-blueprint administration, category attribute assignment/inheritance, per-variant Pricing-module reads and writes, explicit-ID bulk editing across pages, and survivor-verified duplicate merging. Unsupported sorting, list thumbnails, variant editing, media reordering/replacement, filtered-set bulk operations, and concurrency protection remain documented backend gaps rather than nonfunctional controls.

### 7. Admin verification and documentation — complete

- Vitest/Testing Library for state, forms, permissions and error mapping.
- Playwright for login redirect, product navigation, filtering and supported mutations.
- Accessibility checks and keyboard-only workflows.
- Contract drift, production build and reproducible local startup instructions.

### 8. Customer shop — complete for the supported MVP contracts

- Server-rendered home, categories, product listing, search, facets, sorting and product detail.
- Currency-preserving URL state and forward cursor pagination.
- Shop-realm Authorization Code + PKCE with silent SSO and memory-only tokens.
- Live cart add/update/remove, exact-total checkout, profile and billing address, order history/detail and document links.
- Independent responsive design system with desktop, 375 px and landscape browser coverage.
- GraphQL Snowflake IDs hardened to exact strings after the authenticated browser journey exposed unsafe numeric serialization.
- Shop backend gaps documented in `SHOP_API_GAPS.md` with explicit adoption paths for thumbnails, richer filters/sorts and typed checkout information.

## Verification status

Admin and shop lint, strict type checking, unit/component/transport tests, generated-contract drift checks and production builds pass. Credentialed Playwright runs use isolated local Backoffice and Shop accounts against the real API and Keycloak. The shop suite covers anonymous discovery, URL-owned filtering/sorting, exact currency preservation, 375 px and landscape layouts, Shop login and a live-priced cart addition.

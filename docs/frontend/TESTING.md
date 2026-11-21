# Web Frontend Testing

## Quality gates

Run from the repository root:

```text
pnpm typecheck
pnpm lint
pnpm test
pnpm check:contracts
pnpm build
```

The admin suite covers product, brand, category and attribute URL restoration; opaque identifier and cursor preservation; offset and cursor pagination controls; editor validation; category-tree flattening; exact large-value money formatting; capability mapping; thumbnail adaptation; breadcrumbs; GraphQL error metadata; permission-specific rendering; empty states; and destructive confirmations. The shop suite adds exact display/minor-unit money formatting, malformed-money refusal and currency-preserving product links.

Generated-contract checks use the checked-in schema snapshots and therefore do not require the backend to be running.

## Required integration coverage

Deterministic transport tests now exercise typed GraphQL success/error envelopes, cancellation signals, 401/403 handling, unreadable responses, REST multipart media uploads, correlation IDs, and media error envelopes. The next fixture-based layer should add cache invalidation, complete form validation flows, and stale-update behavior without adding a runtime fixture fallback.

Playwright scenarios require the local Docker dependencies, migrated and seeded database, running host, and Backoffice test user. They must use isolated development data and never target production. Priority scenarios are:

1. Keycloak redirect and session restoration.
2. Product search, filters, paging and URL restoration.
3. Draft creation and general editing.
4. Variant and identifier management.
5. Media upload and removal.
6. Product activation refusal and success.
7. Permission denial and logout.
8. Attribute creation, scope changes, option-removal warnings, and lifecycle refusals.
9. Category attribute assignment, inheritance exclusions, and rule toggles.
10. Variant reference prices and currency overrides.
11. Cross-page explicit-ID bulk editing and partial failures.
12. Survivor verification and duplicate merge.

The committed smoke suite runs serially against the locally installed Chrome browser at desktop and Pixel 7 viewports because its authenticated projects intentionally share one disposable Keycloak account. The anonymous checks follow the real Authorization Code + PKCE redirect into the Backoffice realm and verify the Keycloak login controls. With `E2E_ADMIN_USERNAME` and `E2E_ADMIN_PASSWORD`, the authenticated checks verify the live 160-product seed result, desktop primary navigation, and the mobile navigation drawer. Those two checks skip when credentials are absent.

The shop Playwright suite runs on desktop, a 375 px phone viewport and reduced-motion phone landscape. It verifies the real seeded home and listing, URL-addressable search/sort state, currency preservation, responsive navigation, horizontal-overflow safety, Shop-realm login and a live add-to-cart mutation. The authenticated landscape case is intentionally skipped because the same flow is covered on desktop and the small-phone layout; anonymous discovery still runs in landscape. A normal-limit run must also show no API `429` responses; the 30-second public-read cache keeps a full serial multi-viewport run well below the shared server-IP allowance.

Skipped or unavailable E2E infrastructure must be reported separately; a production build is not a substitute for an authenticated E2E run.

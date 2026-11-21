# Admin Frontend Known Gaps

Verified on 2026-11-10 against the captured runtime contracts.

The detailed backend proposal and adoption sequence are in [`BACKEND_API_GAPS.md`](BACKEND_API_GAPS.md).

## Backend contract gaps

1. Product lists expose no sorting argument. The UI keeps the backend's stable implemented ordering and does not advertise sorting.
2. Product list rows expose image readiness but no thumbnail URL. The UI shows an image-availability marker rather than fabricating a thumbnail.
3. Existing product slugs cannot be changed by the update contract. The editor presents the slug as permanent.
4. Variants can be created and activated/deactivated, but their SKU and attributes cannot be edited afterward.
5. There is no filtered-set bulk operation. Only explicit product IDs are accepted.
6. Catalog mutations expose no concurrency token or precondition. Edits are last-write-wins and refetch after success.
7. Most REST response schemas are absent from OpenAPI. REST is limited to the verified media-upload command.
8. Measurement units can be read and assigned, but the backend exposes no administration mutations for creating or maintaining the unit catalogue.

## Current frontend milestone gaps

- Brand, cursor-paginated category, attribute-blueprint, and category assignment/inheritance administration are available.
- Explicit-ID cross-page bulk editing and survivor-verified duplicate merge are available. There is no duplicate-candidate discovery query, so staff must supply the survivor product ID.
- Per-variant reference prices and currency overrides are available from product detail. The backend still lacks a batch pricing read, so the UI intentionally loads only the selected variant.
- Secondary-category editing remains available in the product editor; category-to-product assignment also exists in the backend but is not duplicated as a second competing product-assignment UI.
- Brand editor choices currently load the first 100 brands, matching the verified maximum page size. A server-search combobox is needed for catalogs larger than that.
- The category selector renders the verified hierarchy to four captured levels. A fully unbounded category picker requires a paged/cursor search interaction.
- Permission-aware navigation currently relies on the protected backend response for catalog access; an effective-permission read contract is not exposed.
- Authenticated Playwright coverage has not yet been executed against a freshly migrated full local stack.

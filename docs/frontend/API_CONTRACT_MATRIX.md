# Admin Frontend API Contract Matrix

Verified on 2026-11-10 from the generated OpenAPI document, generated GraphQL SDL, endpoint source, query projections, and the integration suite. The checked-in snapshots live in `docs/frontend/contracts/`.

Concrete proposals for missing sorting, filters, thumbnails, concurrency, permission discovery, bulk selection, pricing batching and typed REST responses are documented in [`BACKEND_API_GAPS.md`](BACKEND_API_GAPS.md).

## Contract rules

- Backoffice catalog operations require the `admin:staff` policy and `catalog.manage` permission.
- FX mutations and currency enablement require `admin:super` plus `fx.manage`.
- Snowflake identifiers must be treated as opaque strings in TypeScript. GraphQL exposes catalog identifiers as `ID`; REST accepts 64-bit IDs as JSON strings or integers but must be called with strings.
- Catalog and Pricing remain separate. Product reads do not imply that a variant has a price.
- Admin product, brand, attribute, and FX lists are page-based. Category search uses a GraphQL cursor connection; the hierarchy query returns the full tree.
- The generated OpenAPI document describes request bodies and parameters, but most minimal-API responses have no declared response schema. OpenAPI-generated response types therefore cannot be trusted yet.

## Catalog products

| Feature | Transport | Verified operation | Input and pagination | Output | Availability | Source |
|---|---|---|---|---|---|---|
| Product list | GraphQL | `adminProducts` | `q`, `status`, `brandId`, `categoryId`, required `page`, `pageSize`; max 100 | `PaginatedQueryOfProductAdminRowDto` with total count and page count | Verified | `ProductAdminQueries.cs`, generated SDL |
| Product detail | GraphQL | `adminProduct(id: ID!)` | Opaque product ID | Product with variants, identifiers, media, activation problems, categories and timestamps | Verified | `CatalogAdminQueries.cs`, generated SDL |
| Create product | GraphQL | `createProduct(input)` | Brand/category IDs, title, optional slug and descriptions | Reloaded `ProductAdminDetailDto` | Verified | `ProductAdminMutations.cs` |
| Update product | GraphQL | `updateProduct(input)` | Product, brand and category IDs plus editable text | Reloaded product detail | Verified | `ProductAdminMutations.cs` |
| Activate/deactivate | GraphQL | `setProductActive(input)` | Product ID and boolean | Reloaded product detail; refusal includes activation problems/error | Verified | `ProductAdminMutations.cs` |
| Secondary categories | GraphQL | `setProductSecondaryCategories(input)` | Product ID plus category IDs | Reloaded product detail | Verified | `ProductAdminMutations.cs` |
| Add variant | GraphQL | `addVariant(input)` | Product ID, optional SKU, attribute map | Reloaded product detail | Verified | `ProductAdminMutations.cs` |
| Edit variant fields | — | — | — | — | Missing: only active status can be changed after creation | Product application ports |
| Activate/deactivate variant | GraphQL | `setVariantActive(input)` | Product ID, variant ID and boolean | Reloaded product detail | Verified | `ProductAdminMutations.cs` |
| Add/remove identifier | GraphQL | `addProductIdentifier`, `removeProductIdentifier` | Product and identifier IDs; identifier type/value/source | Reloaded product detail | Verified | `ProductAdminMutations.cs` |
| Upload media | REST | `POST /api/v1/admin/catalog/products/{id}/media` | Multipart `file`; optional `alt`, `rights`, `sortOrder` | Created media ID/location, but response is undocumented in OpenAPI | Verified with contract gap | `CatalogAdminEndpoints.cs`, OpenAPI snapshot |
| Remove media | GraphQL | `removeProductMedia(input)` | Product and media IDs | Reloaded product detail | Verified | `ProductAdminMutations.cs` |
| Bulk edit selected products | GraphQL | `bulkEditProducts(input)` | Explicit product IDs plus status/category/brand changes | Per-item `BulkEditResult` | Verified | `ProductAdminMutations.cs` |
| Bulk edit all filtered records | — | — | — | — | Missing: no server-side filtered selection token or matching-set mutation | Endpoint inventory |
| Merge duplicate | GraphQL | `mergeDuplicateProduct(input)` | Duplicate and survivor IDs | Reloaded duplicate showing `mergedIntoProductId` | Verified | `ProductAdminMutations.cs` |

The frontend preserves explicit selections across page navigation and sends one verified batch mutation. It never interprets visible selection as “all filtered records.” Duplicate merge first reads the survivor product and presents a comparison before enabling confirmation; the backend exposes no candidate-discovery query.

REST equivalents exist under `/api/v1/admin/catalog/products`, but GraphQL is selected for typed reads and mutations because it returns complete response shapes. REST remains the media-upload transport.

## Brands, categories and attributes

| Feature | Transport | Verified operation | Pagination | Availability | Source |
|---|---|---|---|---|---|
| Brand list/search | GraphQL | `adminBrands(search, page, pageSize)` | Page-based, stable canonical-name then ID ordering | Verified | Generated SDL, `CatalogAdminQueries.cs` |
| Brand create/rename/aliases/status | GraphQL | `createBrand`, `renameBrand`, `setBrandAliases`, `setBrandActive` | N/A | Verified | `BrandAdminResolvers.cs` |
| Category searchable list | GraphQL | `categories(filter, first, after, last, before)` | Cursor connection with total count | Verified | `CategoryAdminQueries.cs` |
| Category hierarchy | GraphQL | `categoryHierarchy` | Full tree | Verified | `CategoryAdminQueries.cs` |
| Category CRUD/status/move | GraphQL | `createCategory`, `updateCategory`, `setCategoryStatus`, `moveCategory`, `deleteCategory` | N/A | Verified | `CategoryAdminMutations.cs` |
| Category product assignment | GraphQL | `assignCategoryProduct`, `unassignCategoryProduct` | N/A | Verified | `CategoryAdminMutations.cs` |
| Attribute list/detail/usage | GraphQL | `attributeBlueprints`, `attributeBlueprint`, `attributeBlueprintUsage` | Page-based, max 100 | Verified | `AttributeBlueprintQueries.cs` |
| Attribute options and lifecycle | GraphQL | create/update/add/remove/rename/type/status/archive/scope/aliases/unit/delete mutations | N/A | Verified | `AttributeBlueprintMutations.cs` |
| Category attribute assignment/exclusion | GraphQL | assignment and inherited-exclusion query/mutation set | N/A | Verified | Catalog GraphQL resolvers and SDL |
| Measurement unit list | GraphQL | `measurementUnits` | Full reference list | Verified read-only; create/update/delete operations are absent | `AttributeBlueprintQueries.cs`, generated SDL |

## Pricing

| Feature | Transport | Verified operation | Permission | Availability | Source |
|---|---|---|---|---|---|
| Variant pricing detail | GraphQL | `adminVariantPrice(variantId: ID!)` | `catalog.manage` | Verified | Generated SDL, Pricing resolvers |
| Reference price | GraphQL | `adminSetReferencePrice` | `catalog.manage` | Verified | Pricing resolvers |
| Currency override | GraphQL | set/remove override mutations | `catalog.manage` | Verified | Pricing resolvers |
| FX rates | GraphQL | `adminFxRates(quote, page, pageSize)` | `catalog.manage` | Verified | Generated SDL |
| FX overrides | GraphQL | query plus create/remove mutations | Create/remove require `fx.manage` | Verified | Pricing resolvers |
| Currency administration | GraphQL | `adminCurrencies` and enable/disable mutation | `admin:super` + `fx.manage` | Verified | Pricing resolvers |
| FX refresh | REST | `POST /api/v1/admin/pricing/fx/refresh` | `admin:super` + `fx.manage` | Verified; no typed OpenAPI response | OpenAPI snapshot, Pricing REST endpoint |

The Catalog product-detail page uses only `adminVariantPrice`, `setVariantPrice`, `setCurrencyOverride`, `removeCurrencyOverride`, and the currency reference list. FX and currency-administration operations remain outside the Catalog slice because they require the separate Super Admin workflow.

## Wire formats and errors

| Concern | Verified behavior | Frontend rule |
|---|---|---|
| REST IDs | OpenAPI represents `int64` as integer-or-string; domain `SnowflakeId` serializes as a string | Normalize every ID to `string`; never call `Number()` or `parseInt()` |
| GraphQL IDs | Catalog types explicitly bind Snowflake IDs to `ID` | GraphQL Code Generator scalar mapping: `ID -> string`, `Long -> string` at the boundary |
| Money | Minor-unit integers plus ISO currency; storefront display DTOs sometimes use decimal strings | Keep minor units/decimal strings lossless; format only for display |
| Dates | `DateTime`/`DateTimeOffset` ISO strings | Map to `string`; parse only in view helpers |
| Enums | GraphQL enums; REST uses JSON string enums | Generate enums from SDL/OpenAPI; do not duplicate literals |
| REST errors | `{ code, message, retryable, correlationId, details?, items? }` | One normalized application error; retain correlation ID |
| GraphQL errors | Stable error code and HTTP-status extensions | Normalize alongside REST errors without hiding field/path context |
| Concurrency | No catalog row-version/precondition token is exposed by the current admin contract | Refetch after mutation; report this as a last-write-wins limitation |

## Confirmed contract gaps

1. OpenAPI response schemas are absent for most minimal API endpoints, so generated REST clients cannot provide safe response types.
2. There is no variant-edit mutation beyond active status.
3. There is no server-side “all records matching this filter” bulk operation.
4. Catalog admin contracts expose no concurrency token or conditional-write mechanism.
5. No checked-in frontend operation documents existed before this work; the SDL snapshot is now captured for code generation.

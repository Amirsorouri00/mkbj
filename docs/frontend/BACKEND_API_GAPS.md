# Backend API Gaps for Admin Web

Verified against the generated GraphQL SDL, OpenAPI document and backend query/mutation implementations on 2026-11-10. “Proposed” shapes below are design recommendations, not current contracts.

## Priority summary

| Priority | Gap | User impact | Recommended backend direction |
|---|---|---|---|
| P0 | No effective-permission query | Navigation cannot reliably hide actions when data-driven permission assignments change | Add a read-only `adminSession` query with realm roles and effective permission codes |
| P0 | No mutation concurrency token | Catalog editing is last-write-wins | Expose an opaque revision and require it on updates |
| P1 | Product sorting is fixed | Admins cannot sort by update time, title or status | Add an allow-listed sort input with an ID tie-breaker |
| P1 | Product list has no thumbnail | List can show only image readiness | Add nullable primary-thumbnail metadata |
| P1 | Product filtering is narrow | No readiness, image, variant, identifier or updated-date filters | Introduce an extensible filter input while retaining current arguments temporarily |
| P1 | Pricing reads are per variant | Product detail would require one request per variant | Add a batch price query keyed by variant IDs |
| P1 | OpenAPI omits command responses | Generated REST clients cannot safely type responses | Declare success and error response schemas on minimal endpoints |
| P2 | No filtered-set bulk command | UI can act only on explicitly selected IDs | Add a server-side filter-based bulk input or selection token |
| P2 | Variant editing is absent | SKU and attributes are permanent after creation | Add a guarded variant update operation if the domain permits it |
| P2 | Media lifecycle is incomplete | No reorder, replace or explicit primary-image operation | Add reorder and primary selection commands with typed results |
| P2 | Measurement units are read-only | Admins cannot maintain the reference unit catalogue | Add guarded create/update/archive operations or explicitly define units as migration-owned reference data |
| P2 | Duplicate candidates are not queryable | Staff must already know the survivor ID | Add a similarity/candidate read model with meaningful comparison evidence |

## 1. Product list sorting

### Current contract

`adminProducts` accepts `q`, `status`, `brandId`, `categoryId`, `page` and `pageSize`. `CatalogAdminQueries.ListProductsAsync` always orders by `CreatedAt DESC, Id ASC`.

The frontend records this as a fixed server ordering and does not display fake sortable headers.

### Proposed non-breaking addition

```graphql
enum ProductAdminSortField {
  CREATED_AT
  UPDATED_AT
  TITLE
  STATUS
}

enum SortDirection { ASC DESC }

input ProductAdminSortInput {
  field: ProductAdminSortField!
  direction: SortDirection!
}

adminProducts(
  filter: ProductAdminFilterInput
  sort: ProductAdminSortInput
  page: Int!
  pageSize: Int!
): PaginatedQueryOfProductAdminRowDto!
```

Every ordering must append `Id ASC` as a stable unique tie-breaker. Existing scalar filter arguments can remain during a deprecation window.

## 2. Extensible product filters

### Current contract

Search covers title, slug and MPN. Filters cover status, brand, and primary-or-secondary category.

### Proposed filters

Use allow-listed typed fields rather than exposing arbitrary database expressions:

```graphql
input ProductAdminFilterInput {
  q: String
  statuses: [ProductStatus!]
  brandIds: [ID!]
  categoryIds: [ID!]
  hasUsableImage: Boolean
  hasActiveVariant: Boolean
  activationReady: Boolean
  identifierType: ProductIdentifierType
  updatedFrom: DateTime
  updatedTo: DateTime
  merged: Boolean
}
```

The backend must keep business-derived filters such as `activationReady` authoritative. The browser should never reconstruct readiness rules.

## 3. Product thumbnails

### Current contract

`ProductAdminRowDto` exposes only `hasUsableImage`. Product detail exposes every media URL, but requesting details for each row would create an N+1 request pattern.

### Proposed list projection

```graphql
type ProductAdminThumbnailDto {
  mediaId: ID!
  url: String!
  alt: String
  updatedAt: DateTime
}

type ProductAdminRowDto {
  # existing fields
  thumbnail: ProductAdminThumbnailDto
}
```

`thumbnail` should be nullable. The backend should select the explicit primary image when one exists, otherwise the first usable image by `sortOrder, id`. URLs may be short-lived; clients must not persist them beyond normal query caching.

The frontend already maps the current readiness flag through a thumbnail view model. Adding `url` and `alt` will require changing the operation and that mapper, not the table component.

## 4. Effective authorization

Realm roles bound the Backoffice surface, while effective permissions are editable database data. The token alone therefore cannot answer whether the current user has `catalog.manage`.

Proposed query:

```graphql
type AdminSessionDto {
  subject: UUID!
  roles: [String!]!
  permissions: [String!]!
}

adminSession: AdminSessionDto!
```

This is for UX only. Endpoint and resolver authorization remains mandatory.

## 5. Optimistic concurrency

Catalog admin DTOs expose no revision, ETag or row version. A second editor can silently overwrite the first editor’s changes.

Expose an opaque string revision on mutable admin details and require `expectedRevision` on updates. A stale revision should return the existing `CONCURRENCY_CONFLICT`/HTTP 409 shape with the current revision, allowing the UI to refetch and show a comparison.

Do not expose PostgreSQL `xmin` as a numeric JavaScript value; serialize an opaque string.

## 6. REST response descriptions

The media upload is verified as `multipart/form-data` and returns a created media ID, but OpenAPI declares only an undocumented success response. Add request-body and response metadata for:

- multipart fields: `file`, `alt`, `rights`, `sortOrder`;
- `201` created envelope and `Location` header;
- the shared error envelope for 400, 401, 403, 413, 415 and 500.

Apply the same response declarations to the remaining minimal API commands so generated REST types become usable beyond request shapes.

## 7. Batch pricing read

Catalog correctly does not own price. Pricing currently exposes `adminVariantPrice(variantId)` one variant at a time. Product detail needs a Pricing-module batch read to avoid N+1 requests:

```graphql
adminVariantPrices(variantIds: [ID!]!): [VariantPriceAdminDto!]!
```

Missing prices should be omitted or represented explicitly according to Pricing semantics; Catalog status must not imply a price.

## 8. Bulk operations

The existing command accepts explicit product IDs and returns per-item outcomes. This safely supports visible-row selection. It does not support “all matching filters.”

If that workflow is required, accept the same typed filter used by the list plus a clear exclusion list, or issue a short-lived server selection token. The response must keep per-item outcomes and an exact attempted/succeeded/failed count. The frontend must never fan out thousands of individual mutations.

## 9. Variant and media commands

- Variant update is absent. If added, preserve unique SKU/signature rules in the domain and expose a revision.
- Media upload/remove exists. Reorder, replacement, alt-text editing and explicit primary selection do not.
- A primary-image command should reference a media ID owned by the product and refuse unusable media.

## 10. Category and brand scalability

- Brand search is page-based and stable, but has no active-status filter or sort choice.
- Category search is cursor-based, but the editor currently needs a scalable searchable hierarchy picker. Add path/breadcrumb information to category search nodes so a result can be understood without downloading the full hierarchy.
- Category hierarchy is unbounded in the backend but GraphQL selection depth is necessarily finite. A search result should expose `path` or `ancestorNames` directly.

## 11. Attribute and measurement-unit administration

Attribute blueprints have strong filtering and lifecycle coverage, but their list ordering is fixed and not described as a client-selectable capability. If staff need alternative ordering, add an allow-listed sort input with the same stable ID tie-breaker described for products.

`measurementUnits` is read-only. Attribute and option mutations may reference a unit, but no contract creates, renames, archives, or deletes one. Choose and document one ownership model:

- migration-owned reference data, with additions deployed through reviewed database changes; or
- admin-owned reference data, with guarded mutations, usage counts, stable symbols and deletion refusal while referenced.

The frontend currently treats units as reference data and does not present nonfunctional maintenance controls.

## 12. Duplicate discovery

The merge command is verified and implemented, but there is no query for duplicate candidates or similarity reasons. The frontend therefore requires an explicit survivor ID, reads that product, and shows a comparison before confirmation. A future candidate query should return both product IDs, stable match reasons, and the fields that differ; merge authority must remain in the existing command.

## Adoption contract

Frontend capability switches live in one product-list adapter. Until the generated schema contains the proposed fields, sorting controls and thumbnail `<img>` elements remain unavailable. Enabling a feature requires:

1. refresh the checked-in schema snapshot;
2. add the field/input to the operation document;
3. regenerate types;
4. update the adapter capability and mapping;
5. add contract and interaction tests.

No page-level request construction should be added when these capabilities arrive.

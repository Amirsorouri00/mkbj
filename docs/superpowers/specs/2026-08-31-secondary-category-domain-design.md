# Secondary Category Domain Design

## Status

Approved in conversation on 2026-08-31 for implementation on branch `task-177`.

## Purpose

Build an independent `SecondaryCatalog` module for merchandising and product promotion. Secondary categories are curated by product administrators, change independently of primary Catalog categories, and may group products from any primary category. The module must support both secured back-office management and public storefront reads.

## Architectural decision

`SecondaryCatalog` is a separate business module, not part of Catalog and not owned by a missing Admin module. It follows the current `main` branch conventions:

- `Application`: domain, inbound/outbound ports, command services.
- `Adapter/Outbound`: SQL Server persistence and the Catalog product-existence ACL.
- `QueryApplication`: direct, read-only EF projections.
- `Adapter/Inbound/GraphQL`: secured back-office queries and mutations.
- `Adapter/Inbound/Rest`: public read endpoints.
- `Installer`: the module composition root.

The current solution still uses `src/BuildingBlocks/Contracts`, so published DTOs live under `Contracts/SecondaryCatalog` for this task. They will move to a module-owned Contracts project when the planned solution-wide Contracts refactor reaches `main`.

## Domain model

### SecondaryCategory aggregate

`SecondaryCategory : AggregateRoot<long>` owns hierarchy and lifecycle metadata:

- `Id`: SQL Server `bigint IDENTITY`.
- `ParentId`: nullable self-reference; null identifies a root.
- `Name`: required, trimmed, maximum 200 characters.
- `Slug`: required, normalized lower-case identifier, maximum 200 characters.
- `Description`: optional, maximum 1,000 characters.
- `DisplayOrder`: non-negative integer used among siblings.
- `IsActive`: controls eligibility for storefront projection.
- `IsDeleted`, `DeletedAt`: soft-delete state.
- `CreatedAt`, `UpdatedAt`: audit timestamps.
- SQL `rowversion`: optimistic concurrency token.

The aggregate exposes create, update-details, move, activate, deactivate, and soft-delete behavior. Repository/application checks supply facts that cannot be decided by one aggregate instance, including parent state, leaf status, and cycle detection.

### SecondaryCategoryProduct association

Product membership is stored separately from the category aggregate so a popular category never loads millions of product assignments into memory:

- `SecondaryCategoryId`: `bigint`, foreign key to SecondaryCategory.
- `ProductId`: `uniqueidentifier`, matching the current Catalog product identifier.
- `AssignedAt`: assignment timestamp.
- `AssignedByUserId`: optional `uniqueidentifier` audit identity when available.
- Composite primary key `(SecondaryCategoryId, ProductId)` prevents duplicates.

There is intentionally no SQL foreign key to Catalog. Catalog owns products in another module/schema. Assignment validates product existence and active state through a consumer-owned `ICatalogProductLookup` outbound port implemented by an ACL adapter calling Catalog's current inbound service.

## Invariants and lifecycle

1. Secondary categories have no relationship or foreign key to primary categories.
2. A product may belong to any number of secondary categories.
3. A secondary category may contain products from multiple primary categories.
4. A category cannot be its own parent or be moved beneath one of its descendants.
5. A child can be created or moved only beneath an existing, non-deleted parent.
6. A category can be activated only when its parent is active and not deleted. Root categories have no parent prerequisite.
7. Deactivating a category hides that category and all descendants from public queries without rewriting every descendant row.
8. Only a leaf category can be soft-deleted. A category with direct or indirect children must first have those children moved or deleted.
9. Deletion is always soft deletion. It records `IsDeleted` and `DeletedAt`; no category row is physically removed.
10. Product assignment requires a non-deleted category and an existing active Catalog product.
11. Assigning the same product twice is idempotent. Unassigning a missing association is also idempotent.
12. Public queries return only categories whose full ancestor path is active and non-deleted.

## Persistence model and indexes

SQL schema: `secondary_catalog`.

Tables:

- `SecondaryCategories` with self foreign key `ParentId` using `DeleteBehavior.Restrict`.
- `SecondaryCategoryProducts` with cascade delete disabled because category deletion is soft.

Indexes:

- Unique filtered index on normalized `Slug` where `IsDeleted = 0`.
- `(ParentId, IsDeleted, IsActive, DisplayOrder, Id)` for hierarchy reads.
- `(ProductId, SecondaryCategoryId)` for reverse product membership.
- Composite primary key `(SecondaryCategoryId, ProductId)` for category product paging and uniqueness.

Adjacency-list hierarchy is selected instead of a closure table. Category counts and hierarchy mutation frequency are expected to be small compared with the product-assignment table; recursive SQL/ancestor checks keep writes simple. A closure table is deferred until measured hierarchy-read pressure justifies the synchronization cost.

## Application commands

The command service exposes:

- `CreateAsync(name, slug, description, parentId, displayOrder)`.
- `UpdateAsync(categoryId, name, slug, description, displayOrder)`.
- `MoveAsync(categoryId, newParentId, displayOrder)`.
- `ActivateAsync(categoryId)`.
- `DeactivateAsync(categoryId)`.
- `DeleteAsync(categoryId)` for leaf-only soft deletion.
- `AssignProductAsync(categoryId, productId, actorUserId)`.
- `UnassignProductAsync(categoryId, productId)`.

Expected refusals use `Result` errors rather than exceptions. Domain exceptions are translated at the application boundary. Error codes include `SECONDARY_CATEGORY_NOT_FOUND`, `SECONDARY_CATEGORY_PARENT_INVALID`, `SECONDARY_CATEGORY_CYCLE`, `SECONDARY_CATEGORY_PARENT_INACTIVE`, `SECONDARY_CATEGORY_HAS_CHILDREN`, `SECONDARY_CATEGORY_DELETED`, `SECONDARY_CATEGORY_SLUG_CONFLICT`, and `SECONDARY_CATEGORY_PRODUCT_INVALID`.

## Back-office GraphQL

Because this `main` branch contains no Admin module, `SecondaryCatalog.Adapter.Inbound.GraphQL` owns a dedicated endpoint:

`/api/v1/admin/secondary-catalog/graphql`

Every resolver and the endpoint require the existing `Admin` authorization policy. Mutations correspond one-to-one with the application commands. Queries provide paginated category search, a category detail, direct children, and paginated assigned product IDs. Resolvers remain IO-free shells over inbound/query ports.

This task does not re-enable or redesign the currently commented JWT setup. The endpoint declares and tests the Admin policy requirement; host authentication configuration remains an external deployment concern unless separately requested.

## Public REST reads

- `GET /api/v1/secondary-categories/tree` returns the effective active hierarchy.
- `GET /api/v1/secondary-categories/{id:long}` returns one effectively active category.
- `GET /api/v1/secondary-categories/{id:long}/products?page=1&pageSize=20` returns paginated assigned product IDs.
- `GET /api/v1/products/{productId:guid}/secondary-categories` returns the effective active categories promoting one product.

Page size is validated in the range 1–100. Public endpoints return identifiers and secondary-category metadata, not Catalog-owned product detail. This avoids N+1 Catalog calls and lets storefront composition batch or hydrate products through its own read path.

## Scale and consistency

The six-million-product estimate affects the association table, not hierarchy storage. Queries must filter and page in SQL, never load all assignments. No category aggregate contains an assignment collection. Command operations use targeted existence checks and unique constraints. Concurrent edits use rowversion and return a conflict result.

Cross-module product validation is synchronous and not transactional with Catalog. The assignment stores only a product identifier; if a product is later deactivated, public product composition remains responsible for excluding unavailable products. A future cleanup process may remove stale assignments, but it is outside task 177.

## Testing and acceptance

Implementation is test-driven. Required coverage includes:

- Domain tests for normalization, validation, lifecycle, and deleted-state refusal.
- Application tests for parent activation, cycle prevention, leaf-only deletion, idempotent assignment, and Catalog validation.
- Persistence-contract tests for mappings, filtered uniqueness, restrictive self-reference, indexes, paging, and SQL-translatable effective-active hierarchy queries.
- API tests for Admin authorization metadata, GraphQL result/error mapping, public visibility, and paging validation.
- Architecture tests proving Application has no EF/adapter dependency and only SecondaryCatalog outbound owns the Catalog dependency.
- A migration for the secondary catalog schema and both tables.

Acceptance is satisfied when an administrator can manage the hierarchy and product assignments through authorized GraphQL, public clients can read only effectively active promotions, all invariants above have executable tests, and the solution builds without introducing unrelated dependency or configuration changes.

## Explicitly out of scope

- Any relationship to primary categories or primary-category attributes.
- Product-plan pricing, scheduling windows, ranking algorithms, or personalization.
- Product detail duplication inside SecondaryCatalog.
- Bulk import, stale-product cleanup jobs, or a closure table.
- The future per-module Contracts refactor.

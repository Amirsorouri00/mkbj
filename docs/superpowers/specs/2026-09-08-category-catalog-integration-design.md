# Category Rename and Catalog Integration Design

## Decision

The CTO-approved model has no Secondary Category module. The concept is `Category`, and it is
owned by the existing Catalog module alongside Products and future Attributes.

Implementation is deliberately split into two reviewable stages:

1. Rename the standalone Secondary Category implementation to Category and commit the complete,
   buildable rename.
2. Move the Category concept into Catalog, organize Catalog by business concept inside each
   existing architectural boundary, and remove the standalone Category projects.

No compatibility aliases are retained. There is no production category data to migrate.

## Canonical Naming

| Surface | Name |
|---|---|
| Aggregate | `Category` |
| Assignment | `CategoryProduct` |
| Module namespace after integration | `EwaysVNext.Modules.Catalog` |
| Contracts project | `EwaysVNext.Modules.Catalog.Contracts` |
| Contracts namespace | `EwaysVNext.Modules.Catalog.Contracts` |
| Database schema | `catalog` |
| Database tables | `Category`, `CategoryProduct` |
| REST collection | `/api/v1/categories` |
| Admin GraphQL endpoint | `/api/v1/admin/category/graphql` |
| GraphQL fields | `category*` / `categories` |
| Error-code prefix | `CATEGORY_` |

Plural business-concept folders are exactly `Categories`, `Products`, and `Attributes`. The
incorrect spelling `Categorys` is eliminated.

## Stage 1: Standalone Rename

Stage 1 changes terminology without changing ownership, project boundaries, folder organization,
or behavior. The temporary standalone module path becomes `src/Modules/Category`, and the
temporary contract project becomes `EwaysVNext.Modules.Category.Contracts`. All types,
namespaces, project names, DI methods,
GraphQL fields/server name, REST routes, endpoint tags, error codes, DbSets, configurations,
tests, and active documentation use Category terminology.

The unshipped `20260907132714_AddSecondaryCategoryHierarchyAndSnowflakeIds` migration is replaced
by the Category-named equivalent. Because no category data exists, its model creates singular
`catalog.Category` and `catalog.CategoryProduct` tables directly. Earlier unrelated migrations
remain immutable.

Stage 1 preserves all current behavior: hierarchy depth, cycle protection, lifecycle semantics,
find-node/parent/descendants queries, single and bulk state mutations, product assignments,
cancellation, one-query hierarchy reads, and atomic saves.

No Catalog or Product file is moved, regrouped, or otherwise restructured during Stage 1.

## Stage 2: Catalog Ownership and Concept Organization

The standalone Category projects are merged into the matching Catalog architectural projects.
Architectural boundaries remain Application, QueryApplication, Adapter, Contracts, and Installer;
business-concept folders exist within those boundaries.

### Intended final shape

Files from an architectural project are moved into that project's business-concept folder. An
architectural layer name is never repeated beneath the concept folder. For example, Category
commands end at `Catalog/Application/Categories/Commands`, never
`Catalog/Application/Categories/Application/Commands`.

```text
Catalog
├── Application
│   ├── Categories/{Commands,Domain,Ports,Services}
│   └── Products/{Domain,Ports,Services,Validation}
├── QueryApplication
│   ├── Categories
│   └── Products
├── Adapter
│   ├── Inbound/GraphQL/{Categories,Products}
│   ├── Inbound/Rest/{Categories,Products}
│   └── Outbound/Persistence/{Categories,Products}
├── Contracts/{Categories,Products}
└── Installer
```

### Current-to-target mapping

| Current location after Stage 1 | Final location |
|---|---|
| `EwaysVNext.Modules.Category.Contracts/Categories/CategoryDtos.cs` | `EwaysVNext.Modules.Catalog.Contracts/Categories/CategoryDtos.cs` |
| `src/Modules/Category/Application/Commands/*` | `src/Modules/Catalog/Application/Categories/Commands/*` |
| `src/Modules/Category/Application/Domain/*` | `src/Modules/Catalog/Application/Categories/Domain/*` |
| `src/Modules/Category/Application/Ports/*` | `src/Modules/Catalog/Application/Categories/Ports/*` |
| `src/Modules/Category/Application/Services/*` | `src/Modules/Catalog/Application/Categories/Services/*` |
| `src/Modules/Category/QueryApplication/*` | `src/Modules/Catalog/QueryApplication/Categories/*` |
| `src/Modules/Category/Adapter/Inbound/Rest/*` | `src/Modules/Catalog/Adapter/Inbound/Rest/Categories/*` |
| `src/Modules/Category/Adapter/Inbound/GraphQL/*` | `src/Modules/Catalog/Adapter/Inbound/GraphQL/Categories/*` |
| `src/Modules/Category/Adapter/Outbound/Persistence/*` | `src/Modules/Catalog/Adapter/Outbound/Persistence/Categories/*` |
| `src/Database/Category/Configurations/*` | `src/Database/Catalog/Configurations/Categories/*` |
| `src/Modules/Catalog/Application/Domain/Products/*` | `src/Modules/Catalog/Application/Products/Domain/*` |
| `src/Modules/Catalog/Application/Ports/*Product*` | `src/Modules/Catalog/Application/Products/Ports/*` |
| `src/Modules/Catalog/Application/Service/*` | `src/Modules/Catalog/Application/Products/Services/*` |
| `src/Modules/Catalog/Application/Validation/*` | `src/Modules/Catalog/Application/Products/Validation/*` |
| `src/Modules/Catalog/Adapter/Inbound/Rest/Dtos/*` | `src/Modules/Catalog/Adapter/Inbound/Rest/Products/Dtos/*` |
| `src/Modules/Catalog/Adapter/Inbound/Rest/Endpoints/*` | `src/Modules/Catalog/Adapter/Inbound/Rest/Products/Endpoints/*` |
| `src/Modules/Catalog/Adapter/Outbound/Repositories/ProductRepository.cs` | `src/Modules/Catalog/Adapter/Outbound/Persistence/Products/ProductRepository.cs` |
| `src/Modules/Catalog/Adapter/Outbound/ReservationExpirationService.cs` | `src/Modules/Catalog/Adapter/Outbound/Persistence/Products/ReservationExpirationService.cs` |
| `src/Database/Catalog/Configurations/ProductConfiguration.cs` | `src/Database/Catalog/Configurations/Products/ProductConfiguration.cs` |

`Catalog/QueryApplication/Products` and `EwaysVNext.Modules.Catalog.Contracts/Products` already follow the target
concept grouping and stay in place. No Attribute implementation exists on this branch, so no
placeholder types or empty tracked directories are invented; future Attribute files must enter
the corresponding `Attributes` concept folders.

### Project and reference changes

- Catalog.Application absorbs Category domain, commands, ports, and services.
- Catalog.QueryApplication absorbs Category read ports and EF projection services.
- Catalog.Adapter.Outbound absorbs Category repositories and persistence unit of work.
- Catalog.Adapter.Inbound.Rest absorbs Category REST endpoints.
- Category GraphQL becomes Catalog.Adapter.Inbound.GraphQL under Catalog.
- The `EwaysVNext.Modules.Catalog.Contracts` project absorbs Category DTOs; its root namespace is
  also `EwaysVNext.Modules.Catalog.Contracts`, and no singular `Catalog.Contract` name remains.
- Catalog.Contracts remains the only public cross-module contract; consumers do not reference
  Catalog.Application, Catalog.QueryApplication, or Catalog.Adapter.
- Catalog.Installer registers all Product and Category services/adapters and GraphQL.
- Host calls only `AddCatalogModule(...)` and maps Category endpoints from Catalog adapters.
- The standalone Category solution entries, projects, installer, and contract project are removed.

The former same-module Catalog product lookup is retained behind the existing Category outbound
port during the move to minimize behavioral change. It resolves Catalog's public product
availability contract; redesigning that collaboration is outside this restructuring.

## Persistence and Migrations

Category configurations end under `src/Database/Catalog/Configurations/Categories`. Entity
namespaces contain the `Catalog` module segment, so the unified DbContext maps them to the
`catalog` schema. Explicit `ToTable` calls use singular table names.

The Category foreign key, filtered slug index, hierarchy index, composite CategoryProduct key,
Snowflake `ValueGeneratedNever`, and optimistic concurrency mapping are preserved. SQL recursive
queries target `[catalog].[Category]`. No transaction architecture changes are made.

## Testing and Commit Boundaries

- Rename API/schema/path expectations first and observe the intended RED failures.
- Commit the complete buildable Stage 1 rename.
- Before Stage 2 file moves, retain this mapping as the review baseline.
- Add Catalog-composition expectations before merging projects.
- Move one architectural boundary at a time, keeping references buildable at each commit.
- Run focused Category tests after every boundary move.
- Finish with the complete unit suite, architecture suite, solution build, `git diff --check`,
  clean status, and a case-insensitive scan for obsolete Secondary Category terminology.

After every restructuring commit, verify the affected project build plus DI registration,
GraphQL schema, REST endpoint registration, EF entity configuration, and migration/snapshot
consistency before proceeding to the next boundary.

Existing unrelated failures, particularly Customer's ambient `DateTime.Now` architecture failure,
are reported but not changed.

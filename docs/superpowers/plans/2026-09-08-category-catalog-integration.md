# Category Rename and Catalog Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename Secondary Category to Category, then make Category a first-class concept inside the Catalog module with concept-oriented folders.

**Architecture:** First perform a behavior-preserving, buildable standalone rename. Then merge each Category architectural project into the matching Catalog project, reorganize Product files by concept, and remove standalone Category composition without changing domain behavior.

**Tech Stack:** .NET 10, C#, EF Core 10, SQL Server, HotChocolate GraphQL, ASP.NET Core minimal APIs, xUnit v3.

**Spec:** `docs/superpowers/specs/2026-09-08-category-catalog-integration-design.md`

## Global Constraints

- Work only on `feature/SHOP-424`.
- Commit Stage 1 rename before beginning Stage 2 moves.
- Preserve Application, QueryApplication, Adapter, Contracts, and Installer boundaries.
- Project and namespace are both `EwaysVNext.Modules.Catalog.Contracts`; singular
  `Catalog.Contract` naming is forbidden.
- Catalog.Contracts is the Catalog module's only public cross-module contract boundary; other
  modules must not reference Catalog.Application, Catalog.QueryApplication, or Catalog.Adapter.
- Do not add compatibility aliases, transaction architecture, cross-module events, or duplicate DTOs.
- Database schema is `catalog`; tables are singular `Category` and `CategoryProduct`.
- Preserve all existing Category behavior and current public/admin authorization semantics.
- Use `Categories`, `Products`, and `Attributes`; never `Categorys`.
- Do not fabricate Attribute types or tracked placeholder directories.
- Stage 1 preserves standalone Category ownership and existing project/folder boundaries; it does
  not move or reorganize any Catalog or Product file.
- Stage 2 merges project contents directly into concept folders; never create paths such as
  `Catalog/Application/Categories/Application`.
- After each restructuring commit, verify affected builds, DI, GraphQL, REST, EF configuration,
  and migration/snapshot consistency.

---

### Task 1: Define the Category naming contract

**Files:**
- Modify: `tests/UnitTests/SecondaryCategoryApiAdapterTests.cs`
- Modify: `tests/UnitTests/SecondaryCategoryPersistenceModelTests.cs`
- Modify: `tests/UnitTests/SecondaryCategoryProductionCompositionTests.cs`
- Modify: `tests/ArchitectureTests/HexagonalBoundaryTests.cs`

**Interfaces:**
- Consumes: existing Secondary Category API, model, and composition behavior.
- Produces: executable expectations for Category API names, `catalog.Category`, `catalog.CategoryProduct`, and Category project/type names.

- [ ] **Step 1: Rename test fixtures and expected API/schema names to Category terminology**

  Expect `/api/v1/categories`, `/api/v1/admin/category/graphql`, GraphQL `category*` fields,
  singular tables, `CATEGORY_*` errors, and Category-named DI methods/types.

- [ ] **Step 2: Run the focused tests and verify RED**

  Run: `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --no-restore --filter "FullyQualifiedName~Category"`

  Expected: compile/assertion failures naming the still-existing Secondary Category API/types.

- [ ] **Step 3: Commit the RED naming expectations**

  Run: `git commit -m "test: define category naming"`

### Task 2: Rename the standalone module to Category

**Files:**
- Rename: `EwaysVNext.Modules.SecondaryCategory.Contract` → `EwaysVNext.Modules.Category.Contracts`
- Rename: `src/Modules/SecondaryCategory` → `src/Modules/Category`
- Rename: `src/Database/SecondaryCategories` → `src/Database/Category`
- Modify: `src/Database/EwaysVNextDbContext.cs`
- Replace: `src/Database/Migrations/20260907132714_AddSecondaryCategoryHierarchyAndSnowflakeIds.cs`
- Replace: `src/Database/Migrations/20260907132714_AddSecondaryCategoryHierarchyAndSnowflakeIds.Designer.cs`
- Modify: `src/Database/Migrations/EwaysVNextDbContextModelSnapshot.cs`
- Modify: `src/Host/EwaysVNext.Host/Program.cs`
- Modify: `EwaysVNext.slnx`
- Rename: `tests/UnitTests/SecondaryCategory*.cs` → `tests/UnitTests/Category*.cs`

**Interfaces:**
- Consumes: naming expectations from Task 1.
- Produces: buildable standalone `EwaysVNext.Modules.Category` projects and Category-named REST/GraphQL/database surfaces.

- [ ] **Step 1: Rename paths and project files**

  Rename all project filenames, project references, assembly/root namespaces, solution folders,
  and test filenames from SecondaryCategory to Category.

  Keep the renamed Category projects standalone. Do not edit Catalog project paths, Product
  namespaces, Product folders, or Catalog composition in this task.

- [ ] **Step 2: Rename source symbols and public surfaces**

  Apply `SecondaryCategory` → `Category`, `SecondaryCategories` → `Categories`, error prefix
  `SECONDARY_CATEGORY_` → `CATEGORY_`, REST `/secondary-categories` → `/categories`, and
  GraphQL `secondaryCategory*` → `category*`. Eliminate `SecondaryCatalog` and `Categorys` remnants.

- [ ] **Step 3: Update persistence mapping**

  Map `DbSet<Category> Categories`, `DbSet<CategoryProduct> CategoryProducts`, schema `catalog`,
  tables `Category` and `CategoryProduct`, and update recursive SQL to `[catalog].[Category]`.
  Replace the unshipped Category migration/model snapshot while retaining all keys and indexes.

- [ ] **Step 4: Run focused tests and build**

  Run: `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --no-restore --filter "FullyQualifiedName~Category"`

  Run: `dotnet build EwaysVNext.slnx --no-restore`

  Expected: focused tests and build pass.

- [ ] **Step 5: Verify terminology and commit Stage 1**

  Run: `rg -n -i "secondary.?categor|secondary catalog|categorys" --glob "!**/bin/**" --glob "!**/obj/**" .`

  Expected: no active source/project/API matches; historical design documents may be listed and are not runtime surfaces.

  Run: `git commit -m "refactor: rename secondary category to category"`

  Before committing, verify Category DI registration, GraphQL schema, REST routes, EF model,
  migration/model snapshot, and the standalone Category project references.

### Task 3: Define Catalog ownership through executable composition

**Files:**
- Modify: `tests/UnitTests/CategoryProductionCompositionTests.cs`
- Modify: `tests/UnitTests/CategoryApiAdapterTests.cs`
- Modify: `tests/ArchitectureTests/HexagonalBoundaryTests.cs`

**Interfaces:**
- Consumes: standalone Category-named projects from Task 2.
- Produces: tests requiring Category services, queries, REST, and GraphQL to resolve through Catalog composition without standalone Category projects.

- [ ] **Step 1: Update composition tests for Catalog ownership**

  Build a service collection through `AddCatalogModule(configuration, isDevelopment)`, resolve
  Category application/query ports, and execute the Category GraphQL schema through Catalog's
  inbound GraphQL adapter.

- [ ] **Step 2: Run the focused tests and verify RED**

  Run: `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --no-restore --filter "FullyQualifiedName~CategoryProductionCompositionTests"`

  Expected: failure because Catalog does not yet own/register Category.

- [ ] **Step 3: Commit the RED ownership expectations**

  Run: `git commit -m "test: define catalog category ownership"`

### Task 4: Merge Contracts and Application by concept

**Files:**
- Move: `EwaysVNext.Modules.Category.Contracts/Categories/CategoryDtos.cs` → `EwaysVNext.Modules.Catalog.Contracts/Categories/CategoryDtos.cs`
- Move: `src/Modules/Category/Application/*` → `src/Modules/Catalog/Application/Categories/*`
- Move: `src/Modules/Catalog/Application/Domain/Products/*` → `src/Modules/Catalog/Application/Products/Domain/*`
- Move: `src/Modules/Catalog/Application/Ports/Inbound/*` → `src/Modules/Catalog/Application/Products/Ports/Inbound/*`
- Move: `src/Modules/Catalog/Application/Ports/Outbound/*` → `src/Modules/Catalog/Application/Products/Ports/Outbound/*`
- Move: `src/Modules/Catalog/Application/Service/*` → `src/Modules/Catalog/Application/Products/Services/*`
- Move: `src/Modules/Catalog/Application/Validation/*` → `src/Modules/Catalog/Application/Products/Validation/*`
- Modify: `src/Modules/Catalog/Application/EwaysVNext.Modules.Catalog.Application.csproj`

**Interfaces:**
- Consumes: Category contracts/application types and existing Product application types.
- Produces: Category and Product namespaces under Catalog.Application and Catalog.Contracts.

- [ ] **Step 1: Move Category contracts and update namespaces/references**

  Rename the existing Catalog contracts directory/project if needed so both project and namespace
  are exactly `EwaysVNext.Modules.Catalog.Contracts`. Use
  `EwaysVNext.Modules.Catalog.Contracts.Categories` and remove references to the temporary
  Category contracts project. Do not create a singular `Catalog.Contract` directory or project.

- [ ] **Step 2: Move Category application code and update namespaces**

  Place commands, domain, ports, services, and DI under `Catalog.Application.Categories` without
  changing method bodies or domain rules. Move the contents of the former Application project
  directly; do not retain an `Application` directory beneath `Categories`.

- [ ] **Step 3: Group Product application code**

  Move existing Product files beneath `Application/Products` and update namespaces/usings only.

- [ ] **Step 4: Build and run Category/Product unit tests**

  Run: `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --no-restore --filter "FullyQualifiedName~Category|FullyQualifiedName~Product"`

  Expected: pass.

- [ ] **Step 5: Commit**

  Verify the Catalog.Contracts and Catalog.Application builds, DI service descriptors, and all
  Category/Product unit tests before committing.

  Run: `git commit -m "refactor: merge category contracts and application into catalog"`

### Task 5: Merge QueryApplication and outbound persistence

**Files:**
- Move: `src/Modules/Category/QueryApplication/*` → `src/Modules/Catalog/QueryApplication/Categories/*`
- Move: `src/Modules/Category/Adapter/Outbound/Persistence/*` → `src/Modules/Catalog/Adapter/Outbound/Persistence/Categories/*`
- Move: `src/Modules/Category/Adapter/Outbound/CatalogProductLookup.cs` → `src/Modules/Catalog/Adapter/Outbound/Persistence/Categories/CatalogProductLookup.cs`
- Move: `src/Modules/Catalog/Adapter/Outbound/Repositories/ProductRepository.cs` → `src/Modules/Catalog/Adapter/Outbound/Persistence/Products/ProductRepository.cs`
- Move: `src/Modules/Catalog/Adapter/Outbound/ReservationExpirationService.cs` → `src/Modules/Catalog/Adapter/Outbound/Persistence/Products/ReservationExpirationService.cs`
- Move: `src/Database/Category/Configurations/*` → `src/Database/Catalog/Configurations/Categories/*`
- Move: `src/Database/Catalog/Configurations/ProductConfiguration.cs` → `src/Database/Catalog/Configurations/Products/ProductConfiguration.cs`
- Modify: `src/Modules/Catalog/QueryApplication/EwaysVNext.Modules.Catalog.QueryApplication.csproj`
- Modify: `src/Modules/Catalog/Adapter/Outbound/EwaysVNext.Modules.Catalog.Adapter.Outbound.csproj`
- Modify: `src/Modules/Catalog/Adapter/Outbound/DependencyInjection.cs`

**Interfaces:**
- Consumes: Catalog-owned Category and Product application/contract types from Task 4.
- Produces: Catalog-owned Category queries/repositories and concept-grouped Product persistence.

- [ ] **Step 1: Merge Category QueryApplication and registrations**

  Keep direct EF projections, admin lifecycle visibility, one-query descendant materialization,
  deterministic ordering, and all existing query-port signatures.

- [ ] **Step 2: Merge Category outbound persistence**

  Preserve recursive SQL, cancellation, cycle safety, bulk subtree loading, unit-of-work behavior,
  and product availability gateway behavior.

- [ ] **Step 3: Group Product outbound and database configuration files**

  Change only paths/namespaces needed for concept organization; preserve runtime registration.

- [ ] **Step 4: Run query, persistence, service, and reservation tests**

  Run: `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --no-restore --filter "FullyQualifiedName~Category|FullyQualifiedName~ProductReservation"`

  Expected: pass.

- [ ] **Step 5: Commit**

  Verify query DI, repository DI, EF entity mappings, recursive SQL table references, and the
  migration/model snapshot before committing.

  Run: `git commit -m "refactor: merge category queries and persistence into catalog"`

### Task 6: Merge inbound adapters and Catalog composition

**Files:**
- Move: `src/Modules/Category/Adapter/Inbound/Rest/*` → `src/Modules/Catalog/Adapter/Inbound/Rest/Categories/*`
- Move: `src/Modules/Catalog/Adapter/Inbound/Rest/Dtos/*` → `src/Modules/Catalog/Adapter/Inbound/Rest/Products/Dtos/*`
- Move: `src/Modules/Catalog/Adapter/Inbound/Rest/Endpoints/*` → `src/Modules/Catalog/Adapter/Inbound/Rest/Products/Endpoints/*`
- Move: `src/Modules/Category/Adapter/Inbound/GraphQL/*` → `src/Modules/Catalog/Adapter/Inbound/GraphQL/Categories/*`
- Modify: `src/Modules/Catalog/Installer/ModuleInstaller.cs`
- Modify: `src/Modules/Catalog/Installer/EwaysVNext.Modules.Catalog.Installer.csproj`
- Modify: `src/Host/EwaysVNext.Host/Program.cs`
- Modify: `tests/UnitTests/EwaysVNext.UnitTests.csproj`
- Modify: `EwaysVNext.slnx`
- Remove: temporary standalone Category project files and installer.

**Interfaces:**
- Consumes: Catalog-owned Category application/query/outbound services.
- Produces: REST and GraphQL Category APIs composed exclusively by Catalog.Installer.

- [ ] **Step 1: Merge REST and group Product endpoints**

  Keep existing endpoint behavior while registering both Product and Category endpoint sets from
  Catalog's REST adapter.

- [ ] **Step 2: Move GraphQL under Catalog**

  Rename the project to `EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL`, retain thin Category
  resolvers and the approved `/api/v1/admin/category/graphql` endpoint.

- [ ] **Step 3: Merge installer and Host composition**

  Extend `AddCatalogModule` to register Category application, queries, persistence, REST, and
  GraphQL; remove `AddCategoryModule` and all standalone Category project references.

- [ ] **Step 4: Run focused executor and composition tests**

  Run: `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --no-restore --filter "FullyQualifiedName~CategoryApiAdapterTests|FullyQualifiedName~CategoryProductionCompositionTests"`

  Expected: pass.

- [ ] **Step 5: Commit**

  Verify the GraphQL schema, REST endpoint data sources, Catalog installer registrations, Host
  composition, and absence of standalone Category project references before committing.

  Run: `git commit -m "refactor: complete catalog category integration"`

### Task 7: Final verification

**Files:**
- Verify: all modified source, project, migration, test, and documentation files.

**Interfaces:**
- Consumes: completed Catalog integration.
- Produces: evidence that naming, behavior, dependencies, and repository state satisfy the specification.

- [ ] **Step 1: Run focused Category tests**

  Run: `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --no-restore --filter "FullyQualifiedName~Category"`

- [ ] **Step 2: Run full unit and architecture suites**

  Run: `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --no-restore`

  Run: `dotnet test --project tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj --no-restore`

- [ ] **Step 3: Build the solution**

  Run: `dotnet build EwaysVNext.slnx --no-restore`

- [ ] **Step 4: Verify terminology and removed projects**

  Run: `rg -n -i "secondary.?categor|secondary catalog|categorys|Catalog\.Contract(?!s)" --pcre2 --glob "!**/bin/**" --glob "!**/obj/**" src tests EwaysVNext.Modules.Catalog.Contracts EwaysVNext.slnx`

  Expected: no matches.

- [ ] **Step 5: Verify Git state**

  Run: `git diff --check`

  Run: `git status --short`

  Expected: clean after all requested commits.

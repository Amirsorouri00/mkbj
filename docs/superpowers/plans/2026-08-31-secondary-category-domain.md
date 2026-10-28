# Secondary Category Domain Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an independent SecondaryCatalog module that supports secured GraphQL management and public REST reads.

**Architecture:** SecondaryCategory is a `long`-key hierarchy aggregate. Product membership is a separate keyed association, never an aggregate collection. Application owns commands and ports; outbound adapters own EF Core and the Catalog ACL; QueryApplication owns projections.

**Tech Stack:** .NET 10, EF Core 10/SQL Server, HotChocolate 16, ASP.NET Core, xUnit v3, NetArchTest.

**Spec:** `docs/superpowers/specs/2026-08-31-secondary-category-domain-design.md`

## Global Constraints

- No relationship to primary categories.
- Category IDs use `bigint`; Catalog product IDs remain `Guid`.
- Leaf-only soft deletion; effective public visibility includes every ancestor.
- Only SecondaryCatalog outbound may depend on Catalog Application.
- GraphQL requires the existing `Admin` policy; public paging is bounded to 100.
- Do not change dependencies, authentication configuration, or unrelated modules.
- Follow red-green-refactor; verify generated migrations.

---

### Task 1: Scaffold and defend the module dependency graph

**Files:** create the six projects under `src/Modules/SecondaryCatalog/{Application,Adapter/Outbound,QueryApplication,Adapter/Inbound/Rest,Adapter/Inbound/GraphQL,Installer}`; modify `EwaysVNext.slnx`, `tests/ArchitectureTests/ArchitectureTests.csproj`; create `tests/ArchitectureTests/SecondaryCatalogArchitectureTests.cs`.

- [ ] Add failing architecture tests asserting the approved dependency direction and that no SecondaryCatalog project references Catalog except `Adapter.Outbound`.
- [ ] Run `dotnet test tests/ArchitectureTests/ArchitectureTests.csproj --no-restore --filter SecondaryCatalogArchitectureTests`; expect compilation failure because the projects do not exist.
- [ ] Add minimal `.csproj` files and solution entries. Use existing central package versions unchanged.
- [ ] Re-run the focused test, then inspect references with `dotnet sln EwaysVNext.slnx list` and `rg -n "ProjectReference" src/Modules/SecondaryCatalog`.
- [ ] Commit: `build: scaffold secondary catalog module`.

Approved references: Application -> DDD, Framework, Contracts; Outbound -> Application, Persistence, Catalog.Application; QueryApplication -> Outbound, Contracts, Framework; REST -> QueryApplication, Contracts, Framework; GraphQL -> Application, QueryApplication, Contracts, Framework; Installer -> all five module projects.

### Task 2: Define the domain model test-first

**Files:** create `Application/Domain/Categories/SecondaryCategory.cs`, `Application/Domain/ValueObjects/SecondaryCategorySlug.cs`, `Application/Domain/Assignments/SecondaryCategoryProduct.cs`, `Application/Domain/Events/SecondaryCategoryEvents.cs`, `tests/UnitTests/SecondaryCategoryDomainTests.cs`; modify the unit-test project reference.

- [ ] Write tests for creation and slug normalization; invalid name, slug, order, and parent; self-move; activation under inactive parent; leaf-only soft deletion; deleted-state mutation refusal; and assignment identity.
- [ ] Run `dotnet test tests/UnitTests/UnitTests.csproj --no-restore --filter SecondaryCategoryDomainTests`; expect compile failures.
- [ ] Implement only the tested behavior using `Create`, `Update`, `Move`, `Activate`, `Deactivate`, and `SoftDelete`; create associations through `SecondaryCategoryProduct.Create`.
- [ ] Re-run the focused tests and the Application architecture tests.
- [ ] Commit: `feat: define secondary category domain model`.

The aggregate uses `long` identity and row version. The product association is a separate entity keyed by `(SecondaryCategoryId, ProductId)`, never an aggregate collection.

### Task 3: Implement hierarchy and assignment use cases

**Files:** create `Application/Ports/Inbound/ISecondaryCategoryService.cs`, `Application/Ports/Outbound/ISecondaryCategoryRepository.cs`, `ISecondaryCategoryProductRepository.cs`, `ICatalogProductLookup.cs`, command records, `Application/Services/SecondaryCategoryService.cs`, `Application/DependencyInjection.cs`, and `tests/UnitTests/SecondaryCategoryServiceTests.cs`.

- [ ] Write mocked service tests for missing/inactive parents, cycles, slug conflicts, leaf deletion, deleted categories, invalid Catalog products, and idempotent assignment/unassignment.
- [ ] Run the focused tests and confirm compile failures.
- [ ] Define repository operations for lookup, slug uniqueness, child/cycle checks, add/save, and association existence/add/remove/save. Define `ICatalogProductLookup.ExistsAndIsActiveAsync(Guid)` as the anti-corruption boundary.
- [ ] Implement the smallest orchestration service returning the solution's existing `Result` type; domain invariants remain in domain objects.
- [ ] Re-run focused unit and architecture tests.
- [ ] Commit: `feat: manage secondary category hierarchy`.

### Task 4: Persist the module in its own SQL schema

**Files:** create `Adapter/Outbound/Persistence/SecondaryCatalogDbContext.cs`, entity configurations, repositories, design-time factory, Catalog lookup adapter, dependency injection, persistence tests, and the initial migration. Modify Catalog's published application port only if needed to expose a focused `ExistsAndIsActiveAsync(Guid)` operation, with its own unit test.

- [ ] Write failing EF model tests for schema/table names, self-FK restriction, row version, filtered unique slug, hierarchy/reverse indexes, and composite association key.
- [ ] Run the focused persistence tests and confirm failure.
- [ ] Configure schema `secondary_catalog`; implement repositories and iterative, cancellable ancestor traversal for cycle/effective-active checks.
- [ ] Implement the Catalog ACL without a SQL FK and without exposing Catalog persistence types.
- [ ] Generate the migration:

```powershell
dotnet ef migrations add InitialSecondaryCatalog --project src/Modules/SecondaryCatalog/Adapter/Outbound/EwaysVNext.Modules.SecondaryCatalog.Adapter.Outbound.csproj --startup-project src/Host/EwaysVNext.Host.csproj --context SecondaryCatalogDbContext --output-dir Persistence/Migrations
```

- [ ] Re-run persistence, Catalog, and architecture tests; inspect migration SQL for `bigint`, `uniqueidentifier`, the filtered slug index, and no Catalog FK.
- [ ] Commit: `feat: persist secondary categories`.

### Task 5: Build public read models and queries

**Files:** create DTOs under `src/BuildingBlocks/Contracts/SecondaryCatalog`, `QueryApplication/Ports/ISecondaryCategoryQueries.cs`, query service, dependency injection, and `tests/UnitTests/SecondaryCategoryQueryTests.cs`.

- [ ] Write failing tests for full-ancestor visibility, active trees, category lookup, paged product IDs, reverse product lookup, page validation, and no tracking.
- [ ] Add immutable category, tree-node, and product-page DTOs. Implement page numbers from 1 and page sizes from 1 through 100.
- [ ] Build a public tree from one no-tracking category projection. For item/product queries, validate ancestors with targeted reads and page assignments in SQL.
- [ ] Run focused query and architecture tests.
- [ ] Commit: `feat: query active secondary promotions`.

### Task 6: Expose secured backoffice GraphQL and public REST

**Files:** create GraphQL queries, mutations, types, and DI under `Adapter/Inbound/GraphQL`; create endpoint mapping and DI under `Adapter/Inbound/Rest`; create API tests.

- [ ] Write failing tests for authorization, schema registration, exact REST routes, paging, visibility, and error mapping.
- [ ] Register named server `secondaryCatalogAdmin`; expose admin queries and all approved mutations. Apply Admin authorization to endpoint and fields.
- [ ] Map `/api/v1/admin/secondary-catalog/graphql` with Admin policy and `admin` rate limiting.
- [ ] Map the four approved public GET endpoints. Return product IDs, never duplicated product details.
- [ ] Re-run API, unit, and architecture tests.
- [ ] Commit: `feat: expose secondary catalog APIs`.

### Task 7: Integrate through the installer and host

**Files:** create `Installer/SecondaryCatalogModuleInstaller.cs`; modify `src/Host/EwaysVNext.Host.csproj`, `src/Host/Program.cs`, health checks, and integration tests.

- [ ] Write failing integration tests for service/route registration and admin authorization metadata.
- [ ] Add one module installer, its DbContext/health check, and both inbound adapters.
- [ ] Do not enable or redesign the host's commented authentication setup; only declare and test the existing Admin-policy requirement.
- [ ] Run integration and architecture tests and inspect the dependency graph.
- [ ] Commit: `feat: integrate secondary catalog module`.

### Task 8: Document and verify the complete slice

**Files:** modify `README.md` and onboarding/API documentation only where needed.

- [ ] Document the boundary, SQL schema, endpoints, and deferred module-owned Contracts migration.
- [ ] Scan for forbidden primary-category relationships and assignment collections:

```powershell
rg -n "PrimaryCategory|primary_category" src/Modules/SecondaryCatalog
rg -n "ICollection<SecondaryCategoryProduct>|List<SecondaryCategoryProduct>" src/Modules/SecondaryCatalog/Application
```

- [ ] Run `git diff main...HEAD --check` and review `git diff --stat main...HEAD` for scope.
- [ ] Retry `dotnet restore EwaysVNext.slnx`, then `dotnet build EwaysVNext.slnx --no-restore` and `dotnet test EwaysVNext.slnx --no-build --no-restore`.
- [ ] Record NuGet/Windows TLS credential failures separately as external environment failures. Do not change packages or configuration to bypass them.
- [ ] If restore remains blocked, run every available static check and report refactor failures separately from the baseline blocker.
- [ ] Commit: `docs: document secondary catalog module`.

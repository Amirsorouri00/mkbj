# Secondary Category Corrections Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the missing Secondary Category behavior while preserving the `task-177` per-module Contracts architecture.

**Architecture:** Secondary Category keeps Application-owned ports, its existing UnitOfWork convention, and the unified `EwaysVNextDbContext`. A minimal framework-free Catalog Contract replaces the direct Catalog Application dependency. Hierarchy reads use one database query per operation; state changes use domain methods and one save.

**Tech Stack:** .NET 10, EF Core 10, SQL Server/InMemory, Hot Chocolate, xUnit v3.

**Spec:** `docs/superpowers/specs/2026-09-07-secondary-category-main-template-corrections-design.md`

## Global Constraints

- Keep per-module Contracts framework-independent; never restore centralized `BuildingBlocks.Contracts`.
- Keep Secondary Category DTOs solely in `EwaysVNext.Modules.SecondaryCategory.Contracts`; do not duplicate them in QueryApplication.
- Keep the unified DbContext and existing Secondary Category UnitOfWork/transaction convention.
- Do not add cross-module events, direct aggregate bulk SQL, `ExecuteUpdateAsync`, or a repository-wide migration.
- Treat roots as level one; reject level-six create and any move that would place a descendant below level five.
- Make SQL Server recursive hierarchy queries and InMemory traversal cycle-safe; pass cancellation through every async query, traversal, executor, and save.
- Treat soft-deleted intermediate categories as traversal-transparent while never mutating them during deactivation.
- Generate owned `long` IDs with `IIdGenerator` and map them `ValueGeneratedNever()`.
- Use red-green-refactor and commit each checkpoint.

---

### Task 1: Correct the Catalog Contract boundary

**Files:**
- Create: `EwaysVNext.Modules.Catalog.Contract/EwaysVNext.Modules.Catalog.Contracts.csproj`
- Create: `EwaysVNext.Modules.Catalog.Contract/Products/IProductAvailabilityContract.cs`
- Modify: Catalog Application project, `ProductService`, and DI.
- Modify: Secondary Category `CatalogProductLookup`, Outbound project, legacy centralized-contract references, and `EwaysVNext.slnx`.
- Test: Secondary Category architecture and production-composition tests.

**Interfaces:** Produces `IProductAvailabilityContract.ExistsAndIsActiveAsync(long productId, CancellationToken ct)`; Secondary Category retains local `ICatalogProductLookup`. Both `IProductService` and `IProductAvailabilityContract` resolve to the same scoped `ProductService` instance.

- [ ] Write failing architecture tests proving Catalog Contracts has no project/package dependencies and Secondary Category Outbound references it instead of Catalog Application; write a composition test asserting both Catalog interfaces resolve to the same scoped `ProductService` object.
- [ ] Run focused architecture/composition tests; verify failure is caused by the missing Contract boundary.
- [ ] Add the minimal Contract, implement it in `ProductService`, map both interfaces to one scoped implementation, and change only Secondary Category's outbound gateway dependency.
- [ ] Run focused tests, inspect project references, and build the solution.
- [ ] Commit: `refactor: route secondary category through catalog contract`.

### Task 2: Persist Snowflake IDs and explicit self-reference

**Files:**
- Modify: Secondary Category aggregate, service, Application DI, database configuration, unified migration/snapshot.
- Test: domain, service, and EF model/persistence tests.

**Interfaces:** `SecondaryCategory.Create(long id, ...)`; explicit `Parent` and read-only `Children`; consumes `IIdGenerator.Generate()`.

- [ ] Write failing tests using literal ID `912345678901L`; verify create returns it only after save. Assert `ValueGenerated.Never`, optional self-FK, both navigations, and `DeleteBehavior.Restrict`. Persist/reload a parent and child.
- [ ] Run focused tests and verify the expected ID/navigation failures.
- [ ] Inject `IIdGenerator`, pass the ID into the aggregate, configure backed navigations and `ValueGeneratedNever()`, and add one unified-database migration.
- [ ] Run focused tests, architecture tests, and build.
- [ ] Commit: `fix: persist secondary category snowflake ids`.

### Task 3: Replace sequential hierarchy queries and enforce depth five

**Files:**
- Modify: `ISecondaryCategoryRepository`, `SecondaryCategoryRepository`, and `SecondaryCategoryService`.
- Test: service and repository/query tests.

**Interfaces:** Replace `IsDescendantAsync` with a hierarchy-loading repository operation whose result contains the topology needed for the caller's validation in one round-trip. The same cycle-safe hierarchy machinery supplies a tracked subtree for deactivation; no per-level query API is introduced.

- [ ] Write failing tests for roots at level one, successful creation through level five, rejected level-six creation, a direct descendant cycle, a corrupt stored cycle that terminates safely, and a move whose deepest descendant would exceed level five. Include a soft-deleted intermediate node in the subtree and verify cancellation reaches both database and in-memory traversal paths.
- [ ] Run focused tests; verify depth tests fail and current traversal issues multiple queries.
- [ ] Use one recursive, visited-path hierarchy query on SQL Server and a single-query materialization plus visited-ID traversal for InMemory. Each validation performs one hierarchy round-trip, includes deleted topology nodes, calculates proposed parent depth and complete subtree height, and returns `SECONDARY_CATEGORY_CYCLE` or `SECONDARY_CATEGORY_DEPTH_EXCEEDED` with the specified create/move message.
- [ ] Run focused tests, all Secondary Category unit tests, and architecture tests.
- [ ] Commit: `fix: enforce efficient secondary category hierarchy rules`.

### Task 4: Atomically deactivate a hierarchy

**Files:**
- Modify: `SecondaryCategoryService` and repository test doubles.
- Test: Secondary Category service and unified-context persistence tests.

**Interfaces:** Consumes Task 3's tracked subtree and existing `ISecondaryCategoryUnitOfWork`; produces one-save hierarchy deactivation.

- [ ] Write failing tests with an active root, child, soft-deleted intermediate node, and active grandchild below that deleted node. Assert traversal continues to the grandchild, identical `UpdatedAt` on changed non-deleted nodes, deleted-state preservation, caller cancellation through executor/query/save, exactly one save, rollback on failure, and activation of only the selected node.
- [ ] Run focused tests; verify descendants remain active with current code.
- [ ] Load the subtree once, call `Deactivate(now)` for each eligible aggregate, and save once inside the existing transaction. Add no bulk DML, nested transaction, or descendant activation.
- [ ] Run focused tests, all Secondary Category tests, and architecture tests.
- [ ] Commit: `fix: deactivate secondary category hierarchies atomically`.

### Task 5: Expose and verify the GraphQL hierarchy

**Files:**
- Modify: `EwaysVNext.Modules.SecondaryCategory.Contract/SecondaryCategories/SecondaryCategoryDtos.cs`.
- Modify: Secondary Category admin query port/implementation and GraphQL query resolver.
- Verify: QueryApplication imports DTOs from `EwaysVNext.Modules.SecondaryCategory.Contracts` and defines no duplicate transport DTOs.
- Verify: existing create/activate/deactivate mutations and error helper.
- Test: API adapter and query tests.

**Interfaces:** Produces `GetHierarchyAsync(CancellationToken)` and an admin tree DTO with lifecycle fields and recursive `Children`.

- [ ] Write failing authorized Hot Chocolate executor tests for a literal root/child/grandchild hierarchy containing inactive and deleted records, create ID `912345678901L`, activate/deactivate dispatch, unauthorized hierarchy access, and `message`/`code`/`httpStatus` mapping for not-found, invalid parent, inactive parent, deleted category, cycle, depth exceeded, slug conflict, concurrency conflict, and persistence conflict.
- [ ] Run focused tests; verify hierarchy and incomplete executor behaviors fail.
- [ ] Load all admin rows in one cancellation-aware no-tracking query, assemble the tree with cycle-safe in-memory traversal using only DTOs from `SecondaryCategory.Contracts`, expose one authorized Admin resolver, and reuse the existing GraphQL error conversion.
- [ ] Run focused tests, all unit tests, and architecture tests.
- [ ] Commit: `feat: expose secondary category graphql hierarchy`.

### Task 6: Final verification

**Files:** Modify only scoped files required by failures introduced above.

**Interfaces:** Produces an evidence-based command/result report.

- [ ] Run `git diff --check`, architecture tests, and project-reference inspection.
- [ ] Run `dotnet restore EwaysVNext.slnx` and `dotnet build EwaysVNext.slnx --no-restore` without dependency downgrades.
- [ ] Run `dotnet test --solution EwaysVNext.slnx --no-build`; explicitly run any other discovered test projects.
- [ ] Report absent test project files and environment failures separately from implementation failures.
- [ ] Inspect migration/model snapshot, working tree, and checkpoint commits; commit only a scoped correction if verification requires one.

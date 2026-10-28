# Unified Category Status Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace category `IsActive`/`IsDeleted` with one `CategoryStatus`, and support safe status changes through `updateCategory` and `setCategoryStatus`.

**Architecture:** Keep the canonical enum in Catalog Contracts and use it directly in the aggregate, application commands, read models, and GraphQL. `CategoryService` loads every prerequisite before mutating tracked aggregates; EF stores the enum as an integer and a new migration preserves existing data.

**Tech Stack:** .NET 10, C# 14, EF Core 10, SQL Server/InMemory, Hot Chocolate, xUnit v3, AwesomeAssertions, NSubstitute.

**Spec:** `docs/superpowers/specs/2026-09-27-category-status-design.md`

## Global Constraints

- `CategoryStatus` is exactly `Inactive = 0`, `Active = 1`, `Deleted = 2` and is the only category lifecycle state.
- Category IDs remain CLR `long`/Snowflake IDs and GraphQL `ID` strings.
- Each application operation performs at most one `SaveChangesAsync`; no new explicit transaction is allowed.
- Catalog Application stays free of EF Core types; QueryApplication remains read-only.
- Native Hot Chocolate paging on `categories` and `categoryProducts` remains deferred and unchanged.
- Do not add filtering/sorting middleware, authorization, or endpoint-path changes.
- Do not edit the historical category migration; add a new migration and update the snapshot.
- Before every commit, stage only that task's files, show `git diff --cached`, and wait for explicit user approval.

## Review Focus

- Failed update-to-deleted must not leave detail changes tracked and must not save.
- Deactivating an already-inactive root must still deactivate any live descendants below it.
- Undefined CLR enum values must return `CATEGORY_STATUS_INVALID` before I/O.
- Contradictory legacy rows (`IsActive = 1`, `IsDeleted = 1`) must migrate to `Deleted`.
- Deleted state must remain terminal across update, move, assignment, status, and compatibility-delete paths.

---

### Task 1: Canonical contract and aggregate state

**Files:**
- Modify: `EwaysVNext.Modules.Catalog.Contracts/Categories/CategoryDtos.cs`
- Modify: `src/Modules/Catalog/Application/Categories/Domain/Category.cs`
- Modify: `tests/UnitTests/CategoryDomainTests.cs`

**Interfaces:**
- Produces: `CategoryStatus { Inactive = 0, Active = 1, Deleted = 2 }`.
- Produces: `Category.Status` and `Category.SetStatus(CategoryStatus targetStatus, bool parentIsActive, bool hasDependencies, DateTimeOffset now)`.
- Produces: status-bearing `CategoryAdminFilterDto`, `CategoryAdminDto`, and `CategoryAdminTreeNodeDto`.

- [ ] **Step 1: Write failing aggregate tests**

Assert initial `Inactive`; valid activation; inactive-parent refusal; active-to-inactive event; deletion timestamp/event; non-deleted same-status idempotency; dependency refusal; every transition from `Deleted` (including `Deleted` again) returning `CATEGORY_DELETED`; and `(CategoryStatus)999` throwing a domain error with `CATEGORY_STATUS_INVALID`.

- [ ] **Step 2: Prove the focused tests fail**

Run: `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter FullyQualifiedName~CategoryDomainTests`

Expected: FAIL because the enum, property, and transition method do not exist.

- [ ] **Step 3: Implement the enum and aggregate transition**

Rename `CategoryAdminStatus` to `CategoryStatus`; replace both admin DTO booleans with `Status`; replace aggregate flags with `Status`; initialize to `Inactive`; centralize guards, timestamps, and lifecycle events in `SetStatus`. Keep `DeletedAt`; non-deleted same-state calls change nothing and raise no event, while `Deleted` remains terminal.

- [ ] **Step 4: Update domain test setup to use `SetStatus`**

Replace `Activate`, `Deactivate`, and `SoftDelete` calls and assert exact enum values.

- [ ] **Step 5: Run the focused tests**

Run the command from Step 2. Expected: domain tests pass after downstream compile references are adjusted in Task 2; if compilation is blocked, review and commit Tasks 1-2 together.

---

### Task 2: Status-aware application operations

**Files:**
- Modify: `src/Modules/Catalog/Application/Categories/Commands/CategoryCommands.cs`
- Modify: `src/Modules/Catalog/Application/Categories/Ports/Inbound/ICategoryService.cs`
- Modify: `src/Modules/Catalog/Application/Categories/Services/CategoryService.cs`
- Modify: `tests/UnitTests/CategoryServiceTests.cs`

**Interfaces:**
- Produces: `UpdateCategoryCommand(..., int DisplayOrder, CategoryStatus Status)`.
- Produces: `SetCategoryStatusCommand(long CategoryId, CategoryStatus Status)`.
- Produces: `Task<Result<CategoryAdminDto>> SetStatusAsync(SetCategoryStatusCommand command, CancellationToken ct = default)`.
- Retains: bulk deactivation and compatibility delete; legacy single activate/deactivate APIs are removed after GraphQL switches in Task 5.

- [ ] **Step 1: Rewrite/add failing service tests**

Cover update details+status in one save, parent validation, subtree deactivation from both operations, dependency refusal before mutation, non-deleted same-state idempotency, invalid enum before I/O, terminal deletion, and compatibility delete sharing the same rules.

- [ ] **Step 2: Prove service tests fail**

Run: `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter FullyQualifiedName~CategoryServiceTests`

Expected: FAIL on missing command/service members and old flags.

- [ ] **Step 3: Add command and port signatures**

Add required update status, `SetCategoryStatusCommand`, and generic-result `SetStatusAsync`.

- [ ] **Step 4: Implement one transition coordinator**

Share one private path between update, dedicated status, and delete. For `Active`, validate the parent first; for `Inactive`, load the tracked full subtree and transition every non-deleted row; for `Deleted`, check live children and product assignments first. Complete all checks before `Update`/`SetStatus`, then save once and return a status-bearing DTO.

- [ ] **Step 5: Preserve bulk and delete behavior**

Refactor bulk deactivation to target `Inactive` while retaining all-or-nothing selected-ID validation and one save. Make `DeleteAsync` delegate to the shared deleted transition without a second save.

- [ ] **Step 6: Verify Tasks 1-2**

Run: `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter "FullyQualifiedName~CategoryDomainTests|FullyQualifiedName~CategoryServiceTests"`

Expected: PASS.

- [ ] **Step 7: Review gate and commit**

Stage Tasks 1-2 files, show `git diff --cached`, wait for approval, then commit: `feat(catalog): unify category lifecycle status`.

---

### Task 3: EF mapping, repository predicates, and read projections

**Files:**
- Modify: `src/Database/Catalog/Configurations/Categories/CategoryConfiguration.cs`
- Modify: `src/Modules/Catalog/Adapter/Outbound/Persistence/Categories/CategoryRepository.cs`
- Modify: `src/Modules/Catalog/QueryApplication/Categories/CategoryQueryShapes.cs`
- Modify: `src/Modules/Catalog/QueryApplication/Categories/CategoryQueries.cs`
- Modify: `src/Modules/Catalog/QueryApplication/Categories/CategoryAdminQueries.cs`
- Modify: `tests/UnitTests/CategoryPersistenceModelTests.cs`
- Modify: `tests/UnitTests/CategoryQueryTests.cs`

**Interfaces:**
- Produces: required integer `Status`, hierarchy index `(ParentId, Status, DisplayOrder, Id)`, and unique filters `[Status] <> 2`.
- Preserves: ordered/deferred `CategoryAdminQueries.Search(...)` and existence-check-plus-deferred `GetProductIdsAsync(...)`.

- [ ] **Step 1: Write failing persistence tests**

Assert `Status` is required/int; old flags are absent; hierarchy index and both filtered unique indexes have the exact new shape; ID generation, row version, self-navigation, and slug conversion remain unchanged.

- [ ] **Step 2: Write failing query tests**

Assert direct enum filtering for all states, status-only DTO/tree projections, active-only public visibility through the full ancestor chain, stable ordering, deferred admin search, and deferred category-product enumeration after its existence check.

- [ ] **Step 3: Prove model/query tests fail**

Run: `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter "FullyQualifiedName~CategoryPersistenceModelTests|FullyQualifiedName~CategoryQueryTests"`

Expected: FAIL on old columns, predicates, and projections.

- [ ] **Step 4: Update mapping and repository**

Map `Status` as required integer. Replace name/slug/child predicates with `Status != CategoryStatus.Deleted`; update tracked-subtree SQL to select `Status`. Do not add a global query filter.

- [ ] **Step 5: Update QueryApplication**

Carry `CategoryStatus` in category/ancestor projections, compare directly for admin filtering, and require `Active` for every public ancestor. Preserve `AsNoTracking`, ordering, and current materialization boundaries.

- [ ] **Step 6: Verify the category read slice**

Run the command from Step 3, then run: `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter FullyQualifiedName~Category`

Expected: PASS except the already-explicit Identity-dependent skip.

- [ ] **Step 7: Review gate and commit**

Stage Task 3 files, show `git diff --cached`, wait for approval, then commit: `refactor(catalog): query categories by unified status`.

---

### Task 4: Data-preserving EF migration

**Files:**
- Create: `src/Database/Migrations/<EF-generated timestamp>_UnifyCategoryStatus.cs`
- Create: `src/Database/Migrations/<EF-generated timestamp>_UnifyCategoryStatus.Designer.cs`
- Modify: `src/Database/Migrations/EwaysVNextDbContextModelSnapshot.cs`
- Modify: `tests/UnitTests/CategoryPersistenceModelTests.cs`

**Interfaces:**
- Produces: reversible SQL Server migration from two booleans to required integer `Status`.

- [ ] **Step 1: Add a failing migration-operation test**

Instantiate `UnifyCategoryStatus` and invoke `Up`/`Down` through reflection. Assert `Up` backfills with `CASE WHEN IsDeleted = 1 THEN 2 WHEN IsActive = 1 THEN 1 ELSE 0 END` before dropping the source columns. Assert `Down` restores `IsDeleted` only for 2 and `IsActive` only for 1 before dropping `Status`.

- [ ] **Step 2: Generate the migration**

Run: `dotnet ef migrations add UnifyCategoryStatus --project src/Database/EwaysVNext.Database.csproj --startup-project src/Host/EwaysVNext.Host/EwaysVNext.Host.csproj`

Expected: two new files and snapshot update; the historical category migration is unchanged.

- [ ] **Step 3: Complete the generated migration**

Add explicit up/down backfill SQL, drop old indexes before columns, and create the new hierarchy and `[Status] <> 2` unique indexes.

- [ ] **Step 4: Verify migration metadata and SQL**

Run: `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter FullyQualifiedName~CategoryPersistenceModelTests`

Then run: `dotnet ef migrations script 20260923121245_category UnifyCategoryStatus --project src/Database/EwaysVNext.Database.csproj --startup-project src/Host/EwaysVNext.Host/EwaysVNext.Host.csproj --no-build`

Expected: tests pass and generated SQL orders add/backfill/drop/recreate safely. Do not save the script.

- [ ] **Step 5: Review gate and commit**

Stage migration/snapshot/test changes, show `git diff --cached` plus the relevant generated-SQL excerpt, wait for approval, then commit: `feat(database): migrate category lifecycle status`.

---

### Task 5: GraphQL update and dedicated status mutation

**Files:**
- Modify: `src/Modules/Catalog/Adapter/Inbound/GraphQL/Categories/CategoryAdminMutations.cs`
- Modify: `src/Modules/Catalog/Adapter/Inbound/GraphQL/Categories/CategoryGraphQLTypes.cs` only if schema inspection requires explicit enum binding
- Modify: `src/Modules/Catalog/Application/Categories/Commands/CategoryCommands.cs`
- Modify: `src/Modules/Catalog/Application/Categories/Ports/Inbound/ICategoryService.cs`
- Modify: `src/Modules/Catalog/Application/Categories/Services/CategoryService.cs`
- Modify: `tests/UnitTests/CategoryApiAdapterTests.cs`

**Interfaces:**
- Produces: `UpdateCategoryInput(..., int DisplayOrder, CategoryStatus Status)`.
- Produces: `SetCategoryStatusInput([property: ID] long CategoryId, CategoryStatus Status)`.
- Produces: `setCategoryStatus(...): CategoryAdminDto!`.
- Removes: single `activateCategory`/`deactivateCategory` mutations, commands, and service methods.
- Retains: `deactivateCategories` and `deleteCategory`.

- [ ] **Step 1: Write failing schema/execution tests**

Assert required status inputs, exact enum values `INACTIVE/ACTIVE/DELETED`, status-bearing update/result, GraphQL string IDs, new mutation presence, old mutation absence, and retention of bulk deactivate/delete.

- [ ] **Step 2: Add error tests**

Assert `setCategoryStatus` preserves `CATEGORY_PARENT_INACTIVE`, `CATEGORY_HAS_CHILDREN`, and `CATEGORY_DELETED`; invalid GraphQL enum input must fail before service invocation.

- [ ] **Step 3: Prove adapter tests fail**

Run: `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter FullyQualifiedName~CategoryApiAdapterTests`

Expected: FAIL on the old schema and missing mutation.

- [ ] **Step 4: Implement the GraphQL contract**

Pass status through update, add `SetCategoryStatusAsync` returning `ValueOrThrow(await service.SetStatusAsync(...))`, and remove legacy single activate/deactivate APIs and test-fake members. Leave authorization comments and paging attributes untouched.

- [ ] **Step 5: Verify adapter and complete category slice**

Run the command from Step 3, then: `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter FullyQualifiedName~Category`

Expected: PASS except the explicit Identity skip; native paging tests remain green.

- [ ] **Step 6: Review gate and commit**

Stage Task 5 files, show `git diff --cached`, wait for approval, then commit: `feat(catalog): expose category status mutations`.

---

### Task 6: Architecture and full regression verification

**Files:**
- Modify only when a failing test proves a defect in this status refactor; do not clean unrelated code.

**Interfaces:**
- Verifies the complete feature and architectural boundaries.

- [ ] **Step 1: Scan for stale lifecycle symbols**

Run: `rg -n "CategoryAdminStatus|category\.IsActive|category\.IsDeleted|ActivateCategoryCommand|DeactivateCategoryCommand|activateCategory|deactivateCategory" EwaysVNext.Modules.Catalog.Contracts src/Modules/Catalog src/Database tests/UnitTests`

Expected: no category-lifecycle matches. Product/location `IsActive` remains out of scope.

- [ ] **Step 2: Build**

Run: `dotnet build EwaysVNext.slnx --no-restore`

Expected: zero errors.

- [ ] **Step 3: Run active unit tests**

Run: `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --no-build`

Expected: zero failures; the existing Identity-dependent test remains skipped.

- [ ] **Step 4: Run architecture tests**

Run: `dotnet test --project tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj --no-build`

Expected: category architecture tests pass. Report the previously observed unrelated Customer `DateTime.Now` failure separately if it remains.

- [ ] **Step 5: Report suite availability accurately**

Check for executable project files under `tests/PersistenceContractTests` and `tests/IntegrationTests`. If absent, report that these stale SecondaryCatalog files are not active and were not run; do not claim four-suite success or modify them.

- [ ] **Step 6: Final review gate**

Show the complete diff since `55caba2`, test counts/results, migration SQL summary, and any unrelated failure. Make any corrective commit only after showing its staged diff and receiving approval.

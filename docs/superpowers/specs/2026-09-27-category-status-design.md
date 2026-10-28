# Category Status Unification Design

## Objective

Replace the independent `Category.IsActive` and `Category.IsDeleted` flags with one
canonical status so a category cannot represent contradictory states. Category detail
updates and a dedicated GraphQL mutation must both support status transitions without
bypassing the existing hierarchy and deletion invariants.

## Canonical State

`CategoryStatus` is the single status enum used across the Catalog domain, contracts,
query projections, and GraphQL schema:

```csharp
public enum CategoryStatus
{
    Inactive = 0,
    Active = 1,
    Deleted = 2
}
```

The existing framework-free Catalog Contracts project owns this enum. This is the
smallest change compatible with the current dependency graph: Catalog Application and
QueryApplication already reference Contracts, while Contracts must not reference either
layer. `CategoryAdminStatus` is renamed to `CategoryStatus` rather than introducing a
second enum with mapping code.

The `Category` aggregate exposes `Status` and removes `IsActive` and `IsDeleted`.
New categories start as `Inactive`. `DeletedAt` remains because it records when the
terminal transition occurred.

## Transition Rules

The aggregate exposes one status-transition operation instead of separate active and
deleted flags. Existing domain events remain and are raised according to the target
state.

- `Inactive -> Active`: allowed for a root or when its parent is active.
- `Active -> Inactive`: allowed; the application service applies it to the complete live
  subtree so active descendants cannot remain below an inactive category.
- `Active|Inactive -> Deleted`: allowed only when the category has neither children nor
  assigned products. The transition sets `DeletedAt`.
- `Deleted -> *`: refused with `CATEGORY_DELETED`; deletion is terminal.
- Transitioning to the current status is idempotent and raises no status event.

The application service loads and validates every prerequisite before mutating tracked
aggregates. A failed transition therefore cannot leave an in-memory detail update pending
inside the request-scoped DbContext.

## Application Operations

`UpdateCategoryCommand` gains a required `CategoryStatus Status`. `UpdateAsync` validates
the requested transition, loads any required parent/subtree/dependency information, then
updates details and status in one serialized operation and one `SaveChangesAsync`.

A new `SetCategoryStatusCommand` and `ICategoryService.SetStatusAsync` change only status:

- Active loads and validates the parent.
- Inactive loads and updates the full subtree.
- Deleted checks children and product assignments.

The single-category `ActivateAsync` and `DeactivateAsync` application operations and their
GraphQL mutations are removed in favor of `SetStatusAsync`. Bulk deactivation remains as a
specialized batch operation. `DeleteAsync` remains for compatibility and delegates to the
same Deleted transition path, so there is only one implementation of deletion rules.

## GraphQL Contract

`UpdateCategoryInput` gains required `status: CategoryStatus!`. A new mutation is exposed:

```graphql
setCategoryStatus(input: {
  categoryId: ID!
  status: CategoryStatus!
}): CategoryAdminDto!
```

The mutation returns the persisted category, matching `updateCategory` and allowing the
client to refresh from server state. Category Snowflake identifiers remain CLR `long`
values mapped to GraphQL `ID` strings at the adapter boundary.

`activateCategory` and `deactivateCategory` are removed. `deactivateCategories` and
`deleteCategory` remain for batch and compatibility use cases.

`CategoryAdminDto` and `CategoryAdminTreeNodeDto` replace `isActive`/`isDeleted` with a
single `status`. Name/status filtering continues to use the same enum.

Authorization remains disabled until the Identity system is complete; this change does
not restore authorization attributes or endpoint guards.

## Persistence and Migration

EF maps `Category.Status` as a required integer column. A new migration:

1. Adds `Status`.
2. Backfills existing rows with precedence:
   - `IsDeleted = 1` -> `Deleted`
   - otherwise `IsActive = 1` -> `Active`
   - otherwise -> `Inactive`
3. Drops indexes that reference the old flags.
4. Drops `IsActive` and `IsDeleted`.
5. Recreates the hierarchy index as `(ParentId, Status, DisplayOrder, Id)`.
6. Recreates active-record unique filters using `[Status] <> 2`.

The down migration restores both booleans from `Status` before dropping the enum column.
Repository raw SQL, projections, and provider-specific persistence tests are updated to
select/filter/index `Status` rather than the old columns.

## Query Behavior

Public category reads treat only `Active` categories as visible and require every ancestor
to be `Active`. Admin queries include all statuses and filter directly by `Status`. No query
materialization or pagination behavior changes; the native Hot Chocolate connections remain
deferred `IQueryable` pipelines.

## Error Behavior

Existing stable errors are preserved:

- `CATEGORY_NOT_FOUND`
- `CATEGORY_DELETED`
- `CATEGORY_PARENT_INACTIVE`
- `CATEGORY_HAS_CHILDREN`
- persistence/concurrency errors

Invalid enum input is rejected by GraphQL before resolver execution. Domain/application
errors continue to expose their message, code, and HTTP status through the shared GraphQL
error filter.

## Verification

Tests cover:

- aggregate initial state, valid transitions, idempotency, and terminal deletion;
- update-with-status using one save;
- active-parent validation;
- subtree deactivation through both update and dedicated status mutation;
- deletion dependency refusal through update, status mutation, and compatibility delete;
- GraphQL schema/input/output shape and Snowflake ID handling;
- admin and public query behavior for all three statuses;
- EF model indexes, filtered uniqueness, migration up/down backfill, and SQL translation;
- full UnitTests, ArchitectureTests, and persistence/integration suites available in the
  local environment.

## Non-goals

- Changing Product or Location active-state modeling.
- Re-enabling GraphQL authorization.
- Changing native cursor pagination or endpoint paths.

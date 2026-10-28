# SHOP-494 Review Fixes Design

## Scope

Complete the code-review items for the unified Category status without touching or validating migrations, Docker files, or the merge from `main`. Existing uncommitted migration work belongs to the developer and must not be staged or committed with this change.

## Design

- Category updates treat a missing `Status` as "leave the current status unchanged". A supplied status is validated and transitioned only when it differs from the persisted status.
- Reapplying the current status is idempotent. Updating the details of an active category below an inactive parent must not re-run activation rules.
- `updateCategory` and `setCategoryStatus` keep returning `CategoryAdminDto`, but the GraphQL adapter reloads the saved category through `ICategoryAdminQueries.GetByIdAsync`. This keeps `parent` and `childCount` accurate without introducing a QueryApplication dependency into Application.
- `ChildCount` represents live direct children and excludes rows whose status is `Deleted`.
- The optional ADR-0016 provider cleanup is deferred to its own ticket, as requested by the review.

## Verification

- Service test: renaming an active category under an inactive parent with unchanged status succeeds.
- Service test: a null update status leaves the current status unchanged.
- Schema test: `UpdateCategoryInput.status` is nullable while `SetCategoryStatusInput.status` remains required.
- GraphQL execution tests: both update and status mutations return the reloaded `parent` and `childCount` values.
- Query test: deleted direct children do not contribute to `ChildCount`.
- Run the Category-focused unit tests, the complete unit suite, architecture tests, and a Release build. Report unrelated pre-existing failures separately.

## Reusable Review Guardrails

1. Update inputs use patch semantics: an optional field means unchanged unless the API explicitly documents another meaning.
2. State transitions are applied only when the target state differs from the persisted state; repeated requests must not trigger transition guards or cascade side effects again.
3. Mutation payloads must contain real values. If a DTO includes read-side projections or derived fields, reload it through the read side in the driving adapter, or expose a smaller mutation-specific payload.
4. Derived counts must follow the same eligibility rules as the corresponding domain decision; logically deleted records are excluded unless the field explicitly documents historical counting.
5. Application must not depend on QueryApplication to enrich transport responses. Composition belongs in an adapter that can legally reference both.
6. Every GraphQL contract change needs both schema assertions and execution tests for behavior.
7. Provider-specific query branches are avoided. Any `IgnoreQueryFilters()` usage must state the administrative or business reason.

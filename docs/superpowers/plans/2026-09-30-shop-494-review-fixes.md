# SHOP-494 Review Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the non-migration SHOP-494 review fixes while preserving Catalog's hexagonal boundaries.

**Architecture:** Application owns optional and idempotent status updates. The GraphQL adapter composes the write service with the read port for enriched mutation payloads; QueryApplication owns database projections.

**Tech Stack:** .NET 10, C#, EF Core, Hot Chocolate, xUnit, FluentAssertions

**Spec:** `docs/superpowers/specs/2026-09-30-shop-494-review-fixes-design.md`

## Global Constraints

- Do not modify, stage, validate, or commit migrations or Docker files.
- Do not merge `main`.
- Preserve existing uncommitted `CategoryService` changes.
- Defer the optional ADR-0016 provider cleanup.
- Do not commit before the user reviews and approves the final diff.

## Review Focus

- Omitted status and unchanged explicit status do not run transition guards.
- A real status change retains existing validation and cascade behavior.
- Mutation reload failures surface the query error.
- Both affected mutations expose enriched read-side fields.
- Deleted direct children are omitted from `ChildCount`.

---

### Task 1: Optional and idempotent status

**Files:** `CategoryServiceTests.cs`, `CategoryCommands.cs`, `CategoryService.cs`

**Interfaces:** Produces `UpdateCategoryCommand(..., CategoryStatus? Status)`; null means unchanged.

- [ ] Add failing tests for omitted status, unchanged status below an inactive parent, and a real transition.
- [ ] Run focused tests and verify expected failures.
- [ ] Make status nullable; validate and transition only a supplied, changed status.
- [ ] Run focused tests and verify they pass.

### Task 2: Accurate GraphQL mutation payloads

**Files:** `CategoryApiAdapterTests.cs`, `CategoryAdminMutations.cs`

**Interfaces:** Uses `ICategoryAdminQueries.GetByIdAsync` after successful writes; produces nullable `UpdateCategoryInput.Status`.

- [ ] Add failing schema/execution tests for nullable status, omitted-status dispatch, enriched payloads, and reload errors.
- [ ] Run the adapter tests and verify expected failures.
- [ ] Make the input nullable and reload only after a successful write.
- [ ] Run adapter tests and verify they pass.

### Task 3: Live child count

**Files:** `CategoryQueryTests.cs`, `CategoryAdminQueries.cs`

**Interfaces:** `ChildCount` counts direct children whose status is not `Deleted`.

- [ ] Change the count test to assert live-child semantics and verify it fails.
- [ ] Exclude deleted children in the correlated count and verify query tests pass.

### Task 4: Guidance and verification

**Files:** `AGENTS.md` and all files above.

- [ ] Reference the reusable guardrail spec from `AGENTS.md`.
- [ ] Run focused tests, full unit tests, architecture tests, and a Release build.
- [ ] Report unrelated failures separately.
- [ ] Show the non-migration diff and wait for approval before staging or committing.

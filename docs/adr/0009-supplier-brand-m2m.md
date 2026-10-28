# ADR-0009: Supplier–Brand Many-to-Many Relationship Pattern

## Status
Accepted — 2026-08-24

## Context
Two new bounded contexts were introduced:

- **Supplier** — who provides: contact data, verification lifecycle (Pending →
  Verified → Suspended), and commission terms for the brands it supplies.
- **Brand** — what is supplied: brand identity (name, slug, logo, country of origin)
  and its approval lifecycle (PendingReview → Approved / Rejected).

The domains must stand in a many-to-many relationship ("supplier X carries brand Y at
rate Z"). The question was where that relationship lives and how the modules interact.

## Decision

### D1 — Supplier owns the relationship; Brand is reached via a one-method ACL (Option A)
`BrandSupplyAgreement` is an **entity inside the Supplier aggregate**
(`Supplier/Application/Domain/Suppliers/BrandSupplyAgreement.cs`). It stores
`BrandId`, `CommissionRate`, `AssignedAt`, `IsActive` — brand data is referenced by
Id only, never duplicated.

- **Why not Option C (third mapping module)?** The requirement already assigns
  ownership to Supplier; a mapping module would split one aggregate's children across
  two contexts for zero benefit.
- **Why not Option D (one module)?** Verification/commission language and
  approval/logo language are different bounded contexts with different lifecycles;
  merging them recreates the God-context this architecture exists to avoid.
- **Cross-module interaction:** before Supplier mutates an agreement it verifies brand
  existence through `IBrandExistenceChecker` (Supplier.Application port) implemented by
  `Supplier.Adapter.Outbound` delegating to `Brand.IBrandService.EnsureBrandExistsAsync`
  — **sanctioned exception #5**: `Supplier.Adapter.Outbound → Brand.Application`.
  Brand references Supplier NOWHERE (arch-enforced via the sanctioned-gateway theory).

### D2 — Event publishing: one owner, one fact (NoOp today)
When an agreement is assigned, **only Supplier publishes**
(`BrandAgreementAssignedDomainEvent(SupplierId, BrandId, CommissionRate)`). Brand
publishes only its own lifecycle facts (`BrandApproved/Rejected/...`). A mirrored
`SupplierAssignedToBrandEvent` would be a second, weaker claim about the same fact —
drift risk the moment NoOp becomes a real dispatcher.

All publications follow the ADR-0004 pattern: aggregate raises → service saves →
`DispatchAndClearAsync(aggregate)` → buffer cleared. Delivery today: none (NoOp).
Consequently, the spec's `Brand.AddSupplier(supplier)` business method was
**deliberately not implemented** — it would double-bookkeep a Supplier-owned fact.

### D3 — Cross-module composition lives in Admin (ADR-0007 pattern)
"Suppliers with their brands" and "brands with their suppliers" join three read sides:
`Supplier.QueryApplication` (agreement rows) + `Brand.QueryApplication` (names/slugs)
+ batch enrichment. Admin owns the join; domain modules expose flat rows only.
N+1 impossible by construction: agreement rows in one SQL, brand names in one IN-query.

## Consequences
- ✅ M2M relationship with metadata lives in exactly one aggregate; future agreement
  fields (terms, exclusivity) extend `BrandSupplyAgreement` with zero Brand impact.
- ✅ Brand module stays dependency-free toward Supplier.
- ⚠️ Reassigning/removing agreements requires the supplier to exist and be loaded —
  acceptable: all workflows are admin-driven and supplier-centric.
- ⚠️ Agreement rows are soft-deactivated (`IsActive=false`) to preserve history;
  uniqueness is enforced for ACTIVE agreements only (in-aggregate check).

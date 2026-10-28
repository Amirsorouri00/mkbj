# ADR-0006: Cross-Module Read Composition — Reporting Module with GraphQL

## Status
Accepted — 2026-08-24

## Context
The first cross-module *composition* requirement arrived: an admin panel report of
customers with failed payments (customer info + per-customer failure totals), with
filtering, paging, and admin-only access. The architecture review had flagged that
"cross-module reads have no home." This ADR gives them one.

Three options were evaluated.

## Options Considered

### Option A — Dedicated Reporting module, LEAN variant *(CHOSEN)*
A new bounded context whose entire purpose is composition over provider **read sides**:

```
Modules/Reporting/
├── Application/            ← ports (IPaymentReadDataProvider, ICustomerReadDataProvider)
│                              + IFailureReportService (grouping, filtering, paging, cache)
├── Adapter/
│   ├── Inbound/GraphQL/    ← HotChocolate resolvers (driving adapter)
│   └── Outbound/           ← read-side ACL adapters → Customer/Payment QueryApplications
└── Installer/              ← AddReportingModule
```

Deliberately **4 projects, not 5**: Reporting owns NO data — no DbContext, no own
QueryApplication. Its "read side" IS the composition performed through its outbound
ports. Adding a fake QueryApplication would be ceremony without meaning.

Key structural property: the composition hexagon defines its OWN ports; the outbound
adapters delegate to `ICustomerQueryService` / `IPaymentQueryService` (provider read
models crossing the boundary exclusively as Contracts DTOs). This is the read-side
mirror of the write-side ACL gateway pattern (ADR-0002/0005).

- ✅ Machine-enforced read-only: arch test bans ALL Reporting assemblies from referencing ANY write-side Application.
- ✅ Only `Reporting.Adapter.Outbound` may see provider QueryApplications (exception #3).
- ✅ GraphQL adapter is blind to everything except `IFailureReportService`.
- ✅ Composition logic is unit-testable without HTTP or GraphQL.
- ❌ Four new projects for one report (mitigated: each is tiny; the pattern scales to future admin reports).

### Option B — Host-level GraphQL BFF *(REJECTED)*
- ❌ Breaks the documented "thin host" principle — Host would gain business logic
  (filtering semantics, currency rules, caching policy) that module boundaries cannot see or test.
- ❌ No seam for future reports: every admin need would accrete in Program.cs.

### Option C — Extend Payment module *(REJECTED)*
- ❌ Domain leakage: Payment would render customer display data it does not own.
- ❌ Wrong owner: tomorrow's admin report might be "products with most refunds" —
  putting reporting inside one domain forces every future report into whichever
  module got here first.
- ✅ Reuse of existing gateway infra is real but trivial to replicate via ports (done in A).

## Decision
1. Adopt Option A (lean). Sanctioned exception #3:
   `Reporting.Adapter.Outbound → {Customer,Payment}.QueryApplication` — enforced by
   `Only_Reporting_Adapter_Outbound_May_Reference_Read_Sides`.
2. Provider modules expose NEW flat query methods on their existing QueryApplications
   (`GetFailedPaymentsAsync`, `GetByIdsAsync`) — single-statement SQL each.
3. All grouping/filtering/paging/caching lives in Reporting.Application.
4. GraphQL = HotChocolate 16 (`HotChocolate.AspNetCore`, `HotChocolate.AspNetCore.Authorization`),
   endpoint `/api/v1/admin/graphql` (originally `.../admin/reports/graphql` - renamed with the module, see ADR-0007), named rate-limit policy `admin`.

## Consequences
- ✅ Cross-module reads finally have a home with the same enforcement rigor as writes.
- ✅ N+1 impossible by construction: exactly TWO provider calls per request
  (1 filtered SQL on Payment, 1 batched IN-query on Customer), regardless of group count.
  Nested GraphQL types resolve from the already-composed page — no DataLoader needed;
  add DataLoaders only if per-entity sub-fields later gain their own IO.
- ✅ 30s IMemoryCache on composed pages; hard cap 5000 rows/request guards unbounded scans.
- ⚠️ Currency guard: if a range contains mixed currencies the service throws rather than
  summing misleadingly — callers must filter by currency first (future: FX normalization).
- ⚠️ Paging happens post-composition (in-memory) — acceptable for date-bounded admin
  ranges; revisit with keyset pagination if groups grow past ~10⁴.
---

## Amendment: Third Provider (Orders) + N+1 Reaffirmation

**Requirement:** the report must include each customer's **last order** (`customer.lastOrder`).

### Decision #1 — extend Exception #3 to `Orders.QueryApplication` (Option A)
The composition context exists precisely to absorb multi-module coupling; each new
provider costs one port + one delegating adapter + one arch-test list entry, and ZERO
changes to provider domains. Option B (Customer facade over orders) was rejected as
domain leakage that merely relocates the same coupling into a context whose language
has no orders; Option C (denormalized LastOrderId on Customer) was rejected for
distorting an aggregate with a reporting concern and adding saga steps.

New provider method: `IOrderQueryService.GetLastOrdersByCustomerAsync(userIds)` —
single SQL projection, latest-per-customer resolved on the read side.
**Identity translation:** Orders keys ownership by `IdentityUserId`; Reporting maps
`Customer.Id → IdentityUserId → order → back to customer node` in memory.

### Decision #2 — extend Service composition (Option X); NO DataLoader
The invariant generalizes: *GraphQL resolvers perform zero IO; every nested field
resolves from the composed page.* SQL statements per report request remain CONSTANT:

| Step | Provider | SQL |
|---|---|---|
| 1 | Payment read side | 1 filtered query |
| 2–4 | (in-memory group/join/page) | 0 |
| 5 | Customer read side | 1 batched IN-query |
| 6 | Orders read side | 1 batched projection |

→ **3 SQL per request at any scale** (1000 customers ⇒ same 3 queries; page size caps
enrichment at ≤100 ids). Machine-checked by `FailureReportCompositionTests`
(NSubstitute call-count assertions: exactly ONE call per port even at 50 customers;
cache hit ⇒ zero calls). DataLoaders would add request-scoped machinery without a
measured need — trigger to revisit: many optional expensive nested branches.
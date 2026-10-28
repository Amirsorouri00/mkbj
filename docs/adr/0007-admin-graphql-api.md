# ADR-0007: Admin GraphQL API — Reporting as the Admin BFF

## Status
Accepted — 2026-08-24 · **Amended: module renamed `Reporting` → `Admin` (2026-08-24)** —
after absorbing mutations, "Reporting" implied read-only and misled. Namespaces
(`Modules.Admin.*`, `Contracts.Admin.*`), DI methods (`AddAdminModule`), and the arch
rules were renamed with it; ADR-0006 references to "Reporting" are historical.

## Amendment — Domain-Owned Admin Operations ("thin composition layer")

**Problem:** after the mutation wave, `Admin.Application` held five admin services,
several of which were pass-throughs to a single provider (a "God Module" smell):
`CreateProduct`, `ChangePrice`, `AdjustStock`, `Deactivate`, `Suspend`, `Reactivate`,
`ChangeOrderStatus`, `Capture`, `Fail`, `Refund` touched exactly one domain each.

**Classification rule applied to every operation:**

| Test | Classification | Home |
|---|---|---|
| Mutates/reads ONE module only | Domain-owned | `{Module}.Application` admin service |
| Joins ≥2 modules | Composition | `Admin.Application` service |
| Pure projection of one module's store | Read-side ACL | stays via Admin read ports → QueryApplication |

**Changes made:**

1. **Domain-owned writes moved into their owning modules**, each behind a dedicated
   inbound port in that module's `Application/Ports/Inbound/`:
   - `Catalog.ICatalogAdminService` / `CatalogAdminService`
   - `Customer.ICustomerAdminService` / `CustomerAdminService`
   - `Orders.IOrderAdminService` / `OrderAdminService`
   - `Payment.IPaymentAdminService` / `PaymentAdminService`

   v1 implementations delegate to each module's existing public service — zero logic
   duplication, zero behavior change. The deliverable is the *seam*: module-specific
   admin rules (audit fields, approval workflows, bulk ops) now have an obvious,
   boundary-respecting home.

2. **Admin write gateways repointed**: `ICatalogAdminGateway` etc. now delegate to the
   domain admin ports instead of raw public services. Dependency direction unchanged.

3. **Admin.Application slimmed to composition-only**: `IAdminCatalogService` and
   `IAdminPaymentService` deleted entirely; `IAdminCustomerService` reduced to
   `GetDetailAsync` (Customer 360); `IAdminOrderService` reduced to `GetDetailAsync`.
   Analytics + failed-payments report unchanged. GraphQL list queries resolve through
   the read-side ports (`IProductReadDataProvider`, …) as before.

4. **Reads stayed on the QueryApplication path** (not moved into domains) because
   `domain.Application → own QueryApplication` would create a circular reference
   (`QueryApplication → Adapter.Outbound → Application`). Projections are not domain
   logic, so nothing was lost.

5. **Machine check:** `Admin_Composition_Layers_Must_Be_Aggregate_Free` —
   `Admin.Application` and the GraphQL assembly may not reference `EwaysVNext.DDD`;
   any aggregate/entity work would require it, so domain logic physically cannot hide
   there. Combined with the existing write-confinement rule, the composition layer is
   verifiably thin.

   > **This ADR described the check as existing when it did not.** It was cited by name here,
   > in AGENTS.md and in DDD-GUIDELINES while being absent from the test suite — three
   > documents promising a guarantee the build was not making, which is worse than an
   > unguarded invariant because readers stop verifying what they believe is enforced. The
   > test now exists in `HexagonalBoundaryTests`. The invariant it checks was already true, so
   > nothing had to change in the code — but nothing had been stopping it from becoming false.

## Paging contract (all admin lists)

`products`, `customers`, `orders` and `payments` validate `page`/`pageSize` **before** the
arguments reach `.Skip((page - 1) * pageSize)`, via `AdminPaging.Validate`:

| Rule | Error code | HTTP |
|---|---|---|
| `page` must be ≥ 1 | `ADMIN_PAGE_INVALID` | 400 |
| `pageSize` must be 1…100 | `ADMIN_PAGE_SIZE_INVALID` | 400 |

Unvalidated, `page: 0` emitted `OFFSET -20 ROWS` — which SQL Server rejects, so a client
mistake surfaced as a server error — and `pageSize: 1000000` was an unbounded scan available
to any authenticated admin. Indexes make the *ordering* cheap; only this bound limits how much
one request may pull.

**It rejects rather than clamps**, deliberately. Silently answering `page: 0` with page 1
returns rows the caller never asked for, and silently trimming `pageSize: 100000` to 100 tells
a client it has everything when it has the first hundred. The limit of 100 matches
`FailureReportService.Validate` so the module's two paging boundaries cannot disagree.

Analytics `take` arguments are clamped instead (`Math.Clamp(take, 1, 50)`) because they select
a *top-N*, where "give me the best 50" is a coherent answer to "give me the best 5000".

## Error codes this API can return

| Code | HTTP | Meaning |
|---|---|---|
| `ADMIN_PAGE_INVALID` / `ADMIN_PAGE_SIZE_INVALID` | 400 | Paging arguments out of range |
| `REPORT_PAGE_INVALID` / `REPORT_PAGE_SIZE_INVALID` / `REPORT_RANGE_INVALID` / `REPORT_MINAMOUNT_INVALID` / `REPORT_STATUS_INVALID` | 400 | Failure-report filter invalid |
| `REPORT_CURRENCY_MISMATCH` | **409** | The rows in range span several currencies, so one total would be misleading. The request is valid; the *data* conflicts with the question |
| `ANALYTICS_CURRENCY_MISMATCH` | **409** | Same, for the sales summary and top-customers queries |
| `ORDER_NOT_FOUND` / `CUSTOMER_NOT_FOUND` | 404 | |
| `SAGA_STEP_FAILED` / `SAGA_NULL_RESULT` | **500** | A saga step threw, or returned null. Genuine server faults, declared explicitly so they do not report as 400 and vanish from 5xx alerting |

Both currency guards used to **throw `InvalidOperationException`**, so a healthy server
answered a well-formed request with a 500 — and the failure-report message told the caller to
"filter by currency first" when the filter has no currency field. Impossible advice is worse
than none.

### Example — simple operation (domain-owned)
```
GraphQL createProduct ─► ICatalogAdminGateway (Admin.App port)
  ─► CatalogAdminGateway (Admin.Adapter.Outbound)
    ─► Catalog.ICatalogAdminService (Catalog.Application)   ← capability lives HERE
      ─► IProductService ─► Product aggregate
```

### Example — complex composition (Admin-owned)
```
GraphQL customer(id) ─► IAdminCustomerService (Admin.App)
  ├─ ICustomerReadDataProvider ─► Customer.QueryApplication   (profile)
  ├─ IOrderReadDataProvider    ─► Orders.QueryApplication     (history, by IdentityUserId)
  └─ IPaymentAdminReadProvider ─► Payment.QueryApplication    (payments)
  = CustomerDetail composed in-memory
```

## Context
The admin panel needs a single API covering catalog management, customer management,
order management, analytics, and payment operations. The Reporting module (ADR-0006)
already owned cross-module composition; this ADR extends it into a full admin BFF and
amends its read-only guarantee.

## Decisions

### D1 — Schema organization: modular classes, ONE schema (Option B)
`Queries/` and `Mutations/` are separate per-domain classes (`AdminCatalogQueries`,
`AdminOrderMutations`, …) registered as type extensions on the shared `admin`
HotChocolate executor. Modular source layout, one endpoint, one auth story.
Schema stitching rejected: it solves a multi-service problem we do not have.

### D2 — Mutations in GraphQL (Option X)
One protocol for the entire admin panel: same authorization, same rate-limit bucket,
same error shape (`extensions.code` from `Error.Code`). REST remains untouched and
continues to serve public/client traffic — the two coexist as sibling driving adapters
over identical hexagon ports.

### D3 — Authorization: single `Admin` policy, enforced TWICE
Endpoint-level `RequireAuthorization("Admin")` blocks non-admins before execution
(including introspection); field-level `[Authorize(Policy = "Admin")]` guards every
query/mutation independently. Per-role policies (D-Option B) remain a pure addition:
annotate resolvers individually.

## Boundary amendment — writes through the composition context
ADR-0006 declared Reporting provably read-only. With mutations, that guarantee is
replaced by a tighter-scoped one:

| Assembly | Write-side Application access |
|---|---|
| `Reporting.Application` | ❌ never |
| `Reporting.Adapter.Inbound.GraphQL` | ❌ never |
| `Reporting.Adapter.Outbound` | ✅ ONLY here — ACL gateways delegate to provider **inbound ports** |

This is the same sanctioned pattern as exceptions #1/#2 (Orders→Catalog,
Payment→Customer), generalized to all four providers. Enforced by
`Reporting_App_And_GraphQL_Must_Never_Reference_Write_Side_Applications`; even the
gateway adapter cannot touch provider ADAPTERS (DbContexts/repos).

New provider capabilities added where missing (small, domain-owned):
`IProductService.DeactivateProductAsync` (soft-delete via existing aggregate method),
`IPaymentService.RefundAsync` + `PaymentStatus.Refunded` + `MarkRefunded`
(Captured→Refunded only).

## Data flow & N+1 posture
Resolvers stay IO-free shells over five admin services. Every service composes via
ports: reads hit provider QueryApplications (flat DTOs, single filtered SQL each),
writes hit provider inbound ports through gateways.

SQL counts per admin request (constant, independent of page size):
- products / orders / payments list: **1**
- customer detail: **3** (customer + orders-by-identity-user + payments)
- order detail: **2** (order row + latest payment for the order)
- sales summary / top customers: **1–2** (+ name batch), 30s cached
Analytics guard against mixed-currency sums (throws) and cap scans at 5000 rows.

## Deferred (honest scope note)
**Categories** and **product images/media** have no backing concept in any module's
domain. Rather than fabricate data or bolt them onto Catalog hastily, they are
deferred: they require a real Catalog domain extension (Category aggregate +
media entity, EF configurations, migrations) tracked separately. The GraphQL schema
grows additively when that lands.

## Consequences
- ✅ Admin panel: one API surface, machine-enforced boundaries, reusable ports.
- ✅ Provider domains gained capabilities their own aggregates needed anyway
  (deactivation, refunds) — exposed equally to future REST endpoints.
- ⚠️ Reporting now drives writes: mis-scoped mutations would be reachable from the
  admin API — mitigated by the confined-gateway rule and dual authorization.
- ⚠️ Offset pagination chosen for admin tables (simple, jump-to-page); keyset
  pagination is the documented next step if datasets grow past ~10⁵ rows.

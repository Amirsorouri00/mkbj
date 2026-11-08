# Phase 6 — Admin

A data-less composition module, and the guards that make the admin surface answerable: every
route authorized, every admin service able to say who did what, and the one shared GraphQL
endpoint bounded.

Verified at `1ea745d` on `feature/ecommerce-mvp`:

- `dotnet build EwaysVNext.slnx -c Release` — 0 errors, 0 warnings.
- `dotnet test EwaysVNext.slnx` — **2013 tests, 0 failures**, Docker up (1 skipped: an
  Identity assertion parked since phase 1).

## What works

**Admin owns nothing.** It reads other modules' `QueryApplication`, maps their DTOs into views of
its own, and arranges them. `AdminArchitectureTests` fails the build if it acquires a `DbContext`,
a table, a read side of its own, or a dependency on an aggregate. A composition module with a
table stops being one: it becomes a second place where an order's status lives, and the two
drift — which is the failure ADR-0006 exists to prevent.

**The boundary rule got stricter, not looser.** Phase 0 let Admin reference any module's
`QueryApplication` *or* `Application`, which was too loose to be a rule — it would have let the
composition layer call a write service and still pass. It is now an explicit list of providers
(Sales, Customer; Sourcing joins in phase 7), `Admin.Application` references no other module at
all, and exactly one `Application` assembly is allowed through: Customer's, because its read DTO
embeds a contract record that lives there. A second test asserts what that exception does *not*
buy — no service and no aggregate from any other module.

**The mapping is hand-written on purpose.** Handing Sales' DTOs to the backoffice schema would
make every rename in Sales a breaking change for whatever draws it. The gateway tests run against
the real provider types, because a mapping that silently drops a field compiles perfectly.

**Admin has two rules of its own, and both are tested.** Failure codes roll up by count then name,
so the dashboard does not shuffle between refreshes. And an order stays readable when its customer
record is gone — it is a real sale with real documents, and hiding it would hide the evidence
exactly when somebody is looking for it.

### Endpoints

| Route | Gate |
|---|---|
| `GET /api/v1/admin/dashboard` | `admin:staff` + `orders.manage` |
| `GET /api/v1/admin/customers/{id}/360` | `admin:staff` + `customers.manage` |
| `GET /api/v1/admin/orders/{id}/360` | `admin:staff` + `orders.manage` |
| `GET /api/v1/admin/audit/verify` | `admin:super` + `audit.verify` |

GraphQL mirrors all four: `adminDashboard`, `adminCustomer360`, `adminOrder360`,
`adminAuditVerification`.

### The guards

- **Every route is authorized unless allowlisted.** The allowlist is checked against the live
  routing table, so a stale exemption cannot hide a real one. Writing it found two inaccurate
  entries immediately.
- **Every `*AdminService` holds `IAuditTrail`.** Structural, so adding an admin service without
  one fails the build. Mutation-checked.
- **The admin rate limit** is configurable (`RateLimiting:AdminPermitPerMinute`, default 120) and
  partitions by backoffice subject rather than by address — several administrators behind one
  office IP are several people.
- **Acceptance 13** now covers refunds and `audit.verify` on both surfaces.

## What is incomplete

- **No import section on the dashboard.** Sourcing is phase 7. A section of zeros would read as
  "nothing is importing" rather than "nothing imports yet", so the field is absent rather than
  empty, and `AdminReadModules` gains `Sourcing` when the module arrives.
- **The PRD §10 coverage matrix is not executable.** The plan called for a test that enumerates
  every §10 view and fails on a gap. The views exist and are reachable; nothing yet proves the
  list is complete.
- **RBAC mutations are audited by their own save.** They write through `AuthorizationDbContext`,
  so the audit row cannot join their transaction. This remains the documented non-atomic edge.

## Risks

- **The GraphQL protection tested here is a framework default.** `A_Deep_GraphQL_Query_Is_Refused`
  pins HotChocolate's built-in coordinate-cycle guard. `GraphQLLimits.MaxDepth` and the cost
  ceiling are configured as defence in depth, but **both can be deleted with that test still
  green** — two mutation checks showed it. An earlier version of the test asserted only that the
  word "depth" appeared in the message and passed with the depth rule removed, because the
  built-in message happens to contain it. Non-cyclic deep nesting is not covered by any test.
- **The composition trace is visible to any staff admin.** It carries the seed and pool size —
  nothing about another customer, but it does describe how the engine picks.
- **The audit verdict is not asserted intact under the full suite.** Every test writes audit
  events into one shared database, so whether the chain is sealed end to end at a given instant
  is a claim about every other test's data. The test asserts the verdict is reachable and
  stamped, not that it is true.
- **Rate limiting is still in-process** (ADR-0010). With replicas > 1 each instance keeps its
  own window, so the effective admin limit multiplies by the replica count.

## Reproduction

```bash
docker compose -f build/compose/docker-compose.dev.yml up -d
dotnet build EwaysVNext.slnx -c Release
dotnet run --project src/Host/EwaysVNext.Host -- --migrate
dotnet test EwaysVNext.slnx
dotnet test --project tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj --filter-class "*.AdminArchitectureTests"
dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "*.EndpointAuthorizationTests"
```

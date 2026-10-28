# ADR-0016: QueryApplication Is the Read-Side Implementation; Contracts Is the Public API

## Status
Proposed — 2026-09-30.

Supersedes [ADR-0012](0012-queryapplication-is-the-public-contract.md) and
[ADR-0013](0013-narrow-queryapplication-to-actual-contracts.md) once accepted. Amends
[ADR-0003](0003-query-application-separation.md) and
[ADR-0008](0008-read-optimized-cqs-terminology.md): the read/write separation and the
"reads bypass aggregate hydration" guarantee they record still hold; only *which project* does
the projecting and *which project* is the public contract change.

## Context

ADR-0012 made `QueryApplication` the module's public contract (interfaces and models, zero
implementation), moved EF projection code into `Adapter.Outbound` repositories, and made
`Application` host each query service as a thin delegator to those repositories. ADR-0013
narrowed which interfaces live in that contract.

The codebase no longer follows that decision uniformly. As of `main` @ `e7a24d5`:

| Read side | Where the query runs | Matches ADR-0012? |
|---|---|---|
| Catalog — products (`ProductQueryService`) | `Catalog.QueryApplication`, injects `EwaysVNextDbContext` | No — moved back here by SHOP-438 |
| Catalog — categories (`CategoryQueries`, `CategoryAdminQueries`, `CategoryQueryShapes`) | `Catalog.QueryApplication`, injects `EwaysVNextDbContext` | No |
| Catalog — attribute blueprints, measurement units, category attribute assignments | `Catalog.Application` query services → repository ports → `Adapter.Outbound` | Yes |
| Customer — admin list, job types, team members | `Application` → `ICustomerRepository.QueryForAdminList` / `ITeamMemberRepository.QueryForList` (`IQueryable`) → `Adapter.Outbound` | Yes |
| Location — provinces / cities / districts | `Application` → `IProvinceRepository` → `Adapter.Outbound` | Yes |

Alongside that:

- A second public-contract shape exists: `Catalog.Contracts` (introduced by SHOP-438, currently at
  the repository root) and `Location.Contracts` (`src/Modules/Location/Contracts`). Several Catalog
  query interfaces now live in `Catalog.Contracts`, one (`IAttributeBlueprintQueryService`) lives in
  `Catalog.Application` because it needs `Result<T>` and paging types that the dependency-free
  `Contracts` project may not reference.
- The `QueryApplication` projects of Customer, Location and Payment are empty.
- `CategoryArchitectureTests` *asserts* that `Catalog.QueryApplication` references `Database`,
  i.e. it enforces the pattern ADR-0012 forbids.
- `AGENTS.md` still describes `QueryApplication` as "flat read side — direct EF Core projections"
  (the pre-ADR-0012 wording), contradicting ADR-0012.
- `CategoryAdminQueries` branches on `db.Database.IsInMemory()`, so tests exercise a different
  query than production.

The result is two read patterns in one module, a contract layer split across two project kinds,
and documentation that contradicts itself. Either ADR-0012 is restored everywhere, or the direct
pattern is adopted everywhere and recorded. This ADR does the latter.

## Decision

**Each module has three layers with one job each: `Contracts` is its public API,
`Application` is its write side, `QueryApplication` is its read side and talks to EF Core
directly.**

### 1. `{Module}.Contracts` — the public API
- Holds the interfaces and DTOs that anything outside the module's own `Application` may call:
  other modules, and the module's own inbound adapters (REST/GraphQL) for reads.
- Contains **no implementation** and **no DI registration**.
- May reference only framework-free building blocks: `EwaysVNext.Outcomes` and
  `EwaysVNext.Queries.Application`. No EF Core, no ASP.NET Core, no other module.
- Lives at `src/Modules/{Module}/Contracts/` (as `Location.Contracts` already does).
- A module gets a `Contracts` project only when something actually calls it (ADR-0013's
  "narrow to actual contracts" principle carries over).
- Write interfaces called across a module boundary (e.g. `ILocationService.CreateAddressAsync`)
  may live here too; they are still implemented in `Application`.

### 2. `{Module}.QueryApplication` — the read side
- Implements the module's read contracts by querying `EwaysVNextDbContext` **directly** with
  `AsNoTracking()` projections into DTOs. No repository indirection for reads.
- Read-only: no `SaveChanges`, no `Add`/`Update`/`Remove`, no aggregate loading beyond what a
  projection needs, no domain rules.
- May reference: its own `Contracts`, `EwaysVNext.Database`, `EwaysVNext.Outcomes`,
  `EwaysVNext.Queries.Application`. Never another module's `Application` or `QueryApplication`.
- Queries **only its own module's** `DbSet`s. `Database` exposes every module's entities, so this
  is a rule to enforce, not a property of the reference graph (see Enforcement).
- May return `IQueryable<TDto>` to GraphQL for `[UsePaging]`/`[UseProjection]`; every such field
  sets an explicit `MaxPageSize`.
- **No provider branching.** Code must not test `IsInMemory()`/`IsSqlServer()` or the provider
  name. If a query cannot run on EF InMemory, the test belongs on LocalDB, not in a production
  `if`. (Raw SQL for hierarchy queries stays in `Adapter.Outbound`, where the write side needs it.)
- `IgnoreQueryFilters()` is allowed only where a view must show soft-deleted rows (admin
  screens), with a one-line comment saying why.
- Registers its own services via `Add{Module}QueryApplication()`, called from the module
  `Installer`.

### 3. `{Module}.Application` — the write side
- Domain, inbound ports for commands, outbound ports, services. Unchanged otherwise.
- Must **not** reference its own `QueryApplication`. (The project graph also forbids it:
  `QueryApplication → Database → Application`.)
- Repositories in `Adapter.Outbound` serve the write side only: loading aggregates, existence
  and uniqueness checks. Read-model projections (`QueryForAdminList`, admin-row methods, snapshot
  listings) move out of repositories into `QueryApplication`.

### 4. Who calls what
- Inbound adapters call `QueryApplication` (through `Contracts` interfaces) for reads and
  `Application` inbound ports for writes.
- Other modules reference only `{Provider}.Contracts`, never `{Provider}.QueryApplication` (which
  would drag in `Database` and every module's domain).

## Migration

Behaviour does not change; this is structural. Each item can land as its own PR.

| Module | Steps |
|---|---|
| Catalog | Move `Catalog.Contracts` from the repository root to `src/Modules/Catalog/Contracts/`. Move `AttributeBlueprintQueryService`, `MeasurementUnitQueryService`, `CategoryAttributeAssignmentQueryService` from `Application` into `QueryApplication`, querying the DbContext directly. Move their read-only repository methods (admin rows, snapshot listings) with them. Move `IAttributeBlueprintQueryService` into `Contracts` (possible once `Contracts` may reference `Outcomes`/`Queries.Application`). Remove the `IsInMemory()` branch from `CategoryAdminQueries`. |
| Customer | Move `QueryForAdminList`, `QueryJobTypes` and `TeamMember.QueryForList` out of the repositories into `Customer.QueryApplication`; the admin GraphQL resolvers call it. Add `Customer.Contracts` only when another module needs a Customer read. |
| Location | Move province/city/district reads into `Location.QueryApplication`. `Location.Contracts` keeps `ILocationService`; its read methods are implemented in `QueryApplication`, its write method (`CreateAddressAsync`) in `Application`. |
| Payment | Delete the empty `QueryApplication` project until Payment has a read side. |
| Docs | Update `AGENTS.md` (structure diagram, rule 6, "Adding a new module", pitfalls). Mark ADR-0012 and ADR-0013 superseded and point ADR-0003/0008 here. |

## Enforcement

Replace the SHOP-438-specific assertions in `CategoryArchitectureTests` with general rules in
`tests/ArchitectureTests`:

1. No `*.Application` project references any `*.QueryApplication` project.
2. `*.Contracts` projects reference nothing except `EwaysVNext.Outcomes` and
   `EwaysVNext.Queries.Application`, and contain no concrete classes other than records/DTOs.
3. No project outside a module references that module's `QueryApplication`.
4. Source scan of `*/QueryApplication/**`: no `SaveChanges`, `.Add(`, `.Update(`, `.Remove(` on a
   `DbSet`, no `IsInMemory`/`IsSqlServer`/`ProviderName`.
5. Source scan of `{Module}/QueryApplication/**`: only that module's `DbSet`s are used
   (e.g. `Catalog` may use `db.Products`, `db.Categories`, …, never `db.Customers`).

Each rule is mutation-tested once (introduce a violation, watch the test fail, revert), per
AGENTS.md.

## Consequences

### Positive
- One read pattern across all modules; the module layout matches the docs again.
- Removes a layer: query services stop being pass-throughs from `Application` to a repository
  that only exists to host projections.
- Read queries can be written for the screen that needs them (joins, counts, `IQueryable` for
  GraphQL paging/projection) without widening write-side repository interfaces.
- The public contract is one project kind (`Contracts`) with one rule set, instead of
  `QueryApplication` in some modules and `Contracts` in others.

### Negative
- `QueryApplication` depends on EF Core and, through `Database`, can *see* every module's
  entities. Isolation between modules on the read side becomes a tested rule (Enforcement 3–5)
  rather than a compile-time guarantee.
- The same table is queried from two places (write repository, read queries), so a filtering rule
  such as "exclude soft-deleted rows" can be duplicated. Prefer EF global query filters for such
  rules, so both sides inherit them.
- Read-side unit tests that run on EF InMemory can pass on queries SQL Server cannot translate.
  Query services need LocalDB-backed tests (integration or persistence-contract style) to be
  trusted.
- A migration effort touching Catalog, Customer and Location (see Migration).

## Alternatives considered

- **Restore ADR-0012 everywhere** — move Product and Category queries back behind
  `Adapter.Outbound` repositories with thin delegators in `Application`. Rejected: it keeps a
  layer that adds no behaviour, forces every new screen-specific read into a write-side repository
  interface, and fights the `Contracts` split that is already in place.
- **Leave both patterns** — rejected: two ways to do the same thing inside one module, with an
  architecture test enforcing one of them and the ADRs mandating the other, is the worst of both.

## References
- [ADR-0003](0003-query-application-separation.md) — read/write separation (still holds)
- [ADR-0008](0008-read-optimized-cqs-terminology.md) — Read-Optimized CQS terminology (still holds)
- [ADR-0012](0012-queryapplication-is-the-public-contract.md) — superseded by this ADR
- [ADR-0013](0013-narrow-queryapplication-to-actual-contracts.md) — superseded; its "narrow to
  actual contracts" principle carries over to `Contracts`
- `tests/ArchitectureTests/CategoryArchitectureTests.cs` — current SHOP-438-specific assertions
- `src/Modules/Catalog/QueryApplication/Categories/CategoryAdminQueries.cs` — `IsInMemory()` branch

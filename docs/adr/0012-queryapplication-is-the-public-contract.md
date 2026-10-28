# ADR-0012: QueryApplication Becomes the Module's Public Contract; Application Implements It

## Status
Accepted — 2026-09-01. | **Narrowed same day** by
[ADR-0013](0013-narrow-queryapplication-to-actual-contracts.md): only interfaces genuinely
called cross-module stay in `QueryApplication` — `IProductService`/`ICatalogAdminService`
(Catalog) and `ICustomerInboundPort`/`ICustomerAdminService` (Customer) moved back to
`Application/Ports/Inbound`, since nothing outside each module ever called them. The
structural rule below (QueryApplication = contract, Application = implementation) stands;
only which interfaces qualify as "the contract" changed.

Amends [ADR-0001](0001-hexagonal-first-architecture.md),
[ADR-0003](0003-query-application-separation.md), and
[ADR-0008](0008-read-optimized-cqs-terminology.md).

## Context
`QueryApplication` was originally the CQS read side only (ADR-0003): flat DTOs, direct EF Core
projections, no aggregate loading, separate from `Application`'s write-side hexagon. That
still left an open question ADR-0002/ADR-0011 never answered concretely: when one module
needs to call another module (either to read its data or, per the ACL Gateway pattern, to
write to it), which project does the CALLING module reference, and which project defines the
interface it calls?

The answer had drifted into inconsistency. `IProductService` (write commands: create, reserve
stock, change price) lived in `Application/Ports/Inbound/`, alongside its own local copy of
`ProductSnapshot`/`ReservationSnapshot` — a duplicate of the read-side `QueryApplication`
DTOs with the same shape, kept separate only so `Application` would not have to depend on
`QueryApplication`. Meanwhile `QueryApplication` held its own implementation
(`ProductQueryService`), querying `CatalogDbContext` directly. There was no single project a
consuming module — or a new contributor — could point at and say "this is Catalog's public
surface."

## Decision
**`QueryApplication` is the module's contract/public API layer. `Application` is its
implementation.**

1. Every interface another module (or this module's own Adapter.Inbound) is allowed to call —
   both write commands (`IProductService`, `ICatalogAdminService`) and read queries
   (`IProductQueryService`) — lives in `QueryApplication/Abstractions/`. Every
   request/response model those interfaces use (`ProductSnapshot`, `ReservationSnapshot`,
   `ProductAdminRow`) lives in `QueryApplication/Products/` (or the module's equivalent
   folder). `QueryApplication` contains zero implementation classes and zero DI registration
   method — there is nothing there to register.
2. `Application` implements every one of those contracts. `ProductService`,
   `CatalogAdminService`, and now `ProductQueryService` all live in `Application/Service/`,
   alongside `Domain/` and `Ports/Outbound/` (the repository abstractions, which stay internal
   — no other module ever sees `IProductRepository`). `Application` references
   `QueryApplication` to implement its interfaces and return its models; `QueryApplication`
   references nothing back.
3. DI registration for all of it lives in `Application/DependencyInjection.cs`, mapping each
   `QueryApplication` interface to its `Application` class
   (`services.AddScoped<IProductQueryService, ProductQueryService>()`, and likewise for the
   other two).
4. Consumers standardize on one reference: `Adapter.Inbound.Rest` now depends on
   `QueryApplication` only (not `Application` — it never needed the domain model or outbound
   ports, only the interface it calls). A future cross-module Gateway, per ADR-0002's ACL
   pattern, would do the same.

### The EF-in-Application conflict, and how it was resolved
Moving `ProductQueryService`'s implementation into `Application` ran straight into ADR-0001's
other standing rule: Application must not depend on EF Core or any Adapter. The old
`ProductQueryService` queried `CatalogDbContext` directly to get zero-hydration flat
projections (ADR-0008's "reads bypass aggregate hydration" guarantee) — exactly the kind of
access Application is not allowed.

**Resolved by extending the existing write-side repository instead of inventing a new one.**
`IProductRepository` (an `Application/Ports/Outbound` abstraction) gained
`GetSnapshotByIdAsync`, `ListSnapshotsAsync`, and `GetAdminRowsAsync`, implemented in
`Adapter.Outbound`'s `ProductRepository` — the same class, and the same file, that already
implements the write-side methods, since it already holds the `CatalogDbContext`. The actual
EF `.Select(...)` projection — the part that must stay untranslated-LINQ-free and hydration-free
— now lives in `Adapter.Outbound`, not `Application`. `Application`'s `ProductQueryService` is
a thin delegator: it calls the repository and wraps the result in a `Result<T>`, touching
nothing but the interface. ADR-0008's zero-hydration property is preserved; only its physical
location moved one layer out. Customer mirrors this exactly (`ICustomerRepository` gained the
equivalent three methods).

This means `IProductRepository`/`ICustomerRepository` are no longer purely "the write-side
repository" — they now also carry the read-side projection methods. That is an accepted
mixed responsibility, not an oversight: the alternative (a second, parallel outbound port
just for queries) would duplicate repository plumbing next to the existing one for no benefit
this module has needed yet.

## Consequences

### Positive
- One project per module answers "what can another module call here?" — `QueryApplication`,
  always, for both reads and writes. No more duplicate DTOs kept in sync by hand between
  `Application`'s local copy and `QueryApplication`'s ACL copy.
- Mirrors the `Core.Application`/`Core.Implementation` and `Events.Application`/
  `Events.Implementation` split already used in `BuildingBlocks` — one naming pattern for
  "abstractions vs. concrete" everywhere in the codebase, not just here.
- `QueryApplication` is now a genuinely dependency-free contract package (just `Outcomes`,
  plus `Core.Application` for `Actor` where a signature needs it) — safe for any module to
  reference without pulling in EF Core, ASP.NET Core, or another module's domain model.

### Negative
- `IProductRepository`/`ICustomerRepository` mix write and read-projection methods on one
  interface, which is a mild Interface Segregation smell — accepted per above rather than
  adding a second port.
- `QueryApplication`'s name is now a partial misnomer: it holds write-command interfaces too,
  not just queries. Renaming it (e.g. to `Contracts` or `Abstractions`) was considered and
  deliberately deferred — it is a purely cosmetic change with a large diff (every
  cross-project reference, every namespace) and no behavior difference; do it only alongside
  another reason to touch every one of these files.
- `ADR-0003`'s original framing ("QueryApplication = flat read side, Application = write
  side") no longer describes the codebase. Its separation-of-concerns motivation (reads
  shouldn't pay for aggregate hydration) still holds — it is just implemented one layer
  further out than the ADR originally said.

## References
- ADR-0001: Hexagonal-First architecture (the `QueryApplication` folder this amends)
- ADR-0002: Direct Inter-Module Calls / ACL Gateway pattern (the consumer-side half of "one
  project answers what's callable")
- ADR-0003: QueryApplication Separation (superseded: QueryApplication no longer means
  "read side only" or "does its own EF projections")
- ADR-0008: Read-Optimized CQS terminology (the zero-hydration guarantee this ADR relocates,
  not removes)
- ADR-0011: Simplify back to a broker-free, bus-free architecture (the same session's earlier
  decision that Contracts as a shared DTO project should not exist; this ADR is what replaced
  it with per-module QueryApplication as the contract layer)

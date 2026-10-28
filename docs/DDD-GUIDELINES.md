# DDD Best Practices — EwaysVNext v3

> Companion to `docs/ONBOARDING.md`. These rules are enforced where possible by
> `tests/ArchitectureTests/` — treat violations as build failures, not style notes.

## 1. One consistency boundary per transaction
An aggregate is a consistency boundary: its invariants must hold at every commit.

**The rule:** one `SaveChangesAsync` must never span two aggregates whose invariants
**depend on each other**. Coordinate genuinely-related aggregates through services
(intra-module) or a `CrossModuleSaga` (cross-module).

> **This rule used to read "never mutate two aggregates inside a single
> `SaveChangesAsync`", with no exception.** That absolute is wrong, and this codebase's own
> `ReservationExpirationService` breaks it correctly: the TTL reaper loads every product with
> a reclaimable reservation and saves them all in one call. That is fine because the changes
> are **independent, idempotent and commutative** — no invariant spans two products, so no
> consistency boundary is crossed, and one round trip beats N. The code was right and the rule
> was wrong; the rule has been corrected rather than the code.

If you cannot articulate *why* the aggregates in a batch are independent, treat them as
dependent and split the save.

## 2. Reference other aggregates by ID
Aggregate A never holds an object reference to Aggregate B — only its `Id`
(e.g., `Order.UserId`, `BrandSupplyAgreement.BrandId`). Cross-module references are
Guid keys verified via ACL ports when existence matters.

## 3. Model concepts as Value Objects
`Money`, `Email`, `Sku`, `Slug`, `Address`… If a concept has validation rules or
formatting, it is a Value Object, not a primitive. Primitive Obsession is a defect.
Smart enums (`OrderStatus`, `SupplierStatus`) follow the same equality discipline.

## 4. Business logic lives IN the aggregate; Services orchestrate
The aggregate guards its invariants (state transitions, quantity checks, token
formats). Application services do exactly four things: validate → load → call a
domain method → save. A service that branches on domain state is a smell.

## 5. One Repository per Aggregate Root
Repositories load/save aggregate roots only. Child entities (order lines,
saved payment methods, supply agreements) are reached through the root — never via
their own repository or `DbSet`.

## 6. Ports live in `Application/Ports`; implementations in Adapters
Inbound ports (`IProductService`) define what the hexagon offers; outbound ports
(`IProductRepository`, `ICatalogAdminGateway`) define what it needs. The Application
project must not reference EF Core or any adapter — NetArchTest fails otherwise.

## 7. QueryApplication owns ALL reads
Reads project directly into flat Contract DTOs with single SQL statements. No
aggregate hydration, no domain rules, no `SaveChanges` (arch-tested). See ADR-0008
for why this is Read-Optimized CQS rather than full CQRS.

## 8. Saga-based ACL for cross-module writes
Cross-module operations are synchronous direct calls through consumer-owned gateway
ports, wrapped in `CrossModuleSaga` with idempotent compensations (ADR-0005). They
are sagas, **NOT atomic transactions** — document and test failure paths.

> The heading used to read "Transactional ACL", which contradicted its own body. There is no
> shared transaction anywhere: separate DbContext, separate connection, separate schema.

## 9. Map by hand, in the projection
Project directly into flat `Contracts` DTOs inside the LINQ `Select`. No AutoMapper, no
mapping framework.

> **This rule used to mandate the Riok.Mapperly source generator.** Three `.csproj` files
> referenced the package and the codebase contained **zero** `[Mapper]` declarations — so the
> guideline sent every new contributor to wire up a tool nobody here uses. The references have
> been removed.
>
> It is not merely unused, it is **wrong for this architecture**. Read-side mapping has to
> happen *inside* the query so EF can translate it to columns (ADR-0008): a generated mapper
> runs after materialization, which means hydrating the entity first — exactly the aggregate
> hydration the read side exists to avoid. One such slip (projecting an unmapped computed
> property) made EF materialise whole `Product` aggregates, with their owned reservation
> collections, to render one admin row.
>
> Write-side mapping is small enough that a generator earns nothing.

## 10. Domain events use the pluggable dispatcher
Aggregates raise events; application services publish via
`eventDispatcher.DispatchAndClearAsync(aggregate)` after a successful save. The
dispatcher is currently NoOp (ADR-0004); events remain intra-module — cross-module
communication is ACL ports + Contracts, never events.

## 11. Module boundaries are non-negotiable
No module **Application** references another module's Application. The only sanctioned
bridges are **adapter-level ACL gateways** (ADR-0002/0006/0007/0009).

**The `Admin` module exception:**
The `Admin` module is a dedicated **Composition Layer** (ADR-0007). Its `Adapter.Outbound`
intentionally references the Application/QueryApplication ports of all domain modules
to orchestrate cross-module operations and admin mutations. This is **by design**, not a
violation — and it is confined: only `Admin.Adapter.Outbound` may hold those references;
`Admin.Application` and the GraphQL adapter stay blind to provider internals
(arch-tested via `Admin_App_And_GraphQL_Must_Never_Reference_Write_Side_Applications`
and `Admin_Composition_Layers_Must_Be_Aggregate_Free`).

**For all other modules:**
Adding a cross-module reference? Stop — extend `Contracts` or add a port instead.
Never let domain logic leak through composition seams.

## 12. Architecture tests are part of the definition of done
Run `tests/ArchitectureTests` before every push. If a boundary rule fires, the fix
is in your code — never delete or weaken a rule without a new ADR.

**One qualification, learned the hard way:** a rule may be weakened when the *rule* is wrong
rather than the code (see rule 1 above, and AGENTS.md's transaction-tier rule). The test is
whether you can name the invariant the rule protects and show the code does not violate it. If
you cannot, the rule wins.

And do not cite a rule that does not exist. `Admin_Composition_Layers_Must_Be_Aggregate_Free`
was named as a machine check in this file, AGENTS.md **and** ADR-0007 while existing nowhere in
the test suite — a documented guarantee nobody was making, which is worse than an unguarded
invariant because readers stop checking what they believe is enforced. It is now a real test.

---

## Known inconsistencies (documented rather than hidden)

A template is copied verbatim, so its unresolved tensions get copied too. These are real and
deliberate; do not assume the codebase is uniform here.

> Two entries previously listed here — "`IClock` is used by exactly one module" and
> "`Order.TotalAmount` is a bare `decimal`" — have been **resolved and removed**. A section
> whose whole value is honesty is worse than useless when it describes problems that no longer
> exist, because a reader then trusts the rest of it less. See *Deterministic time* and *Money*
> below for what replaced them.

- **`Money` supports two-decimal currencies only, and refuses the rest.** Every amount is
  rounded to 2dp. That is correct for USD/EUR/GBP and wrong for JPY and KRW (no minor unit) and
  for KWD/BHD/OMR/TND (three). `Money.Create` therefore throws
  `MONEY_UNSUPPORTED_CURRENCY_DECIMALS` for a known non-2dp code rather than silently
  mis-rounding it — turning 1.234 KWD into 1.23 KWD loses money on every transaction.
  Supporting them properly needs a per-currency minor-unit exponent and matching storage
  precision, which is out of scope here. The check is a **denylist of known-different
  currencies, not an allowlist of known-2dp ones**: an allowlist would reject every unlisted
  code, converting a rounding limitation into an availability problem. The trade-off is that an
  exotic non-2dp currency missing from the list would still be mis-rounded — extend the list,
  do not assume it is complete.
- **`RowVersion` is a concurrency token only on aggregate ROOTS, and it is not a column.** The
  property is a `uint` on `Entity<TId>` mapped to PostgreSQL's system column `xmin` — Postgres
  maintains it, no migration creates it, and an `INSERT` neither can nor needs to supply it.
  Only root configurations map it (`.Property(x => x.RowVersion).IsRowVersion()` against
  `xmin`); child and owned tables leave the inherited property unmapped entirely, so there is no
  misleading column on them at all. **This is the correct DDD granularity** — the consistency
  boundary is the aggregate, and a child can only be written through its root. Two practical
  consequences: mutating a child must also touch its root (see `RecordChildChanged`) or the
  concurrent writer is never detected, and EF InMemory has no `xmin`, so the token is explicitly
  disabled there and no InMemory test can see a concurrency conflict. Full reasoning is on the
  property itself in `Entity.cs`.
- **The failure report and analytics refuse a mixed-currency range instead of answering it.**
  `REPORT_CURRENCY_MISMATCH` / `ANALYTICS_CURRENCY_MISMATCH` (both 409). Adding a currency
  filter would make such a range answerable; that is a capability, not a correctness fix, and is
  deliberately deferred.

### Deterministic time (resolved — this is now the convention)

Aggregates do **not** read the clock. Application services take `IClock`, read it **once per
operation**, and pass the instant in as a `DateTimeOffset now` parameter. `DeterministicTimeTests`
scans every `Application/Domain` folder and fails the build on `DateTimeOffset.UtcNow`,
`DateTime.UtcNow` or `DateTime.Now`.

`IClock` is registered with `TryAddSingleton` in all seven domain modules, so each installer
stays independently composable. The one documented exemption is `DomainEvent.OccurredOn`, which
keeps its ambient default: it is event metadata with zero consumers, so threading an instant
through every event construction would add churn without buying anything testable.

The reason to prefer a parameter over injecting `IClock` into the aggregate: aggregates are built
by static factories and rehydrated by EF, so a service dependency means either constructor
injection that EF materialization fights, or a static ambient clock — the original problem
wearing a different hat.

### Money in Orders (resolved)

`Order.TotalAmount`, `OrderLine.UnitPrice` and `LineTotal` are `Money`. Currency is **derived**
from Catalog's `Product.Price`, carried across the ACL by `ProductSnapshot`, and never accepted
from the caller — `Orders_Inbound_Request_Dtos_Must_Not_Accept_Money_From_The_Caller` still fails
the build if any Orders request DTO gains an amount or currency property.

`Order.Place` **rejects** a basket spanning currencies (`ORDER_MIXED_CURRENCIES_NOT_SUPPORTED`)
rather than guessing a total's currency or splitting the order. The confirm-time payment gate
compares `Money`, so a capture for the right amount in the wrong currency is refused; an
unparseable provider currency is treated as a mismatch, never as a pass.

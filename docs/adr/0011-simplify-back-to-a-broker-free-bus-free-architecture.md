# ADR-0011: Simplify Back to a Broker-Free, Bus-Free Architecture; Snowflake IDs

## Status
Accepted — 2026-09-01.

Amends [ADR-0002](0002-transactional-inter-module-communication.md),
[ADR-0004](0004-domain-events-no-dispatching.md),
[ADR-0008](0008-read-optimized-cqs-terminology.md), and
[ADR-0010](0010-kubernetes-deployment-strategy.md). Supersedes
[ADR-0005](0005-cross-module-consistency-saga-toolkit.md) in full.

**Amended 2026-09-28 (decision 10, id layout and wire format).** The worker id is gone: every
process ran with worker id `0`, so two replicas would have generated identical ids. The layout is
now 41-bit millisecond timestamp + 22-bit sequence whose starting value is random for each new
millisecond and then increments. Ids from one process stay unique and strictly increasing; two
processes collide only if they generate in the same millisecond *and* draw the same 22-bit start
(about 1 in 4.2 million per shared millisecond), which the primary-key constraint rejects. A clock
that moves backwards, or a millisecond whose sequence is exhausted, borrows the next millisecond
instead of throwing or spinning. Because these ids exceed JavaScript's 2^53 safe-integer range, a
developer marks each one that crosses the wire with `[Snowflake]` (`EwaysVNext.Domain.Common`) —
`[property: Snowflake] long Id` on a record's positional parameter, `[Snowflake] long customerId`
on a GraphQL resolver argument. A marked `long`, `long?` or collection of `long` is written as a
JSON string in REST responses (numbers or strings accepted on input) and typed `ID` in GraphQL;
anything unmarked keeps its normal number type. Marking a member that is not a `long` throws, so a
misplaced mark fails at schema build or first serialization instead of silently doing nothing. The
mark is recognized by its type name, `SnowflakeAttribute`, not by assembly: a project that must stay
reference-free, such as `EwaysVNext.Modules.Catalog.Contracts`, declares its own internal
`SnowflakeAttribute` instead of referencing Domain.

## Context
Earlier the same week, a full "building-block" layer was ported in wholesale (CQRS command/query
bus, RabbitMQ/MassTransit integration events, a transactional Outbox/Inbox, a cross-module saga
toolkit, a shared `Contracts` DTO project, Redis-backed distributed cache/lock, and a
Newtonsoft.Json service abstraction). Auditing that layer against what the codebase actually
used turned up:

- **`ICommandBus`/`IQueryBus`/`ICommandHandler`/`IQueryHandler`** had zero callers in either
  module. Every `Application/Ports/Service` class was called directly, as ADR-0008 already
  documented was the case before the bus was added.
- **`IDistributedCacheService`/`IDistributedLockService`/`IJsonService`** had zero real
  consumers outside their own Redis/Newtonsoft implementation files.
- **`CrossModuleSaga`/`SagasMetrics`** existed but neither module had an actual multi-step
  cross-module write to orchestrate yet — it was infrastructure ahead of a need.
- **The Outbox/Inbox + MassTransit path** added a message broker, a background processor, and a
  new failure mode (a message stuck mid-retry) to a system whose own prior decision (ADR-0002,
  ADR-0004) was explicitly "no bus, direct calls, strong consistency."
- **`BusinessRule`/`IBusinessRule`** (a `Check(params IBusinessRule[])` guard-clause helper) was
  also unused and was deleted in the same pass — then reinstated days later as intentional
  available Domain infrastructure, on the reasoning that an unused *guard* helper is cheap to
  keep (unlike a bus, cache, or broker, it carries no runtime dependency, background service, or
  operational surface) and DDD codebases reach for it regularly once aggregates grow past a
  couple of invariants.
- **Primary keys were `Guid.NewGuid()`** for every aggregate root and owned child entity. Nothing
  wrong per se, but `long` Snowflake ids give better index locality and smaller keys, and the
  existing cursor-pagination building block (`CursorPaginationQuery`/`ICursorResponse`) already
  assumed a `long` id — the two were inconsistent.

In short: several building blocks were added on the assumption that this system would grow into
needing CQRS-scale infrastructure, a message broker, and cross-module sagas, before any module
actually needed them. Once measured against real call sites, most of it was unused weight.

## Decision
Remove every building block enumerated above that had no real caller, and simplify the two that
had a real (if narrow) purpose:

1. **No message broker.** Delete `MassTransit`, `MassTransit.RabbitMQ`, `IIntegrationEventBus`,
   `IntegrationEventBus`, `RabbitMqSettings`, `AddMassTransitBus`, and the RabbitMQ service from
   `docker-compose.yml`/`appsettings.json`/`.env.example`.
2. **No transactional Outbox/Inbox.** Delete `OutboxMessage`/`OutboxInterceptor`/
   `OutboxProcessorJob` and `InboxMessage`/`InboxCleanerJob`, and the `.AddOutbox()`/`.AddInbox()`
   model-builder calls in `ModuleDbContextBase`.
3. **Domain events dispatch synchronously, in-process, without persistence.** `EfCoreUnitOfWork`
   (in `EwaysVNext.Persistence`, the one class that actually implements `IUnitOfWork.
   SaveChangesAsync` for both modules via their thin `CatalogUnitOfWork`/`CustomerUnitOfWork`
   wrappers) now captures every tracked aggregate's pending `DomainEvents` immediately before
   calling `SaveChangesAsync`, and — once the save succeeds — publishes each one through
   `IDomainEventBus` (unchanged: in-process reflection dispatch to `IDomainEventHandler<T>`) and
   clears the buffer. If `SaveChangesAsync` throws, nothing is published. If a handler throws
   after a successful save, the event is lost — there is no retry, no dead-letter, no durable
   row. This is deliberately weaker than the Outbox it replaces; see Consequences.
4. **No CQRS command/query bus (CQS, not CQRS).** Delete `ICommand`/`ICommandBus`/
   `ICommandHandler`, `IQuery`/`IQueryBus`/`IQueryHandler`, `CommandBus`, `QueryBus`, and the
   entire `Commands` building block (`EwaysVNext.Commands.Application`/`.Implementation`).
   `IUnitOfWork` — the one piece of `Commands.Application` with a real implementation
   (`EfCoreUnitOfWork<TDbContext>`) — moves to `EwaysVNext.Persistence`, the layer that actually
   owns it. `EwaysVNext.Queries.Application`/`.Implementation` keep only the pagination shapes
   (`PaginationQuery`, `CursorPaginationQuery`, `ICursorResponse`, …) and the Gridify-based
   `QueryExtensions` filtering/ordering helpers — application services remain plain classes
   called directly.
5. **No cross-module saga toolkit.** Delete `CrossModuleSaga`/`SagasMetrics` and their tests. A
   cross-module write is a plain sequence of direct calls through hexagonal ports, same as
   ADR-0002 always described; there is no compensation executor. If a future multi-step
   cross-module flow genuinely needs compensation, build it then, scoped to that flow, rather
   than as generic ahead-of-need infrastructure.
6. **No shared `Contracts` project.** Each module's `QueryApplication` now owns the read-model
   DTOs it exposes as its ACL — other modules/Admin consume a module's data through that
   module's `QueryApplication`, never through a third shared DTO assembly. Each module's
   write-side `Application` layer keeps its own local copies of whatever DTOs its own ports
   return; it must not reference its sibling `QueryApplication` project, because
   `QueryApplication` depends on `Adapter.Outbound` → EF Core, which would break Application's
   "no framework dependencies" rule (ADR-0001) by a transitive project reference.
7. **No distributed cache/lock, no Newtonsoft.** Delete `IDistributedCacheService`/
   `IDistributedLockService` and their Redis/RedLock implementations, and `IJsonService`/
   `NewtonsoftJsonService`. Nothing replaces `IJsonService` — call `System.Text.Json` directly at
   the (currently nonexistent) call site if one appears.
8. **Mapster → Mapperly.** `QueryExtensions.ApplyPagination`/`ApplyCursorPagination` take an
   `Expression<Func<TSource,TDestination>>` projector parameter (a Mapperly-generated
   query-projection method) instead of Mapster's `TypeAdapterConfig`. There were no real callers
   to migrate; this only changes the shape offered to the first caller.
9. **`BusinessRule`/`IBusinessRule` stays** as available Domain building-block infrastructure,
   despite having no caller today — see Context above for why this one small guard-clause helper
   is being treated differently from the removed infrastructure.
10. **Aggregate root and owned-entity primary keys move from `Guid` to `long`, generated by
    `IIdGenerator` (a hand-rolled `SnowflakeIdGenerator`: 41-bit timestamp + 10-bit worker id +
    12-bit sequence, monotonic within a millisecond).** The id is generated in the owning
    Application service — the layer that already holds `IClock` — and passed into the
    aggregate's static factory or the entity-creating instance method as a parameter, exactly
    the way `now` already is. `Product`, `StockReservation`, `Customer`, and `SavedPaymentMethod`
    all convert; EF configurations gain `.Property(x => x.Id).ValueGeneratedNever()`, since the
    default convention for a numeric key is SQL identity, which would otherwise discard the
    client-generated value. `Customer.IdentityUserId` — a reference to an identity system this
    codebase does not own — stays `Guid`; only primary keys generated by this codebase move.
    Both modules' EF migration history is squashed to a fresh `InitialCreate` against the new
    schema and column types, since the Outbox/Inbox tables were dropped in the same window and
    the schema was still pre-production.

## Consequences

### Positive
- Far less surface area: five deleted NuGet packages (`MassTransit`, `MassTransit.RabbitMQ`,
  `Newtonsoft.Json`, `Microsoft.Extensions.Caching.StackExchangeRedis`, `RedLock.net`) and a
  deleted `Mapster` in favor of a not-yet-referenced `Riok.Mapperly`, three fewer services in
  `docker-compose.yml` (RabbitMQ, Redis, and their env vars), and roughly a dozen fewer
  BuildingBlocks projects/files, all of which had zero or near-zero real callers.
- Every remaining building block corresponds to something a module actually calls, which is
  easier to audit and onboard against than infrastructure sized for a future that had not
  arrived.
- `long` Snowflake keys are smaller, index-friendlier, and consistent with the pagination
  building block's existing `long`-id assumption.

### Negative
- **Domain event delivery is now strictly weaker than the Outbox it replaces.** A handler
  failure after a successful `SaveChangesAsync` silently drops that event — no retry, no
  dead-letter, no operator-visible signal. ADR-0004's 2026-08-28 amendment describes exactly this
  trade-off and names the fix (an outbox in the owning module, drained by a `BackgroundService`,
  bus only after that) — that guidance is reaffirmed for whenever an async requirement actually
  arrives, not retracted.
- **No compensation toolkit for cross-module writes.** If a genuine multi-step cross-module flow
  fails partway, nothing undoes the completed steps automatically; whatever calls the ports is
  responsible for reasoning about partial failure itself. ADR-0005's reasoning for why sagas beat
  raw try/catch (observability, idempotent compensation, avoiding phantom inventory) remains
  correct **if and when** such a flow exists again — it should be rebuilt scoped to that flow,
  not reinstated as ahead-of-need infrastructure.
- **Squashing migration history discards the incremental record** of how the schema evolved
  (constraint additions, index additions) in favor of one current-state baseline. Acceptable
  only because the schema is pre-production; would not be an acceptable choice against a
  database with real data.
- Any future need for cross-process/durable messaging, a real cache, or generic command/query
  dispatch means re-adding infrastructure that existed a few days ago and was removed — a real
  cost if the removal turns out to be premature, weighed against the cost of carrying unused
  infrastructure in the meantime.

## References
- ADR-0002: Direct Inter-Module Calls (the no-bus, direct-call decision this ADR reaffirms)
- ADR-0004: Domain Events dispatching — the 2026-08-28 amendment's outbox path is still the
  right next step if async delivery is needed again
- ADR-0005: `CrossModuleSaga` (superseded in full — kept for historical record)
- ADR-0008: Read-Optimized CQS terminology (the command/query bus this ADR removes was additive
  infrastructure on top of the CQS split described there, not a replacement for it)
- ADR-0010: Kubernetes deployment strategy — D5's compensation-token fix is moot with the saga
  gone; D1–D4/D6/D7 are unaffected

# ADR-0004: Pluggable Event Dispatching (Currently NoOp)

## Status
Accepted - 2026-08-08 | **Amended (2026-08-24): pluggable seam introduced, NoOp chosen.**
The zero-dispatch decision below is superseded by the seam described at the bottom;
the reasoning (strong consistency, no bus) is reaffirmed and extended. | **Amended
(2026-09-01): the 2026-08-28 outbox migration was built, then removed.** See
[ADR-0011](0011-simplify-back-to-a-broker-free-bus-free-architecture.md). Domain events now
dispatch **synchronously, in-process, undurably**: `EfCoreUnitOfWork` captures an aggregate's
raised events before `SaveChangesAsync`, publishes them via `IDomainEventBus` once the save
succeeds, then clears the buffer. This is a real dispatcher (unlike the original NoOp below) but
still has no bus, no outbox row, and no cross-process delivery — a handler failure after a
successful save is lost, not retried. The "add an outbox first, not a bus" guidance in the
2026-08-28 amendment remains correct advice for whenever async delivery is actually needed again;
it was not being needed that motivated today's removal, not a change of opinion about the path.

## Context
v2 had a full Domain Event dispatch pipeline (`IDomainEventDispatcher`, `DomainEventDispatchRunner<TDbContext>`) that forwarded events to Wolverine's message bus after commit. This added complexity:
- Explicit `_dispatcher.DispatchAsync(ct)` calls in every handler
- Wolverine dependency for event publishing
- Infrastructure coupling in Application layer

In v3, we removed Wolverine and switched to direct synchronous inter-module calls (ADR-0002 — note that ADR's "transactional" claim is **withdrawn**; the real model is a saga with compensation, ADR-0005). The question arose: what to do with Domain Events that aggregates still raise internally?

## Decision
**Domain Events may exist inside Aggregates, but no dispatching infrastructure is introduced in v3.**

### What this means:
- Aggregates can still `Raise(new SomeDomainEvent(...))` — this is a pure in-memory operation
- Domain Events are collected in `Entity.DomainEvents` collection
- No `IDomainEventDispatcher` interface exists
- No post-commit dispatch pipeline exists
- No handler infrastructure for domain events exists

### Why keep them at all?
1. **Intent documentation** — `OrderPlacedDomainEvent` documents what happened
2. **Test assertions** — tests can assert `order.DomainEvents.Contains(...)`
3. **Audit trail** — events are available for future logging/audit
4. **Zero cost** — if not dispatched, they live in memory and are garbage-collected

### Why not add dispatch?
- v3 uses direct synchronous calls, not event-driven communication
- Inter-module communication goes through ACL ports, not events
- Adding dispatch would re-introduce the complexity we removed

## Consequences

### Positive
- Simpler Application layer — no `IDomainEventDispatcher` to inject and call
- No Wolverine or message bus dependency
- Domain Events still serve as documentation and test assertions

### Negative
- Domain Events are "fire and forget" — no side effects
- If a future requirement needs event-driven side effects, infrastructure must be added

### Future Migration
If event dispatching becomes necessary:
1. Add `IDomainEventDispatcher` to `Application/Ports/Outbound`
2. Implement in `Adapter/Outbound` (could use in-process, MediatR, or Wolverine)
3. Call after `SaveChangesAsync` in Service methods
4. Keep domain events intra-module — use Contracts for cross-module communication

## References
- ADR-0002: Direct Inter-Module Calls (the "transactional" claim is withdrawn)
- v2 ADR-0005: Domain Event Dispatch Pipeline (superseded by this ADR)

---

## Amendment (2026-08-24): Pluggable Event Dispatching — NoOp Today

The original decision left `ClearDomainEvents()` with zero callers and dispatch as a
non-existent concept. That created a dead seam: when async workflows eventually arrive,
every aggregate would need re-touching. This amendment installs the *abstraction*
without installing any delivery mechanism.

### What was added
- `EwaysVNext.DDD.Events.IEventDispatcher` — the seam. Application services may
  now `await` dispatch; the core has no idea what happens behind it.
- `NoOpEventDispatcher` — the current implementation: completes, delivers nothing,
  zero dependencies. Registered in the Host as singleton; swapping implementations is
  ONE DI line.
- `DispatchAndClearAsync(entity)` extension — publishes everything an aggregate
  raised, then clears its buffer (`ClearDomainEvents()` finally has a caller).
- First wired call sites: `CatalogAdminService.DeactivateProductAsync`
  (`ProductDeactivatedDomainEvent`, newly raised by the aggregate) and
  `PaymentAdminService.RefundAsync` (`PaymentRefundedDomainEvent`).

### Why we explicitly choose NoOp today
1. **Strong consistency stays default.** Nothing happens after commit that a caller
   did not already see. No dual-write windows, no retry semantics to reason about.
2. **In-process handlers would be premature coupling.** The only plausible consumers
   today would be cross-module reactions — and those MUST go through ACL ports
   (ADR-0002), not events.
3. **A real bus is a one-line DI swap away.** The seam makes waiting cheap.

### Future triggers — replace NoOp when:
| Trigger | Example | Replacement shape |
|---|---|---|
| Background side effects after commit | email/notification on refund | In-memory background dispatcher (fire-and-forget, same process) |
| Cross-module reactions that must not share the request transaction | analytics roll-ups, read-model warm-up | Outbox + transport (NEW ADR required — revisits ADR-0002) |
| Integration with external systems | PSP webhooks, CRM sync | Same outbox path |
| Event-sourced read models (ADR-0008 Stage 2+) | denormalized reporting store | Projection pump consuming the dispatcher |

### Rules that do NOT change
- Domain events remain **intra-module**. Cross-module communication is still ACL ports
  + Contracts (ADR-0002) — never events.
- Aggregates still own their events; services never invent events for state they did
  not load.
- No bus packages. **Correction (2026-08-28): only Wolverine is arch-tested.**
  `No_Module_Should_Use_Wolverine` bans exactly the string `"Wolverine"`; MassTransit,
  RabbitMQ, Azure Service Bus and Kafka would all pass today. This line previously read
  "(Wolverine/MassTransit/RabbitMQ) — arch-tested", which was false. The ban on the others is
  a **convention**, not a check — treat it as reviewable, not enforced.

---

## Amendment (2026-08-28): the async migration path, before it is needed

Written because a production-readiness review flagged the seam as the highest-impact *future*
gap: `IEventDispatcher` exists, 29 domain event types are raised, and none of them go anywhere.
The risk is not that the current system is broken — it is that the first async requirement
arrives under time pressure and the choice gets made badly. So the choice is made here, now.

### The decision, in advance

**When the first async requirement lands, add an OUTBOX in the module that owns the event, drained
by a `BackgroundService` in that module's `Adapter/Outbound`. Do not add a message bus first.**

Rationale, in order of weight:

1. **The transactional property is the hard part, and only an outbox gives it.** An event that
   matters must be persisted *in the same `SaveChangesAsync`* as the state change that produced
   it, or you get the classic pair of failures: the state committed and the notification lost, or
   the notification sent for a state change that rolled back. A bus does not solve this; an
   outbox does, and it does so **without any new infrastructure** because the module already has
   a database and a single-`SaveChangesAsync` write path.
2. **The reaper is a working precedent.** `ReservationExpirationService` is already a
   `BackgroundService` in `Catalog/Adapter/Outbound` that scans, acts and saves on a timer. An
   outbox drainer is the same shape. Copy it — including its honest limitation (see *Operational
   preconditions* below).
3. **A bus is the second step, not the first.** Once events are durably captured, the transport
   is a swappable detail: the drainer can hand off to MassTransit, Azure Service Bus, or an HTTP
   call. Choosing a transport before the events are durable means writing the hard part twice.

### Concretely

| Step | Change | Where |
|---|---|---|
| 1 | `OutboxMessage` entity (Id, Type, Payload, OccurredOn, ProcessedOn?, Attempts, Error?) | owning module's `Application/Domain` |
| 2 | Replace `NoOpEventDispatcher` with an `OutboxEventDispatcher` that **appends rows to the same DbContext** — so `DispatchAndClearAsync` enlists in the caller's pending `SaveChangesAsync` rather than doing IO | `Adapter/Outbound` |
| 3 | `OutboxDrainService : BackgroundService` — claim a batch, handle, mark processed, bounded retries, then a dead-letter state. **Log and meter a permanently failed message**; a silent drop is the failure mode ADR-0005's compensation counter exists to prevent | owning module's `Adapter/Outbound` |
| 4 | Index `(ProcessedOn, OccurredOn)` filtered on `ProcessedOn IS NULL` — the drainer's query must not scan processed history. Same filtered-index pattern as `ClientRequestId` | module's EF configuration |
| 5 | Only if an external system is involved: introduce a transport behind an outbound port, and let the drainer call it | new ADR |

### Operational preconditions — read before shipping this

- **The drainer needs leader election or a scheduler.** Every replica runs every
  `BackgroundService`, so N instances means N drainers competing. The existing reservation reaper
  has the same property and gets away with it because its operations are idempotent and
  commutative; an outbox drainer that sends email does **not** get away with it. Claim rows with
  `UPDATE … OUTPUT` or an equivalent atomic claim, or elect a leader.
- **Ordering is not free.** An outbox drained in parallel delivers out of order. If a handler
  needs ordering, partition by aggregate id and drain each partition serially.
- **At-least-once, not exactly-once.** Handlers must be idempotent. The codebase already has the
  pattern — `ClientRequestId` plus a filtered unique index (ADR-0005 follow-up).

### What does NOT change

Cross-module communication stays ACL ports + Contracts (ADR-0002). **An outbox is for
side effects, not for module-to-module calls** — replacing a synchronous ACL call with an event
would trade a refusal you can act on for an eventual consistency this system does not otherwise
have, and would invalidate ADR-0008's "reads are never stale".

## References
- ADR-0002: Direct Inter-Module Calls (the "transactional" claim is withdrawn)
- ADR-0005: CrossModuleSaga (compensation, not events)
- ADR-0008: Read-Optimized CQS terminology
- v2 ADR-0005: Domain Event Dispatch Pipeline (superseded)
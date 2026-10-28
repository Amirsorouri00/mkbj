# ADR-0005: Cross-Module Consistency — Saga Toolkit, Not Wolverine

## Status
Accepted — 2026-08-24 | **Superseded (2026-09-01)** by
[ADR-0011](0011-simplify-back-to-a-broker-free-bus-free-architecture.md) — `CrossModuleSaga` and
`SagasMetrics` were removed. Cross-module writes are now plain sequential method calls with no
compensation toolkit; a caller that needs to undo a partial cross-module effect writes that logic
itself. The reaper (`ReservationExpirationService`) described below is untouched and still runs.

## Context
ADR-0002 originally claimed cross-module operations share one database transaction.
That claim was false: each module owns a separate database and connection, and no
`TransactionScope` exists in the codebase. The real semantics are a **synchronous
saga with compensation**, previously hand-rolled and inconsistent:

- `OrderService.CompensateReservationsAsync` swallowed exceptions (`catch { /* Best effort */ }`)
  — failed compensations were invisible.
- No tooling existed for the next cross-module flows (Orders→Payment, Payment→Customer).
- Reservation TTL expiry had **no reclaimer**: expired reservations leaked `ReservedStock`
  until an order cancel happened to release them.

Before building anything, three options were evaluated.

## Options Considered

### Option A — Custom saga toolkit in BuildingBlocks *(CHOSEN)*
A minimal `CrossModuleSaga` executor: ordered steps, each with an optional typed
compensation; on failure, completed steps are compensated in reverse order,
best-effort but **logged and counted as metrics**. Plus a Catalog
`ReservationExpirationService` (`BackgroundService`) that reclaims expired stock.

- ✅ Zero new dependencies; ~120 lines total.
- ✅ Pure delegates → unit-testable without mocking any framework.
- ✅ Matches the v3 constraint set exactly (direct calls, no events).
- ✅ Compensation failures become observable instead of swallowed.
- ❌ We own it (small surface, low risk).
- ❌ Synchronous only — no durability across process crashes mid-saga
  (mitigated below).

### Option B — Wolverine *(REJECTED)*
- ❌ Directly contradicts the v3 founding decision (ADR-0002/0004) made after v2's
  Wolverine pain (runtime compilation, outbox overhead, eventual consistency).
- ❌ Violates the enforced architecture rules: `No_Module_Should_Use_Wolverine`
  fails the build on any Wolverine reference.
- ❌ Introduces a message bus + handler model for what is fundamentally one process,
  one request path — massive mechanism-to-problem mismatch.

### Option C — Transactional Outbox + BackgroundService *(REJECTED as primary)*
An outbox solves *reliable asynchronous delivery of messages*. This system has no
message delivery — inter-module calls are synchronous method calls. An outbox here
would be machinery without a consumer: rows written to a table nobody reads.
The only durable element we actually need — TTL-based reservation reclaim — is
delivered by a plain `BackgroundService` inside Option A.

## Decision
1. Adopt **Option A**: `EwaysVNext.Framework.Sagas.CrossModuleSaga` +
   `SagasMetrics`, used by all current and future cross-module write operations.
2. Add a Catalog-hosted `ReservationExpirationService` (Option C's durable element)
   that releases expired reservations every minute — closing the stock-leak hole.
   **It now does two jobs per cycle** (see "The reaper does two jobs" below).
3. **Reaffirm ADR-0004**: Wolverine remains banned. This ADR does *not* reconsider
   that rejection — it removes the last practical argument for revisiting it by
   giving sagas first-class support.

## Consequences
- ✅ Cross-module failure semantics are explicit, uniform, logged, and measured.
- ✅ Expired reservations are reclaimed automatically; `AvailableStock` self-heals.
- ✅ New modules follow a documented pattern instead of inventing try/catch chains.
- ✅ **A transient database fault no longer reaches the saga.** `AddModuleDbContext` configures
  `EnableRetryOnFailure(3, 2s)`, so a deadlock victim or dropped connection is retried *before*
  `SaveChangesAsync` can fail. This changes the failure model this ADR describes: previously a
  50 ms blip surfaced as a failed step and triggered full compensation — reservations released
  and an order refused — for a fault nothing was actually wrong with. Compensation is now
  reserved for real failures.
  **The precondition:** an EF execution strategy refuses user-initiated transactions, so every
  write path must stay load → mutate → one `SaveChangesAsync`. A source scan in
  `TransientFaultResilienceTests` fails the build if `BeginTransaction`/`UseTransaction`
  appears in `src/`, because the InMemory provider has no execution strategy and no test would
  otherwise catch it before production.
- ⚠️ Mid-saga process crash can still strand partial state (e.g., reservation held).
  Mitigations: every reservation carries a TTL and is now reclaimed automatically;
  compensation failures are logged/metered for ops follow-up. If these guarantees
  ever prove insufficient, the migration path is per-step idempotency keys + a
  persisted saga log — NOT a message bus.
- ⚠️ Compensations must remain idempotent. `ReleaseReservation` no-ops when the
  reservation is ABSENT — but that is only half the story, and the missing half was a
  live defect: it also used to accept an already-COMMITTED reservation and decrement
  `ReservedStock` a second time, driving it negative and inflating `AvailableStock`
  (phantom inventory that oversells). Release now REJECTS a committed reservation;
  undoing a commitment is `RevertCommitment`'s job and the two are not interchangeable.
  Cancellation therefore chooses between them based on order status — see
  `Order.Cancel` and `CancellationInventoryEffect`. A `CHECK (ReservedStock >= 0)`
  constraint backstops the arithmetic at the database.

## The reaper does two jobs

`ReservationExpirationService` runs every 60 seconds and, per cycle:

1. **Releases** uncommitted holds past their TTL — the original job. An uncommitted hold still
   counts against `AvailableStock`, so this is the urgent half.
2. **Prunes** committed reservation rows past a retention window — added later, because nothing
   ever deleted them.

**Why pruning became necessary.** Committed rows are retained on purpose: a replayed
`CommitReservation` must stay an idempotent no-op rather than throwing
`RESERVATION_NOT_FOUND` and wedging the order, and `RevertCommitment` needs the row when a
**confirmed** order is cancelled. But nothing deleted them either, so `Product` was an
**unbounded aggregate** — and because reservations are an owned collection, every load of the
aggregate dragged the product's entire commercial history with it. A hot product got slower
forever.

Worse, the reaper's own query matched `ExpiresAt <= now` for committed rows too. A committed
row's `ExpiresAt` stays in the past permanently, so **every product that had ever sold a unit
was re-loaded, with its full reservation history, on every tick** — while the release logic
(which filters `!IsCommitted`) reclaimed nothing and the log reported success. A predicate that
disagrees with the aggregate is a scheduled no-op that gets more expensive every day.

**Why a retention window rather than delete-on-commit.** `Order.Cancel` is legal from both
`Pending` and `Confirmed` with no time bound, and Catalog must not ask Orders whether shipping
has happened. So the row's lifetime is a documented **policy** on the reaper —
`CommittedRetention = 90 days` — not a fact derived from order state. Growth becomes bounded by
retention × throughput instead of unbounded.

**The accepted trade-off:** a cancellation more than 90 days after confirmation fails loudly
with `RESERVATION_NOT_COMMITTED`. Refusing an impossibly-late cancellation is recoverable;
silently re-crediting stock for one is not.

A `CommittedAt` column records when the commitment happened, because `ExpiresAt` is the TTL of
the *hold* and nothing stops a commit landing after it — reusing it would be the same
"one field, two meanings" conflation that once let a confirmed order's reservations be
"released".

## Scope boundary vs single-module writes
CrossModuleSaga exists for CROSS-module workflows only. Within a single module there
is deliberately **no explicit multi-aggregate transaction primitive**: every module
is designed with exactly one aggregate root, and normal writes complete through a
single `SaveChangesAsync`, which EF Core wraps in an implicit atomic transaction.
Never use the saga for single-module atomicity, and never attempt cross-module
atomicity through any module's unit-of-work primitive. (An earlier
`ExecuteInTransactionAsync` UoW member existed in the baseline and was removed as
unconsumed — see the EfCoreBase docs.)

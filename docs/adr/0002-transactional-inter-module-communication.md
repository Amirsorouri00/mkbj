# ADR-0002: Direct Inter-Module Calls (No Wolverine, No Events)

> The title originally read "**Transactional** Inter-Module Communication". The word was
> wrong and is the single most-copied mistake in this repository's documentation. The
> filename is kept so existing links do not break.

## Status
**Amended (2026-08-24)** — superseded in part by
[ADR-0005](0005-cross-module-consistency-saga-toolkit.md).
**Amended again (2026-09-01)** — ADR-0005's `CrossModuleSaga` compensation toolkit was itself
removed; see [ADR-0011](0011-simplify-back-to-a-broker-free-bus-free-architecture.md). A
cross-module operation is now a plain sequence of direct calls with no compensation
infrastructure — a caller that needs to undo a partial effect on failure writes that logic
itself, per operation.

The **no-bus, direct-call** decision stands and is still how the system works.
The **transactional** claim was false and is withdrawn.

## Context
v2 used WolverineFX with integration events and a transactional outbox. That works, but it
adds Wolverine runtime compilation, publish-after-commit, eventual consistency between
modules, and outbox overhead. The team wanted a simpler mental model for a modular monolith.

## Decision
**Remove WolverineFX entirely. Use direct method calls through hexagonal ports.**

Orders calls Catalog's reservation port directly, via the `ICatalogGateway` ACL adapter.

## WITHDRAWN — what this ADR used to claim

> ~~"Both modules participate in the same database transaction — if either fails, both roll
> back."~~ and ~~"Strong consistency (not eventual)."~~

Neither was ever true of the code:

- Every module owns a **separate `DbContext`, schema and connection**. There is no
  `TransactionScope`, no distributed transaction, and no shared connection anywhere in the
  repository — verified by a source scan in `TransientFaultResilienceTests`, which fails the
  build if `BeginTransaction` or `UseTransaction` appears in `src/`.
- A cross-module operation therefore **cannot** roll back atomically. Orders reserving stock
  in Catalog and then failing to persist its own order leaves the reservation behind until
  something releases it.

## The real semantics
A **synchronous saga with best-effort compensation**, made explicit and tool-supported by
`CrossModuleSaga` — see [ADR-0005](0005-cross-module-consistency-saga-toolkit.md) for the
executor, the compensation ordering, the `SagaOutcome` model and the failure metrics.

Practical consequences a reader must not get wrong:

| | |
|---|---|
| Consistency between modules | **Not** atomic. Eventually consistent via compensation, with a TTL reaper as the backstop |
| Within one module | Atomic — one `SaveChangesAsync` per write path, wrapped in EF Core's implicit transaction |
| If compensation itself fails | Logged and metered (`sagas.compensation_failures`), never swallowed |
| Transient DB faults | Retried before they can trigger compensation (`EnableRetryOnFailure`, ADR-0005) |

## Consequences
- No message bus dependency; no outbox to operate.
- **Not** strong consistency across modules. Anyone who needs that must put the data in one
  module, because the aggregate boundary — not a transaction — is what makes it possible.
- Simpler mental model, *provided* the model is the accurate one: a saga, not a transaction.
- Still extractable to a microservice later by swapping the adapter for an HTTP client, and
  the saga semantics mean that swap does not change the consistency story.

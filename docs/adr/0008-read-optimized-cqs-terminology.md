# ADR-0008: Terminology Correction — "Read-Optimized CQS" (CQS+), not CQRS

## Status
Accepted — 2026-08-24 | **Amended (2026-09-01)**: a generic `ICommandBus`/`IQueryBus`
(service-locator dispatch) was added to `EwaysVNext.Commands.*`/`EwaysVNext.Queries.*` as
available infrastructure, then removed — see
[ADR-0011](0011-simplify-back-to-a-broker-free-bus-free-architecture.md). It had zero real
callers in either module; every `Application/Ports/Service` class was, and still is, called
directly. The CQS terminology and the `Application`/`QueryApplication` split described below are
unaffected — this amendment only concerns the bus that never got adopted. | **Amended again
(2026-09-01)**: `QueryApplication` no longer does its own EF projections (see
[ADR-0012](0012-queryapplication-is-the-public-contract.md)) — it now holds contract
interfaces only, `Application/Ports/Outbound` and `Adapter.Outbound` do the projecting. The
CQS terminology and the "reads bypass aggregate hydration" *property* this ADR names both
still hold; only which project performs the projection changed.

## Context
A naming audit found this architecture repeatedly described as "CQRS". That term is
inaccurate and creates false expectations. Precise taxonomy:

| Property | CQS (Meyer) | **This system (CQS+)** | Full CQRS (Young) |
|---|---|---|---|
| Commands never return data / vice versa | ✅ | ✅ | ✅ |
| Separate write & read **code** models | ❌ | ✅ (`Application/` vs `QueryApplication/`) | ✅ |
| Read shape differs from aggregate shape | n/a | ✅ (flat Contract projections) | ✅ |
| Reads bypass aggregate hydration | n/a | ✅ (direct SQL projections) | ✅ |
| Single source of truth database | ✅ | ✅ (**same DB serves reads**) | often ❌ |
| Event-driven read-model propagation | ❌ | ❌ (**synchronous projections**) | typical ✅ |
| Read replicas / independent scaling | ❌ | ❌ | common ✅ |

So: we have genuine *model* separation (more than plain CQS — reads never touch
aggregates and project into materially different shapes), but none of the operational
machinery that makes CQRS what it is: separate stores, event-driven updates,
eventual consistency, independent scaling. "CQRS" as a label promised machinery the
architecture deliberately rejected (no bus, ADR-0002; no events dispatched, ADR-0004).

**Names are contracts.** An architect reading "CQRS" in our docs would assume
eventual consistency between sides, look for projection replay infrastructure, and
misjudge failure modes. None exist.

## Decision
Adopt **"Read-Optimized CQS (CQS+)"** as the official term everywhere:
AGENTS.md, ADRs, code comments, onboarding material. Definition to use:

> Each module separates commands (`Application/` hexagon: aggregates + ports +
> services) from queries (`QueryApplication/`: direct, flat EF Core projections into
> Contract DTOs). Both sides share one database per module and one transactional
> truth; queries never load aggregates or execute domain rules; there is no
> asynchronous propagation because both sides read the same store.

### Why true CQRS is not needed for this project
1. **No propagation mechanism exists by design.** Without a bus (ADR-0002) there is no
   way to update a separate read model asynchronously — building one would mean
   reintroducing exactly the machinery v3 removed.
2. **Strong consistency is a feature here.** Payment verification, stock reservation,
   and admin panels must see post-commit state immediately. Eventual consistency
   between command and query sides would be a behavior change, not an optimization.
3. **The read-side benefits CQRS delivers are already captured** by the QueryApplication
   seam: shape freedom (flat DTOs ≠ aggregates), zero aggregate hydration, single
   tunable SQL per query, isolation from domain churn — without projection maintenance.
   "Zero aggregate hydration" was an **aspiration** when this ADR was written, and one query
   silently broke it: `GetProductRowsAsync` projected `p.AvailableStock`, an *unmapped computed
   property*, which made EF abandon the flat projection and materialise the whole `Product`
   entity — including its owned reservation collection via a LEFT JOIN, tracked — to render one
   admin row. It is now a **machine-checked fact**: `ReadSideProjectionTests` asserts the change
   tracker is EMPTY after the query, which is the direct observable symptom of hydration.
4. **Scale evidence does not justify it.** Monolith, one primary per module, admin and
   client traffic well inside synchronous capacity. No measured read pressure.

### Migration path if true CQRS becomes necessary
The seams make this incremental — every consumer already depends only on
`I{Module}QueryService` interfaces + Contracts DTOs:

- **Stage 1 — Repoint implementations (low effort, no contract changes):**
  QueryApplication service implementations switch from live EF projections to
  denormalized read tables/views maintained in the same module database
  (sync-maintained in-transaction or via triggers/CDC capture). Callers unchanged;
  still strongly consistent per module.
- **Stage 2 — Async propagation:** introduce outbox + bus (revisits ADR-0002/0004 —
  new ADR required) so read models update after commit; accept eventual consistency
  for designated read models; Reporting switches to reading propagated stores.
- **Stage 3 — Physical split:** move read stores to replicas/separate databases;
  QueryApplications repointed via connection-string configuration only.
- **Stage 4 — Independent scaling/deployment:** possible extraction path already
  described by the port/adapter seams (ADR-0002 consequences).

**Trigger conditions to revisit:** sustained read QPS saturating a module's primary,
reporting/analytics load degrading OLTP latency, cross-region read requirements, or
analytics needing deep denormalization that synchronous composition cannot serve.

## Consequences
- ✅ Documentation matches reality; newcomers form correct expectations.
- ✅ **The no-hydration property is enforced, not just claimed.** Two tests pin it
  (`GetProductRowsAsync` and `ListAsync`, so the pair cannot diverge). The failure mode this
  guards against is invisible without it: the query returns correct data, just at the cost of
  loading an entire aggregate per row.
- ⚠️ **A projection may only reference MAPPED members.** Reaching for a computed property
  (`AvailableStock`), or any member behind a value converter (`p.Sku.Value`), either silently
  hydrates the aggregate or fails to translate at all. Project the columns and compute in the
  DTO — `p.Stock - p.ReservedStock`, not `p.AvailableStock`.
- ⚠️ **Mapping must stay inside the `Select`.** A mapping generator or post-materialization
  mapper defeats this ADR by construction, which is why DDD-GUIDELINES rule 9 no longer
  mandates one.
- ✅ The migration path costs little today *because* the seams exist (this is the
  strongest argument for keeping QueryApplication even under the corrected name).
- ⚠️ External candidates/conversations may still say "CQRS"; correct gently with
  this ADR as reference.

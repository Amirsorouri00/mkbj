# ADR-0003: QueryApplication Separation (Read-Optimized CQS)

> **Terminology note (2026-08-24):** originally titled with "(CQRS)". Renamed per
> [ADR-0008](0008-read-optimized-cqs-terminology.md) — this architecture is CQS with
> physical read/write separation, not full CQRS (no separate stores, no event-driven
> propagation). The separation decision itself stands unchanged.

## Status
Accepted — 2026-08-07 | **Superseded in part (2026-09-01)** by
[ADR-0012](0012-queryapplication-is-the-public-contract.md): `QueryApplication` is no longer
"flat read side, direct EF Core projections" — it is now the module's public contract
(interfaces + models, both read and write), with zero implementation. The actual EF
projection code moved to `Adapter.Outbound` (via the write-side repository, extended with
projection methods), and `Application` now hosts the query-service implementation as a thin
delegator to it. The read/write *separation* this ADR argued for still holds; only where the
line is drawn, and who implements which side, changed.

## Context
Hexagonal architecture imposes overhead on read operations: aggregate loading, domain rules, port indirection. For read-heavy operations (product listing, order history), this overhead is unnecessary.

## Decision
**Separate QueryApplication from the hexagon.** Each module has:
- `Application/` — hexagon (write side: Domain + Ports + Service)
- `QueryApplication/` — flat read side (direct EF Core projections, no aggregate loading)

## Consequences
- Read operations are fast (no aggregate overhead)
- Write operations are protected by hexagonal boundaries
- Clear read/write separation without infrastructure complexity (terminology: ADR-0008)

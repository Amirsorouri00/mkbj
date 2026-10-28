# ADR-0001: Hexagonal-First Architecture (v3)

## Status
Accepted — 2026-08-07 | **Amended (2026-09-01)**: `QueryApplication` is no longer just "the
read side, outside the hexagon" — see
[ADR-0012](0012-queryapplication-is-the-public-contract.md). It now holds every interface
(read AND write) another module is allowed to call, plus the models those interfaces use;
`Application` implements all of it. The rest of this ADR's structure stands unchanged.

## Context
v1 and v2 used Layer-First architecture (Domain/Application/Infrastructure/Presentation). While this is a valid approach, it creates cognitive friction: developers must think in "layers" first, then retrofit Hexagonal concepts inside them.

## Decision
**Use Hexagonal-First architecture.** Each module is organized around the hexagon boundary:
- `Application/` (hexagon: Domain + Ports + Service)
- `Adapter/Inbound/` (driving adapters)
- `Adapter/Outbound/` (driven adapters)
- `QueryApplication/` (read side, outside hexagon)

No layer names (Infrastructure, Presentation) are used — only Hexagonal terminology.

## Consequences
- Clearer mental model: "where is the port? where is the adapter?"
- Easier to extract a module to a microservice (swap adapter, keep hexagon)
- Slightly more projects per module (5 instead of 4)

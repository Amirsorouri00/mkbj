# ADR-0013: Narrow QueryApplication to Only the Interfaces Modules Actually Call

## Status
Accepted — 2026-09-01.

Amends [ADR-0012](0012-queryapplication-is-the-public-contract.md).

## Context
ADR-0012 moved every inbound-facing interface — both write commands (`IProductService`,
`ICatalogAdminService`) and read queries (`IProductQueryService`) — into `QueryApplication`,
on the reasoning that any of them *could* be called cross-module under ADR-0002's ACL Gateway
pattern. ADR-0012 itself flagged the cost of this as a "partial misnomer": `QueryApplication`
holding write-command interfaces contradicts its own name, and deferred fixing it "only
alongside another reason to touch every one of these files."

That reason arrived immediately: in this codebase, right now, `IProductService` and
`ICustomerInboundPort` are called only by their own module's Rest adapter — never by another
module. `ICatalogAdminService` and `ICustomerAdminService` are called by nothing at all (dead
scaffolding, registered in DI, invoked by no one). Only the read query interfaces
(`IProductQueryService`, `ICustomerQueryService`) were ever actually exercised cross-module,
in the reverted Admin ACL Gateway experiment. Putting every interface in `QueryApplication`
"just in case" reintroduced exactly the kind of ahead-of-need infrastructure this whole
session has been removing everywhere else (the CQRS bus, the cache/lock abstractions, the
saga toolkit) — the difference here being organizational rather than a runtime dependency, but
the same mistake: designing for a consumer that does not exist yet.

## Decision
**`QueryApplication` holds only the interfaces and models a genuine cross-module (or
same-module-boundary-crossing) contract actually needs today. Everything else goes back to
`Application/Ports/Inbound/`, where it is actually consumed.**

For both modules, that means:
- `QueryApplication` keeps: the query contract (`IProductQueryService`/`ICustomerQueryService`)
  and the models it returns (`ProductSnapshot`, `ProductAdminRow`, `CustomerSnapshot`).
- `Application/Ports/Inbound/` gets back: the write-command port (`IProductService`/
  `ICustomerInboundPort`, called only by that module's own Rest adapter) and the admin port
  (`ICatalogAdminService`/`ICustomerAdminService`, called by nothing). Their exclusively-owned
  models (`ReservationSnapshot`, `CustomerStatusDto`, `SavedPaymentMethodDto`) move with them,
  since nothing in `QueryApplication` uses them anymore.

This is not a reversal of ADR-0012's core structural rule — `QueryApplication` is still the
contract layer, `Application` still implements contracts, DI registration still lives in
`Application`. It only narrows WHICH interfaces qualify as "the contract": genuinely
cross-module-callable, not merely hexagonal-inbound-port-shaped. If a real cross-module
Gateway is ever built against `IProductService` (e.g. a future module writing to Catalog
through the ACL pattern), that is the "another reason to touch these files" — move it back to
`QueryApplication` then, backed by an actual caller, not in anticipation of one.

## Consequences
- `QueryApplication`'s name is accurate again: it holds queries, because in this codebase
  today, queries are the only thing genuinely called across a module boundary.
- `Application/Ports/Inbound/` — Rest adapters go back to depending on `Application` directly
  (they never needed `QueryApplication` for anything except the interface that moved out).
  `QueryApplication` is now referenced by exactly two things: `Application` (to implement the
  query contract) and `Adapter.Outbound` (for the projection methods' return types).
- The next module that needs to call another module's write port has a clear, cheap move:
  promote that one interface (and only that one) to `QueryApplication`, at the point someone
  actually writes the Gateway — not before.

## References
- ADR-0002: Direct Inter-Module Calls / ACL Gateway pattern
- ADR-0011: Simplify back to a broker-free, bus-free architecture (the pattern this ADR
  follows: remove or narrow anything built ahead of an actual caller)
- ADR-0012: QueryApplication Becomes the Module's Public Contract (the structural rule this
  ADR narrows, not reverses)

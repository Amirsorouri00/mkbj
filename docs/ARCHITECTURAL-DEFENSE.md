# Architectural Defense & Rationale Guide

> **Audience:** senior engineers reviewing or extending EwaysVNext v3.
>
> **Contract of this document.** Every answer is grounded in the current codebase, the ten ADRs,
> or a named test. Where a decision has a real cost, the cost is stated. Where we do not know
> something — notably capacity (Q9) — the answer says so rather than estimating.
>
> Two questions arrived with a factually incorrect premise (Q4 and, partly, Q2). Those are
> corrected in the answer rather than answered as asked.

---

## Q1 — Why a `SharedKernel` project inside `BuildingBlocks`? Isn't all of `BuildingBlocks` the Shared Kernel?

### Rationale

`BuildingBlocks` is a **folder**, not a project. It contains five separate assemblies, and only
one of them is a Shared Kernel in Evans' sense:

| Project | What it is | Contains |
|---|---|---|
| `SharedKernel` | **The DDD Shared Kernel** — domain model shared by ≥2 contexts | `Money`, `Email` — that is all |
| `DDD` | Tactical base types | `Entity`, `AggregateRoot`, `ValueObject`, `DomainException`, `IEventDispatcher` |
| `Framework` | Cross-cutting, non-domain | `Result`/`Error`, `Actor`, `IClock`, `CrossModuleSaga` |
| `Contracts` | Cross-boundary DTOs | Flat records, one folder per module |
| `Infrastructure.Persistence` | EF Core plumbing | `AddModuleDbContext`, `EfCoreRepository`, UoW |

The Shared Kernel is a *bounded-context concept with a heavy governance cost*: every context that
uses it is coupled to it, so changing it requires agreement from all of them. That is why it
holds exactly **two types**. `Money` and `Email` are genuine shared *domain* concepts — Catalog,
Orders and Payment must agree on what an amount is; Identity and Supplier must agree on what an
email is.

`Result` is not a domain concept, it is a control-flow type. `Entity` is not shared understanding,
it is a base class. Putting them in `SharedKernel` would imply a governance burden that does not
apply, and would blur the one folder where "changing this needs everyone's agreement" is true.

### Pros & cons

**Pros.** The name means what DDD says it means, and its tiny size is the signal — two types is a
*healthy* Shared Kernel. A reviewer can tell at a glance which shared code carries the
cross-context contract and which is infrastructure.

**Cons.** The folder name `BuildingBlocks` invites exactly this question, and will keep doing so.
Five projects for shared code is more ceremony than one. The distinction is conceptual and not
machine-enforced — nothing stops someone dropping `Result` into `SharedKernel` tomorrow.

### Alternatives considered

- **One `Shared` project.** Rejected: it would let a domain value object and an EF extension
  method live in the same assembly, so `Application` could no longer reference domain shared code
  without also gaining a transitive path to persistence.
- **Rename the folder to `Common` or `Platform`.** Reasonable, and would remove the ambiguity.
  Rejected as churn: the folder name has no semantic weight, and the ADRs and guide all reference
  the current paths.

### Evidence

`find src/BuildingBlocks -name "*.cs"` returns 24 files, of which exactly **two** are under
`SharedKernel/`: `ValueObjects/Money.cs` and `ValueObjects/Email.cs`.

---

## Q2 — We are against the Repository pattern over EF Core, since `DbContext` is already UoW + Repository. Why is it here?

### Rationale

**That objection is correct, and it is the reason this is not a generic repository.** There is no
`IRepository<T>`, no `GetAll()`, no `IQueryable` leaking out of an adapter. Verified:
`grep "interface IRepository"` returns nothing.

What exists is a **module-owned outbound port** listing only the operations that module's hexagon
actually needs:

```csharp
public interface IProductRepository
{
    Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Product?> GetBySkuAsync(string sku, CancellationToken ct = default);
    Task<bool> SkuExistsAsync(string sku, CancellationToken ct = default);
    Task<IReadOnlyList<Product>> ListAsync(int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<Product>> FindWithReclaimableReservationsAsync(
        DateTimeOffset utcNow, TimeSpan committedRetention, CancellationToken ct = default);
    Task AddAsync(Product aggregate, CancellationToken ct = default);
}
```

Three concrete things this buys that a bare `DbContext` does not:

1. **`Application` cannot reference EF Core.** That is arch-tested
   (`Application_Should_Not_Depend_On_EntityFrameworkCore`). Without the port, every service would
   need `Microsoft.EntityFrameworkCore` and the hexagon boundary would be a naming convention.
2. **The reads are curated, and their SQL is reviewable in one place.** The value-object predicate
   trap that broke login lives *here* — `p.Sku == value`, never `p.Sku.Value == sku` — and it is
   documented at the exact line where someone would get it wrong.
3. **The port is the seam the dual-provider tests drive.** `PersistenceContractTests` resolves
   `IProductRepository` from DI and runs the real implementation on two providers. A bare
   `DbContext` in a service would leave nothing to resolve.

The generic-repository criticism targets a different pattern: `Repository<T>` with `Find`,
`Query`, `Update`, `SaveChanges`, wrapping `DbSet` one-to-one. That pattern adds a layer, hides
EF's capabilities, and gives nothing back. We do not have it.

### Pros & cons

**Pros.** Hexagon purity is machine-checked, not aspirational. Query shapes are auditable. Tests
reach real code through DI. Swapping SQL Server for another relational store touches adapters
only.

**Cons — real ones.** Every new query means a new port method plus an implementation: two edits
where `_db.Products.Where(...)` inline would be one. The `IUnitOfWork` port is a *second*
abstraction over the same `DbContext` (`EfCoreUnitOfWork` is a five-line pass-through). And it
genuinely constrains: no `Include` tuning or split queries from the Application layer without
adding a method for it. That is the intended trade — it is still a cost.

### Alternatives considered

- **Inject `DbContext` into services directly.** Rejected: it deletes the arch test, and the read
  side would then be able to mutate. `QueryApplication_Must_Not_Reference_Write_Side_Outbound_Ports`
  exists precisely because we split reads from writes.
- **Generic `IRepository<T>`.** Rejected for exactly the reason in the question — it wraps EF
  without adding meaning.
- **Repository for writes, raw `DbContext` for reads.** This is what we actually do.
  `QueryApplication` injects the module's `DbContext` directly and projects. The repository exists
  *only* on the write side, where aggregate loading is the point.

### Evidence

`tests/ArchitectureTests/HexagonalBoundaryTests.cs` — `Application_Should_Not_Depend_On_EntityFrameworkCore`,
`Outbound_Ports_Must_Be_Interfaces`. `docs/DDD-GUIDELINES.md` rule 5 ("One Repository per Aggregate
Root… child entities are reached through the root"). `CustomerQueryService` injects
`CustomerDbContext` with no repository in sight — the asymmetry is deliberate.

---

## Q3 — Won't `Contracts` become a monolith? Why not move `Snapshot` records into each module's `QueryApplication`?

### Rationale

Because a snapshot is a **contract between two modules**, and its owner is neither of them.

`ProductSnapshot` is produced by Catalog and consumed by Orders. If it lived in
`Catalog.QueryApplication`, then Orders would need a project reference to
`Catalog.QueryApplication` to name the type — which is precisely the cross-module coupling the
whole architecture forbids. The DTO must live somewhere both can reference *without* referencing
each other. That is what `Contracts` is.

The structure is already folder-per-module inside one assembly:

```
Contracts/{Admin,Brand,Catalog,Customer,Identity,Orders,Payment,Supplier}/
```

### Pros & cons

**Pros.** Zero cross-module project references for shared vocabulary. One place to look for
"what does module X expose". Records only — arch-testable as behaviour-free.

**Cons — the question is right about the cost.** A one-field change to any DTO recompiles every
module. At 8 modules that is seconds; at 30 it is a build-time tax. There is also nothing
enforcing that a module only touches *its own* Contracts folder — `Orders.Application` could
reference `Contracts.Supplier` today and no test would object. That is a real, unguarded seam.

### Alternatives considered

- **Snapshots in `QueryApplication`.** Rejected: creates the exact cross-module reference the
  architecture bans. Would also fail `QueryApplication_Must_Be_Isolated_From_Other_Modules_And_Infrastructure`.
- **One `Contracts` assembly per module** (`EwaysVNext.Contracts.Catalog`, etc.). Genuinely
  better at scale — it makes "who may reference whom" a project-level fact, and kills the
  rebuild-everything problem. Rejected **for now** as premature at 8 modules; it multiplies the
  project count by ~8 for a cost we have not yet felt. **This is the recommended refactor at
  ~15 modules**, and it is cheap because the folder-per-module layout already maps one-to-one
  onto it.
- **Duplicate DTOs per consumer.** Rejected: two definitions of "what a product looks like" drift,
  and the drift is silent.

### Evidence

`src/BuildingBlocks/Contracts/` — nine folders, records only. `ProductSnapshot` in
`Contracts/Catalog/Snapshots.cs` carries the doc comment explaining that dropping its `Currency`
field is what let a EUR payment satisfy a USD order.

---

## Q4 — Are cross-module project references allowed? Rule 11 says "non-negotiable". Which is it?

### The premise needs correcting

**Rule 11 does not say "no cross-module project reference".** Its actual text:

> **11. Module boundaries are non-negotiable**
> No module **Application** references another module's Application. The only sanctioned
> bridges are **adapter-level ACL gateways** (ADR-0002/0006/0007/0009).

The rule is about **which layer may hold the reference**, not a blanket ban. Cross-module
references are not merely allowed — they are the mechanism. They are confined to
`Adapter.Outbound`.

### Rationale

A hexagon must not know another hexagon exists; an *adapter* is exactly the place where the
outside world is allowed in. So `Payment.Application` defines `ICustomerGatewayPort` in its own
terms, and `Payment.Adapter.Outbound.CustomerGateway` — the only assembly holding the reference —
implements it by delegating to `Customer.Application`'s inbound port, passing Contracts DTOs only.

Five sanctioned exceptions exist today:

| Consumer → Provider | Reason |
|---|---|
| `Orders.Adapter.Outbound` → `Catalog.Application` | Stock reservation ACL |
| `Payment.Adapter.Outbound` → `Customer.Application` | Customer standing + wallet |
| `Supplier.Adapter.Outbound` → `Brand.Application` | Brand existence check (ADR-0009) |
| `Orders.Adapter.Outbound` → `Payment.Application` | Order-owned payment gate |
| `Admin.Adapter.Outbound` → provider `Application`/`QueryApplication` | Composition layer (ADR-0006/0007) |

### Pros & cons

**Pros.** The consumer owns its contract, so a provider change cannot silently reshape it. Each
exception is one line of test data and therefore a conscious decision.

**Cons.** The arch test file becomes a coordination point for cross-module communication. At 20
modules with 10+ ACLs it will be large, and refactoring its logic risks every existing exception
at once.

### Alternatives considered

- **A literal no-reference rule with an in-process bus.** Rejected by ADR-0002/0004: a bus buys
  decoupling you do not need in one process, and costs you the ability to *refuse* — a synchronous
  ACL can say "suspended" before anything is written.
- **Reflection / `IServiceProvider` lookup to dodge the compile-time reference.** Rejected: it
  removes the reference from the csproj and therefore from the arch test, converting a checked
  boundary into an unchecked one. Strictly worse.

### Evidence

`docs/DDD-GUIDELINES.md` rule 11 (full text above).
`Only_The_Sanctioned_Gateway_Adapter_May_Reference_Provider_Application` — a `[Theory]` whose
`[InlineData]` rows *are* the exception list.

**A cautionary note, and it is ours to own.** The `Supplier → Brand` exception existed for some
time with **no row in that theory**. The csproj said "the ONLY bridge to Brand.Application" and
nothing verified it. It was added on 2026-08-28 and proven with a mutation: a forbidden reference
from `Supplier.Adapter.Inbound.Rest` is caught by **exactly one test** — that row. Before it, the
violation would have shipped green. A documented exception that is not in the matrix reads as
checked and is not.

---

## Q5 — If the rule is "no events", what is `IEventDispatcher` for?

### Rationale

The rule is narrower than "no events". It is: **no events for cross-module communication.** Domain
events remain intra-module.

`IEventDispatcher` is a **seam**, not dead code. Aggregates raise events (`Raise(...)`); services
call `DispatchAndClearAsync` after `SaveChangesAsync`; today the registered implementation is
`NoOpEventDispatcher` and nothing is delivered. What it buys is that the *call sites already
exist*. Adding async side effects later means changing one registration, not editing every
service.

29 domain event types are raised today. All go nowhere.

### Pros & cons

**Pros.** The seam is real and correctly placed — after the write, inside the service, never
inside the aggregate. Swapping the implementation requires no change to any hexagon.

**Cons, stated plainly.** 29 event types that nothing consumes is ceremony with a maintenance
cost: they must be kept accurate for a consumer that does not exist, and a reader reasonably
asks whether raising them is doing anything. An external audit called this the highest-impact
*future* risk, and that framing is fair — nothing breaks today, but the first async requirement
arrives with no path.

**So the path was decided in advance rather than under pressure.** ADR-0004's 2026-08-28 amendment
specifies: **outbox in the owning module, drained by a `BackgroundService`, transport only later**
— because the transactional property is the hard part and only an outbox provides it, and choosing
a transport first means writing the hard part twice.

### Alternatives considered

- **Delete `IEventDispatcher` until needed.** Rejected: re-introducing it later means editing
  every service and every aggregate at once, under deadline.
- **Wire an in-memory dispatcher now.** Rejected: handlers running inside the request transaction
  are a foot-gun (a failing handler rolls back the business operation), and handlers running
  outside it lose the transactional guarantee an outbox would give.
- **Wolverine / MassTransit now.** Rejected by ADR-0002/0004 — machinery without a consumer.

### Evidence

`Program.cs` registers `NoOpEventDispatcher.Instance`. ADR-0004 and its amendment.
**One correction found while writing that amendment:** ADR-0004 previously claimed
"No bus packages (Wolverine/MassTransit/RabbitMQ) — arch-tested". Only **Wolverine** is
arch-tested; the others are convention. The ADR now says so.

---

## Q6 — Where is the saga used, is it reliable, and what does it cost?

### Rationale

The saga exists because **modules do not share a transaction.** Each owns its own `DbContext` and
connection, so a cross-module operation cannot roll back atomically. ADR-0002's original "same
transaction" claim is formally **withdrawn**.

### Exact technical flow

`CrossModuleSaga` is a fluent list of `SagaStep(Name, Action, Compensation?)`. `ExecuteAsync`:

1. Runs steps **in order**, tracking the **indices** of completed steps that have a compensation.
2. On a failed `Result` **or** a thrown exception, it stops and compensates the completed steps in
   **reverse order**.
3. It returns the **original** error — compensation never masks the cause.
4. Every compensation failure is logged at Error and increments `sagas.compensation_failures`.

Three subtleties that are each a fixed defect:

- **Indices, not names.** Name lookup misrouted whenever two steps shared a name — and the
  place-order saga names steps `reserve-stock:{productId}`, so one product on two lines
  compensated the first step twice and never released the second.
- **`Compensation` returns `Task<Result>`, not `Task`.** As bare `Task`, `Task<Result>` converted
  implicitly and every compensation silently discarded its own failure — and providers report
  failure by *returning* `Result.Failure`, so the only failure mode that occurs was the one nobody
  could see.
- **Compensation runs on an independent 30-second budget**, not the caller's token — see Q12.

**Where it is used:** placing an order (reserve stock per line → persist order), confirming an
order (verify payment → commit reservations), and payment initiation (authorize + verify standing
→ fetch token → persist).

### Is it reliable?

**Within a running process: yes, and it is the best-tested component here.** Ordering,
index-targeting, abort-vs-failure accounting and compensation-failure observability are all
unit-tested and mutation-proven.

**Across a crash: no, and it does not claim to be.** A `kill -9` between two steps strands partial
state. The mitigations are specific rather than general:

- Catalog reservations carry a TTL and are reclaimed by `ReservationExpirationService`.
- Order and payment creation are idempotent via `clientRequestId` + a filtered unique index, so a
  retry resolves to the original.
- Confirmation **reads** payment standing rather than reacting to an event, so a captured payment
  with an unconfirmed order is a *resumable* state, not a stranded one.

An external audit claimed a crash "strands a captured payment with no automatic reconciliation".
That is inaccurate for this design — the read-at-confirm-time model makes the flow resumable. The
genuine residual is a `Pending` payment the customer may not know about.

### Performance

Steps are sequential `await`s in one request. There is no queue, no serialisation, no extra
network hop — a three-step saga costs three in-process calls plus their database round trips. The
overhead of the executor itself is negligible.

**The real cost is availability coupling.** If Catalog's database is down, order placement fails —
there is no queue to absorb it. That is inherent to choosing refusal over eventual consistency:
you cannot both reserve stock synchronously and survive the inventory store being unreachable
without accepting oversell.

### Alternatives considered

- **Distributed transactions (MSDTC / 2PC).** Rejected: operationally hostile, poor cloud support,
  and it would couple module availability even harder.
- **Event-driven eventual consistency.** Rejected by ADR-0002: it converts a synchronous refusal
  ("insufficient stock") into an asynchronous compensation the customer sees minutes later.
- **A single shared database with one transaction.** Rejected by ADR-0001 — it deletes the module
  boundary, which is the entire point.

### Evidence

`src/BuildingBlocks/Framework/Sagas/CrossModuleSaga.cs`; ADR-0005;
`tests/UnitTests/CrossModuleSagaTests.cs` (14 tests) and `OrderConfirmationSagaTests`.

---

## Q7 — Why the `Actor` type? What else was considered?

### Rationale

Because the alternative was an IDOR vulnerability, and we had it.

Inbound ports used to take the subject id as a plain parameter —
`InitiateWithSavedCardAsync(Guid customerId, …)` — with the value coming from the request body.
Any authenticated user could charge another customer's stored card, or read and cancel another
user's order. The signature made the wrong thing easy and the right thing invisible.

`Actor` is a two-field record — `(Guid UserId, bool IsAdmin)` — with one method:

```csharp
public bool CanActFor(Guid ownerUserId) => IsAdmin || UserId == ownerUserId;
```

It is built by the inbound adapter from `ClaimsPrincipal` via `Actor.TryFrom` and is **never**
deserialized from a payload. Three rules follow:

- **Writes authorize before mutating**, inside the Application service (`LoadOwnedAsync`).
- **Reads** may authorize in the adapter against the owner id the port returned.
- **Admin-only ports take no `Actor` at all** — the *absence* is the signal that authorization
  happened at the Admin-policy-gated transport.

That last rule is why the type is load-bearing rather than cosmetic: the signature now tells you
which authorization model applies.

### Pros & cons

**Pros.** Identity cannot arrive in a request body — there is no property to bind to. Ownership
checks are uniform and greppable. It composes with the ACL: Payment gets the owner's identity id
from `CustomerStatusDto` in the same call that checks standing, so one round trip answers both.

**Cons.** Every customer-facing port signature carries an extra parameter — visual noise on
methods where ownership is obvious. And the "no `Actor` means admin-gated" convention is a
convention: nothing fails the build if someone adds an admin method to a customer-facing port.
The mitigation is transport-level (`AuthorizationBoundaryTests` walks the live
`EndpointDataSource`), not signature-level.

### Alternatives considered

- **`IHttpContextAccessor` in the service.** Rejected: it drags ASP.NET into `Application`, which
  the arch tests forbid, and makes the hexagon untestable without a fake HTTP context.
- **`ClaimsPrincipal` as the parameter.** Rejected: same coupling in a thinner disguise, and it
  hands the domain a bag of strings to re-parse at every call site.
- **Authorize entirely in the adapter.** Rejected for **writes**: the adapter does not know the
  owner until the aggregate is loaded, so the check would run against a value the caller supplied
  — the original bug. It *is* used for reads, where the port returns the owner id.
- **An ambient `ICurrentUser` service.** Rejected: ambient identity has the same testability
  problem as an ambient clock (Q13's cousin) — you cannot see from the signature that a method
  depends on it.

### Evidence

`src/BuildingBlocks/Framework/Security/Actor.cs`;
`CustomerService.LoadOwnedAsync`; `tests/IntegrationTests/AuthorizationBoundaryTests.cs` —
including `Every_Mapped_Endpoint_Requires_Authorization_Except_An_Explicit_Allowlist`, whose
allowlist has six entries (three health probes plus register/login/refresh).

---

## Q8 — Did we make exceptions to using `Money`?

### Rationale

Two, both principled.

**Exception 1 — `BrandSupplyAgreement.CommissionRate` stays a `decimal`.** It is a *percentage*
(0–100), not an amount of money. It has no currency, cannot be added to a price, and wrapping it
in `Money` would assert a unit it does not have. The 0–100 validation lives in the aggregate.

**Exception 2 — Contracts and REST DTOs carry `(decimal Amount, string Currency)` pairs, not
`Money`.** Deliberate: serializing the domain value object would couple the wire contract to the
domain's shape, so a rename or a new invariant becomes a breaking API change. The customer-facing
REST layer uses its own `MoneyResponse(decimal Amount, string Currency)` record for the same
reason.

The non-negotiable part is that **amount and currency travel together**. That was not true until
recently: `OrderResponse` returned a bare `decimal TotalAmount`, so currency was threaded through
the ACL, the aggregate, the read side and every admin DTO — and then dropped at the one boundary a
customer reads.

### Pros & cons

**Pros.** The domain gets invariants (non-negative, currency-matched addition, `MONEY_CURRENCY_MISMATCH`);
the wire stays stable and independent.

**Cons.** Two representations of the same idea means a mapping at every boundary, and a missed
mapping is exactly the defect above. There is no arch test asserting "every monetary DTO carries a
currency" — that guard would be worth adding.

**A third, honest limitation.** `Money` supports **two-decimal currencies only** and *refuses* the
rest: `MONEY_UNSUPPORTED_CURRENCY_DECIMALS` for JPY/KRW (no minor unit) and KWD/BHD/OMR/TND
(three). This became reachable only when currency became derived rather than pinned to `"USD"`.
The check is a **denylist**, not an allowlist — an allowlist would reject every unlisted code and
turn a rounding limitation into an availability problem — so an exotic non-2dp code missing from
the list would still be mis-rounded. Extend the list; do not assume it is complete.

### Evidence

`src/BuildingBlocks/SharedKernel/ValueObjects/Money.cs`; `tests/UnitTests/MoneyTests.cs` (23
tests); `docs/DDD-GUIDELINES.md` "Known inconsistencies"; the payment gate in `OrderService`
compares `Money`, so a right-amount/wrong-currency capture is refused —
`Confirming_When_The_Captured_Currency_Does_Not_Match_Is_Refused`.

---

## Q9 — Assuming the resilience tests pass, what is the request/user capacity per pod?

### The honest answer

**We do not know, and nobody should quote a number from this document.**

Zero load tests exist. Every performance statement in this codebase — including in the
production-readiness assessment and ADR-0010 — is *structural reasoning*, not measurement. The
resilience tests prove that a deadlock is retried and that a concurrent write loses on its
rowversion. They say nothing about throughput, and passing them is not evidence of capacity.

### What we can say without measuring

These are **structural ceilings**, i.e. the things that will bind first. They are hypotheses to
test, not estimates:

| Limit | Value | Note |
|---|---|---|
| Global rate limit | **100 req/min per IP** | In-process. At `replicas: N` it becomes 100 × N |
| Admin GraphQL limit | **10 req/min per user** | Same caveat |
| SQL connection pool | **~100 per connection string** | ADO.NET default. **Seven** pools, one per module |
| Retry budget | 3 attempts, 2s max delay | A retry storm consumes pool slots |
| Compensation budget | 30s per saga | Blocks the request thread |

The most likely first bottleneck is **connection-pool exhaustion under saga load**, not CPU: a
place-order saga touches Catalog and Orders in one request, so it holds slots in two pools
simultaneously for the duration.

### What must be measured before any capacity claim

1. Throughput and p99 for `POST /orders` (the saga path) — the most expensive endpoint.
2. Behaviour at connection-pool saturation: does it queue, or does it fail?
3. The read paths that were unbounded until recently and are now paged
   (`ListForUserAsync`, `ListForCustomerAsync`) — with a customer holding thousands of rows.
4. Reaper cost at scale: the 60-second scan against a large `ProductReservations` table.

### Pros & cons of answering this way

**Pro.** Nobody sizes a cluster on a guess that came from an architecture document.

**Con.** It is genuinely unhelpful for planning. If a number is needed this week, the fastest
credible path is a k6 or NBomber run against a single pod on production-shaped data — roughly a
day's work, and it would replace this entire section with facts.

### Evidence

`Program.cs` rate-limiter configuration; `AddModuleDbContext` (no pool size override, so ADO.NET
defaults apply); `docs/TESTING-GUARANTEES.md` — *"Nothing here is a performance test. 'Seeds 60
noise orders' checks correctness past a page boundary, not throughput."*

---

## Q10 — Is the architecture ready for a Meilisearch-backed Search module?

### Rationale

**Structurally, yes — and it lands cleanly in an existing shape.** But the interesting half of the
question is not the query path, it is the *indexing* path, and that is where the current gap is.

**Reading (easy).** Search is a read concern, so it is `QueryApplication`-shaped: a
`Search.QueryApplication` with an `ISearchQueryService` port, implemented by an adapter that talks
to Meilisearch instead of EF. ADR-0008's CQS+ split already establishes that the read side owns
its own store access, and `QueryApplication_Must_Be_Isolated_From_Other_Modules_And_Infrastructure`
already permits an adapter with a non-EF backing store. Admin/GraphQL composes it the same way it
composes any other read side.

**Indexing (the real question).** Meilisearch needs to be told when a product changes. There are
exactly three options, and only one is consistent with the current architecture:

1. **Synchronous write-through in `ProductService`** — reject. It couples a catalog write to
   Meilisearch availability, so a search outage breaks product creation.
2. **Periodic full/delta re-index via a `BackgroundService`** — viable today, no new machinery,
   and it fits ADR-0010's CronJob story once `replicas > 1`. Cost: staleness proportional to the
   scan interval.
3. **Outbox drained into an indexer** — the correct destination, and **exactly the path ADR-0004's
   amendment already specifies**: `OutboxMessage` written in the same `SaveChangesAsync` as the
   product change, drained by a `BackgroundService`, transport optional.

So: **search is the most likely first consumer of the outbox seam**, and it is the concrete
requirement that should trigger implementing it. That connects directly to Q5 — the seam is not
speculative, this is what it is for.

### Pros & cons

**Pros.** No architectural change is needed. The module shape, the read-side split and the
ADR-0004 path all already exist. Catalog stays unaware that search exists.

**Cons.** Option 3 means implementing the outbox, including its operational preconditions —
atomic row claim or leader election, because **every replica runs every `BackgroundService`** and
an indexer running three times is wasteful (though, unlike email, idempotent). Meilisearch also
introduces a second source of truth for product data, with all the reconciliation questions that
implies: what happens when the index and the database disagree, and who notices?

### Alternatives considered

- **Meilisearch as a read model inside Catalog** rather than its own module. Reasonable if search
  only ever covers products. Rejected as the default because search typically spans contexts
  (products *and* brands *and* suppliers), and cross-context composition is Admin's job or a
  dedicated module's — not Catalog's.
- **Replace the relational read side entirely.** Rejected: ADR-0008 is explicit that reads are
  never stale today. Routing customer-facing reads through an eventually-consistent index would
  silently break that guarantee.

### Evidence

ADR-0008 (CQS+ terminology and the "no stale reads" guarantee); ADR-0004 amendment (outbox path,
step table, operational preconditions); ADR-0010 D3 (CronJob shape, and why its at-least-once
semantics are safe for idempotent work but not for side effects).

---

## Q11 — Could `Brand` be remodelled as a Variant or Product Option later?

### Rationale

**Technically possible; conceptually it would be a different system.** Worth separating those.

`Brand` today is an **aggregate root with its own lifecycle**: `Slug`, `CountryOfOrigin`,
`BrandStatus` (PendingReview → Approved/Rejected), `RejectionReason`, plus `Approve()`,
`Reject(reason)` and `UpdateDetails(...)`. It is referenced by `BrandSupplyAgreement`, which
Supplier owns (ADR-0009), and it has an approval workflow that outlives any individual product.

A **Variant** or **Product Option** is a different animal: it has no independent lifecycle, no
approval, and no meaning outside its parent product. It would be an owned entity of the `Product`
aggregate — reached only through the root, per DDD-GUIDELINES rule 5.

So the question is really: *is "brand" a thing the business approves and contracts with, or a
label on a product?* Today it is unambiguously the former — the approval workflow and the
supplier agreements are the evidence.

### What the move would actually cost

If the business genuinely reclassified it:

1. **Delete the approval lifecycle**, or move it somewhere else. A `Variant` with a
   `PendingReview` state is a smell.
2. **Resolve `BrandSupplyAgreement`.** Supplier holds agreements keyed by brand id. If Brand
   becomes an owned entity of Product, that key now points *inside* another aggregate — which
   rule 2 ("reference other aggregates by ID") forbids. This is the expensive part, not the
   modelling.
3. **Delete the module**, its DbContext, its migrations, its ACL (`IBrandExistenceChecker`), and
   the `[InlineData("Brand", "Supplier")]` arch row. Migrate the data into Catalog.

### Pros & cons of the current boundary

**Pros.** Brand's independent lifecycle is modelled where it belongs. Catalog stays about stock
and price. The M2M with Supplier has an owner (ADR-0009), so there is no shared-table ambiguity.

**Cons.** A product's brand is a cross-module lookup rather than a field, so any read wanting
"product with brand name" is a composition — Admin's job, or a denormalized column. And if the
business *does* treat brand as a mere label, the current model is over-engineered for it.

### Alternatives considered at the time

- **Brand as a Catalog value object.** Rejected because of the approval workflow: a value object
  cannot have a lifecycle, and PendingReview → Approved is a lifecycle.
- **Brand owned by Supplier.** Rejected: brands outlive supplier relationships and can have
  several suppliers. ADR-0009 gives Supplier the *agreement*, not the brand.

### Honest summary

The architecture makes this **possible but not cheap** — and that is correct. Aggregate boundaries
are meant to be expensive to move, because moving one is a statement about the business, not a
refactor. The module structure at least makes the cost *visible and bounded*: it is one module,
one database, one ACL and one arch row, rather than a search-and-replace through a shared schema.

### Evidence

`src/Modules/Brand/Application/Domain/Brands/Brand.cs`; ADR-0009 (Supplier owns the M2M, Brand
never references Supplier); `docs/DDD-GUIDELINES.md` rules 2 and 5.

---

## Q12 — Why remove the `CancellationToken` parameter from `CompensateAsync` instead of shadowing it?

### Rationale

Because shadowing fixes today's bug; removal fixes the *class* of bug.

The defect: `CompensateAsync` took the saga's `CancellationToken`. On the cancellation path that
token is **already cancelled by definition**, so every compensation touching the database threw
`OperationCanceledException` on its first await, landed in the catch-all, logged
`COMPENSATION FAILED — manual reconciliation required` and incremented the metric **without
compensating anything**. The saga reported that it had tried to undo its work and had not.

Shadowing — keeping the parameter and ignoring it in favour of a local token — leaves an unused
`ct` in scope. The next person to touch that method sees a `CancellationToken` and an `await`, and
does the natural thing. The bug is one plausible edit away, and it fails *silently*: the
compensation still returns, the metric still increments, and the log line is the one you would
expect from a genuine failure.

Removing the parameter makes the mistake **unrepresentable**. Both call sites now pass nothing;
there is no token to misuse.

This is the same reasoning that typed `SagaStep.Compensation` as `Func<…, Task<Result>>` rather
than `Task`: when a failure mode is invisible at runtime, move the guard into the type system.

### Pros & cons

**Pros.** The defect cannot recur by accident. The call sites got simpler. A 30-second budget
bounds the pass, so a hung compensation cannot hold the shutdown open indefinitely.

**Cons.** Compensation is now genuinely **uncancellable** by the caller — a deliberate 30-second
worst case is accepted in exchange for actually undoing the work. The budget is a `const`, not
configuration, so tuning it means a code change. And it is shared across the whole pass rather
than per step, so a saga with many slow compensations could exhaust it on the later ones (they are
then logged as `COMPENSATION TIMED OUT` — a distinct message, because a timeout may mean the work
*partially* applied, which is a different remedy from "never started").

### Alternatives considered

- **Shadow the parameter.** Rejected above.
- **`CancellationToken.None`.** Rejected: unbounded. A compensation that hangs would hold the
  shutdown open until SIGKILL — turning a graceful-shutdown fix into a graceful-shutdown hazard.
  The test asserts `CanBeCanceled == true` specifically to prevent someone "simplifying" it to
  `None`.
- **`CancellationTokenSource.CreateLinkedTokenSource(ct)`.** Rejected — it re-links to the token
  we are escaping, so it would cancel immediately on the shutdown path. It is the obvious-looking
  wrong answer, which is why it is named here.
- **Per-step budgets.** Rejected: the constraint being modelled is the platform's shutdown grace
  period, which is a budget for the *process*, not per unit of work.

### Evidence

`CrossModuleSaga.CompensateAsync` and the `CompensationBudget` field.
Three tests in `tests/UnitTests/CrossModuleSagaTests.cs`:
`A_Cancelled_Saga_Still_Compensates_On_An_Independent_Token`,
`Compensation_Runs_On_An_Independent_Token_On_The_Failure_Path_Too`,
`A_Compensation_That_Fails_After_Cancellation_Is_Still_Metered`.
**Mutation-proven:** restoring the old signature fails the first test.
ADR-0010 D5 (marked resolved).

**What this does *not* prove:** the 30-second budget must fit inside the host's shutdown timeout,
which must fit inside `terminationGracePeriodSeconds`. Those are deployment configuration and no
test asserts them.

---

## Q13 — How does the reaper avoid running three times at `replicas: 3`?

### The honest answer for today

**It does not.** At `replicas: 3` you get three reapers.

`ReservationExpirationService` is a `BackgroundService` registered by
`Catalog.Adapter.Outbound.DependencyInjection`, and **every replica runs every hosted service**.
There is no leader election in the code, and none is planned.

The consequence is bounded but real: three scans and three writes per minute instead of one, plus
a steady trickle of `DbUpdateConcurrencyException` in the logs as concurrent reapers lose on
`rowversion`. It does **not** corrupt data — releasing an expired hold and pruning a
retention-elapsed row are idempotent and commutative, and no invariant spans two products — but it
looks like a fault and is 3× the load.

### Rationale for the chosen solution

ADR-0010 answers it at the **platform** layer rather than in C#:

- **Phase 1 (`replicas: 1`)**: the `BackgroundService` stays, and `strategy: Recreate` is used
  instead of `RollingUpdate` — because a rolling update briefly runs two pods, silently creating
  the multi-instance condition during every deploy.
- **Phase 2 (`replicas > 1`)**: the reaper becomes a **Kubernetes CronJob** running the *same
  image* with `--reap-once` (following the existing `--migrate` precedent), and the in-process
  registration is disabled via `Reaper:Mode`.

Kubernetes already owns scheduling and singleton execution. Reimplementing that in C# means
owning a leader-election implementation and a distributed-lock story for a problem the platform is
built to solve.

### What Kubernetes does and does not guarantee — precisely

`concurrencyPolicy: Forbid` prevents a **new** Job while the previous is running. It is **not**
exactly-once:

- CronJob is **at-least-once** — a controller restart can produce two Jobs for one schedule.
- It can also **miss** a schedule under controller pressure or clock skew.

**This is acceptable for the reaper specifically**, because its operations are idempotent and
commutative and a missed minute only delays reclamation by one tick.

**It is not a general licence, and this matters.** ADR-0004's outbox amendment requires an atomic
row claim or leader election precisely because a drainer that sends email must not run twice.
"Kubernetes handles singleton execution" is true for *idempotent batch work* and false for *side
effects* — do not generalise this answer to the indexer in Q10.

### Pros & cons

**Pros.** No leader-election code to own, no Redis in the critical path, one image for API,
migrations and reaper. The schedule becomes an operational dial rather than a recompile.

**Cons.** The reaper's cadence leaves the repository and lives in cluster config, so a developer
reading the code can no longer see how often it runs. The idempotence of the reaper moves from
"nice property" to **load-bearing requirement**. And the CronJob path is exercised only in the
cluster — local development and the test suite keep the in-process path, so a staging smoke test
is the cheapest cover for that gap.

### Evidence

`Catalog/Adapter/Outbound/DependencyInjection.cs` (`AddHostedService`) — the only one in the
solution. ADR-0010 D1/D3 and its compliance checklist.
`One_Reaper_Cycle_Leaves_Nothing_For_The_Next_One` now protects a production invariant, not just a
performance one.

---

## Q14 — Why run `PersistenceContractTests` on both SQL Server and InMemory? Isn't that redundant?

### Rationale

It is not redundant, because **each provider catches a failure the other structurally cannot.**

| Provider | Configured as | Catches |
|---|---|---|
| **SQL Server** | pointed at a **refused port** | **Translation** failures. Translation happens *before* connection, so an untranslatable query throws a translation error while a translatable one throws a connection error |
| **EF InMemory** | actually executes | **Materialization** failures — a value converter that cannot parse a stored value, which the connection error would mask |

Two real defects, one per provider:

1. `Where(u => u.Email.Value == name)` on a `HasConversion` property is untranslatable on SQL
   Server. Eleven sites used it, **including login**, and every test passed because InMemory
   evaluates untranslatable LINQ client-side. **The system could not log a user in against SQL
   Server while 95 tests were green.**
2. `PaymentStatus.Parse` was missing `"Refunded"`. Because the converter runs on *read*, one
   refunded row became **permanently unreadable** — entity loads and projections both threw,
   poisoning the payment list, the sales summary and the customer detail page. A connection error
   would have hidden this entirely.

The classifier is an **allowlist**: a `DbException` anywhere in the exception chain means "reached
the database", i.e. the shape translated. An earlier version used a message *denylist* and passed
a genuinely broken fix.

### But doesn't LocalDB make this obsolete?

Reasonable question, and the answer is no — they cover different things:

- `PersistenceContractTests` needs **no database**, runs in ~5 seconds, and covers *every* query
  shape through DI. It is the fast feedback loop.
- The LocalDB integration suite exercises *the paths the tests happen to walk*, needs a running
  instance, and takes ~20 seconds.

A query used only by a rarely-exercised endpoint is covered by the first and quite possibly not by
the second.

### Pros & cons

**Pros.** Catches the highest-severity historical defect class in seconds with no infrastructure.
Runs the **real production code paths** — the services are `internal sealed`, so tests reach them
through DI rather than reimplementing their queries, which means the assertion cannot drift from
what ships.

**Cons.** Every new query method needs a line added to the relevant theory, and a forgotten one is
silently uncovered. Passing a `null` for an optional filter makes that filter's assertion
**vacuous** — a trap we hit, and the reason the suite now passes non-null values for every
optional parameter. And the refused-port trick is clever in a way that needs its comment: someone
"fixing" the connection string would silently convert translation tests into connection tests.

### Alternatives considered

- **SQL Server only.** Rejected: it cannot catch materialization failures, because the connection
  error arrives first.
- **InMemory only.** Rejected: this is what the codebase had, and it is how a system that could
  not log a user in stayed green.
- **LocalDB for everything.** Rejected as the *primary* guard: slower, requires infrastructure,
  and covers walked paths rather than all query shapes. It was added **in addition**, not instead
  — it is what proves optimistic concurrency, check-constraint enforcement, the migration backfill
  and deadlock survival, none of which either contract provider can express.

### Evidence

`tests/PersistenceContractTests/` — `QueryTranslationTests`, `SqlServerModuleFixture`.
`docs/TESTING-GUARANTEES.md`, section *"Why PersistenceContractTests runs on two providers"*.

---

## Appendix — the five things a reviewer should check first

1. **`docs/TESTING-GUARANTEES.md`** — what the 265 tests prove and, more usefully, what they do
   not. Read it before trusting a green run.
2. **`tests/ArchitectureTests/HexagonalBoundaryTests.cs`** — the boundary rules are executable.
   The header states that adding a module costs **five** edits, and why missing the
   `ModuleNames` entry is the dangerous one (the module escapes every rule with the build green).
3. **`docs/DDD-GUIDELINES.md` → "Known inconsistencies"** — the tensions we chose to keep, stated
   as facts rather than hidden.
4. **`docs/adr/`** — ten ADRs, several carrying explicit *amendments* where a decision was later
   withdrawn (ADR-0002's "same transaction" claim being the notable one).
5. **`docs/ADDING-CUSTOMER-PAYMENT-MODULES.md`** — the walkthrough, with every code sample cited
   back to its source file.


# Phase 4 — Sales: the partner channel

A partner sends an **amount**. The system composes a real basket of active, priced SKUs totalling
it exactly, writes one order, issues a commercial invoice and renders the PDF on first download.
That path is complete and gated here.

Verified at `1b37444` on `feature/ecommerce-mvp`:

- `dotnet build EwaysVNext.slnx -c Release` — 0 errors, 0 warnings (`TreatWarningsAsErrors` is on
  in Release only, so this is the build that counts).
- `dotnet test EwaysVNext.slnx` — **1927 tests, 0 failures**, Docker up.

## What works

**Composition.** `CompositionEngine` is pure: no ports, no IO, no clock. Its seed is persisted
with the order, so a basket is reproducible from its inputs years later —
`SalesArchitectureTests` fails the build on any dependency that would break that. Arithmetic is
integer-only; the ±5% band is applied per unit price, never to the total. All 25 amounts the PRD
names compose exactly, under property tests as well as examples.

**The order.** One aggregate owns the lifecycle (ADR-0018), so the partner path's "all or
nothing" is met by aggregate design rather than by a transaction — which `EnableRetryOnFailure`
forbids anyway. One save, ordered writes, `xmin` concurrency.

**Idempotency.** A partner's `externalOrderId` is unique per partner. Ten concurrent retries of
the same request yield exactly one order: the winner gets 201, the losers 200 with
`Idempotent-Replay: true`, and the losers do not re-sync contacts. That was a real defect found
by the concurrency test — the first implementation returned 201 to all ten.

**Live re-quote.** A candidate pool is cached in-process with a version stamp; a stale candidate
is re-quoted against live tables before the order is written, never trusted from the cache, and
never read from a storefront view (ADR-0020).

**Bulk.** Each item runs in its own DI scope, so one poisoned change tracker cannot fail its
neighbours. Parallelism is bounded. The deadline bounds the *batch*: anything not started by then
returns `BATCH_DEADLINE_EXCEEDED` (retryable) while running items finish. Always HTTP 200 — the
batch request was understood; each order's fate is in its own outcome.

**Invoices.** Rendered lazily, on first download, and stored. A deliberately broken object store
still lets `POST /orders` return 201 — MinIO is not on the path of a partner order. The renderer
is pure, so the same snapshot renders byte-identical bytes and "is this the invoice we sent?" has
an answer. The PDF route is a 302 to a five-minute presigned link, not a proxy. The phrase "tax
invoice" is forbidden anywhere in the module, in source and in rendered text: the seller is not
GST-registered and in Singapore that phrase is reserved for one who is.

**Storefront popularity.** `popularity_30d` is now units sold in the last 30 days, excluding
cancelled orders, and `mv_home` best sellers is no longer empty — the phase 4 debt ADR-0020
recorded. A left join, not a filter: an unsold product keeps its row at zero.

### Endpoints

| Route | Scope |
|---|---|
| `POST /api/v1/orders` | `partner:orders.create` |
| `POST /api/v1/orders/bulk` | `partner:orders.create` |
| `GET /api/v1/orders`, `GET /api/v1/orders/{id}` | `partner:orders.read` |
| `GET /api/v1/customers/{id}/orders` | `partner:customers.read` |
| `GET /api/v1/invoices/{id}`, `/{id}/pdf`, `POST /api/v1/invoices/search` | `partner:invoices.read` |

A partner's own id is never bindable; it arrives through `PartnerPrincipal` alone, and
`PartnerTenancyArchitectureTests` fails the build if any request shape carries a `PartnerId`.

PRD acceptance 1–7 and 14 pass by name in `tests/IntegrationTests/Sales/`.

## What is incomplete

- **The website channel is phase 5** — cart, checkout, pro forma, admin payment confirmation,
  refunds. Nothing here serves a website customer.
- **The composition coverage simulator and the k6 load script were dropped** from this phase by
  decision, not oversight. Both produce reports rather than product. Load testing belongs in
  phase 8 against a deployed stack; the engine's behaviour is already covered by property tests
  over the mandated amounts.
- **No GraphQL for the partner channel.** Partners use REST; GraphQL is the admin and storefront
  surface.

## Risks

- **QuestPDF Community licence** caps the revenue of the deploying business. A commercial licence
  is required past that threshold — a business decision, not a technical one.
- **The candidate pool cache and the rate limiter are in-process** (ADR-0010). With replicas > 1
  each instance keeps its own, so a partner's effective rate limit multiplies by the replica count
  and cache warmth is per instance. Correctness is unaffected — the pool is re-quoted live before
  any write — but the limit is not a global one.
- **A new customer persists if the order save fails** (spec §10.3). Cross-module writes are not
  atomic by design; the orphan is harmless and the next order with the same external customer id
  reuses it.
- **Popularity is a display ranking.** Its window is `now()`-relative and the views are stale by
  construction. It must never be read as an accounting figure.

## Reproduction

```bash
docker compose -f build/compose/docker-compose.dev.yml up -d
dotnet build EwaysVNext.slnx -c Release
dotnet run --project src/Host/EwaysVNext.Host -- --migrate
dotnet test EwaysVNext.slnx
dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "*.StorefrontPopularityTests"
```

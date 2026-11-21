# MKBJ — API-first B2B e-commerce MVP

A .NET 10 modular monolith built Hexagonal-First (Ports & Adapters), with machine-enforced module
boundaries, synchronous in-process domain events, and no message broker.

The product: a Singapore seller with two sales paths over one catalogue.

1. **Partner API.** A partner sends an amount, a currency and a customer reference. The system
   composes a basket of real, active SKUs that totals that amount **exactly** — each unit price
   moved at most ±5% from its reference — records the order as partner-reported PAID and
   system-automatic FULFILLED, and issues a commercial invoice, synchronously.
2. **Website.** A customer buys at displayed prices, gets a pro forma invoice and one payment
   instruction, pays outside the system, and an admin confirms receipt before the commercial
   invoice is issued.

Specified in [`docs/product-scope/ecommerce_mvp_prd.md`](docs/product-scope/ecommerce_mvp_prd.md),
designed in [`docs/superpowers/specs/2026-10-28-ecommerce-mvp-design.md`](docs/superpowers/specs/2026-10-28-ecommerce-mvp-design.md),
built against [`docs/superpowers/plans/2026-10-28-ecommerce-mvp.md`](docs/superpowers/plans/2026-10-28-ecommerce-mvp.md).

> **Status: implementation and validation in progress.** The modules below are implemented.
> To test amount-to-order-to-invoice creation in Scalar and inspect submissions in Backoffice,
> follow [Partner invoice testing](docs/PARTNER-INVOICE-TESTING.md).

## Stack

| Area | Choice |
|---|---|
| Runtime | .NET 10 |
| Persistence | EF Core 10 on **PostgreSQL** — one shared DbContext, schema per module, `xmin` concurrency ([ADR-0017](docs/adr/0017-postgres-and-minio.md)) |
| Money | `amount_minor bigint` + `currency char(3)`; integer arithmetic only |
| Object storage | MinIO behind an `IObjectStorage` port |
| Background work | Postgres job queue, `FOR UPDATE SKIP LOCKED` ([ADR-0019](docs/adr/0019-postgres-backed-job-queue.md)) |
| Auth | Keycloak (Shop + Backoffice realms) for humans; hashed API keys for partners ([ADR-0021](docs/adr/0021-partner-api-key-authentication.md)) |
| APIs | REST + HotChocolate 16 GraphQL on one `/graphql` endpoint |
| Tests | **xUnit v3** on Microsoft.Testing.Platform — architecture · unit · persistence-contract · integration |
| Deployment | One chiselled non-root image, four roles; compose stacks in `build/` |

## Modules

| Module | Status | Owns |
|---|---|---|
| **Catalog** | built | Categories, attribute blueprints, brands, products, variants, media |
| **Customer** | built | Website accounts and partner-linked identities ([ADR-0022](docs/adr/0022-remove-iran-specific-modules.md)) |
| Partners | built | B2B partners, API keys, scopes, usage log |
| Settings | built | Seller identity, payment instruction, commerce limits |
| Pricing | built | Reference prices, currency overrides, FX rates |
| Sales | built | Carts, orders, composition, invoices, refunds, partner request history |
| Admin | built | Cross-module composition and admin APIs |
| Sourcing | built | Import jobs, staging, crawling, publishing |

`HexagonalBoundaryTests.ModuleNames` is the authoritative list; a module missing from it escapes
every boundary rule with the build still green.

## Architecture in one page

- **Hexagon per module**: `Application` (domain + ports + services) knows nothing of adapters or
  EF Core. `Adapter/Inbound` drives it (REST, GraphQL); `Adapter/Outbound` serves it (repositories,
  ACL gateways). `QueryApplication` is the read side, projecting straight from the shared context.
- **Cross-module calls are not transactional.** Each operation does its own `SaveChangesAsync`, so a
  later step's failure does not undo an earlier module's committed write. Where the product
  genuinely requires atomicity — the partner order — it is achieved by putting the parts in **one
  aggregate** rather than a transaction or a saga ([ADR-0018](docs/adr/0018-sales-aggregate-owns-order-lifecycle.md)).
- **No transactions anywhere.** Retries are enabled, and an EF execution strategy throws on a
  transaction it did not begin. A source scan fails the build on `BeginTransaction`.
- **Storefront browsing reads materialized views**; nothing that decides a sale does
  ([ADR-0020](docs/adr/0020-storefront-materialized-views.md)).
- **The audit log is append-only by grant**, not by convention, and hash-chained so tampering by
  anyone with database access is detectable.

## Run it

```bash
# Dependencies: Postgres, MinIO, Keycloak. The API runs from your IDE against these.
docker compose -f build/compose/docker-compose.dev.yml up -d

dotnet run --project src/Host/Mkbj.Host -- --migrate   # the host never migrates on startup
dotnet run --project src/Host/Mkbj.Host -- --seed      # refuses in Production
dotnet run --project src/Host/Mkbj.Host
```

One image, four roles — no arguments serves the API, plus `--worker`, `--migrate`, `--seed` and
`--probe <url>`.

```bash
# Production stack. Every secret is ${VAR:?}: a missing one fails the stack rather than defaulting.
cp build/compose/.env.example build/compose/.env    # then fill it in
docker compose -f build/compose/docker-compose.yml up -d
```

## Tests

```bash
dotnet test Mkbj.slnx
```

**IntegrationTests requires Docker**; the other three suites need nothing. It runs the real host
against a Postgres container built by the same roles script production uses, connecting as the
application role that owns nothing — so anything the production grants forbid fails in the suite
too.

Read [`docs/TESTING-GUARANTEES.md`](docs/TESTING-GUARANTEES.md) before trusting a green run. It
lists what each suite cannot see, including the parts that are written but not yet verified.

## Free demo deployment

The complete API, worker, PostgreSQL, Keycloak, MinIO, Backoffice and shop can be deployed to one
OCI Always Free ARM VM through GitHub Actions. See
[`docs/DEPLOYMENT-DEMO.md`](docs/DEPLOYMENT-DEMO.md) for the first-run seed, stable demo identities,
required repository secrets and continuous-deployment workflow.

## Documentation

- [`AGENTS.md`](AGENTS.md) — the architectural rules and what breaks if you ignore them
- [`docs/ONBOARDING.md`](docs/ONBOARDING.md) — eagle-eye view and run instructions
- [`docs/DDD-GUIDELINES.md`](docs/DDD-GUIDELINES.md) — DDD practice here, including the
  inconsistencies kept on purpose
- [`docs/TESTING-GUARANTEES.md`](docs/TESTING-GUARANTEES.md) — what the tests prove, and what they
  do not
- [`docs/adr/`](docs/adr/) — decision records; **0017–0022** cover this MVP

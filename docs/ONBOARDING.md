# Onboarding — EwaysVNext

What this is, how to run it, and where things live.

## What this is

An **API-first B2B e-commerce MVP** for a Singapore seller, on a .NET 10 modular monolith built
Hexagonal-First (Ports & Adapters). Two sales paths over one catalogue:

1. **Partner API.** A partner sends an amount, a currency and a customer reference. The system
   composes a basket of real, active SKUs totalling that amount **exactly**, adjusting each unit
   price by at most ±5% of its reference price, then records the order as partner-reported PAID and
   system-automatic FULFILLED and issues a commercial invoice — all in one request.
2. **Website.** A customer browses and buys at displayed prices, receives a pro forma invoice and
   one payment instruction, pays outside the system, and an admin confirms receipt before the
   commercial invoice is issued.

The PRD is [`product-scope/ecommerce_mvp_prd.md`](product-scope/ecommerce_mvp_prd.md); the approved
design is [`superpowers/specs/2026-10-28-ecommerce-mvp-design.md`](superpowers/specs/2026-10-28-ecommerce-mvp-design.md).

**The most important thing to understand before reading any code:** cross-module calls are **not
transactional**. Every module shares one `EwaysVNextDbContext`, but each operation still does its
own `SaveChangesAsync` (AGENTS.md rule 7), so a later step's failure does not roll back an earlier
module's committed write. Where the product genuinely requires atomicity — the partner order — it
is achieved by putting the parts in **one aggregate**, not by a transaction or a saga
([ADR-0018](adr/0018-sales-aggregate-owns-order-lifecycle.md)).

## Modules

Built today: **Catalog** and **Customer**. Customer is a compile-only shell; its Iran-market domain
was removed ([ADR-0022](adr/0022-remove-iran-specific-modules.md)) and phase 1 rebuilds it around
website accounts and partner-linked identities.

Planned, in build order: Partners and Settings (phase 1), Pricing (2), Sales (4–5), Admin (6),
Sourcing (7). `HexagonalBoundaryTests.ModuleNames` is the authoritative list.

## Tech stack

| Area | Choice |
|---|---|
| Architecture | Hexagonal-First + read-optimized CQS ([ADR-0008](adr/0008-read-optimized-cqs-terminology.md), [ADR-0016](adr/0016-queryapplication-is-the-read-side-implementation.md)) |
| Persistence | EF Core 10 on **PostgreSQL**, one shared DbContext, schema per module ([ADR-0017](adr/0017-postgres-and-minio.md)) |
| Concurrency | Postgres `xmin` as the token on aggregate roots |
| Money | `amount_minor bigint` + `currency char(3)`; integer arithmetic only |
| Object storage | MinIO behind `IObjectStorage` — product images, imports, invoice PDFs |
| Background work | Postgres job queue, `FOR UPDATE SKIP LOCKED` ([ADR-0019](adr/0019-postgres-backed-job-queue.md)) |
| Cross-module | Direct ACL calls through gateway ports — **not** transactional |
| Domain events | Synchronous, in-process, no persistence |
| Auth | Keycloak for humans (Shop and Backoffice realms); hashed API keys for partners ([ADR-0021](adr/0021-partner-api-key-authentication.md)) |
| APIs | REST + HotChocolate GraphQL on one `/graphql` endpoint |
| Docs UI | Scalar · OpenAPI (dev only) |

## Run it

```bash
# 1. Dependencies: Postgres, MinIO, Keycloak. The API runs from your IDE against these.
docker compose -f build/compose/docker-compose.dev.yml up -d

# 2. Create the schema. The host NEVER migrates on startup.
dotnet run --project src/Host/EwaysVNext.Host -- --migrate

# 3. Demo data (refuses to run when ASPNETCORE_ENVIRONMENT=Production)
dotnet run --project src/Host/EwaysVNext.Host -- --seed

# 4. Serve
dotnet run --project src/Host/EwaysVNext.Host
```

With no connection string configured in Development the host falls back to EF InMemory, so it
starts without a database — useful for a quick look, useless for anything touching persistence
behaviour.

The same image also runs as a worker (`--worker`) or a health probe (`--probe <url>`). Production
compose uses all four modes from one artefact:

```bash
cp build/compose/.env.example build/compose/.env    # then fill it in
docker compose -f build/compose/docker-compose.yml up -d
```

Every secret in that stack is `${VAR:?}` — a missing one fails the stack rather than defaulting.

## Where things live

| Path | What |
|---|---|
| `src/BuildingBlocks/` | Domain primitives, Result/Error, persistence, storage, jobs, audit, endpoint plumbing |
| `src/Authorization/` | RBAC: permissions, roles, scopes (its own DbContext) |
| `src/Database/` | The one `EwaysVNextDbContext`, every module's EF configuration, migrations, repeatable scripts |
| `src/Modules/{Module}/` | Application · Adapter/Inbound · Adapter/Outbound · QueryApplication · Installer |
| `src/Host/` | Entry point, host commands, the request pipeline |
| `build/` | `docker/` image, `compose/` stacks, `keycloak/` realms, `postgres/` roles |
| `docs/adr/` | Decision records — 0017–0022 are this MVP |
| `tests/` | Architecture · Unit · PersistenceContract · Integration |

## Tests

```bash
dotnet test EwaysVNext.slnx
```

Four suites; **IntegrationTests needs Docker**, the rest need nothing. Read
[`TESTING-GUARANTEES.md`](TESTING-GUARANTEES.md) before trusting a green run — it says what each
suite cannot see.

## Before a release

Two things `dotnet test` does not do.

```bash
# The production stack, end to end. Builds the real image with the real Dockerfile, migrates with
# the real roles script, runs 14 checks, tears it all down from a trap even on failure.
bash tools/smoke/smoke.sh
```

```bash
# Composition coverage: how often the engine can hit an exact amount, across the whole range.
# Pure — no database, no host, no container, and the same seed gives the same report anywhere.
dotnet run --project tools/CompositionSimulator/EwaysVNext.Tools.CompositionSimulator.csproj -c Release
```

Then read [`RUNBOOK.md`](RUNBOOK.md) and [`SECURITY-CHECKLIST.md`](SECURITY-CHECKLIST.md).

## Poking at the API by hand

[`api/bruno/`](api/bruno/) is a Bruno collection covering the partner, shop and admin paths. It
holds no credentials — every one is a `{{var}}` you supply:

```bash
npx @usebruno/cli run docs/api/bruno/shop --env local
```

Two things that cost an hour each if you guess:

- **The partner endpoints take snake_case on the wire** (`external_order_id`), while the shop and
  admin endpoints take camelCase. The partner contract follows PRD §9; the rest follow the DTOs.
- **Partner scopes use colons** — `orders:create`, not `orders.create`. The authorization policy
  name is `partner:orders:create`.

## Reading order

1. This file.
2. [`AGENTS.md`](../AGENTS.md) — the rules, and what breaks if you ignore them.
3. [`superpowers/specs/2026-10-28-ecommerce-mvp-design.md`](superpowers/specs/2026-10-28-ecommerce-mvp-design.md) — the design, including every decision made on the product owner's behalf.
4. [`tests/ArchitectureTests/HexagonalBoundaryTests.cs`](../tests/ArchitectureTests/HexagonalBoundaryTests.cs) — the rules as code.
5. [`DDD-GUIDELINES.md`](DDD-GUIDELINES.md) and the ADRs for anything that surprises you.

# Onboarding — EwaysVNext v3

Eagle-eye view of the system: what it is, how it runs, and where everything lives.

## What this is

A .NET 10 **modular monolith** built Hexagonal-First (Ports & Adapters) with eight
bounded-context modules, each owning its own DbContext and following the same
five-project layout. There is no message bus — modules cooperate through direct method
calls over ACL gateway ports.

**The single most important thing to understand before reading any code:** those calls are
**not transactional**. Each module has its own DbContext, connection and schema, so a
cross-module operation cannot roll back atomically. Multi-step cross-module work runs inside a
`CrossModuleSaga` with an explicit compensation per step, and consistency between modules is
*eventual*, reached by compensation with a TTL reaper as the backstop. Within a single module,
one `SaveChangesAsync` is atomic.

Earlier versions of this document, the README, AGENTS.md and ADR-0002 all called these calls
"transactional". They never were. See [ADR-0002](adr/0002-transactional-inter-module-communication.md)
for the withdrawal and [ADR-0005](adr/0005-cross-module-consistency-saga-toolkit.md) for the
real model.

## The eight modules

| Module | Owns |
|--------|------|
| Identity | Users, credentials, JWT issue/refresh, lockout |
| Catalog | Products, stock, reservations (TTL reaper included) |
| Orders | Order lifecycle; reserves stock through Catalog's ACL |
| Customer | Profiles, addresses, saved payment methods (tokens only) |
| Payment | Payment lifecycle: initiate → capture / fail / refund |
| Supplier | Supplier verification + owns BrandSupplyAgreement (M2M with Brand) |
| Brand | Brand identity + approval lifecycle |
| Category | Independent merchandising taxonomy and curated product promotion; category-to-product assignments by product ID |
| Admin | Thin composition layer + admin GraphQL panel |

## Tech stack

| Area | Choice |
|------|--------|
| Architecture | Hexagonal-First + Read-Optimized CQS (CQS+, ADR-0008) |
| Cross-module | Direct ACL calls inside a `CrossModuleSaga` with compensation — **not** transactional |
| Domain events | Pluggable `IEventDispatcher` — currently NoOp (ADR-0004) |
| Persistence | EF Core 10, one DbContext per module, SQL Server (InMemory in dev) |
| Admin API | HotChocolate 16 GraphQL (`/api/v1/admin/graphql`) |
| Auth | JWT Bearer + refresh + lockout · Admin policy on all admin surfaces |
| Docs | Scalar UI · OpenAPI |

## Run it

```bash
dotnet build EwaysVNext.slnx
export JWT__SIGNINGKEY=$(openssl rand -base64 48)
dotnet run --project src/Host/EwaysVNext.Host      # dev: InMemory DBs
# explicit schema migration when using SQL Server:
dotnet run --project src/Host/EwaysVNext.Host -- --migrate
```

## Tests

```bash
dotnet test tests/ArchitectureTests   # 17 boundary rules — run before every push
dotnet test tests/UnitTests           # domain + saga toolkit
dotnet test tests/IntegrationTests    # end-to-end via WebApplicationFactory
```

## The five architecture rules that matter most

1. **Application vs Adapters** — the hexagon never references adapters or EF Core;
   ports in, implementations out.
2. **No Wolverine, no events across modules** — cross-module = ACL ports + Contracts.
   Domain events dispatch through a pluggable seam (NoOp today).
3. **QueryApplication for every read** — flat projections into Contracts; never load
   aggregates for display.
4. **Adapters stay thin** — REST/GraphQL resolvers are shells over inbound ports;
   gateways are pure delegation.
5. **Module Installer is the composition root** — `Add{Module}Module()` wires
   Application → Outbound → Inbound → QueryApplication, nothing else.

## Category boundary and API

`Category` owns a separate merchandising taxonomy. It is not an extension of
the primary Catalog category hierarchy: its Application layer has no Catalog dependency,
and the only cross-module call is the outbound `CatalogProductLookup` ACL, which asks
Catalog whether a product exists and is active. The assignment entity stores only the
`ProductId` plus assignment metadata, so there is no primary-category relationship or
aggregate assignment collection.

Its `CategoryDbContext` owns the `secondary_catalog` schema:

| Table | Columns and constraints |
|------|--------------------------|
| `Categories` | `Id` (`bigint` identity), nullable self-parent `ParentId`, required `Name`/`Slug` (200 chars), nullable `Description` (1000 chars), `DisplayOrder`, `IsActive`, `IsDeleted`, `DeletedAt`, `CreatedAt`, `UpdatedAt`, `RowVersion`; restricted self-FK; unique non-deleted slug and hierarchy indexes |
| `CategoryProducts` | Composite PK (`CategoryId`, `ProductId`), `AssignedAt`, nullable `AssignedByUserId`; restricted FK to `Categories`; product/category lookup index. `ProductId` is a Catalog identifier, not a database FK. |

Public REST endpoints are:

| Method and path | Contract |
|----------------|----------|
| `GET /api/v1/categories/tree` | Active, effectively visible category tree |
| `GET /api/v1/categories/{id}` | Active category detail; not-found returns problem details |
| `GET /api/v1/categories/{id}/products?page=&pageSize=` | Paged assigned product IDs; page size 1–100 |
| `GET /api/v1/products/{productId}/categories` | Active categories for a product |

Back-office GraphQL is mounted at `/api/v1/admin/secondary-catalog/graphql`, protected by
the `Admin` policy and `admin` rate limiter. It provides category search/detail/children and
product-ID queries, plus create, update, move, activate, deactivate, soft-delete, assign,
and unassign mutations. Public projections omit inactive/deleted categories and descendants
of inactive/deleted ancestors; admin projections include all statuses.

The published DTOs remain under `src/BuildingBlocks/Contracts/Category` for now.
Migration to a module-owned Contracts project is intentionally deferred until the planned
solution-wide Contracts refactor reaches `main`; this boundary should not be changed as part
of the Category slice.

## Where to go next

- [`AGENTS.md`](../AGENTS.md) — conventions, pitfalls, commands
- [`DDD-GUIDELINES.md`](DDD-GUIDELINES.md) — the twelve DDD rules
- [`adr/`](adr/) — why things are the way they are (10 ADRs)

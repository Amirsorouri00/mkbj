# AGENTS.md — EwaysVNext

## Project Overview

An **API-first B2B e-commerce MVP** for a Singapore seller, built on a .NET 10 modular monolith
with **Hexagonal-First** architecture (Ports & Adapters). The product is specified by
[`docs/product-scope/ecommerce_mvp_prd.md`](docs/product-scope/ecommerce_mvp_prd.md) and designed
in [`docs/superpowers/specs/2026-10-28-ecommerce-mvp-design.md`](docs/superpowers/specs/2026-10-28-ecommerce-mvp-design.md).
It is being built phase by phase against
[`docs/superpowers/plans/2026-10-28-ecommerce-mvp.md`](docs/superpowers/plans/2026-10-28-ecommerce-mvp.md).

Two sales paths: a partner submits an **amount** and the system composes a real basket of active
SKUs totalling it exactly (±5% per unit price), paid-by-partner and invoiced immediately; and a
website customer buys at displayed prices, gets a pro forma, pays externally, and an admin confirms
receipt before the commercial invoice is issued.

- **PostgreSQL** (Npgsql, snake_case, `xmin` concurrency, minor-unit money) — [ADR-0017](docs/adr/0017-postgres-and-minio.md)
- **MinIO** behind an `IObjectStorage` port for images, imports and invoice PDFs
- **No message broker.** Background work is a Postgres job queue claimed with `FOR UPDATE SKIP
  LOCKED` — [ADR-0019](docs/adr/0019-postgres-backed-job-queue.md)
- **Domain events dispatch synchronously, in-process, with no persistence.** `EfCoreUnitOfWork`
  captures each tracked aggregate's pending events before the save and publishes them through
  `IDomainEventBus` once it succeeds. A handler failure after a successful save is lost, not
  retried.
- **CQS, not CQRS.** `Application/` (write hexagon) + `QueryApplication/` (read side, projecting
  directly from the shared `DbContext` — [ADR-0016](docs/adr/0016-queryapplication-is-the-read-side-implementation.md)).
  No command/query bus; application services are called directly.

### Modules

Present today: **Catalog**, **Customer**, **Partners**, **Settings**, **Pricing**, **Sales**,
**Admin**.
Partners owns B2B partners, peppered API keys, the `PartnerApiKey` authentication scheme, the
usage log and the admin surface. Settings owns the seller profile, the versioned payment
instruction and the commerce limits, three singletons seeded by migration. Customer was rebuilt
around website and partner-linked buyers with contact-change history
([ADR-0022](docs/adr/0022-remove-iran-specific-modules.md) removed the Iran-market domain it
replaced). Catalog owns brands, categories, attribute blueprints, products with their variants,
identifiers and media, the publishing and sellable-variant ports, and the storefront read side —
four materialized views refreshed by a job, behind `IStorefrontQueries`. Pricing owns currencies,
append-only FX rates and their overrides, reference prices and per-currency price overrides, the
±5% price band and the quote port; it is the only module that may read Catalog and Settings —
through its `Adapter.Outbound`, never its Application.

**The two answer different questions and must stay apart.** Catalog says what is *buyable* and
nothing about price; Pricing says what something *costs* and nothing about availability. A
variant with no usable price is omitted from a quote, never reported as an error, so "published"
never implies "priced".

Sales owns carts, orders, the amount-driven composition engine, invoices and refunds, on both
channels. Admin owns nothing: it is a data-less composition module (ADR-0006) that reads other
modules' **QueryApplication** only — Sales and Customer today, Sourcing in phase 7 — and arranges
what they return into the dashboard, customer 360, order 360 and audit verification.
`AdminArchitectureTests` fails the build if it acquires a table, a `DbContext` or a read side of
its own. It is the only module that reads four others — Catalog for what is sellable, Pricing for
what it costs, Customer for who is billed and Settings for the seller block — all through its
`Adapter.Outbound`, and **none of those calls shares a transaction with the order it writes**.

**The composition engine is pure.** No ports, no IO, no clock: its seed is persisted with the
order, so a basket has to be reproducible from its inputs years later.
`SalesArchitectureTests` fails the build on a dependency that would break that, and on the phrase
"tax invoice" anywhere in the module — the seller is not GST-registered, and in Singapore that
phrase is reserved for one who is.

Planned (not yet present; do not reference them):

| Module | Phase | Owns |
|---|---|---|
| Sourcing | 7 | Import jobs, staging, crawling, publishing |

**A shop request may name one amount, and only one**: `expectedTotal`, the figure the customer
was shown. It is compared against a live quote and can cause a refusal, never a price —
`Shop_Request_Dtos_Accept_No_Money_Except_ExpectedTotal` fails the build on any other
money-shaped field, because one would let the browser set what it pays.

`HexagonalBoundaryTests.ModuleNames` is the authoritative list. A module missing from it escapes
every boundary rule with the build still green, so adding the name is part of adding the module.

## Structure (src/)

```
BuildingBlocks/
├── Domain/                      ← Entity<TId> (RowVersion is uint → xmin), ValueObject,
│                                   AggregateRoot<TId>, DomainEvent, Guard, IClock, IIdGenerator
│                                   (Snowflake), Money, Email
├── Outcomes/                    ← Result/Result<T>/Error — the ONE outcome type, framework-free.
│                                   Error carries Retryable (spec §6.3).
├── Persistence/                 ← EfCoreRepository, EfCoreUnitOfWork, IUnitOfWork
│                                   (TrySaveChangesAsync → SaveOutcome), MinorUnits,
│                                   TransientFailure, audit/soft-delete interceptors
├── Storage/{Application,Implementation}/  ← IObjectStorage + S3/MinIO adapter, three buckets
├── Jobs/{Application,Implementation}/     ← IJobQueue, IJobHandler<T>, JobWorker, the
│                                   SKIP LOCKED store, Jobs:Mode
├── Audit/{Application,Implementation}/    ← IAuditTrail (recorded in the caller's save),
│                                   hash-chain seals, IAuditVerifier
├── Queries/{Application,Implementation}/  ← pagination shapes and Gridify helpers
├── Events/{Application,Implementation}/   ← IDomainEvent/IDomainEventBus/IDomainEventHandler
├── Core/{Application,Implementation}/     ← IUserProfileService, AdminActor, PartnerPrincipal,
│                                   PartnerScopes, ICorrelationContext, IDataSeeder
├── Presentation.Endpoint/       ← IEndpoint discovery, EndpointResult (the single error body),
│                                   CorrelationIdMiddleware, GraphQL bootstrap
└── DependencyInjection/         ← AddEvents, AddUnitOfWork, AddPersistenceInterceptors,
                                    AddUserProfileService

Authorization/                   ← RBAC subsystem (permissions, roles, scopes), its own DbContext
Database/                        ← The ONE EwaysVNextDbContext, every module's EF configurations,
                                    DatabaseMigrator, Migrations/, repeatable Scripts/*.sql
Host/EwaysVNext.Host/            ← Entry point: module wiring, host commands, the pipeline
Modules/{Catalog,Customer,Partners,Settings,Pricing,Sales}/
├── Application/                 ← Hexagon: Domain + Ports + Service
├── Adapter/Inbound/{Rest,GraphQL}/
├── Adapter/Outbound/            ← Repositories and ACL gateways
├── QueryApplication/            ← Read side
└── Installer/                   ← AddXModule(...)

build/                           ← Deployment: docker/api.Dockerfile, compose/, keycloak/, postgres/
```

**DI composition**: each layer registers its own services; each module's `Installer/
ModuleInstaller.cs` rolls them into one `AddXModule(...)`. `Program.cs` calls the module installers,
then `AddUnifiedDatabase`, `AddObjectStorage`, `AddJobs` and `AddAudit`. No module registers a
`DbContext`.

## Key Architectural Rules (enforced by ArchitectureTests)

1. **Application → Adapter**: Application must NOT depend on any Adapter.
2. **Application → EF Core**: Application must NOT depend on `Microsoft.EntityFrameworkCore`
   (`Application_Depends_On_No_Adapter_And_No_EfCore`).
3. **Module isolation**: no module's Application references another module's. The only sanctioned
   bridges are `{Consumer}.Adapter.Outbound → {Provider}.Application` rows in
   `HexagonalBoundaryTests.SanctionedReferences` — that theory **is** the exception list; do not
   duplicate it here. Admin's composition layer may additionally read any module's
   `QueryApplication` (ADR-0006/0007).
4. **No Wolverine**: any reference fails the build.
5. **Inbound/Outbound ports** are interfaces; **Services** are `sealed`.
6. **QueryApplication** is read-only — no writes, no aggregate loading, no domain rules.
7. **One `SaveChangesAsync` per operation.** One save must never span two aggregates whose
   invariants **depend on each other**. A batch of independent, idempotent, commutative rows is
   fine. Cross-module consistency is NOT atomic: a later step's failure does not roll back an
   earlier module's committed write. If an operation seems to need a multi-aggregate transaction,
   the aggregate boundaries are probably wrong — see [ADR-0018](docs/adr/0018-sales-aggregate-owns-order-lifecycle.md),
   where the partner order's "atomic" requirement was met by aggregate design rather than a
   transaction.
8. **Retry is enabled, so never start a transaction.** `EnableRetryOnFailure` is on, and an EF
   execution strategy throws on a user-initiated transaction. Every write path is
   load → mutate → one save. `No_User_Initiated_Transactions_In_Src` scans `src/` for
   `BeginTransaction`/`UseTransaction` and fails the build. If you genuinely need an intermediate
   statement, wrap it in `Database.CreateExecutionStrategy().ExecuteAsync(...)`.
9. **A `Result`-returning method must not throw for a refusal it can express.** Return the `Error`.
10. **Primary keys are `long` from `IIdGenerator` (Snowflake), never `Guid.NewGuid()`.** The id is
    generated in the service and passed into the aggregate's factory. Every root's configuration
    calls `.Property(x => x.Id).ValueGeneratedNever()`, since EF's default for a numeric key is
    `ValueGeneratedOnAdd` and would silently discard the client-generated value. External
    references this codebase does not own (a Keycloak `sub`) stay `Guid`.
11. **Money is `amount_minor bigint` + `currency char(3)`**, converted at the persistence edge by
    `MinorUnits`. Composition and price-band arithmetic are integer-only — never floating point.
12. **Every table lives in a named schema**, derived from the aggregate's CLR namespace. An
    unrecognised namespace throws rather than defaulting.

## Developer Commands

```bash
# Dependencies (Postgres, MinIO, Keycloak). The API runs from your IDE against these.
docker compose -f build/compose/docker-compose.dev.yml up -d

dotnet restore EwaysVNext.slnx
dotnet build EwaysVNext.slnx
dotnet run --project src/Host/EwaysVNext.Host

# Apply migrations — the host NEVER migrates on startup
dotnet run --project src/Host/EwaysVNext.Host -- --migrate

# Other host modes (one image, four roles)
dotnet run --project src/Host/EwaysVNext.Host -- --worker          # background worker only
dotnet run --project src/Host/EwaysVNext.Host -- --seed            # refuses in Production
#   Runs every registered IDataSeeder in Order, the demo catalogue (160 products /
#   400 variants, deterministic) last. A seeder reports failure by throwing; --seed
#   turns that into exit code 2.
dotnet run --project src/Host/EwaysVNext.Host -- --probe <url>     # 0 healthy, 1 not, 64 usage

# One DbContext for the whole app, so this is the only migration command
dotnet ef migrations add {Name} -p src/Database -s src/Host/EwaysVNext.Host

# Tests. xUnit v3 on Microsoft.Testing.Platform; global.json's
# "test": { "runner": "Microsoft.Testing.Platform" } is what makes `dotnet test` work here.
dotnet test EwaysVNext.slnx
dotnet test --project tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj
dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj
dotnet test --project tests/PersistenceContractTests/EwaysVNext.PersistenceContractTests.csproj
dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj   # needs Docker

# Filters (Microsoft.Testing.Platform, not --filter)
dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "*.MinorUnitsTests"
dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-method "*Rejects_Overflow"

# Production image and stack
docker build -f build/docker/api.Dockerfile -t eways-api:local .
docker compose -f build/compose/docker-compose.yml --env-file build/compose/.env config --quiet
```

## Environment Variables

The full set is [`build/compose/.env.example`](build/compose/.env.example), which ships empty on
purpose. Compose uses `${VAR:?}` for every secret, so a missing one fails the stack rather than
defaulting.

| Variable | Required | Notes |
|---|---|---|
| `ConnectionStrings__Default` | Yes | Postgres. Missing outside Development throws; in Development it falls back to EF InMemory. |
| `APP_DB_PASSWORD` / `MIGRATOR_DB_PASSWORD` | Yes (compose) | `eways_app` writes rows and owns nothing; `eways_migrator` owns the schema. |
| `Minio__Endpoint` / `Minio__PublicEndpoint` | Yes | Internal address, and the browser-facing one presigned URLs are signed for. |
| `Minio__AccessKey` / `Minio__SecretKey` | Yes | The application's own MinIO user, never the root account. |
| `Jobs__Mode` | No | `InProcess` (default), `WorkerOnly`, `Disabled`. Production runs the API Disabled and one worker. |
| `ApiKeys__Pepper` | Yes | Mixed into every partner key hash. Rotating it invalidates every issued key. |
| `Fx__MaxRateAge` | No | Default 72 h. How stale an FX rate may be before pricing refuses it. Read by Settings and Pricing from the one key, never stored. |
| `Keycloak__InternalUrl` / `Keycloak__ExternalUrl` | Yes | Container address and public address. |
| `Brave__ApiKey` / `Anthropic__ApiKey` | No | Catalog discovery and enrichment; each feature disables itself when absent. |
| `RateLimiting__AnonymousPermitPerMinute` | No | Default 100, per IP, for callers with no partner key. A partner is limited by its own `rate_limit_per_minute` instead, read from the claim the key handler mints. In-process. |

## Authentication, authorization and endpoints

Humans authenticate through Keycloak (realms `Shop` and `Backoffice`); partners present an API
key. A key arrives as `Authorization: Bearer pk_…` and is routed to the `PartnerApiKey` scheme by
`AuthSchemes.IsPartnerKey` **before** any JWT code sees it — one implementation shared by the
production router and the test fixture's.

| Policy | Means |
|---|---|
| `admin:staff` | Backoffice realm, role `staff` or `super_admin` |
| `admin:super` | Backoffice realm, role `super_admin` |
| `shop:customer` | Shop realm |
| `partner:{scope}` | A live API key granting that scope; authenticates with `PartnerApiKey` only |

**Admin surfaces are gated twice**: the group takes `.RequireAuthorization(AuthPolicies.AdminStaff
| AdminSuper)` and each endpoint `.RequirePermission(AdminPermissions.X)`. Permission rows are
editable data; the realm policy is not, so it bounds what a misedited row can reach. GraphQL uses
`[Authorize(Policy = …)]` then `[RequirePermission(…)]`, **in that order**, on **each resolver** —
never on the class. A type-level attribute on an `[ExtendObjectType]` is merged onto the whole
`Query`/`Mutation` and would gate every other module's fields; `GraphQLGateScopeTests` fails the
build on both halves of that rule.

Backoffice authority comes from Keycloak realm roles, forwarded to the permission check by
`BackofficeRoles.From(user)` — and **only** when the tenant claim is `Backoffice`, since the Shop
realm is open self-service registration.

| Route | Gate |
|---|---|
| `/api/v1/admin/partners` | `admin:super` + `partners.manage` |
| `/api/v1/admin/settings` | `admin:super` + `settings.manage` |
| `/api/v1/admin/customers` | `admin:staff` + `customers.manage` |
| `/api/v1/admin/catalog` | `admin:staff` + `catalog.manage` |
| `/api/v1/admin/pricing` | `admin:staff` + `catalog.manage` for prices; `admin:super` + `fx.manage` for rates and currency enablement |
| `/api/v1/orders`, `/orders/bulk` | `partner:orders.create`; read with `partner:orders.read` |
| `/api/v1/customers/{id}/orders` | `partner:customers.read` |
| `/api/v1/invoices/{id}`, `/{id}/pdf`, `/invoices/search` | `partner:invoices.read` |
| `/api/v1/admin/orders` | `admin:staff` + `orders.manage`; refunds take `admin:super` + `refunds.manage` |
| `/api/v1/shop/me` | `shop:customer` — creates the customer record on first call |
| `/api/v1/shop/{cart,checkout,orders,documents}` | `shop:customer` — the caller's own cart and orders only |
| `/api/v1/shop/*` (home, categories, products, facets, search, currencies) | **anonymous** |
| `/graphql` | per resolver, as above; the `shop*` fields are anonymous |

The storefront is anonymous on purpose: a shop that demands a token to show a product page is a
shop nobody indexes and nobody shares a link to. It reads the materialized views only (ADR-0020),
so nothing it returns may price a basket — the cart and the composition engine read
`ISellableVariantPort` and `IPriceQuotePort` against live tables.

A partner's own id is never bindable: it reaches a port through `PartnerPrincipal` alone, and
`PartnerTenancyArchitectureTests` fails the build if any inbound request shape carries a
`PartnerId`.

## Database Migrations

- The host **never** migrates on startup (replicas, rollback, DBA-controlled releases).
- `--migrate` applies the EF migrations and then every repeatable `src/Database/Scripts/*.sql` in
  name order — grants and anything else the EF model cannot express. The scripts are idempotent
  and run on every migrate.
- There is one `EwaysVNextDbContext` and one history table; `Authorization` has its own.
- **EF validates a plpgsql body at `CREATE` time, and reports a reserved parameter name as a
  syntax error positioned inside the body.** It reads like a truncated statement. `concurrently`,
  `user`, `end` and friends are not available as parameter names.
- **The storefront views are versioned, never edited in place** — a change adds `…V2` to
  `StorefrontViewSql` plus a migration that drops and recreates the view with its indexes.
- Development falls back to EF InMemory with no connection string. **InMemory is not a database**:
  it ignores check constraints and filtered unique indexes and evaluates untranslatable LINQ
  client-side. Never let it stand in for a persistence test.

## Testing

Four suites. **IntegrationTests needs Docker**; the other three need nothing.

- **ArchitectureTests** — boundary rules over compiled assemblies, plus source scans (no
  user-initiated transactions, no ambient clock, no Wolverine), the `build/` layout and the
  documentation checks.
- **UnitTests** — domain logic and pure helpers. Time is a parameter, so every one is
  deterministic.
- **PersistenceContractTests** — the EF model against **two** providers: Npgsql pointed at a
  refused port (translation happens before connection, so an untranslatable shape is caught) and
  EF InMemory (which executes, catching materialization failures a connection error would mask).
  Neither alone sees the whole contract.
- **IntegrationTests** — the real host over a Testcontainers Postgres built by the same roles
  script production uses, plus an S3 server. Tests connect as `eways_app`, so anything the
  production grants forbid fails here too.

Read [`docs/TESTING-GUARANTEES.md`](docs/TESTING-GUARANTEES.md) before trusting a green run.

**Mutation-test every guard you add.** Reintroduce the bug, watch a test fail, restore. A guard
nobody has seen fail is not known to work — and a mutation that does not actually change the file
is indistinguishable from one that works.

## Adding a New Module

1. `./New-EwaysModule.ps1 -ModuleName {Name}` scaffolds the projects and wires the solution.
2. Add the name to `HexagonalBoundaryTests.ModuleNames` **in the same change**.
3. Put read DTOs in that module's `QueryApplication`; the write side keeps its own local copies.
4. Cross-module: Outbound Port in the consumer's Application → Gateway in its `Adapter.Outbound` →
   provider's Inbound Port, plus a row in `SanctionedReferences`. This is not transactional.
5. Add `DbSet<T>` and `IEntityTypeConfiguration<T>` to `EwaysVNext.Database` under
   `{Module}/Configurations/` — never a second `DbContext`.
6. Register in `Program.cs`.

## Common Pitfalls

- **Putting logic in QueryApplication** — it is read-only by design (arch-tested).
- **Referencing another module's Application** without a `SanctionedReferences` row.
- **Having Application reference its own QueryApplication** — that would drag EF Core in.
- **Reaching for a message bus, outbox or CQRS bus** — none exist by design.
- **Starting a transaction** — rule 8; the source scan fails the build.
- **Committing a secret** — every value lives in `.env` or the environment.
- **Expecting auto-migrations on startup** — run `--migrate`.
- **`Guid.NewGuid()` for a key**, or a numeric key without `ValueGeneratedNever()`.
- **A `decimal` money column** — minor units, always.
- **Treating "sellable" as "priced"** — they are separate ports and separate modules; a quote
  omits what it cannot price rather than failing.
- **Multi-statement raw SQL with parameters** — Npgsql's extended protocol refuses it (`08P01`),
  and every sub-statement sees one snapshot. Write it as a single CTE.
- **`$1` placeholders in `ExecuteSqlRaw`** — EF binds `DbParameter`s by **name**; use `@p0`.
- **PascalCase aliases in `SqlQueryRaw<TRow>`** — the snake_case convention applies to ad-hoc
  query types too, so `AS "BrandId"` is looked up as `brand_id` and reported missing. A **scalar**
  `SqlQueryRaw<T>` is the opposite: it wants a column named exactly `Value`.
- **Reading a storefront view in a purchasing path** — ADR-0020; they are stale by construction.
- **Assuming cross-module calls are atomic** — each module commits independently.
- **Trusting a green Debug run for a release** — `TreatWarningsAsErrors` is on in Release only,
  and the Release build was broken for a long time while Debug CI stayed green. CI gates on it.

## References

- [`docs/superpowers/specs/2026-10-28-ecommerce-mvp-design.md`](docs/superpowers/specs/2026-10-28-ecommerce-mvp-design.md) — the approved design
- [`docs/product-scope/ecommerce_mvp_prd.md`](docs/product-scope/ecommerce_mvp_prd.md) — the PRD
- [`docs/ONBOARDING.md`](docs/ONBOARDING.md) — how to run it
- [`docs/DDD-GUIDELINES.md`](docs/DDD-GUIDELINES.md) — DDD practice here
- [`docs/TESTING-GUARANTEES.md`](docs/TESTING-GUARANTEES.md) — what the tests do and do not prove
- [`docs/adr/`](docs/adr/) — decision records; 0017–0022 cover this MVP
- [`tests/ArchitectureTests/HexagonalBoundaryTests.cs`](tests/ArchitectureTests/HexagonalBoundaryTests.cs) — the executable rules

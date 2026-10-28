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

Present today: **Catalog**, **Customer**, **Partners**. Customer is a compile-only shell — its
Iran-market domain was removed ([ADR-0022](docs/adr/0022-remove-iran-specific-modules.md)) and
phase 1 rebuilds it. Partners is being built now: the key format and peppered hashing exist, the
aggregate and adapters do not yet.

Planned (not yet present; do not reference them):

| Module | Phase | Owns |
|---|---|---|
| Settings | 1 | Seller identity, payment instruction, commerce limits |
| Pricing | 2 | Reference prices, currency overrides, FX rates |
| Sales | 4–5 | Carts, orders, composition engine, invoices, refunds |
| Sourcing | 7 | Import jobs, staging, crawling, publishing |
| Admin | 6 | Cross-module composition and the admin GraphQL |

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
Modules/{Catalog,Customer,Partners}/
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
| `Keycloak__InternalUrl` / `Keycloak__ExternalUrl` | Yes | Container address and public address. |
| `Brave__ApiKey` / `Anthropic__ApiKey` | No | Catalog discovery and enrichment; each feature disables itself when absent. |
| `Host__RateLimit__GlobalPermitPerMinute` | No | Default 100, per IP. In-process. |

## Database Migrations

- The host **never** migrates on startup (replicas, rollback, DBA-controlled releases).
- `--migrate` applies the EF migrations and then every repeatable `src/Database/Scripts/*.sql` in
  name order — grants and anything else the EF model cannot express. The scripts are idempotent
  and run on every migrate.
- There is one `EwaysVNextDbContext` and one history table; `Authorization` has its own.
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

# AGENTS.md — EwaysVNext v3

## Project Overview
- **.NET 10** Modular Monolith with **Hexagonal-First** architecture (Ports & Adapters)
- **No message broker.** MassTransit/RabbitMQ, the transactional Outbox/Inbox, and `IIntegrationEventBus` have all been removed. Cross-module WRITE orchestration is direct method calls via hexagonal ports (Outbound Port in the consumer's Application → Gateway in the consumer's Adapter.Outbound → provider's Inbound Port). It is NOT transactional: every module shares the single `EwaysVNextDbContext` (see `src/Database`), but each operation still does its own `SaveChangesAsync` (rule 7 below), so a cross-module operation cannot roll back atomically just because the context is shared.
- **Domain events dispatch synchronously, in-process, no persistence.** An aggregate raises events via `Raise(...)`; `EfCoreUnitOfWork<TDbContext>` (in `EwaysVNext.Persistence`) captures each tracked aggregate's pending events before `SaveChangesAsync`, and once the save succeeds it publishes them through `IDomainEventBus` (reflection dispatch to `IDomainEventHandler<T>`, in `EwaysVNext.Events.Implementation`) and clears the buffer. There is no Outbox row, no background processor, and no cross-process delivery — a handler failure after a successful save is lost, not retried. This is a deliberate simplification: no message broker means no durable async delivery.
- **CQS, not CQRS.** `Application/` (write hexagon) + `QueryApplication/` (flat read side — same DB, direct EF Core projections). There is no command/query bus, mediator, or handler-lookup layer — application services are called directly. `EwaysVNext.Queries.Application`/`.Implementation` hold only pagination shapes (`PaginationQuery`, `CursorPaginationQuery`, …) and Gridify-based query helpers, not a dispatch abstraction.
- 8 modules: **Identity**, **Catalog**, **Orders**, **Customer** (provider), **Payment** (consumer), **Supplier** (owns BrandSupplyAgreement M2M, ADR-0009), **Brand** (identity/approval lifecycle), **Admin** (composition + admin GraphQL, ADR-0006/0007)

## Structure (src/)

```
BuildingBlocks/
├── Domain/                      ← Entity&lt;TId&gt;, ValueObject, AggregateRoot&lt;TId&gt;/IAggregateRoot, DomainEvent, Guard,
│                                   IAuditable/ISoftDeletable, IIdGenerator/SnowflakeIdGenerator, IClock/SystemClock,
│                                   Money/Email
├── Outcomes/                    ← Result/Result&lt;T&gt;/Error: the ONE outcome type used across the WHOLE system
│                                   (Application code, QueryApplication, REST endpoints — everywhere). Deliberately
│                                   framework-free (no ASP.NET Core reference) so any building block or module can
│                                   depend on it without dragging HTTP concerns in. Kept OUT of Domain itself (Domain
│                                   throws DomainException; translating that into a Result is an Application concern).
├── Persistence/                 ← EF Core base: EfCoreRepository, EfCoreUnitOfWork (captures + dispatches domain
│                                   events around SaveChanges, see above), IUnitOfWork, Audit/SoftDelete interceptors,
│                                   ModuleDbContextBase (generic schema/InMemory-RowVersion base, now only used by
│                                   test fixtures — production uses the single EwaysVNextDbContext below instead).
├── Queries/{Application,Implementation}/  ← Pagination shapes (PaginationQuery, CursorPaginationQuery, …) and
│                                   Gridify-based filtering/ordering helpers. No bus, no handler abstraction — see
│                                   "CQS, not CQRS" above. `ApplyPagination`/`ApplyCursorPagination` take a Mapperly-
│                                   generated `Expression&lt;Func&lt;TSource,TDestination&gt;&gt;` projector.
├── Events/{Application,Implementation}/   ← IDomainEvent/IDomainEventBus/IDomainEventHandler + DomainEventBus
│                                   (in-process reflection dispatch only — no integration event bus, no broker).
├── Core/{Application,Implementation}/     ← IUserProfileService, Actor. That is the whole of it — distributed
│                                   cache/lock and the Newtonsoft-backed IJsonService were removed as dead weight
│                                   (zero real consumers); use System.Text.Json directly if you need JSON.
├── Presentation.Endpoint/       ← IEndpoint discovery, EndpointResult (shapes an Outcomes.Result/Result&lt;T&gt; into
│                                   HTTP via ToHttpResult()/ToCreatedHttpResult(), plus ad-hoc Succeed/Created/
│                                   Unauthorized/Forbidden for responses not derived from an existing Result), enum-
│                                   value endpoint filter, HttpLoggingHandler. References Outcomes (for the type it
│                                   shapes) but Outcomes itself has no reverse dependency — the framework-carrying
│                                   HTTP-shaping code stays fully separate from the plain outcome type. Every REST
│                                   endpoint response (success AND error) goes through EndpointResult/Result — no
│                                   bare Results.Ok/BadRequest, no ProblemDetails. Host calls one `app.MapEndpoints()`
│                                   for all modules instead of a per-module Map*Endpoints call.
└── DependencyInjection/         ← Composition helpers wiring the above into a host: AddEvents,
                                    AddPersistenceInterceptors, AddUserProfileService.

There is no `Framework/`, `SharedKernel/`, `Contracts/`, or `Commands/` project. `Contracts` (a
shared cross-module DTO project) was removed — each module's own `QueryApplication` now owns the
read-model DTOs it exposes as the ACL other modules/Admin consume, and each module's write-side
`Application` layer keeps its own local copies of whatever DTOs its ports return (never a reference
to another layer's DTOs — see rule 2 below on why Application can't reach into QueryApplication).

Modules/{Identity,Catalog,Orders,Customer,Payment}/
├── Application/            ← Hexagon: Domain + Ports + Service
├── Adapter/
│   ├── Inbound/Rest/       ← Driving adapter (REST endpoints)
│   └── Outbound/           ← Driven adapters (SQL repos + ACL gateways)
├── QueryApplication/       ← Read side (direct EF Core projections)
└── Installer/              ← Composition Root (Add{Module}Module)

Modules/Admin/          ← Composition context (NO own DbContext, ADR-0006/0007):
   Application/ (ports + FailureReportService + admin services), Adapter/
   Outbound/ (read-side ACL → Customer/Payment/Orders QueryApplications +
   write-side ACL gateways → provider inbound ports), Adapter/Inbound/GraphQL/
   (HotChocolate admin endpoint), Installer/. Writes flow only via gateway ports.

Database/EwaysVNext.Database/  ← The ONE EF Core context for the whole app: EwaysVNextDbContext
   (DbSets for every module's aggregates, schema-per-module via reflection over each entity's
   CLR namespace), PersistenceDependencyInjection.AddUnifiedDatabase (SQL Server or, with no
   connection string, InMemory in dev — wires Audit/SoftDelete interceptors and the DB health
   check), EwaysVNextDbContextDesignTimeFactory, Migrations/. Each module's own
   Catalog/Configurations or Customer/Configurations folder here holds that module's
   IEntityTypeConfiguration<T> classes — no module owns its own DbContext or migration history
   table anymore.

Host/EwaysVNext.Host/  ← Entry point (thin, only wires modules)
```

**DI composition pattern**: each layer registers only its own services (`Application/
DependencyInjection.cs` → `AddXApplication()`, `Adapter/Outbound/DependencyInjection.cs` →
`AddXOutboundAdapters()`, etc.), and each module's `Installer/ModuleInstaller.cs` rolls all of
that up into ONE `AddXModule(...)` call — including that module's own building-block wiring:
`AddEvents(applicationAssembly)` (scans that module's own Application assembly for
`IDomainEventHandler<T>`), and `AddPersistenceInterceptors()`/`AddUserProfileService()`.
`AddPersistenceInterceptors`'s two interceptor registrations (Audit, SoftDelete) use
`TryAddEnumerable`, not `AddScoped`, specifically so calling it from more than one module doesn't
double-register an interceptor and run it twice per save. Neither module registers its own
`DbContext` or health check anymore — `Program.cs` calls `AddCatalogModule()`/
`AddCustomerModule(config)` for module wiring, then one `AddUnifiedDatabase(config,
isDevelopment)` (in `EwaysVNext.Database`) for the shared `EwaysVNextDbContext`: it configures
SQL Server (or InMemory in dev with no connection string), attaches every registered
`ISaveChangesInterceptor` via `AddInterceptors(sp.GetServices<ISaveChangesInterceptor>())` so the
per-module `AddPersistenceInterceptors()` calls actually take effect, and registers the one DB
health check (`AddDbContextCheck<EwaysVNextDbContext>("Database", tags: ["ready"])`). There is no
Host-level message bus registration to worry about, since there is no message bus.

`Application/DependencyInjection.cs` itself stays untouched by any of this — it doesn't reference
`EwaysVNext.DependencyInjection` (which pulls in `EwaysVNext.Persistence` → EF Core), keeping
Application's "no framework dependencies" claim true. The events/interceptor/health-check wiring
lives in `Installer/ModuleInstaller.cs` and `Adapter/Outbound/DependencyInjection.cs` instead, both
of which already touch EF Core, so nothing new leaks into Application.

## Key Architectural Rules (enforced by ArchitectureTests)

1. **Application → Adapter**: Application must NOT depend on any Adapter (Inbound or Outbound)
2. **Application → EF Core**: Application must NOT depend on `Microsoft.EntityFrameworkCore` (not currently enforced by an architecture test — `Catalog.Application`/`Customer.Application` both reference `EwaysVNext.Persistence` for the single `IUnitOfWork`, which makes EF Core a transitive *package* dependency, though no EF Core *type* is used directly in Application code)
3. **Module isolation**: No module Application may reference another module's Application
   - **Exceptions**: `Orders.Adapter.Outbound` → `Catalog.Application`; `Payment.Adapter.Outbound` → `Customer.Application` (write-side ACL gateways)
   - **Exception #3 (reads)**: `Admin.Adapter.Outbound` → `Customer.QueryApplication`, `Payment.QueryApplication`, `Orders.QueryApplication` (ADR-0006)
   - **Exception #5**: `Supplier.Adapter.Outbound` → `Brand.Application` — brand existence check before agreement mutations (ADR-0009); Brand never references Supplier
4. **No Wolverine**: Any reference to `Wolverine` fails the build
5. **Inbound/Outbound ports** must be interfaces; **Services** must be `sealed`
6. **QueryApplication**: Read-only — no writes, no aggregate loading, no domain rules
7. **One `SaveChangesAsync` per operation, no cross-module transaction primitive.** A write is one aggregate write per *operation* (EF implicit transaction, atomic). Cross-module consistency is NOT atomic — a later step's failure does not roll back an earlier module's already-committed write; the caller is responsible for any compensation it needs. There is deliberately NO explicit multi-aggregate transaction primitive: if an operation seems to need one, the aggregate boundaries are probably wrong — fix the boundaries instead.

   **This rule used to read "never mutate two aggregates inside a single `SaveChangesAsync`", full stop. That absolute is wrong, and the codebase's own `ReservationExpirationService` breaks it correctly:** the TTL reaper loads every product with a reclaimable reservation and saves them in one call. That is legitimate because the changes are **independent, idempotent and commutative** — no invariant spans two products, so there is no consistency boundary being violated, and one round trip beats N.

   The real rule: **one `SaveChangesAsync` must never span two aggregates whose invariants depend on each other.** A batch of unrelated aggregates is fine. If you cannot say why the aggregates are independent, treat them as dependent.

8. **Retry is enabled, so do not start transactions.** `AddUnifiedDatabase` configures `EnableRetryOnFailure`, and an EF execution strategy throws on a user-initiated transaction — it cannot replay work it did not begin. Every write path must stay load → mutate → one `SaveChangesAsync`. A source scan in `TransientFaultResilienceTests` fails the build if `BeginTransaction`/`UseTransaction` appears in `src/`, because the InMemory provider has no execution strategy and no test would otherwise catch it before production. If you genuinely need an intermediate save, wrap it in `Database.CreateExecutionStrategy().ExecuteAsync(...)`.

9. **A `Result`-returning method must not throw for a refusal it can express.** Both currency guards used to throw `InvalidOperationException`, turning a data condition into a 500 on a server that was working fine — misinforming the caller and polluting the 5xx signal used to detect real outages. Return the `Error`.
10. **Aggregate root and entity primary keys are `long`, generated by `IIdGenerator` (Snowflake), never `Guid.NewGuid()`.** The id is generated in the Application service (which holds the `IIdGenerator` dependency) and passed as a parameter into the aggregate's static factory or the instance method that creates a child entity — the same pattern already used for `IClock`/`now`. EF configurations must call `.Property(x => x.Id).ValueGeneratedNever()` on every aggregate root, since the default convention for a numeric key is `ValueGeneratedOnAdd` (SQL identity), which would silently discard the client-generated value. External references that are not our own primary keys (e.g. `Customer.IdentityUserId`, pointing at an identity system this codebase doesn't own) stay `Guid` — only PKs generated by this codebase move to Snowflake.

## Developer Commands

```bash
# Restore, build, run (dev)
dotnet restore EwaysVNext.slnx
dotnet build EwaysVNext.slnx
export JWT__SIGNINGKEY=$(openssl rand -base64 48)   # Required in prod; auto-generated in dev
dotnet run --project src/Host/EwaysVNext.Host

# Apply EF migrations (explicit — the host NEVER auto-migrates on startup)
dotnet run --project src/Host/EwaysVNext.Host -- --migrate

# Create a new migration after changing any module's domain model — there is one
# DbContext for the whole app, so this is the only migration command regardless of module
dotnet ef migrations add {Name} -p src/Database -s src/Host/EwaysVNext.Host

# Run all tests. xUnit.net v3 runs on Microsoft.Testing.Platform; global.json's
# "test": { "runner": "Microsoft.Testing.Platform" } is what makes `dotnet test` work
# at all on the .NET 10 SDK. Each test project is an Exe and can also be run directly.
dotnet test EwaysVNext.slnx

# Run only architecture tests (fast, no DB)
dotnet test tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj

# Run unit tests (domain logic, no DB)
dotnet test tests/UnitTests/EwaysVNext.UnitTests.csproj

# Run integration tests (requires LocalDB: sqllocaldb start MSSQLLocalDB)
dotnet test tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj

# Docker (requires .env — copy .env.example; compose runs migrations via init container)
docker compose up --build
```

## Environment Variables

| Variable | Required | Notes |
|----------|----------|-------|
| `JWT__SIGNINGKEY` | **Yes (prod)** | Min 32 chars. In dev, auto-generated if missing (with warning). Never put in `appsettings.json`. Compose reads `JWT_SIGNING_KEY` from `.env` and fails fast if unset. |
| `ConnectionStrings__Default` | Yes | SQL Server, for the single shared `EwaysVNextDbContext`. Missing connstring throws outside Development (no silent InMemory fallback). |
| `Jwt__Issuer` / `Jwt__Audience` | No | Defaults in `docker-compose.yml` |
| `MSSQL_SA_PASSWORD` | Yes (compose) | SQL Server SA password via `.env` — never committed. |

## Database Migrations

- The host **never** migrates on startup (production safety: replicas, rollback, DBA-controlled releases).
- Dev uses InMemory when no connection string is configured; **the integration tests run on LocalDB and apply the migrations**, so a broken migration fails the suite.
- Provision with `--migrate`, not `dotnet ef database update`: there is one `EwaysVNextDbContext` and one history table (`__EFMigrationsHistory`), configured in `AddUnifiedDatabase` and matched by `EwaysVNextDbContextDesignTimeFactory`.
- **Local/prod manual:** `dotnet run --project src/Host/EwaysVNext.Host -- --migrate`
- **Docker:** the `migrations` init container runs `--migrate`; `api` starts only after it exits successfully.
- `EwaysVNextDbContextDesignTimeFactory` (`src/Database`) lets `dotnet ef` build the relational model even though dev runs InMemory.

## API Endpoints (dev)

| Base | Description |
|------|-------------|
| `/health` | Health check (the one shared `EwaysVNextDbContext`) |
| `/scalar` | Scalar API docs UI (dev only) |
| `/openapi/v1.json` | OpenAPI document |
| `/api/v1/identity` | Register, Login, Refresh, /me, Admin user lookup |
| `/api/v1/auth/token/{realm}` | Dev/testing convenience: exchange email+password for a Keycloak access token via ROPC, routed to the `Shop`/`Backoffice` realm named in the path (uses each realm's `Keycloak:Realms:{realm}:ClientId`; `Shop`'s client currently has Direct Access Grants disabled in Keycloak, so only `Backoffice` works until that's turned on) |
| `/scalar` "Authorize" | Real Keycloak SSO from Scalar's own UI: one OAuth2 (Authorization Code + PKCE) security scheme per realm, named `Shop`/`Backoffice`, added to the OpenAPI doc by `KeycloakOAuthDocumentTransformer`. Requires each Keycloak client (`modular-monolith`, `backoffice-console`) to have this Host's `/scalar/` URL registered as a valid redirect URI in Keycloak — neither client has it yet |
| `/api/v1/catalog` | Products CRUD, price/stock, reservations |
| `/api/v1/orders` | Create order (with stock reservation), status changes |
| `/api/v1/customers` | Customer profiles, addresses, saved payment methods (tokens only) |
| `/api/v1/payments` | Payment initiation (saved-card / new-token), capture/fail |
| `/api/v1/admin/graphql` | Admin GraphQL (HotChocolate): full admin panel — Catalog/Customer/Order/Payment queries+mutations, analytics, failed-payments report. Admin policy ×2 + `admin` rate-limit + max-depth 10 (ADR-0007) |

### GraphQL example

```graphql
query FailedPayments($f: FailureReportFilterInput!) {
  customersWithFailedPayments(filter: $f) {
    totalCount page pageSize
    items {
      totalFailedAmount currency failureCount
      customer {
        id displayName status
        lastOrder { orderId orderDate totalAmount status itemCount }  # Orders module enrichment
      }
      failures { paymentId orderId amount currency failedAt reason }
    }
  }
}
# variables: { "f": { "from": "2026-08-01T00:00:00Z", "minAmount": 50, "customerStatus": "Active", "page": 1, "pageSize": 20 } }
```
## Testing Notes

**265 tests, four suites.** The integration suite needs SQL Server LocalDB
(`sqllocaldb start MSSQLLocalDB`); the other three need no database. Read
[`docs/TESTING-GUARANTEES.md`](docs/TESTING-GUARANTEES.md) before trusting a green run — it
lists what each suite *cannot* see.

- **ArchitectureTests**: NetArchTest over compiled assemblies, plus source scans (no
  user-initiated transactions, no ambient clock outside the `IClock`/`IIdGenerator`
  implementations themselves). Module boundaries, hexagonal compliance, no Wolverine,
  QueryApplication isolation.
- **UnitTests**: domain logic (reservation math/TTL/retention, `Money` currency rules,
  customer registration/payment-method rules) + domain-event dispatch (`EfCoreUnitOfWork`) +
  HTTP status semantics — pure, no IO. Time is a parameter, so every one is deterministic.
- **PersistenceContractTests** (31): runs the real query services through DI against **both**
  SQL Server (unreachable port — catches untranslatable LINQ) **and** EF InMemory (executes —
  catches materialization failures). Also asserts DDL: check constraints and read-path index
  column order. **Neither provider alone sees the whole contract**, which is why both run.
- **IntegrationTests** (60): full HTTP host via `WebApplicationFactory<Program>` on **SQL
  Server LocalDB**, schema built by running the migrations. Authorization, ownership,
  idempotency, admin paging refusals, read-side correctness past a page boundary, plus
  optimistic concurrency, check-constraint enforcement, the migration backfill and real
  deadlock recovery — the four that EF InMemory could not express.

**Two things to internalise:**
- **InMemory is not a database.** It evaluates untranslatable LINQ client-side, ignores check
  constraints and filtered unique indexes, and has no execution strategy. This repository once
  had 95 green tests on a system that could not log a user in against SQL Server. The
  integration suite runs on LocalDB for exactly this reason; `PersistenceContractTests` still
  uses both providers deliberately, since neither alone sees the whole mapping contract.
- **Mutation-test every guard you add.** Reintroduce the bug, watch a test fail, restore. A
  guard nobody has seen fail is not known to work.

Test order: architecture tests should pass before integration tests.

## Adding a New Module

1. Create `src/Modules/{NewModule}/` with 5 projects (Application, Adapter/Inbound/Rest, Adapter/Outbound, QueryApplication, Installer)
2. Follow existing module structure exactly — architecture tests will validate
3. Define the module's read-model DTOs in its own `QueryApplication` — that is the ACL other modules/Admin consume to read this module's data. Application (write side) keeps its own local copies of whatever DTOs its ports return; it must never reference QueryApplication (QueryApplication depends on Adapter.Outbound → EF Core, which would break Application's framework-free rule).
4. For cross-module calls: add Outbound Port in consumer's Application → implement Gateway in consumer's Adapter.Outbound → call provider's Inbound Port directly. This is not transactional — a later step's failure does not undo an earlier module's already-committed write, so design each step to be safe to fail after.
5. Add the new aggregate's `DbSet<T>` and `IEntityTypeConfiguration<T>` to `EwaysVNext.Database` (its own `{NewModule}/Configurations/` folder) — **never a per-module `AddDbContext`/new `DbContext` subclass**. There is exactly one `EwaysVNextDbContext`, wired once via `AddUnifiedDatabase` in `Program.cs`.
6. Register in Host: `builder.Services.AddNewModuleModule(builder.Configuration)`

## Common Pitfalls

- **Putting logic in QueryApplication** — it's read-only by design (arch-tested)
- **Referencing another module's Application** — use that module's QueryApplication (reads) or its Inbound Port via an ACL Gateway (writes) instead
- **Having Application reference its own module's QueryApplication** — QueryApplication depends on Adapter.Outbound → EF Core; Application must stay framework-free. Define local DTOs in Application instead of reaching into QueryApplication's.
- **Reaching for a message bus, Outbox, or CQRS command/query bus** — none exist in this codebase by design; domain events dispatch synchronously in-process via `EfCoreUnitOfWork`/`IDomainEventBus`, and application services are called directly
- **Adding Wolverine** — architecture tests will fail
- **Committing JWT key or SA password to config** — use env vars / `.env` only
- **Expecting auto-migrations on startup** — they don't happen; run `--migrate` explicitly
- **Composing cross-module reads outside Admin** — provider QueryApplications may only be consumed by `Admin.Adapter.Outbound` (arch-tested)
- **Adding a DbContext to Admin, or a new per-module DbContext at all** — there is exactly one `EwaysVNextDbContext` (`src/Database`) for the whole app; Admin owns no data by design (composition goes through its outbound ports), and every other module adds its entities/configurations to `EwaysVNext.Database` instead of standing up its own context
- **Putting single-module admin operations in the Admin module** — they belong in the owning module's admin service (`Catalog.ICatalogAdminService`, `Customer.ICustomerAdminService`, …); Admin composes, domains own
- **Generating a new aggregate root or entity id with `Guid.NewGuid()`** — use the injected `IIdGenerator` (Snowflake, `long`) instead; only external references this codebase doesn't own (e.g. `IdentityUserId`) stay `Guid`
- **Setting a numeric key on an aggregate root without `ValueGeneratedNever()`** — EF's default convention for a `long`/`int` key is `ValueGeneratedOnAdd` (SQL identity), which silently discards the client-generated Snowflake id
- **Assuming cross-module calls are atomic** — each module's write commits independently; a later step's failure does not roll back an earlier one
- **Forgetting to run architecture tests** — they catch boundary violations early

## References

- `docs/ONBOARDING.md` — Eagle-eye view + run instructions
- `docs/DDD-GUIDELINES.md` — DDD best practices for this codebase
- `docs/adr/` — Architecture Decision Records (10 ADRs). **ADR-0010 is a prerequisite
  before `replicas > 1`:** the reaper, rate limiter and cache are all in-process today, and it
  carries a blocking compensation-on-SIGTERM fix.
- `docs/TESTING-GUARANTEES.md` — what the 265 tests prove and what they do NOT
- `tests/ArchitectureTests/HexagonalBoundaryTests.cs` — executable rules
- `docs/superpowers/specs/2026-09-30-shop-494-review-fixes-design.md` — reusable GraphQL mutation and state-update review guardrails


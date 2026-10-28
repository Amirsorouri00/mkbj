# EwaysVNext v3 — Hexagonal-First Modular Monolith for .NET 10

A production-shaped reference implementation of a **Hexagonal-First modular monolith**:
eight bounded-context modules plus an Admin composition layer, machine-enforced boundaries, synchronous cross-module
ACL gateways, a read-optimized query side per module, and a full admin GraphQL panel —
**no message bus, no Wolverine/MassTransit/MediatR**.

## Technologies

| Area | Technology |
|------|------------|
| Architecture | **Hexagonal-First** (Ports & Adapters), enforced by NetArchTest |
| Inter-module calls | **Direct method calls** via ACL gateway ports, wrapped in a **synchronous saga with compensation** — NOT a shared transaction (ADR-0002/0005) |
| Query side | **Read-Optimized CQS ("CQS+")** — flat `QueryApplication` projections per module (ADR-0008) |
| Domain events | **Pluggable `IEventDispatcher` — currently NoOp** (ADR-0004); swap without touching the core |
| ORM | EF Core 10 — independent DbContext per module |
| Validation | FluentValidation at hexagon boundaries |
| Auth | JWT Bearer + refresh tokens + account lockout |
| API surface | REST (+ OpenAPI/Scalar) and **Admin GraphQL** (HotChocolate 16) |
| Observability | Serilog · OpenTelemetry tracing & metrics (incl. saga counters) |
| Tests | **xUnit.net v3** on **Microsoft.Testing.Platform** (not VSTest) — **265 tests**: 151 unit · 60 integration · 31 persistence-contract · 23 architecture. Integration runs on **SQL Server LocalDB** (see prerequisite below). See [what they do and do not prove](docs/TESTING-GUARANTEES.md) |

## Modules (`src/Modules/`)

| Module | Responsibility |
|--------|----------------|
| **Identity** | Users, credentials, JWT issue/refresh, lockout |
| **Catalog** | Products, stock, reservations (TTL reaper included) |
| **Orders** | Order lifecycle; reserves stock via Catalog ACL (saga with compensation) |
| **Customer** | Profiles, addresses, saved payment methods (**vault tokens only**) |
| **Payment** | Payment lifecycle: initiate → capture / fail / refund |
| **Supplier** | Supplier verification + **owns** the BrandSupplyAgreement M2M (ADR-0009) |
| **Brand** | Brand identity + approval lifecycle (PendingReview → Approved/Rejected) |
| **Category** | Independent merchandising taxonomy and curated product promotion; stores category-to-product assignments by product ID |
| **Admin** | Thin composition layer: admin GraphQL panel over provider read sides + write gateways (ADR-0006/0007) |

Every domain module follows the same five-project layout:

```
Modules/{Module}/
├── Application/        ← Hexagon: Domain + Ports(Inbound/Outbound) + Services
├── Adapter/
│   ├── Inbound/        ← Driving adapters (REST, GraphQL for Admin)
│   └── Outbound/       ← Driven adapters: EF Core repos, ACL gateways
├── QueryApplication/   ← Read side: flat EF projections into Contract DTOs
└── Installer/          ← Add{Module}Module() composition root
```

## Authorization (`src/Authorization/`, not a module)

A cross-cutting RBAC subsystem — permissions, roles, per-user product-scope assignments —
that any module can use to gate its own REST or GraphQL endpoints, without being a
`src/Modules/` module itself. It's a fourth project group, parallel to `src/BuildingBlocks/`
and `src/Modules/`, sharing the same physical database (its own `authorization` schema, its
own migrations history table) but with no `Program.cs` and no dependency on
`BuildingBlocks/Core`. Enforcement runs entirely through ASP.NET Core's own policy-based
authorization (dynamic `"authz-permission:{code}"` / `"authz-role:{role}"` policies +
`IAuthorizationHandler`) — no custom middleware, and the same mechanism HotChocolate's
`[Authorize(Policy = ...)]` already understands. See
[`src/Authorization/README.md`](src/Authorization/README.md) for the full picture — layout,
the rationale for keeping it out of `src/Modules/`, how another module attaches
`.RequirePermission()`/`.RequireRole()` (REST) or `[Authorize(Policy = ...)]` (GraphQL), and
its own migration commands.

## Key architectural rules (machine-enforced — 20 rules, 23 test cases)

- Application never depends on Adapters or EF Core; ports are interfaces; services are `sealed`
- No module Application references another module's Application — cross-module access only via Contracts DTOs through consumer-owned ports
- Sanctioned ACL bridges: `Orders.Adapter.Outbound → Catalog.Application`, `Payment.Adapter.Outbound → Customer.Application`, `Supplier.Adapter.Outbound → Brand.Application`, `Category.Adapter.Outbound → Catalog.Application` (active-product lookup only), `Admin.Adapter.Outbound → provider Applications/QueryApplications`
- `Category` is independent from the primary Catalog category hierarchy. Its Application layer has no Catalog reference; only its outbound ACL adapter may call Catalog, and assignments retain a `ProductId` rather than a Catalog entity or relationship.
- Single-module admin operations live in **domain-owned** admin services (`Catalog.ICatalogAdminService`, …); Admin composes and stays aggregate-free
- No Wolverine / MediatR / external message bus — anywhere

See [`tests/ArchitectureTests/HexagonalBoundaryTests.cs`](tests/ArchitectureTests/HexagonalBoundaryTests.cs) for the executable rules.

## Getting started

```bash
dotnet restore EwaysVNext.slnx
dotnet build EwaysVNext.slnx

# Local connection strings go in User Secrets, never in appsettings*.json — set once per machine:
dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost,1433;Database=EwaysVNext;User Id=sa;Password=<yours>;TrustServerCertificate=True;MultipleActiveResultSets=True;Encrypt=False;Connect Timeout=30" --project src/Host/EwaysVNext.Host

export JWT__SIGNINGKEY=$(openssl rand -base64 48)   # required in prod; auto-generated in dev
dotnet run --project src/Host/EwaysVNext.Host

# Apply EF migrations (the host NEVER auto-migrates on startup)
dotnet run --project src/Host/EwaysVNext.Host -- --migrate

### Database migration helpers

Create a new migration in the unified `EwaysVNext.Database` project:

```powershell
dotnet ef migrations add Init --project src\Database\EwaysVNext.Database.csproj --startup-project src\Host\EwaysVNext.Host\EwaysVNext.Host.csproj --context EwaysVNextDbContext --output-dir Migrations
```

Generate the SQL script for the unified database:

```powershell
dotnet ef migrations script --project src\Database\EwaysVNext.Database.csproj --startup-project src\Host\EwaysVNext.Host\EwaysVNext.Host.csproj --context EwaysVNextDbContext
```

# Tests. The INTEGRATION suite runs against SQL Server LocalDB and creates/drops seven
# databases per run, so start the instance first (ships with Visual Studio and with the
# SQL Server Express installer). The other three suites need no database.
sqllocaldb start MSSQLLocalDB

# The suites run on Microsoft.Testing.Platform, which `global.json` opts into
# ("test": { "runner": "Microsoft.Testing.Platform" }). Without that entry every
# `dotnet test` fails: the .NET 10 SDK refuses the VSTest target for an MTP project.

dotnet test EwaysVNext.slnx                                            # everything
dotnet test tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj # boundaries (fast)
dotnet test tests/UnitTests/EwaysVNext.UnitTests.csproj                 # domain + saga toolkit
dotnet test tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj   # E2E via WebApplicationFactory

# Docker (requires .env — copy .env.example; migrations run via init container)
docker compose up --build
```

### Endpoints

| Base | Description |
|------|-------------|
| `/health/live` | Liveness — runs NO checks. Point Kubernetes liveness here (ADR-0010) |
| `/health/ready` | Readiness — all module DbContexts |
| `/health` | Retained for existing probes and compose; equivalent to readiness |
| `/scalar` | Scalar API docs UI (dev only) |
| `/openapi/v1.json` | OpenAPI document |
| `/api/v1/identity` | Register, Login, Refresh, `/me`, Admin user lookup |
| `/api/v1/catalog` | Products CRUD, price/stock, reservations |
| `/api/v1/orders` | Create order (stock reservation saga, accepts an idempotency key), `POST /orders/{id}/pay`, status changes |
| `/api/v1/customers` | Customer profiles, addresses, saved payment methods (tokens only) |
| `/api/v1/payments` | **Admin-only** initiation (saved-card / new-token — they accept a caller-supplied amount), capture/fail. Customers pay via `POST /orders/{id}/pay`, which charges `order.TotalAmount` and takes no amount parameter |
| `/api/v1/suppliers` | Suppliers: create, get (admin ops via GraphQL) |
| `/api/v1/brands` | Brands: create, get (admin ops via GraphQL) |
| `/api/v1/categories/tree` | Public active category tree |
| `/api/v1/categories/{id}` | Public active category detail |
| `/api/v1/categories/{id}/products` | Public product IDs assigned to a category (`page`/`pageSize`, max 100) |
| `/api/v1/products/{productId}/categories` | Public active categories assigned to a product |
| `/api/v1/admin/graphql` | **Admin GraphQL** — full panel (see below); Admin policy ×2 + rate limit + max-depth 10 |
| `/graphql` (Authorization) | Permissions/roles/scopes/user-role-scopes over GraphQL only (no REST routes): writes are `[Authorize]`d mutations (`createPermission`… `bootstrapAdmin`, permission gates temporarily removed), reads are queries (`permissions`, `roles`, `scopes`, `userRoleScopesForUser`); see [`src/Authorization/README.md`](src/Authorization/README.md) |

The Category admin GraphQL server is at `/api/v1/admin/category/graphql`.
It requires the `Admin` policy and the `admin` rate limiter. Queries expose category search/detail,
children, and paged product IDs; mutations create, update, move, activate, deactivate, and soft-delete
categories and assign/unassign product IDs. Public reads return only effectively active categories
(inactive/deleted ancestors hide descendants). Published DTOs currently live in
`src/BuildingBlocks/Contracts/Category`; moving them to a module-owned Contracts project is
deferred until the planned solution-wide Contracts refactor reaches `main`.

### Admin GraphQL example

```graphql
query FailedPayments($f: FailureReportFilterInput!) {
  customersWithFailedPayments(filter: $f) {
    totalCount page pageSize
    items {
      totalFailedAmount currency failureCount
      customer {
        id displayName status
        lastOrder { orderId orderDate totalAmount status itemCount }  # Orders enrichment
      }
      failures { paymentId orderId amount currency failedAt reason }
    }
  }
}
# variables: { "f": { "from": "2026-08-01T00:00:00Z", "minAmount": 50, "page": 1, "pageSize": 20 } }
```

Mutations cover catalog CRUD, customer suspension, order status changes,
payment capture/refund/fail, supplier verification, brand approval/rejection,
and brand↔supplier agreement assignment (commission rates).

## Environment variables

| Variable | Required | Notes |
|----------|----------|-------|
| `JWT__SIGNINGKEY` | Yes (prod) | Min 32 chars; dev auto-generates with warning. Compose reads `JWT_SIGNING_KEY` from `.env` and fails fast if unset |
| `ConnectionStrings__{Identity,Catalog,Orders,Customer,Payment,Supplier,Brand,Category}` | Yes | SQL Server. Missing connstring throws outside Development (no silent InMemory fallback) |
| `MSSQL_SA_PASSWORD` | Yes (compose) | Via `.env` — never committed |

## Documentation

- [`AGENTS.md`](AGENTS.md) — authoritative conventions, structure, pitfalls
- [`src/Authorization/README.md`](src/Authorization/README.md) — the cross-cutting RBAC
  subsystem: layout, why it isn't a `src/Modules/` module, how to gate a REST endpoint or a
  GraphQL resolver from another module, and its own migration commands
- [`docs/ONBOARDING.md`](docs/ONBOARDING.md) — eagle-eye view
- [`docs/DDD-GUIDELINES.md`](docs/DDD-GUIDELINES.md) — DDD practices for this codebase
- [`docs/ADDING-CUSTOMER-PAYMENT-MODULES.md`](docs/ADDING-CUSTOMER-PAYMENT-MODULES.md) — **adding
  a module pair, end to end.** Walks the real Customer (provider) / Payment (consumer) code:
  ACL gateway, `Actor` authorization, deterministic time, `CrossModuleSaga`, idempotency, and the
  four verification tiers. Every code sample is cited back to its source file
- [`docs/TESTING-GUARANTEES.md`](docs/TESTING-GUARANTEES.md) — **what the 265 tests prove and,
  more importantly, what they do NOT.** Read this before trusting a green run
- [`docs/ARCHITECTURAL-DEFENSE.md`](docs/ARCHITECTURAL-DEFENSE.md) — **why each contested
  decision was made**, as 14 questions with rationale, trade-offs, rejected alternatives and
  evidence: the SharedKernel/Contracts split, repositories over EF Core, the no-events rule,
  the saga's real cost, `Actor`, `Money` exceptions, pod capacity (and the absence of load
  testing), and the reaper at `replicas > 1`
- [`docs/adr/`](docs/adr/) — Architecture Decision Records:
  - 0001 Hexagonal-First · 0002 Direct inter-module calls (the "transactional" claim is
    **withdrawn** — see the ADR) ·
    0003 QueryApplication separation · 0004 Pluggable event dispatching (NoOp) ·
    0005 CrossModuleSaga toolkit · 0006 Reporting/Admin composition + GraphQL ·
    0007 Admin GraphQL API · 0008 Read-Optimized CQS terminology ·
    0009 Supplier–Brand M2M pattern · 0010 Kubernetes deployment strategy
    (phased; the reaper/rate-limit/cache concerns move to platform primitives)

## v2 → v3 differences

| v2 | v3 |
|----|-----|
| Layer-First (Domain/Application/Infrastructure/Presentation) | **Hexagonal-First** (`Application` / `Adapter/Inbound·Outbound` / `QueryApplication`) |
| WolverineFX message bus | **No bus** — direct ACL calls inside an explicit compensation saga (not a shared transaction) |
| Integration events | Contracts DTOs through consumer-owned ports |
| Hard-wired domain event dispatcher | **Pluggable `IEventDispatcher`, currently NoOp** (ADR-0004) |
| "CQRS" label | **Read-Optimized CQS (CQS+)** — same DB, synchronous projections (ADR-0008) |
| CQRS inside one Application | Separate `QueryApplication` per module |

## License

MIT

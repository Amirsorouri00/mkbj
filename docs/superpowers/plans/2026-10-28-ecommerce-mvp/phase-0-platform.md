# Phase 0 — Platform Implementation Plan

> Part of docs/superpowers/plans/2026-10-28-ecommerce-mvp.md — read its Global Constraints, Review Focus and Cross-phase contracts first.

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development (or
> superpowers:executing-plans). Steps use `- [ ]` checkboxes. Run every command from the
> repository root `C:\Users\Daniyal\Documents\mkbj`. Docker must be running from Task 4 on.

**Goal:** Turn the Iran-market template into the platform the MVP is built on: Postgres (Npgsql,
snake_case, `xmin`, minor-unit money) for both DbContexts, the legacy modules gone, three new
building blocks (`Storage`, `Jobs`, `Audit`), the standard error body with `X-Correlation-Id`,
partner/admin principals in Core, a Testcontainers test platform, a `build/` directory that runs the
whole stack, and ADRs/docs that describe the code as it really is. The build and every test suite
are green at the end of every task.

**Spec sections:** §3 (all), §6.3, §6.4, §7, §12, §13 (infrastructure parts: ArchitectureTests
rows, PersistenceContractTests, Testcontainers), §14 phase 0, §17 calls 1, 2, 12, 13, 14, 15, 17.

---

## Decisions this plan makes (read before Task 1)

These resolve spec gaps or conflicts found in the real code. Each one is also recorded in the ADR
named in brackets.

1. **`ITransactionExecutor` is deleted** [ADR-0017]. `src/BuildingBlocks/Persistence/
   EfCoreTransactionExecutor.cs` calls `BeginTransactionAsync(IsolationLevel.Serializable)`. It is
   wrapped in an execution strategy, but the master plan forbids `BeginTransaction` anywhere in
   `src/` and spec §13 makes the source scan a test. `CategoryService` (its only user) becomes
   load → mutate → one save. The races the serializable transaction covered are closed by
   optimistic concurrency instead: every Category write whose validity depends on another
   category's state also *touches* that category (`RecordChildChanged(now)`), so the `xmin` check
   rejects the loser with `CONCURRENCY_CONFLICT`. Unique indexes stay the backstop for names and
   slugs (`23505` → `Error.Conflict`).
2. **Customer is stripped to a compile-only shell.** Its six projects stay (phase 1 rebuilds the
   module inside them) but every source file is deleted except
   `src/Modules/Customer/Installer/ModuleInstaller.cs`, which keeps `AddCustomerModule(IServiceCollection,
   IConfiguration)` as a no-op. Leaving the old code is impossible: `Customer.Application`
   references `Location.Contracts`, which this phase deletes.
3. **`RowVersion` stays on `Entity<TId>` but becomes `uint`**, mapped to Postgres `xmin` (column
   `xmin`, type `xid`) on every entity type by a model convention. That costs nothing, so spec
   §3.3's "keep it only if free" is satisfied. DDD-GUIDELINES "Known inconsistencies" is updated.
4. **Jobs `dedupe_key` is unique only among `Queued` rows** (`UNIQUE (dedupe_key) WHERE status =
   'Queued'`), not table-wide as spec §7 literally says. A table-wide unique key would block a new
   `RefreshStorefrontViews` forever after the first completed one, and would lose a refresh
   requested while one is running. A retry that collides with a queued duplicate is coalesced
   (marked `Completed`, `last_error = 'coalesced into queued duplicate'`). `jobs.recurring` gains a
   `next_run_at` column so the recurring tick is a single guarded `UPDATE`.
5. **Database grants are repeatable post-migration scripts**, not EF migration code. Spec §14 lets
   `init` be regenerated until the run ends; hand-written SQL inside it would be lost on every
   regeneration. `src/Database/Scripts/*.sql` (embedded, idempotent, run in file-name order after
   `MigrateAsync`) are executed by `DatabaseMigrator.MigrateAsync`, which both `--migrate` and the
   test fixture call. Model-expressible things (extensions, indexes, check constraints) stay in the
   model and therefore in `init`.
6. **Storage, Jobs and Audit are each two projects** (`*.Application` = dependency-free
   abstractions, `*.Implementation` = EF/S3 code), mirroring `BuildingBlocks/{Core,Events,Queries}`.
   The master plan names one project each; one project would drag EF Core into every Application
   layer through `Core.Application → AuditActor`. Public type names and namespaces
   (`EwaysVNext.Storage`, `EwaysVNext.Jobs`, `EwaysVNext.Audit`) are exactly the contract's.
7. **Jobs and Audit reach the DbContext as `DbContext`.** `AddUnifiedDatabase` registers
   `services.AddScoped<DbContext>(sp => sp.GetRequiredService<EwaysVNextDbContext>())`, so
   `AddJobs(IConfiguration)` keeps the contract's exact signature with no reference to
   `EwaysVNext.Database` (which would be a cycle).
8. **Docker ignore file is `build/docker/api.Dockerfile.dockerignore`**, not `build/docker/.dockerignore`.
   The build context is the repository root, and BuildKit only honours an ignore file next to the
   Dockerfile when it is named `<Dockerfile>.dockerignore`.
9. **Integration tests apply the migrations** (`DatabaseMigrator`) instead of today's
   `EnsureCreated`, so a broken `init` fails the suite, as AGENTS.md already claims.
10. **`tests/PersistenceContractTests` is created** (spec §13 says "as today"; it does not exist).
    Model-contract tests move there; the translation tests embedded in `tests/UnitTests` switch
    provider in place.
11. **`Error.Validation`'s code becomes `VALIDATION_FAILED`** (spec §6.3). The static member name is
    unchanged; only string literals `"VALIDATION"` change.
12. **InMemory dev fallback is kept** (no connection string in Development). Under InMemory,
    `IJobQueue.EnqueueAsync` logs and drops, and no job worker starts; a startup warning says so.

### Additions to the cross-phase contracts (phase 0 produces these exactly)

```csharp
// EwaysVNext.Persistence
public interface IUnitOfWork {                       // extended
  Task<int> SaveChangesAsync(CancellationToken ct = default);
  int SaveChanges();
  Task<SaveOutcome> TrySaveChangesAsync(CancellationToken ct = default); // xmin mismatch → ConcurrencyConflict; 23505 → UniqueViolation (ChangeTracker cleared); other exceptions propagate
}
public enum SaveOutcome { Saved, UniqueViolation, ConcurrencyConflict }
public sealed class MinorUnitsConverter : ValueConverter<decimal, long> { }   // uses MinorUnits
public static class TransientFailure { public static bool Is(Exception exception); }

// EwaysVNext.Database
public static class DatabaseMigrator { public static Task MigrateAsync(IServiceProvider services, CancellationToken ct = default); }

// EwaysVNext.Core.Application
public interface ICorrelationContext { string CorrelationId { get; } }
public interface IDataSeeder { int Order { get; } Task SeedAsync(CancellationToken ct = default); }
public static class PartnerScopes { /* the five constants */ public static IReadOnlySet<string> All { get; } }

// EwaysVNext.Outcomes
public sealed record Error(string Code, string Message, int HttpStatus, bool Retryable = false) {
  public static readonly Error RateLimited;        // RATE_LIMITED, 429, retryable
  public static readonly Error ServiceUnavailable; // SERVICE_UNAVAILABLE, 503, retryable (transient DB errors)
}

// EwaysVNext.Presentation.Endpoint
public sealed record ErrorResponse(string Code, string Message, bool Retryable, string CorrelationId, object? Details = null, object? Items = null);
public static class CorrelationHeaders { public const string Name = "X-Correlation-Id"; }
public static IResult EndpointResult.Fail(Error error, object? details = null, object? items = null); // now public

// EwaysVNext.Jobs
public sealed record EmptyPayload;               // payload of every recurring job
public abstract record JobOutcome {
  public static JobOutcome Completed { get; }
  public static JobOutcome RetryLater(TimeSpan delay, string reason);
  public static JobOutcome Failed(string reason);
  public sealed record CompletedOutcome : JobOutcome;
  public sealed record RetryLaterOutcome(TimeSpan Delay, string Reason) : JobOutcome;
  public sealed record FailedOutcome(string Reason) : JobOutcome;
}
public sealed class JobLeaseLostException : Exception;   // thrown by JobContext.HeartbeatAsync when the lease was lost

// EwaysVNext.Audit
public interface IAuditVerifier { Task<AuditVerificationResult> VerifyAsync(CancellationToken ct = default); }
public sealed record AuditVerificationResult(bool Intact, long SealedCount, long? FirstMismatchEventId, long UnsealedBelowWatermark);

// Host: --probe <url> exits 0 when the URL answers 2xx, 1 otherwise, 64 on a missing URL.
```

---

## File Structure

| Path | Action | Responsibility |
|---|---|---|
| `.gitignore` | Modify | Also ignore `build/compose/.env`, `TestResults/` |
| `src/Modules/{Payment,Location,Notification}/**`, `tests/PaymentTests/**` | Delete | Removed modules (spec §3.2) |
| `src/Modules/Customer/**` (all `.cs` except Installer/ModuleInstaller.cs) | Delete | Customer becomes a shell for phase 1 |
| `src/Database/{Customer,Location}/**`, `src/Database/Catalog/Configurations/ProductReservationConfiguration.cs` | Delete | Configurations of removed entities |
| `src/Database/Migrations/**`, `src/Authorization/Authorization.Implementation/Persistence/Migrations/**` | Delete, then regenerate `init` | Fresh Postgres history |
| `src/Host/EwaysVNext.Host/ModuleAppSetting/{Customer,Notification,Payment}` | Delete | Settings of removed modules |
| `src/Modules/Catalog/Application/Products/**`, `.../Adapter/Outbound/Persistence/Products/ReservationExpirationService.cs` | Modify / Delete | No stock, no reservations, no reaper |
| `src/BuildingBlocks/Persistence/{ITransactionExecutor,EfCoreTransactionExecutor}.cs` | Delete | Rule 8 |
| `src/BuildingBlocks/Persistence/{IUnitOfWork,EfCoreUnitOfWork,MinorUnits,MinorUnitsConverter,TransientFailure}.cs` | Modify / Create | Save result mapping, money edge, transient detection |
| `src/BuildingBlocks/Domain/Common/Entity.cs` | Modify | `uint RowVersion` |
| `src/Database/{EwaysVNextDbContext,PersistenceDependencyInjection,EwaysVNextDbContextDesignTimeFactory,DatabaseMigrator}.cs` | Modify / Create | Npgsql, naming, xmin, schemas, extensions, migrate + scripts |
| `src/Database/{Jobs,Audit}/Configurations/*.cs`, `src/Database/Scripts/010-audit-append-only.sql` | Create | Table mappings, grants |
| `src/Authorization/Authorization.Implementation/{DependencyInjection.cs,Persistence/AuthorizationDbContextDesignTimeFactory.cs}` | Modify | Npgsql |
| `src/BuildingBlocks/Storage/{Application,Implementation}/` | Create | `IObjectStorage`, S3/MinIO adapter |
| `src/BuildingBlocks/Jobs/{Application,Implementation}/` | Create | Queue, store, worker, recurring |
| `src/BuildingBlocks/Audit/{Application,Implementation}/` | Create | Audit trail, seal job, verifier |
| `src/BuildingBlocks/Core/Application/{AdminActor,PartnerPrincipal,PartnerScopes,ICorrelationContext,IDataSeeder}.cs` | Create | Principals and cross-cutting ports |
| `src/BuildingBlocks/Core/Implementation/CorrelationContext.cs` | Create | Scoped mutable correlation id |
| `src/BuildingBlocks/Outcomes/Result.cs` | Modify | `Retryable`, new presets, `VALIDATION_FAILED` |
| `src/BuildingBlocks/Presentation.Endpoint/{EndpointResult,ErrorResponse,CorrelationIdMiddleware,CorrelationHeaders}.cs`, `GraphQL/GraphQLErrorFilter.cs` | Modify / Create | Error body and correlation |
| `src/Host/EwaysVNext.Host/{Program,HostCommands,Probe}.cs`, `Middleware/GlobalExceptionHandlerMiddleware.cs`, `appsettings*.json` | Modify / Create | Registrations, command flags, error body |
| `tests/ArchitectureTests/{HexagonalBoundaryTests,BuildDirectoryTests,DocumentationTests}.cs` | Create | Executable architecture rules |
| `tests/PersistenceContractTests/**` | Create | Model contract on Npgsql + InMemory |
| `tests/IntegrationTests/PostgresFixture.cs` (replaces `LocalDbFixture.cs`), `Platform/**`, `Host/**` | Create | Testcontainers Postgres + MinIO and platform tests |
| `build/docker/{api.Dockerfile,api.Dockerfile.dockerignore}` | Create | Production image |
| `build/compose/{docker-compose.yml,docker-compose.dev.yml,.env.example,ci.env,caddy/Caddyfile}` | Create | Stack definitions |
| `build/keycloak/{shop-realm.json,backoffice-realm.json}`, `build/postgres/init/01-roles.sql` | Create | Realm stubs, DB roles |
| `Dockerfile`, `Dockerfile.ci`, `docker-compose.yml`, `.dockerignore`, `.dockerignore.ci`, `.env.example` (root) | Delete | Moved into `build/` |
| `.github/workflows/ci.yml`, `EwaysVNext.slnx` | Modify | New projects, Docker build step |
| `docs/adr/0016…0022`, `0015`, `0010`, `AGENTS.md`, `docs/{ONBOARDING,TESTING-GUARANTEES,DDD-GUIDELINES}.md`, `README.md`, `docs/ADDING-CUSTOMER-PAYMENT-MODULES.md` | Create / Modify / Delete | Docs match code |
| `docs/reports/phase-0.md` | Create | Phase report |

---

### Task 0: Baseline repository

**Files:** Modify: `.gitignore`.
**Interfaces:** none.

- [ ] **Step 1: Ignore build outputs and local env.** Append to `.gitignore`:
  `build/compose/.env` and `TestResults/` (`.env` and `bin/`/`obj/` are already ignored).
- [ ] **Step 2: Initialise and verify nothing generated is staged.**
  ```bash
  git init -b main
  git add -A
  git status --short | grep -E '(^|/)(bin|obj)/|\.env$|\.user$' || echo CLEAN
  ```
  Expected: `CLEAN`.
- [ ] **Step 3: Baseline build and test record.** Run `dotnet build EwaysVNext.slnx` and
  `dotnet test --project tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj`. Note the pass
  counts in the commit body (the integration suite needs LocalDB and may be skipped here).
- [ ] **Step 4: Commit.**
  ```bash
  git commit -m "chore: baseline of the EwaysVNext template before the MVP platform phase" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
  ```

---

### Task 1: Remove Payment, Location and Notification; strip Customer to a shell

**Files:**
- Delete: `src/Modules/Payment/` (includes `Tests/`), `src/Modules/Location/`, `src/Modules/Notification/`,
  `tests/PaymentTests/`, `src/Database/Customer/`, `src/Database/Location/`,
  `src/Host/EwaysVNext.Host/ModuleAppSetting/{Customer,Notification,Payment}/`,
  `tests/IntegrationTests/CustomerAddresses/`, `docs/ADDING-CUSTOMER-PAYMENT-MODULES.md`,
  every `.cs` under `src/Modules/Customer/` except `Installer/ModuleInstaller.cs`.
- Delete (unit tests of removed code): `tests/UnitTests/{CompanyVerificationServiceTests,CustomerAddressTests,CustomerAdminListGraphQLSchemaTests,CustomerAdminListQueryTests,CustomerAdminUpdateTests,CustomerDomainTests,CustomerMutationsGraphQLSchemaTests,CustomerRegistrationReviewGraphQLSchemaTests,CustomerRegistrationReviewQueryTests,CustomerServiceTests,CustomerSnowflakeIdSchemaTests,CustomerVerificationDomainTests,LegalInformationDomainTests,LegalInformationMutationsGraphQLSchemaTests,LegalInformationServiceTests,LocationGraphQLSchemaTests,LocationQueriesTests,NotificationServiceTests,TeamMemberDomainTests}.cs`.
- Modify: `EwaysVNext.slnx`, `src/Database/EwaysVNext.Database.csproj`, `src/Database/EwaysVNextDbContext.cs`,
  `src/Host/EwaysVNext.Host/{EwaysVNext.Host.csproj,Program.cs}`,
  `src/Modules/Customer/Application/EwaysVNext.Modules.Customer.Application.csproj`,
  `src/Modules/Customer/Installer/ModuleInstaller.cs`, `tests/UnitTests/EwaysVNext.UnitTests.csproj`.
- Create: `tests/ArchitectureTests/HexagonalBoundaryTests.cs`, `docs/adr/0022-remove-iran-specific-modules.md`.

**Interfaces:**
- Produces: `public static IServiceCollection AddCustomerModule(this IServiceCollection services, IConfiguration configuration)` (no-op, XML doc "rebuilt in phase 1"); `HexagonalBoundaryTests.ModuleNames`.

- [ ] **Step 1: Write the failing test** in `tests/ArchitectureTests/HexagonalBoundaryTests.cs`
  (namespace `EwaysVNext.ArchitectureTests`, reuse the `FindSolutionDirectory` walk-up from
  `CategoryArchitectureTests`):
  ```csharp
  public sealed class HexagonalBoundaryTests
  {
      // Final set (later phases append as they create each module):
      // Catalog, Customer, Partners, Settings, Pricing, Sales, Sourcing, Admin.
      public static readonly string[] ModuleNames = ["Catalog", "Customer"];

      [Fact]
      public void Src_Modules_Are_Exactly_ModuleNames() =>
          Directory.EnumerateDirectories(Path.Combine(Root, "src", "Modules"))
              .Select(Path.GetFileName).Should().BeEquivalentTo(ModuleNames);
  }
  ```
- [ ] **Step 2: Run, expect FAIL** (Location, Notification, Payment present):
  `dotnet test --project tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj --filter-class "*.HexagonalBoundaryTests"`
- [ ] **Step 3: Delete** every path listed under *Delete*; remove their `<Project>`/`<Folder>`
  entries from `EwaysVNext.slnx` (Payment, Notification, Location, PaymentTests, Payment GatewayTests).
- [ ] **Step 4: Rewire.**
  - `EwaysVNext.Database.csproj`: drop the Location and Notification project references.
  - `EwaysVNextDbContext.cs`: drop the `Customers`, `TeamMembers`, `Provinces`, `Addresses` DbSets.
  - `Customer.Application.csproj`: drop the `Location.Contracts` reference.
  - `ModuleInstaller.cs` (Customer): body `=> services;`.
  - Host csproj: drop the Payment, Notification, Location installer references. `Program.cs`:
    `AddModuleConfiguration(["catalog"], …)`; module chain becomes
    `.AddCatalogModule().AddCustomerModule(builder.Configuration)`; drop the removed `using`s.
  - `UnitTests.csproj`: drop the Location and Notification references.
- [ ] **Step 5: Build until clean.** `dotnet build EwaysVNext.slnx`. For each remaining compile
  error in `tests/` that names a deleted namespace, delete only the test cases that exercise deleted
  code (e.g. Customer/Location registrations in `GraphQLBootstrapTests`,
  `CategoryProductionCompositionTests`). Do not touch Catalog behaviour.
- [ ] **Step 6: Write `docs/adr/0022-remove-iran-specific-modules.md`** (sections Status: Accepted
  2026-10-28, Context, Decision = spec §3.2 table verbatim in substance, Consequences: Customer
  shell until phase 1, ADR-0015 superseded by ADR-0018, no data migration).
- [ ] **Step 7: Run, expect PASS:** the Step 2 command, then
  `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj`.
- [ ] **Step 8: Commit.**
  ```bash
  git add -A
  git commit -m "refactor: remove Payment, Location, Notification; strip Customer to a shell (ADR-0022)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
  ```

---

### Task 2: Remove Catalog stock, reservations and the reaper

**Files:**
- Delete: `src/Modules/Catalog/Application/Products/Domain/ProductReservation.cs`,
  `.../Products/Ports/Inbound/ReservationSnapshot.cs`, `.../Products/Validation/ReserveStockValidator.cs`,
  `src/Modules/Catalog/Adapter/Outbound/Persistence/Products/ReservationExpirationService.cs`,
  `src/Database/Catalog/Configurations/ProductReservationConfiguration.cs`, `tests/UnitTests/ProductReservationTests.cs`.
- Modify: `.../Products/Domain/{Product,ProductEvents,ProductMessages}.cs`,
  `.../Products/Ports/Inbound/{IProductService,ICatalogAdminService}.cs`,
  `.../Products/Ports/Outbound/IProductRepository.cs`, `.../Products/Services/{ProductService,CatalogAdminService}.cs`,
  `.../Products/DependencyInjection.cs`, `src/Modules/Catalog/Adapter/Outbound/{DependencyInjection.cs,Persistence/Products/ProductRepository.cs}`,
  `src/Modules/Catalog/Adapter/Inbound/Rest/{Dtos/ProductDtos.cs,Endpoints/CatalogEndpoints.cs,Products/Dtos/ProductDtos.cs,Products/Endpoints/ProductEndpoints.cs}`,
  `src/Modules/Catalog/QueryApplication/Products/ProductQueryService.cs`,
  `src/Database/Catalog/Configurations/Products/ProductConfiguration.cs`, `tests/UnitTests/SnowflakeIdJsonTests.cs`.
- Test: `tests/UnitTests/Catalog/ProductShapeTests.cs`.

**Interfaces:** Produces `Product.Create(long id, Sku sku, string name, Money price, DateTimeOffset now, string? description = null)` (the `initialStock` parameter is gone).

- [ ] **Step 1: Write the failing test**
  ```csharp
  [Fact]
  public void Product_Exposes_No_Stock_Or_Reservations() =>
      typeof(Product).GetProperties().Select(p => p.Name).Should()
          .NotContain(["Stock", "ReservedStock", "AvailableStock", "Reservations"]);

  [Fact]
  public void Catalog_Registers_No_Hosted_Service()
  {
      var services = new ServiceCollection().AddCatalogModule();
      services.Should().NotContain(d => d.ServiceType == typeof(IHostedService));
  }
  ```
- [ ] **Step 2: Run, expect FAIL:** `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "*.ProductShapeTests"`
- [ ] **Step 3: Implement.** Delete the files; remove `Stock`, `ReservedStock`, `AvailableStock`,
  `_reservations`, `AdjustStock`, `ReserveStock`, `ReleaseReservation`, `CommitReservation`,
  `RevertCommitment`, `ReleaseExpiredReservations`, `PruneCommittedReservations` and their events and
  messages; remove the reservation/stock operations from the ports, services, endpoints and DTOs;
  remove `AddHostedService<ReservationExpirationService>()`; remove the two stock check constraints
  and stock property mappings; delete the `ReservationSnapshot` case from `SnowflakeIdJsonTests`.
- [ ] **Step 4: Run, expect PASS:** Step 2 command, then the whole UnitTests project.
- [ ] **Step 5: Commit** `git add -A && git commit -m "refactor(catalog): remove stock, reservations and the TTL reaper (PRD §2.1)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 3: Executable hexagonal boundary rules

**Files:** Modify: `tests/ArchitectureTests/HexagonalBoundaryTests.cs`.
**Interfaces:** Produces `HexagonalBoundaryTests.SanctionedReferences` (`TheoryData<string,string>`, rows
`(consumerModule, providerModule)` meaning `EwaysVNext.Modules.{consumer}.Adapter.Outbound` may reference
`EwaysVNext.Modules.{provider}.Application`). Phase 0 has **no rows**; each later phase adds its row in
the task that introduces the reference. The final rows are (spec §3.6): `(Sales,Catalog)`,
`(Sales,Pricing)`, `(Sales,Customer)`, `(Sales,Settings)`, `(Pricing,Catalog)`, `(Pricing,Settings)`,
`(Sourcing,Catalog)`, `(Sourcing,Pricing)`. Later phases add rows as `{ "Consumer", "Provider" }`
entries in `SanctionedReferences`, never as `[InlineData]`. Admin is handled by a built-in rule: `EwaysVNext.Modules.Admin.Adapter.Outbound`
may reference any other module's `QueryApplication` or `Application`.

- [ ] **Step 1: Write the failing tests** (module assemblies are found as
  `EwaysVNext.Modules.{module}.*.dll` in `AppContext.BaseDirectory`; references are read with
  `Assembly.GetReferencedAssemblies()`):
  ```csharp
  public static TheoryData<string> Modules => new(ModuleNames);
  public static TheoryData<string, string> SanctionedReferences => new();

  [Theory, MemberData(nameof(Modules))]
  public void Module_References_No_Other_Module_Unless_Sanctioned(string module) =>
      ForbiddenCrossModuleReferences(module).Should().BeEmpty();

  [Theory(SkipTestWithoutData = true), MemberData(nameof(SanctionedReferences))]
  public void Sanctioned_Row_Is_Well_Formed(string consumer, string provider)
  {
      ModuleNames.Should().Contain([consumer, provider]);
      consumer.Should().NotBe(provider);
      provider.Should().NotBe("Partners");
      consumer.Should().NotBeOneOf("Catalog", "Customer", "Settings", "Partners");
  }

  [Theory, MemberData(nameof(Modules))]
  public void Application_Depends_On_No_Adapter_And_No_EfCore(string module) =>
      Types.InAssembly(Load($"EwaysVNext.Modules.{module}.Application")).ShouldNot()
          .HaveDependencyOnAny($"EwaysVNext.Modules.{module}.Adapter", "Microsoft.EntityFrameworkCore")
          .GetResult().IsSuccessful.Should().BeTrue();

  [Theory, MemberData(nameof(Modules))]
  public void Application_Services_Are_Sealed(string module) =>
      Types.InAssembly(Load($"EwaysVNext.Modules.{module}.Application")).That().AreClasses()
          .And().AreNotAbstract().And().HaveNameEndingWith("Service")
          .Should().BeSealed().GetResult().IsSuccessful.Should().BeTrue();

  [Theory, MemberData(nameof(Modules))]
  public void QueryApplication_Never_Writes(string module) =>
      SourceFiles($"src/Modules/{module}/QueryApplication")
          .Where(f => Regex.IsMatch(File.ReadAllText(f), @"SaveChanges|IUnitOfWork|ExecuteUpdate|ExecuteDelete"))
          .Should().BeEmpty();

  [Fact]
  public void No_Wolverine_Anywhere() =>
      Directory.EnumerateFiles(Root, "*.*", SearchOption.AllDirectories)
          .Where(f => f.EndsWith(".csproj") || f.EndsWith(".props")).Where(NotInBinObj)
          .Where(f => File.ReadAllText(f).Contains("Wolverine")).Should().BeEmpty();
  ```
  `ForbiddenCrossModuleReferences(module)` returns `"{assembly} -> {reference}"` for every reference
  to `EwaysVNext.Modules.{other}.*` (`other != module`) that is not (a) `{module}.Adapter.Outbound →
  {other}.Application` with `(module, other)` in `SanctionedReferences`, or (b) `module == "Admin"`,
  assembly `Admin.Adapter.Outbound`, target `.QueryApplication` or `.Application`.
- [ ] **Step 2: Mutation check, expect FAIL.** Temporarily add
  `<ProjectReference Include="..\..\..\Customer\Application\EwaysVNext.Modules.Customer.Application.csproj" />`
  to the Catalog Adapter.Outbound csproj and use one Customer type there; run
  `dotnet test --project tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj --filter-class "*.HexagonalBoundaryTests"`.
  Expected: `Module_References_No_Other_Module_Unless_Sanctioned(Catalog)` fails. Revert.
- [ ] **Step 3: Make it pass.** If `Application_Depends_On_No_Adapter_And_No_EfCore(Catalog)` or
  `Application_Services_Are_Sealed(Catalog)` fails on existing code, fix the code (seal the class;
  move the EF-typed usage into `Catalog.Adapter.Outbound` behind the existing repository port), not
  the test.
- [ ] **Step 4: Run, expect PASS** (Step 2 command).
- [ ] **Step 5: Commit** `git add -A && git commit -m "test(arch): executable hexagonal boundary rules and sanctioned-reference theory" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 4: Postgres for both DbContexts, Testcontainers platform, fresh `init`

**Files:**
- Modify: `Directory.Packages.props` (remove `Microsoft.EntityFrameworkCore.SqlServer`,
  `Microsoft.Data.SqlClient`; add `Npgsql.EntityFrameworkCore.PostgreSQL`, `EFCore.NamingConventions`,
  `Testcontainers.PostgreSql`, `Testcontainers.Minio`, `AWSSDK.S3` — each at the latest stable version
  the configured feed returns for `dotnet package search <id> --exact-match`, `10.0.x` for the two EF
  packages), `src/BuildingBlocks/Persistence/EwaysVNext.Persistence.csproj`,
  `src/Database/EwaysVNext.Database.csproj`, `src/Modules/{Catalog,Customer}/Adapter/Outbound/*.csproj`,
  `src/Authorization/Authorization.Implementation/Authorization.Implementation.csproj`,
  `src/BuildingBlocks/Domain/Common/Entity.cs`, `src/BuildingBlocks/Persistence/EfCoreTransactionExecutor.cs`
  (only `SqlException {Number: 2601 or 2627}` → `DbException { SqlState: "23505" }`; deleted in Task 5),
  `src/BuildingBlocks/Persistence/ModuleDbContextBase.cs` (delete if `grep -rn ModuleDbContextBase src tests`
  shows only itself and `tests/UnitTests/EventDispatchingTests.cs`; otherwise switch it to `uint`),
  `src/Database/{EwaysVNextDbContext,PersistenceDependencyInjection,EwaysVNextDbContextDesignTimeFactory}.cs`,
  every file under `src/Database/Catalog/Configurations/`,
  `src/Authorization/Authorization.Implementation/{DependencyInjection.cs,Persistence/AuthorizationDbContextDesignTimeFactory.cs}`,
  `src/Modules/Catalog/Adapter/Outbound/Persistence/Categories/{CategoryRepository,CategoryProductRepository}.cs`
  (delete the commented-out SQL Server blocks), `tests/ArchitectureTests/CategoryArchitectureTests.cs`
  (delete `Hierarchy_loading_has_cycle_safe_single_roundtrip_provider_paths`: it asserts on comments),
  `src/Host/EwaysVNext.Host/{Program.cs,appsettings.json,appsettings.Development.json}`,
  `tests/UnitTests/{AttributeBlueprintFilteringTests,CategoryQueryTests,CategoryProjectionGraphQLTests,CategoryDomainTests}.cs`,
  `tests/IntegrationTests/**` (`[Collection(LocalDbSuite.Name)]` → `[Collection(PostgresSuite.Name)]`),
  `tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj`, `EwaysVNext.slnx`.
- Delete: `src/Database/Migrations/*`, `src/Authorization/Authorization.Implementation/Persistence/Migrations/*`,
  `tests/IntegrationTests/LocalDbFixture.cs`, `tests/UnitTests/CategoryPersistenceModelTests.cs` (moved).
- Create: `src/Database/DatabaseMigrator.cs`, `build/postgres/init/01-roles.sql`,
  `tests/IntegrationTests/PostgresFixture.cs`, `tests/IntegrationTests/Platform/DatabaseTests.cs`,
  `tests/PersistenceContractTests/{EwaysVNext.PersistenceContractTests.csproj,ModelContractTests.cs,CategoryPersistenceModelTests.cs,Providers.cs}`,
  `docs/adr/0017-postgres-and-minio.md`, then regenerated `init` migrations for both contexts.

**Interfaces:**
- Produces: `public uint RowVersion { get; protected set; }` on `Entity<TId>`;
  `DatabaseMigrator.MigrateAsync(IServiceProvider services, CancellationToken ct = default)` (applies
  `EwaysVNextDbContext` migrations, then every embedded `EwaysVNext.Database.Scripts.*.sql` in ordinal
  name order, each through `CreateExecutionStrategy().ExecuteAsync`; the scripts folder is empty until Task 12);
  `PostgresFixture` with `string AppConnectionString`, `string MigratorConnectionString`,
  `string SuperuserConnectionString`, `string MinioEndpoint`, `WebApplicationFactory<Program> CreateFactory(IReadOnlyDictionary<string,string?>? overrides = null)`;
  `PostgresSuite.Name = "Platform"`; DB roles `eways_app`, `eways_migrator`.

- [ ] **Step 1: Write the failing model-contract tests** in `tests/PersistenceContractTests`
  (new xUnit v3 Exe project, references `src/Database` and `Authorization.Implementation`, packages
  `xunit.v3`, `AwesomeAssertions`, `Microsoft.EntityFrameworkCore.InMemory`; add to the slnx under
  `/tests/PersistenceContractTests/`). `Providers.Npgsql<TContext>()` builds options with
  `UseNpgsql("Host=127.0.0.1;Port=1;Database=contract;Username=contract;Password=contract;Timeout=1").UseSnakeCaseNamingConvention()`
  (a refused port: translation works, execution cannot); `Providers.InMemory<TContext>()` uses a
  fresh InMemory name.
  ```csharp
  [Fact] public void Every_Name_Is_Snake_Case()          // tables, columns, indexes, keys, FKs, check constraints, both contexts
      => NonSnakeCaseNames().Should().BeEmpty();        // regex ^[a-z][a-z0-9_]*$
  [Fact] public void Every_Entity_Uses_Xmin_As_RowVersion()
  {
      foreach (var p in Model(EwaysNpgsql).GetEntityTypes().Select(e => e.FindProperty("RowVersion")).OfType<IProperty>())
      { p.GetColumnName().Should().Be("xmin"); p.GetColumnType().Should().Be("xid"); p.IsConcurrencyToken.Should().BeTrue(); }
  }
  [Fact] public void Aggregate_Root_Keys_Are_Never_Generated()   // every IAggregateRoot CLR type: Id ValueGenerated.Never
  [Fact] public void Extensions_Are_Declared() =>
      Model(EwaysNpgsql).GetPostgresExtensions().Select(e => e.Name).Should().Contain(["pg_trgm", "unaccent"]);
  [Fact] public void Every_Table_Lives_In_A_Named_Schema() =>
      Model(EwaysNpgsql).GetEntityTypes().Select(e => e.GetSchema()).Should().OnlyContain(s => s == "catalog");
  [Fact] public void InMemory_Model_Disables_RowVersion_Concurrency() // InMemory context: RowVersion not a concurrency token
  ```
  Move `tests/UnitTests/CategoryPersistenceModelTests.cs` here unchanged in intent, with
  `MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL")` and Postgres SQL in its assertions.
  (`Every_Table_Lives_In_A_Named_Schema` widens to `catalog, jobs, audit` in Tasks 10 and 12, and
  to each new module's schema in later phases.)
- [ ] **Step 2: Run, expect FAIL** (SQL Server provider, `dbo`, PascalCase):
  `dotnet test --project tests/PersistenceContractTests/EwaysVNext.PersistenceContractTests.csproj`
- [ ] **Step 3: Switch the provider.**
  - `AddUnifiedDatabase`: `options.UseNpgsql(cs, npgsql => { npgsql.MigrationsAssembly(...);
    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "public"); npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null); }).UseSnakeCaseNamingConvention();`
    InMemory branch unchanged; add `services.AddScoped<DbContext>(sp => sp.GetRequiredService<EwaysVNextDbContext>());`.
  - Design-time factory: Npgsql with the same options; when no connection string is configured use
    `Host=localhost;Database=eways_design;Username=design;Password=design` (`migrations add` never connects).
  - `EwaysVNextDbContext.OnModelCreating`: `HasPostgresExtension("pg_trgm")`,
    `HasPostgresExtension("unaccent")`; schema = lower-cased module name for `*.Modules.{X}.*`,
    `"jobs"` for `EwaysVNext.Jobs*`, `"audit"` for `EwaysVNext.Audit*`, otherwise throw
    `InvalidOperationException` naming the CLR type (no silent `dbo`/`public`); remove
    `HasDefaultSchema("dbo")`. For every entity type with a `RowVersion` property: Npgsql →
    `HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken()`;
    InMemory → keep today's behaviour (not a token, `ValueGenerated.Never`). Remove the per-config
    `IsRowVersion()` calls.
  - `Entity<TId>.RowVersion` becomes `uint`; `CategoryDomainTests` asserts `.Be(0u)`.
  - Catalog configurations: remove explicit PascalCase `ToTable`/`HasColumnName`/`HasDatabaseName`
    names (let the convention name them) and rewrite filters/checks in Postgres syntax, e.g.
    `.HasFilter("status <> 2")`, `.HasFilter("removed_at IS NULL AND is_primary")`, `.HasFilter("deleted_at IS NULL")`.
  - Authorization: same Npgsql options with `MigrationsHistoryTable("__AuthorizationMigrationsHistory", "authorization")`
    and `UseSnakeCaseNamingConvention()`, in both DI and its design-time factory.
  - `appsettings.json`: `"ConnectionStrings": { "Default": "" }` (the committed password goes).
    `appsettings.Development.json`: `"Default": "Host=localhost;Port=5432;Database=eways;Username=eways_app;Password=dev-app-password"`
    (dev-only value that matches `docker-compose.dev.yml` defaults).
  - `Program.cs` `--migrate`: `await DatabaseMigrator.MigrateAsync(scope.ServiceProvider)`, then the
    existing Authorization migrate block.
  - UnitTests translation tests: replace each `UseSqlServer("…")` with the refused-port Npgsql
    options above; update SQL-text assertions to Postgres syntax, keeping what each test pins.
- [ ] **Step 4: Regenerate migrations.**
  ```bash
  dotnet ef migrations add init -p src/Database -s src/Host/EwaysVNext.Host -c EwaysVNextDbContext -o Migrations
  dotnet ef migrations add init -p src/Authorization/Authorization.Implementation -s src/Host/EwaysVNext.Host -c AuthorizationDbContext -o Persistence/Migrations
  ```
  Delete `IdentityColumnMigrationExtensions.cs` with the old migrations (Step "Delete" above).
- [ ] **Step 5: Run, expect PASS:** Step 2 command and `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj`.
- [ ] **Step 6: Roles script** `build/postgres/init/01-roles.sql` (psql ≥15 `\getenv`, so no
  password is ever in the file):
  ```sql
  \getenv app_password APP_DB_PASSWORD
  \getenv migrator_password MIGRATOR_DB_PASSWORD
  \getenv db_name POSTGRES_DB
  CREATE ROLE eways_migrator LOGIN PASSWORD :'migrator_password';
  CREATE ROLE eways_app LOGIN PASSWORD :'app_password';
  ALTER DATABASE :"db_name" OWNER TO eways_migrator;
  GRANT CONNECT ON DATABASE :"db_name" TO eways_app;
  ALTER DEFAULT PRIVILEGES FOR ROLE eways_migrator GRANT USAGE ON SCHEMAS TO eways_app;
  ALTER DEFAULT PRIVILEGES FOR ROLE eways_migrator GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO eways_app;
  ALTER DEFAULT PRIVILEGES FOR ROLE eways_migrator GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO eways_app;
  ```
- [ ] **Step 7: Write the failing integration test** `tests/IntegrationTests/Platform/DatabaseTests.cs`
  (`[Collection(PostgresSuite.Name)]`):
  ```csharp
  [Fact] public async Task Migrations_Applied_With_Extensions()
  {   // as eways_app
      (await Scalar<long>(f.AppConnectionString, "select count(*) from pg_extension where extname in ('pg_trgm','unaccent')")).Should().Be(2);
      (await Scalar<long>(f.AppConnectionString, "select count(*) from public.\"__EFMigrationsHistory\"")).Should().BeGreaterThan(0);
  }
  [Fact] public async Task App_Role_Can_Write_Catalog_Tables_But_Owns_Nothing() =>
      (await Scalar<long>(f.AppConnectionString, "select count(*) from pg_tables where tableowner = 'eways_app'")).Should().Be(0);
  ```
- [ ] **Step 8: Run, expect FAIL** (no fixture yet):
  `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "*.DatabaseTests"`
- [ ] **Step 9: Implement `PostgresFixture`** (replaces `LocalDbFixture`; keep its test JWT scheme
  wiring verbatim). Containers start in parallel:
  `new PostgreSqlBuilder().WithImage("postgres:17-alpine").WithDatabase("eways").WithUsername("postgres").WithPassword("postgres").WithEnvironment("APP_DB_PASSWORD","app").WithEnvironment("MIGRATOR_DB_PASSWORD","migrator").WithResourceMapping(new FileInfo(<repo>/build/postgres/init/01-roles.sql), "/docker-entrypoint-initdb.d/")`
  and `new MinioBuilder().WithImage("minio/minio:RELEASE.2025-04-22T22-12-26Z")`. `InitializeAsync`:
  create buckets `catalog-media`, `import-staging`, `documents` with an `AmazonS3Client`
  (`ForcePathStyle = true`); build a factory with `ConnectionStrings:Default = MigratorConnectionString`,
  call `DatabaseMigrator.MigrateAsync` and migrate `AuthorizationDbContext`. `CreateFactory` sets
  `ConnectionStrings:Default = AppConnectionString`, `Minio:Endpoint`, `Minio:PublicEndpoint`,
  `Minio:AccessKey`, `Minio:SecretKey`, `Jobs:Mode = Disabled`, then applies `overrides`.
  `DisposeAsync` disposes both containers.
  `[CollectionDefinition(PostgresSuite.Name)] public sealed class PostgresSuite : ICollectionFixture<PostgresFixture> { public const string Name = "Postgres"; }`.
- [ ] **Step 10: Run, expect PASS:** Step 8 command, then the whole IntegrationTests project
  (the existing Category GraphQL tests must pass on Postgres; a "Cannot write DateTimeOffset with
  Offset" failure is fixed at the value's source by using UTC, never with a converter).
- [ ] **Step 11: Write `docs/adr/0017-postgres-and-minio.md`** (Accepted): provider, snake_case,
  `xmin` via `uint RowVersion`, `amount_minor bigint` + `currency char(3)` with `MinorUnits` at the
  edge, extensions, the `IObjectStorage` port and buckets, app/migrator roles, repeatable scripts
  (decision 5), Testcontainers replaces LocalDB, `ITransactionExecutor` removal (decision 1), the
  InMemory dev fallback (decision 12). Consequence to flag for phase 3: `REFRESH MATERIALIZED VIEW`
  needs ownership, so the refresh must run as a role that owns the views.
- [ ] **Step 12: Commit** `git add -A && git commit -m "feat(persistence): switch both DbContexts to Postgres; Testcontainers platform; fresh init (ADR-0017)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 5: Delete `ITransactionExecutor`; single-save Category writes

**Files:**
- Delete: `src/BuildingBlocks/Persistence/{ITransactionExecutor,EfCoreTransactionExecutor}.cs`.
- Modify: `src/BuildingBlocks/Persistence/{IUnitOfWork,EfCoreUnitOfWork}.cs`,
  `src/BuildingBlocks/DependencyInjection/DependencyInjection.cs` (drop the executor registration),
  `src/Modules/Catalog/Application/Categories/Domain/Category.cs`,
  `src/Modules/Catalog/Application/Categories/Services/CategoryService.cs`, `tests/UnitTests/CategoryServiceTests.cs`.
- Test: `tests/ArchitectureTests/HexagonalBoundaryTests.cs`, `tests/UnitTests/CategoryServiceTests.cs`,
  `tests/IntegrationTests/Platform/UnitOfWorkTests.cs`.

**Interfaces:**
- Produces: `Task<SaveOutcome> IUnitOfWork.TrySaveChangesAsync(CancellationToken ct = default)` and
  `enum SaveOutcome { Saved, UniqueViolation, ConcurrencyConflict }` —
  `DbUpdateConcurrencyException` → `ConcurrencyConflict`; `DbUpdateException` whose inner
  `DbException.SqlState == "23505"` → `UniqueViolation` (after `ChangeTracker.Clear()`); anything
  else propagates. Every hand-written `IUnitOfWork` fake under `tests/` implements it by delegating
  to `SaveChangesAsync` and returning `Saved`. Domain events are
  dispatched only on success, as in `SaveChangesAsync`.
- Produces: `public void Category.RecordChildChanged(DateTimeOffset now)` (sets `UpdatedAt = now`).

- [ ] **Step 1: Write the failing tests.**
  ```csharp
  // ArchitectureTests
  [Fact] public void No_User_Initiated_Transactions_In_Src() =>
      SourceFiles("src").Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
          .Where(f => Regex.IsMatch(File.ReadAllText(f), @"BeginTransaction|UseTransaction"))
          .Should().BeEmpty("rule 8: EnableRetryOnFailure cannot replay a user-initiated transaction");

  // UnitTests/CategoryServiceTests (InMemory, FixedClock at TestTime.Now)
  [Fact] public async Task Create_Touches_The_Parent()      // parent.UpdatedAt == clock.UtcNow after CreateAsync(child of parent)
  [Fact] public async Task Move_Touches_The_New_Parent()     // newParent.UpdatedAt == clock.UtcNow after MoveAsync
  [Fact] public async Task Activation_Touches_The_Parent()   // SetStatusAsync(child, Active) → parent.UpdatedAt == clock.UtcNow

  // IntegrationTests/Platform/UnitOfWorkTests
  [Fact] public async Task Stale_Save_Returns_ConcurrencyConflict()
  {   // scope A loads category X; scope B renames X and saves; scope A renames X and TrySaveChangesAsync
      outcome.Should().Be(SaveOutcome.ConcurrencyConflict);
  }
  [Fact] public async Task Unique_Violation_Returns_UniqueViolation()   // two categories, same slug, inserted from two scopes → second TrySave = SaveOutcome.UniqueViolation
  ```
- [ ] **Step 2: Run, expect FAIL** (scan finds `BeginTransactionAsync`; the touch tests fail; `TrySaveChangesAsync` missing — compile error counts as FAIL):
  `dotnet test --project tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj --filter-method "*No_User_Initiated_Transactions_In_Src"`
  and `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "*.CategoryServiceTests"`.
- [ ] **Step 3: Implement.** Delete both executor files. In `CategoryService`, unwrap every
  `ExecuteSerializableAsync(async token => …)` body into the method itself and replace its
  `SaveChangesAsync` with `var saved = await _unitOfWork.TrySaveChangesAsync(ct);` mapping `ConcurrencyConflict` → `Error.ConcurrencyConflict` and `UniqueViolation` → `Error.Conflict`.
  Before saving: `CreateAsync` calls `parent?.RecordChildChanged(now)`; `MoveAsync` calls
  `newParent?.RecordChildChanged(now)`; a transition to `Active` (in `UpdateAsync` and
  `SetStatusAsync`) calls `parent?.RecordChildChanged(now)`. In `AssignProductAsync` a
  `SaveOutcome.UniqueViolation` from the save means the assignment already exists → `Result.Success()`.
  Remove `InMemoryTransactionExecutor` and its registration from `CategoryServiceTests`.
- [ ] **Step 4: Run, expect PASS:** Step 2 commands, then
  `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "*.UnitOfWorkTests"`.
- [ ] **Step 5: Mutation check.** Remove the `RecordChildChanged` call from `MoveAsync`; expect
  `Move_Touches_The_New_Parent` to FAIL; restore.
- [ ] **Step 6: Commit** `git add -A && git commit -m "refactor(persistence): remove serializable transaction executor; optimistic single-save Category writes" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 6: `MinorUnits` and minor-unit price storage

**Files:** Create: `src/BuildingBlocks/Persistence/{MinorUnits,MinorUnitsConverter}.cs`,
`tests/UnitTests/MinorUnitsTests.cs`. Modify: `src/Database/Catalog/Configurations/Products/ProductConfiguration.cs`,
`tests/PersistenceContractTests/ModelContractTests.cs`; regenerate `init` (delete
`src/Database/Migrations/*`, rerun the Task 4 Step 4 `EwaysVNextDbContext` command).

**Interfaces:** Produces `public static class MinorUnits { public static long ToMinor(decimal amount); public static decimal FromMinor(long minor); }`
(throws `ArgumentException` when `amount` has more than 2 decimal places, `OverflowException` when
`amount * 100` does not fit in `long`) and `MinorUnitsConverter`.

- [ ] **Step 1: Write the failing tests.**
  ```csharp
  [Theory]
  [InlineData("10.00", 1000L)] [InlineData("17.43", 1743L)] [InlineData("500.00", 50000L)]
  [InlineData("12845.99", 1284599L)] [InlineData("1000000.00", 100000000L)] [InlineData("0.01", 1L)] [InlineData("-5.00", -500L)]
  public void ToMinor_Is_Exact(string amount, long minor) =>
      MinorUnits.ToMinor(decimal.Parse(amount, CultureInfo.InvariantCulture)).Should().Be(minor);

  [Fact] public void ToMinor_Rejects_Three_Decimals() =>
      FluentActions.Invoking(() => MinorUnits.ToMinor(1.005m)).Should().Throw<ArgumentException>();
  [Fact] public void ToMinor_Rejects_Overflow() =>
      FluentActions.Invoking(() => MinorUnits.ToMinor(decimal.MaxValue)).Should().Throw<OverflowException>();
  [Fact] public void FromMinor_Round_Trips() => MinorUnits.FromMinor(1284599L).Should().Be(12845.99m);

  // ModelContractTests
  [Fact] public void Product_Price_Is_Stored_In_Minor_Units()
  {   // owned Price: Amount → column price_amount_minor, type bigint; Currency → price_currency, type character(3)
  }
  ```
- [ ] **Step 2: Run, expect FAIL:**
  `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "*.MinorUnitsTests"` and
  `dotnet test --project tests/PersistenceContractTests/EwaysVNext.PersistenceContractTests.csproj --filter-method "*Product_Price_Is_Stored_In_Minor_Units"`
- [ ] **Step 3: Implement.** `ToMinor` uses `checked` arithmetic and the check
  `amount != decimal.Round(amount, 2)`. In `ProductConfiguration`: `Amount` →
  `.HasColumnName("price_amount_minor").HasConversion(new MinorUnitsConverter())`, `Currency` →
  `.HasColumnName("price_currency").HasColumnType("char(3)")`. Regenerate `init`.
- [ ] **Step 4: Run, expect PASS** (Step 2 commands, then full IntegrationTests).
- [ ] **Step 5: Commit** `git add -A && git commit -m "feat(persistence): MinorUnits; money stored as amount_minor bigint + char(3)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 7: Error body, `Retryable`, correlation id, rate-limit and transient responses

**Files:**
- Modify: `src/BuildingBlocks/Outcomes/Result.cs`, `src/BuildingBlocks/Presentation.Endpoint/EndpointResult.cs`,
  `src/BuildingBlocks/Presentation.Endpoint/GraphQL/{GraphQLErrorFilter.cs,Extensions/ErrorGraphQLExtensions.cs}`,
  `src/Host/EwaysVNext.Host/{Program.cs,Middleware/GlobalExceptionHandlerMiddleware.cs}`, every file
  with the literal `"VALIDATION"` (listed by `grep -rln '"VALIDATION"' src tests`).
- Create: `src/BuildingBlocks/Presentation.Endpoint/{ErrorResponse,CorrelationHeaders,CorrelationIdMiddleware}.cs`,
  `src/BuildingBlocks/Core/Application/ICorrelationContext.cs`, `src/BuildingBlocks/Core/Implementation/CorrelationContext.cs`,
  `src/BuildingBlocks/Persistence/TransientFailure.cs`.
- Test: `tests/UnitTests/{ResultHttpSemanticsTests,ErrorBodyTests,CorrelationIdMiddlewareTests,TransientFailureTests,GraphQLErrorFilterTests}.cs`,
  `tests/IntegrationTests/Platform/ErrorBodyEndToEndTests.cs`.

**Interfaces:** Produces the `Error`, `ErrorResponse`, `CorrelationHeaders`, `ICorrelationContext`,
`TransientFailure` and `EndpointResult.Fail` signatures in "Additions" above, plus
`public sealed class CorrelationContext : ICorrelationContext { public string CorrelationId { get; set; } }`
(scoped; registered by `AddUserProfileService()`), and
`app.UseMiddleware<CorrelationIdMiddleware>()` placed first in the pipeline.

- [ ] **Step 1: Write the failing tests.**
  ```csharp
  [Fact] public void Retryable_Defaults_To_False() => new Error("X_NOT_FOUND", "x").Retryable.Should().BeFalse();
  [Fact] public void Retryable_Presets()
  {
      Error.RateLimited.Should().Be(new Error("RATE_LIMITED", Error.RateLimited.Message, 429, true));
      Error.ServiceUnavailable.Should().Be(new Error("SERVICE_UNAVAILABLE", Error.ServiceUnavailable.Message, 503, true));
      Error.Validation.Code.Should().Be("VALIDATION_FAILED");
  }

  [Fact] public async Task Fail_Writes_The_Standard_Body()
  {   // DefaultHttpContext { TraceIdentifier = "corr-1" }, body MemoryStream, RequestServices with JSON options
      await EndpointResult.Fail(new Error("PRICE_CHANGED", "Prices changed.")).ExecuteAsync(ctx);
      ctx.Response.StatusCode.Should().Be(400);
      Json(ctx).Should().Be("""{"code":"PRICE_CHANGED","message":"Prices changed.","retryable":false,"correlationId":"corr-1"}""");
  }
  [Fact] public async Task Fail_Includes_Details_And_Items_Only_When_Given()

  [Theory] [InlineData("abc-123_X.y:z", "abc-123_X.y:z")]
  public async Task Valid_Incoming_Id_Is_Echoed(string incoming, string expected)  // header, TraceIdentifier and ICorrelationContext all equal expected
  [Theory] [InlineData("has space")] [InlineData("")] [InlineData(null)]
  public async Task Invalid_Or_Missing_Id_Is_Replaced(string? incoming)           // ^[0-9a-f]{32}$; a 129-char id is also replaced

  [Fact] public void Transient_Detection()
  {
      TransientFailure.Is(new RetryLimitExceededException("x", new TimeoutException())).Should().BeTrue();
      TransientFailure.Is(new TimeoutException()).Should().BeTrue();
      TransientFailure.Is(new NpgsqlException("x", new SocketException())).Should().BeTrue();
      TransientFailure.Is(new InvalidOperationException()).Should().BeFalse();
  }

  [Fact] public void GraphQL_Error_Carries_Retryable_And_CorrelationId()  // filter output extensions: retryable=false, correlationId = HttpContext.TraceIdentifier

  // Integration
  [Fact] public async Task Every_Response_Has_A_Correlation_Header()          // GET /health/live → X-Correlation-Id present
  [Fact] public async Task Rate_Limited_Response_Uses_The_Error_Body()
  {   // overrides Host:RateLimit:GlobalPermitPerMinute = 2; third GET /health/live:
      res.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
      body.code.Should().Be("RATE_LIMITED"); body.retryable.Should().BeTrue();
      body.correlationId.Should().Be(res.Headers.GetValues("X-Correlation-Id").Single());
  }
  ```
  Also flip `ResultHttpSemanticsTests`' `[InlineData("VALIDATION")]` to `"VALIDATION_FAILED"`.
- [ ] **Step 2: Run, expect FAIL:**
  `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "*.ErrorBodyTests"` (and the other four classes),
  `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "*.ErrorBodyEndToEndTests"`.
- [ ] **Step 3: Implement.**
  - `Error`: add `bool Retryable = false` as the 4th positional parameter; add the two presets; set
    `Validation`'s code to `VALIDATION_FAILED`; replace every `"VALIDATION"` literal.
  - `EndpointResult.Fail` returns a private `IResult` that writes `ErrorResponse` (camelCase,
    `JsonIgnoreCondition.WhenWritingNull`) with `CorrelationId = HttpContext.TraceIdentifier`.
  - `CorrelationIdMiddleware`: accept `X-Correlation-Id` matching `^[A-Za-z0-9\-_.:]{1,128}$`, else
    `Guid.NewGuid().ToString("N")`; set `TraceIdentifier`, the scoped `CorrelationContext`, the
    response header (via `OnStarting`) and a Serilog `LogContext` property `CorrelationId`.
  - `GlobalExceptionHandlerMiddleware`: write every error through `ErrorResponse`
    (`DomainException` → 400 with its code; `DbUpdateConcurrencyException` → `Error.ConcurrencyConflict`;
    `TransientFailure.Is(ex)` → `Error.ServiceUnavailable`; else `Error.Internal`, with the exception
    in `details` only in Development).
  - `Program.cs`: global limiter permit from `Host:RateLimit:GlobalPermitPerMinute` (default 100);
    `opts.OnRejected` sets `Retry-After: 60` and writes `Error.RateLimited` as `ErrorResponse`.
  - `GraphQLErrorFilter`: inject `IHttpContextAccessor`; set extensions `retryable` (from the
    `AppGraphQLException`'s `Error`, otherwise false) and `correlationId`; the `"VALIDATION"` case in
    `ErrorGraphQLExtensions` becomes `"VALIDATION_FAILED"`.
- [ ] **Step 4: Run, expect PASS** (Step 2 commands, then all UnitTests and IntegrationTests).
- [ ] **Step 5: Commit** `git add -A && git commit -m "feat(errors): standard error body with retryable and X-Correlation-Id; RATE_LIMITED and SERVICE_UNAVAILABLE" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 8: Admin and partner principals in Core

**Files:** Create: `src/BuildingBlocks/Audit/Application/{EwaysVNext.Audit.Application.csproj,AuditActor.cs}`,
`src/BuildingBlocks/Core/Application/{AdminActor,PartnerPrincipal,PartnerScopes,IDataSeeder}.cs`,
`tests/UnitTests/PrincipalTests.cs`. Modify: `src/BuildingBlocks/Core/Application/EwaysVNext.Core.Application.csproj`
(reference `Audit.Application`), `EwaysVNext.slnx`.

**Interfaces:** Produces exactly the contract's `AuditActor`, `AdminActor` (`ToAudit()` →
`new AuditActor(UserId, IsSuperAdmin ? "super_admin" : "staff")`), `PartnerPrincipal`,
`PartnerScopes` (+ `All`), and `IDataSeeder`. `AuditActor` has `public static AuditActor System { get; } = new(Guid.Empty, "system");`.

- [ ] **Step 1: Write the failing tests.**
  ```csharp
  [Fact] public void Partner_Principal_From_Claims()
  {
      var user = Principal(("partner_id", "42"), ("api_key_id", "7"), ("scope", "orders:create"), ("scope", "orders:read"));
      PartnerPrincipal.TryFrom(user, out var p).Should().BeTrue();
      p.Should().BeEquivalentTo(new { PartnerId = 42L, ApiKeyId = 7L });
      p.Has(PartnerScopes.OrdersCreate).Should().BeTrue();
      p.Has(PartnerScopes.InvoicesRead).Should().BeFalse();
  }
  [Theory] [InlineData(null, "7")] [InlineData("abc", "7")] [InlineData("42", null)] [InlineData("0", "7")] [InlineData("-1", "7")]
  public void Partner_Principal_Requires_Positive_Numeric_Ids(string? partnerId, string? keyId) // TryFrom → false
  [Fact] public void Unknown_Scopes_Are_Dropped()    // scope "admin:all" is not in Scopes
  [Fact] public void Scopes_Constants() => PartnerScopes.All.Should().BeEquivalentTo(
      ["customers:read", "customers:write", "orders:create", "orders:read", "invoices:read"]);
  [Theory] [InlineData(true, "super_admin")] [InlineData(false, "staff")]
  public void AdminActor_Maps_To_Audit_Role(bool super, string role) =>
      new AdminActor(Guid.Parse("11111111-1111-1111-1111-111111111111"), super).ToAudit().Role.Should().Be(role);
  ```
- [ ] **Step 2: Run, expect FAIL:** `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "*.PrincipalTests"`
- [ ] **Step 3: Implement** the types with the signatures above (Audit.Application has no
  references; Core.Application references only Audit.Application).
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `git add -A && git commit -m "feat(core): AdminActor, PartnerPrincipal, PartnerScopes, IDataSeeder" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 9: Storage building block (MinIO via S3)

**Files:** Create: `src/BuildingBlocks/Storage/Application/{EwaysVNext.Storage.Application.csproj,IObjectStorage.cs,StorageBuckets.cs}`,
`src/BuildingBlocks/Storage/Implementation/{EwaysVNext.Storage.Implementation.csproj,MinioOptions.cs,S3ObjectStorage.cs,ObjectStorageHealthCheck.cs,DependencyInjection.cs}`,
`tests/IntegrationTests/Platform/ObjectStorageTests.cs`. Modify: `EwaysVNext.slnx`, `src/Host/EwaysVNext.Host/{EwaysVNext.Host.csproj,Program.cs}`,
`tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj`.

**Interfaces:** Produces `IObjectStorage`/`StorageBuckets` exactly as the contract;
`public static IServiceCollection AddObjectStorage(this IServiceCollection services, IConfiguration configuration)`
binding `Minio:{Endpoint,PublicEndpoint,AccessKey,SecretKey,Region}` (Region default `us-east-1`,
`PublicEndpoint` defaults to `Endpoint`), registering a health check `"Storage"` tagged `ready` that
checks bucket `documents` exists. Host calls it after `AddUnifiedDatabase`.

- [ ] **Step 1: Write the failing tests** (`[Collection(PostgresSuite.Name)]`, resolve `IObjectStorage` from `CreateFactory().Services`):
  ```csharp
  [Fact] public async Task Put_Then_Get_Returns_The_Same_Bytes()
  [Fact] public async Task Get_Missing_Returns_Null() => (await storage.GetAsync(StorageBuckets.Documents, "missing/x.pdf")).Should().BeNull();
  [Fact] public async Task Exists_Follows_Put_And_Delete()
  [Fact] public async Task Copy_Moves_Bytes_Between_Buckets()        // import-staging → catalog-media, source still exists
  [Fact] public async Task Presigned_Url_Downloads_Then_Expires()
  {
      var url = await storage.GetPresignedUrlAsync(StorageBuckets.Documents, key, TimeSpan.FromSeconds(2));
      (await http.GetByteArrayAsync(url)).Should().Equal(bytes);
      await Task.Delay(TimeSpan.FromSeconds(4));
      (await http.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
  }
  [Theory] [InlineData(0)] [InlineData(-1)] [InlineData(604801)]
  public async Task Presign_Rejects_Ttl_Outside_One_Second_To_Seven_Days(int seconds)  // ArgumentOutOfRangeException
  ```
- [ ] **Step 2: Run, expect FAIL:** `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "*.ObjectStorageTests"`
- [ ] **Step 3: Implement** `S3ObjectStorage` on `AmazonS3Client` (`ForcePathStyle = true`,
  `ServiceURL = Endpoint`); presigning uses a second client configured with `PublicEndpoint` (signing is
  local, so the public host need not be reachable from the container). `GetAsync` returns `null` on
  HTTP 404 / `NoSuchKey`; the caller disposes the stream.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `git add -A && git commit -m "feat(storage): IObjectStorage with S3/MinIO adapter and health check" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 10: Jobs — tables, enqueue with dedupe, `SKIP LOCKED` claim

**Files:** Create: `src/BuildingBlocks/Jobs/Application/{EwaysVNext.Jobs.Application.csproj,IJobQueue.cs,IJobHandler.cs,JobContext.cs,JobOutcome.cs,JobTypes.cs,EmptyPayload.cs,JobLeaseLostException.cs}`,
`src/BuildingBlocks/Jobs/Implementation/{EwaysVNext.Jobs.Implementation.csproj,JobRecord.cs,RecurringJobRecord.cs,JobStatus.cs,IJobStore.cs,PostgresJobStore.cs,PostgresJobQueue.cs,JobsOptions.cs,DependencyInjection.cs}`,
`src/Database/Jobs/Configurations/{JobRecordConfiguration,RecurringJobRecordConfiguration}.cs`,
`tests/IntegrationTests/Platform/JobStoreTests.cs`. Modify: `src/Database/{EwaysVNext.Database.csproj,EwaysVNextDbContext.cs}`,
`EwaysVNext.slnx`, `tests/PersistenceContractTests/ModelContractTests.cs` (schemas now `catalog, jobs`); regenerate `init`.

**Interfaces:**
- Produces the contract's `IJobQueue`, `IJobHandler<TPayload>`, `JobContext`, `JobOutcome`, `JobTypes`
  (values equal their names: `"ImportJobStep"`, `"FxRefresh"`, `"RefreshStorefrontViews"`, `"AuditSeal"`,
  `"UsagePrune"`, `"StagingImageCleanup"`), and `AddJobs(IConfiguration)`.
- Produces (infrastructure, public in Implementation):
  ```csharp
  public sealed record ClaimedJob(long Id, string Type, string Payload, int Attempts, int MaxAttempts);
  public interface IJobStore {
    Task<IReadOnlyList<ClaimedJob>> ClaimAsync(string workerId, int batchSize, TimeSpan lease, DateTimeOffset now, CancellationToken ct = default);
    Task<bool> HeartbeatAsync(long jobId, string workerId, TimeSpan lease, DateTimeOffset now, CancellationToken ct = default);
    Task CompleteAsync(long jobId, string workerId, DateTimeOffset now, CancellationToken ct = default);
    Task RetryAsync(long jobId, string workerId, DateTimeOffset runAt, string reason, CancellationToken ct = default); // coalesces on 23505
    Task FailAsync(long jobId, string workerId, string reason, DateTimeOffset now, CancellationToken ct = default);
    Task RegisterRecurringAsync(string type, TimeSpan interval, DateTimeOffset now, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ClaimDueRecurringAsync(DateTimeOffset now, CancellationToken ct = default);
  }
  ```
- Table `jobs.job`: `id bigint PK (Snowflake)`, `type varchar(100)`, `payload jsonb`, `status varchar(16)`
  with `CHECK (status IN ('Queued','Running','Completed','Failed'))`, `run_at timestamptz`, `attempts int`,
  `max_attempts int`, `locked_by varchar(200) null`, `locked_until timestamptz null`, `last_error text null`,
  `dedupe_key varchar(200) null`, `created_at timestamptz`, `completed_at timestamptz null`;
  unique index `ux_job_dedupe_key_queued (dedupe_key) WHERE status = 'Queued'`; index `(status, run_at)`.
  Table `jobs.recurring`: `type varchar(100) PK`, `interval interval`, `next_run_at timestamptz`.
- `Jobs:*` options: `Mode` (`InProcess` default | `WorkerOnly` | `Disabled`), `PollInterval` 1s,
  `BatchSize` 4, `LeaseDuration` 5m, `MaxAttempts` 10, `BaseRetryDelay` 5s, `MaxRetryDelay` 1h,
  `WorkerId` default `$"{Environment.MachineName}:{Environment.ProcessId}"`.

- [ ] **Step 1: Write the failing tests** (factory with a `MutableClock : IClock` swapped in via `ConfigureTestServices`):
  ```csharp
  [Fact] public async Task Enqueue_Inserts_A_Queued_Job()                 // status Queued, attempts 0, run_at = clock now, payload jsonb round-trips
  [Fact] public async Task Same_DedupeKey_While_Queued_Coalesces()        // 2 enqueues (runAt now+5s, then now+60s) → 1 row, run_at = now+5s
  [Fact] public async Task Same_DedupeKey_After_Claim_Inserts_A_New_Job() // enqueue, claim, enqueue → 2 rows
  [Fact] public async Task Future_RunAt_Is_Not_Claimed()
  [Fact] public async Task Concurrent_Claims_Never_Share_A_Job()
  {   // 50 jobs; 5 stores in separate scopes loop ClaimAsync(batch 4) in parallel until empty
      claimed.Should().HaveCount(50).And.OnlyHaveUniqueItems();
  }
  [Fact] public async Task Expired_Lease_Is_Reclaimed()
  {   // claim at T (lease 5m); claim at T+4m → empty; claim at T+5m+1s → same id, Attempts == 2
  }
  [Fact] public async Task Heartbeat_After_Lease_Lost_Returns_False()   // worker A claims, lease expires, B reclaims; A.Heartbeat → false
  [Fact] public async Task Complete_By_A_Worker_That_Lost_The_Lease_Is_Ignored()
  ```
- [ ] **Step 2: Run, expect FAIL:** `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "*.JobStoreTests"`
- [ ] **Step 3: Implement.** `PostgresJobStore(DbContext db, IOptions<JobsOptions>)` runs every
  statement through `db.Database.CreateExecutionStrategy().ExecuteAsync(...)`. The claim is one statement:
  ```sql
  UPDATE jobs.job SET status = 'Running', locked_by = @worker, locked_until = @now + @lease, attempts = attempts + 1
  WHERE id IN (SELECT id FROM jobs.job
               WHERE (status = 'Queued' AND run_at <= @now) OR (status = 'Running' AND locked_until < @now)
               ORDER BY run_at FOR UPDATE SKIP LOCKED LIMIT @n)
  RETURNING id, type, payload::text, attempts, max_attempts;
  ```
  Enqueue (`PostgresJobQueue(DbContext, IIdGenerator, IClock, ILogger)`):
  `INSERT … ON CONFLICT (dedupe_key) WHERE status = 'Queued' DO NOTHING`; payload serialised with
  `System.Text.Json` (web defaults). Enqueue is its own statement, never part of the caller's save:
  callers enqueue **after** their business save. Under a non-Npgsql provider it logs a warning and
  returns. Recurring tick: `UPDATE jobs.recurring SET next_run_at = @now + interval WHERE next_run_at <= @now RETURNING type`.
  Register `RecurringJobRecord`/`JobRecord` configurations under `src/Database/Jobs/Configurations`
  (`EwaysVNext.Database` references `EwaysVNext.Jobs.Implementation`); regenerate `init`.
- [ ] **Step 4: Run, expect PASS** (Step 2 command, then PersistenceContractTests).
- [ ] **Step 5: Commit** `git add -A && git commit -m "feat(jobs): Postgres job queue with dedupe and SKIP LOCKED claim" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 11: Jobs — worker, retries, recurring jobs, `Jobs:Mode`

**Files:** Create: `src/BuildingBlocks/Jobs/Implementation/{JobWorker,JobBackoff,JobHandlerRegistry,RecurringJobRegistration}.cs`,
`tests/UnitTests/JobBackoffTests.cs`, `tests/IntegrationTests/Platform/JobWorkerTests.cs`,
`docs/adr/0019-postgres-backed-job-queue.md`. Modify: `src/BuildingBlocks/Jobs/Implementation/DependencyInjection.cs`,
`src/Host/EwaysVNext.Host/Program.cs` (`builder.Services.AddJobs(builder.Configuration)`), `docs/adr/0010-kubernetes-deployment-strategy.md`.

**Interfaces:**
- Produces `AddJobHandler<THandler, TPayload>()` (scoped handler; registry maps `Type` → payload type),
  `AddRecurringJob(string type, TimeSpan interval)`, and
  `public sealed class JobWorker : BackgroundService { public Task<int> RunOnceAsync(CancellationToken ct = default); }`
  (claims one batch, runs each job in its own DI scope with `CorrelationContext.CorrelationId = $"job-{id}"`,
  returns the number processed). `ExecuteAsync` = registers recurring jobs, then loops
  `ClaimDueRecurring → enqueue (dedupeKey "recurring:{type}", payload EmptyPayload) → RunOnceAsync → delay PollInterval`.
- `public static class JobBackoff { public static TimeSpan For(int attempt, TimeSpan baseDelay, TimeSpan max); }`
  = `min(baseDelay * 2^(attempt-1), max)`.
- `JobWorker` is registered as a hosted service only when `Jobs:Mode` is `InProcess` or `WorkerOnly`
  **and** the provider is Npgsql.

- [ ] **Step 1: Write the failing tests.**
  ```csharp
  [Theory] [InlineData(1, 5)] [InlineData(2, 10)] [InlineData(3, 20)] [InlineData(20, 3600)]
  public void Backoff_Doubles_And_Caps(int attempt, int seconds) =>
      JobBackoff.For(attempt, TimeSpan.FromSeconds(5), TimeSpan.FromHours(1)).Should().Be(TimeSpan.FromSeconds(seconds));

  // Integration (test handler TestHandler : IJobHandler<TestPayload> whose outcome the test sets)
  [Fact] public async Task Completed_Marks_The_Job_Completed()                // status Completed, completed_at = clock now, locked_by null
  [Fact] public async Task RetryLater_Requeues_With_Delay_And_Reason()        // run_at = now + 30s, last_error "busy", status Queued
  [Fact] public async Task Exception_Requeues_With_Backoff()                  // first attempt throws → run_at = now + 5s, last_error contains exception message
  [Fact] public async Task MaxAttempts_Reached_Marks_Failed()                 // Jobs:MaxAttempts = 2; two throwing runs → Failed
  [Fact] public async Task Failed_Outcome_Marks_Failed_Immediately()
  [Fact] public async Task Unknown_Type_Is_Failed() // last_error == "no handler registered for job type 'nope'"
  [Fact] public async Task Retry_Into_A_Queued_Duplicate_Is_Coalesced()      // last_error == "coalesced into queued duplicate", status Completed
  [Fact] public async Task Handler_Sees_The_Job_Correlation_Id()             // ICorrelationContext.CorrelationId == $"job-{id}"
  [Fact] public async Task Recurring_Job_Fires_Once_Per_Interval_Across_Two_Workers()
  {   // AddRecurringJob("TestTick", 1 minute); two JobWorker instances tick at T and T+30s → 1 job; at T+61s → 2 jobs
  }
  [Fact] public void Disabled_Mode_Registers_No_Worker()   // services with Jobs:Mode=Disabled contain no JobWorker hosted service
  ```
- [ ] **Step 2: Run, expect FAIL:** `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "*.JobBackoffTests"`;
  `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "*.JobWorkerTests"`
- [ ] **Step 3: Implement.** Outcome handling: `Completed` → `CompleteAsync`; `RetryLater(d, r)`
  → `RetryAsync(now + d, r)`; `Failed(r)` → `FailAsync`; exception → if `Attempts >= MaxAttempts`
  `FailAsync(ex.Message)` else `RetryAsync(now + JobBackoff.For(Attempts, …), ex.Message)`;
  `JobLeaseLostException` → log and abandon (no write). A claimed job with `Attempts > MaxAttempts`
  (re-claimed after lease expiry) is failed without running. `JobContext.HeartbeatAsync` calls
  `IJobStore.HeartbeatAsync` and throws `JobLeaseLostException` on `false`.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Docs.** Write `docs/adr/0019-postgres-backed-job-queue.md` (Accepted; amends ADR-0010):
  tables, claim SQL, leases + explicit heartbeat (no automatic heartbeat, so a hung handler loses its
  lease), at-least-once ⇒ idempotent handlers, backoff, dedupe-while-queued (decision 4), enqueue
  after save, recurring tick, `Jobs:Mode`, compose topology (api `Disabled` + one `worker`), partner
  orders are never jobs. Add a "Superseded in part by ADR-0019" note to ADR-0010's status.
- [ ] **Step 6: Commit** `git add -A && git commit -m "feat(jobs): worker with retries, leases, recurring jobs and Jobs:Mode (ADR-0019)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 12: Audit trail — append-only events recorded in the caller's save

**Files:** Create: `src/BuildingBlocks/Audit/Application/{IAuditTrail,IAuditVerifier,AuditVerificationResult}.cs`,
`src/BuildingBlocks/Audit/Implementation/{EwaysVNext.Audit.Implementation.csproj,AuditEvent.cs,AuditSeal.cs,AuditDiff.cs,EfAuditTrail.cs,DependencyInjection.cs}`,
`src/Database/Audit/Configurations/{AuditEventConfiguration,AuditSealConfiguration}.cs`,
`src/Database/Scripts/010-audit-append-only.sql` (`<EmbeddedResource>` in the Database csproj),
`tests/UnitTests/AuditDiffTests.cs`, `tests/IntegrationTests/Platform/AuditTrailTests.cs`.
Modify: `src/Database/{EwaysVNext.Database.csproj,EwaysVNextDbContext.cs}`, `src/Host/EwaysVNext.Host/Program.cs`
(`builder.Services.AddAudit()`), `EwaysVNext.slnx`, `tests/PersistenceContractTests/ModelContractTests.cs`
(schemas `catalog, jobs, audit`); regenerate `init`.

**Interfaces:**
- Produces the contract's `IAuditTrail.Record(...)`; `EfAuditTrail(DbContext db, IIdGenerator ids, IClock clock, ICorrelationContext correlation)`
  adds one `AuditEvent` to `db` and never saves.
- `public static class AuditDiff { public static (JsonObject? Before, JsonObject? After) Changed(object? before, object? after); }`
  — serialises both with web defaults and keeps only top-level properties whose JSON differs (a
  property missing on one side counts as changed).
- `audit.audit_events`: `id bigint PK`, `occurred_at timestamptz`, `actor_user_id uuid`, `actor_role varchar(32)`,
  `action varchar(100)`, `target_type varchar(100)`, `target_id varchar(100)`, `before jsonb null`,
  `after jsonb null`, `correlation_id varchar(128)`. `audit.audit_seals`: `event_id bigint PK`,
  `hash bytea not null`, `sealed_at timestamptz`. Neither entity has `RowVersion` (plain classes, not `Entity<TId>`).
- `AddAudit()` registers `IAuditTrail` (scoped) and, from Task 13, the seal handler, the recurring
  job and `IAuditVerifier`.

- [ ] **Step 1: Write the failing tests.**
  ```csharp
  [Fact] public void Diff_Keeps_Only_Changed_Fields()
  {
      var (b, a) = AuditDiff.Changed(new { Name = "a", Email = "x@y.z" }, new { Name = "b", Email = "x@y.z" });
      b!.ToJsonString().Should().Be("""{"name":"a"}"""); a!.ToJsonString().Should().Be("""{"name":"b"}""");
  }
  [Fact] public void Diff_Of_A_Creation_Keeps_Every_After_Field()   // before null → (null, all after fields)
  [Fact] public void Diff_Of_Identical_Objects_Is_Empty()           // ({}, {})

  // Integration
  [Fact] public async Task Record_Is_Persisted_Only_By_The_Callers_Save()
  {   // Record(AuditActor.System, "category.renamed", "Category", "42", before, after) → 0 rows; SaveChangesAsync → 1 row
      row.Should().BeEquivalentTo(new { action = "category.renamed", target_type = "Category", target_id = "42", actor_role = "system" });
  }
  [Theory] [InlineData("UPDATE audit.audit_events SET action = 'x'")] [InlineData("DELETE FROM audit.audit_events")]
  [InlineData("UPDATE audit.audit_seals SET hash = '\\x00'")] [InlineData("DELETE FROM audit.audit_seals")]
  public async Task App_Role_Cannot_Rewrite_Audit_History(string sql)
  {   // executed with f.AppConnectionString
      (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
  }
  [Fact] public async Task App_Role_Can_Insert_Audit_Events()
  ```
- [ ] **Step 2: Run, expect FAIL:** `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "*.AuditDiffTests"`;
  `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "*.AuditTrailTests"`
- [ ] **Step 3: Implement** the types and configurations; regenerate `init`; the script:
  ```sql
  DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'eways_app') THEN
      REVOKE UPDATE, DELETE, TRUNCATE ON audit.audit_events, audit.audit_seals FROM eways_app;
    END IF;
  END $$;
  ```
- [ ] **Step 4: Run, expect PASS.** Mutation check: delete the script's `REVOKE` line, rerun,
  expect `App_Role_Cannot_Rewrite_Audit_History` to FAIL; restore.
- [ ] **Step 5: Commit** `git add -A && git commit -m "feat(audit): append-only audit trail recorded in the caller's save" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 13: Audit sealing and verification

**Files:** Create: `src/BuildingBlocks/Audit/Implementation/{AuditSealJobHandler,AuditHashing,AuditVerifier}.cs`,
`tests/IntegrationTests/Platform/AuditSealTests.cs`. Modify: `src/BuildingBlocks/Audit/Implementation/DependencyInjection.cs`.

**Interfaces:**
- `public sealed class AuditSealJobHandler : IJobHandler<EmptyPayload>` with `Type => JobTypes.AuditSeal`;
  `AddAudit()` calls `AddJobHandler<AuditSealJobHandler, EmptyPayload>()` and
  `AddRecurringJob(JobTypes.AuditSeal, TimeSpan.FromMinutes(1))`.
- `public static class AuditHashing { public static byte[] Next(ReadOnlySpan<byte> previous, AuditEventRow row); }` =
  `SHA256(previous ‖ UTF8(canonical))`, canonical = fields joined by `'\u001f'`:
  `id, occurred_at.UtcTicks, actor_user_id:D, actor_role, action, target_type, target_id, before ?? "", after ?? "", correlation_id`,
  always computed from values **read back from the database** (jsonb normalises text). Genesis
  `previous` = 32 zero bytes. `AuditEventRow` is the read model record with those fields.
- Seal algorithm (one run): load the last seal; select up to 1000 events with `id > last.event_id`
  and `occurred_at <= now - 60s` ordered by `id`; chain; add `AuditSeal` rows; one `SaveChangesAsync`
  (independent append-only rows, rule 7). Returns `JobOutcome.Completed`.
- `AuditVerifier.VerifyAsync`: recompute the chain over sealed events; `Intact = FirstMismatchEventId is null`;
  `UnsealedBelowWatermark` = events with `id < max(sealed event_id)` and no seal (a late commit or a deletion).

- [ ] **Step 1: Write the failing tests** (`MutableClock`; events inserted through `IAuditTrail` + save):
  ```csharp
  [Fact] public async Task Seal_Chains_Events_In_Id_Order()      // 3 events at T, run at T+61s → 3 seals, seal[i].hash == AuditHashing.Next(seal[i-1].hash, row[i])
  [Fact] public async Task Seal_Skips_Events_Younger_Than_60s()  // event at T, run at T+30s → 0 seals
  [Fact] public async Task Seal_Is_Idempotent()                  // two runs → still 3 seals
  [Fact] public async Task Verify_Reports_An_Intact_Chain()      // Intact true, SealedCount 3, UnsealedBelowWatermark 0
  [Fact] public async Task Verify_Detects_A_Tampered_Event()
  {   // superuser: UPDATE audit.audit_events SET after = '{"name":"evil"}' WHERE id = second.Id
      result.Intact.Should().BeFalse(); result.FirstMismatchEventId.Should().Be(second.Id);
  }
  [Fact] public async Task Verify_Counts_A_Deleted_Event_As_Unsealed_Gap() // superuser deletes a sealed event's seal → UnsealedBelowWatermark 1
  [Fact] public void AuditSeal_Is_Recurring_Every_Minute()       // RecurringJobRegistration for "AuditSeal" has interval 00:01:00
  ```
- [ ] **Step 2: Run, expect FAIL:** `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "*.AuditSealTests"`
- [ ] **Step 3: Implement** the handler, hashing and verifier per the interfaces.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `git add -A && git commit -m "feat(audit): minute-by-minute hash-chain seals and verifier" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 14: Host commands — `--migrate`, `--worker`, `--seed`, `--probe`

**Files:** Create: `src/Host/EwaysVNext.Host/{HostCommands,Probe}.cs`, `tests/IntegrationTests/Host/HostCommandsTests.cs`.
Modify: `src/Host/EwaysVNext.Host/{Program.cs,ProgramMessages.cs}`.

**Interfaces:**
- `public static class HostCommands`:
  `public static void ApplyFlags(ConfigurationManager configuration, string[] args)` (`--worker` →
  in-memory `Jobs:Mode = WorkerOnly`, added last so it wins) and
  `public static Task<int?> RunAsync(WebApplication app, string[] args)` — `null` means "serve";
  `--migrate` → `DatabaseMigrator.MigrateAsync` + Authorization migrate → `0`; `--seed` → `2` with
  `ProgramMessages.SeedRefusedInProduction` (`"--seed refuses to run when ASPNETCORE_ENVIRONMENT=Production"`)
  when `app.Environment.IsProduction()`, else runs every `IDataSeeder` ordered by `Order` in one scope → `0`.
- `public static class Probe { public static Task<int> RunAsync(string[] args); }` — reads the URL after
  `--probe`; `64` if missing; `0` on a 2xx within 3s; `1` otherwise. `Program.cs` checks `--probe`
  before creating the builder.
- `Jobs:Mode == WorkerOnly` → `Program.cs` maps only the three health endpoints (no REST, GraphQL,
  OpenAPI, Scalar).

- [ ] **Step 1: Write the failing tests** (no Platform collection needed except the last two):
  ```csharp
  [Fact] public async Task Seed_Refuses_In_Production()
  {   // WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" }), a recording IDataSeeder
      (await HostCommands.RunAsync(app, ["--seed"])).Should().Be(2); seeder.Calls.Should().Be(0);
  }
  [Fact] public async Task Seed_Runs_Seeders_In_Order_Outside_Production()   // Development; seeders Order 20, 10 → call log [10, 20]; exit 0
  [Fact] public async Task No_Command_Means_Serve() => (await HostCommands.RunAsync(app, [])).Should().BeNull();
  [Fact] public void Worker_Flag_Selects_WorkerOnly()   // ApplyFlags(config with Jobs:Mode=InProcess, ["--worker"]) → config["Jobs:Mode"] == "WorkerOnly"
  [Fact] public async Task Probe_Fails_For_An_Unreachable_Url() => (await Probe.RunAsync(["--probe", "http://127.0.0.1:1/health"])).Should().Be(1);
  [Fact] public async Task Probe_Without_Url_Is_A_Usage_Error() => (await Probe.RunAsync(["--probe"])).Should().Be(64);
  [Fact] public async Task WorkerOnly_Host_Serves_Only_Health()  // Platform factory, overrides Jobs:Mode=WorkerOnly: /health/live 200, POST /graphql 404
  [Fact] public async Task Migrate_Is_Idempotent()               // RunAsync(app built with migrator connection, ["--migrate"]) twice → 0, 0
  ```
- [ ] **Step 2: Run, expect FAIL:** `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "*.HostCommandsTests"`
- [ ] **Step 3: Implement**; `Program.cs` becomes: probe check → builder → `HostCommands.ApplyFlags`
  → registrations → `Build()` → `if (await HostCommands.RunAsync(app, args) is { } code) return code;` → pipeline.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `git add -A && git commit -m "feat(host): --worker, --seed (refused in Production), --probe; WorkerOnly serves health only" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 15: Production image (`build/docker`)

**Files:** Create: `build/docker/api.Dockerfile`, `build/docker/api.Dockerfile.dockerignore`.
Delete: `Dockerfile`, `Dockerfile.ci`, `.dockerignore`, `.dockerignore.ci`. Modify: `EwaysVNext.slnx`
(Solution Items point at the new files), `.github/workflows/ci.yml`.

**Interfaces:** Image entrypoint `dotnet EwaysVNext.Host.dll`; arguments select the role: none (api),
`--worker`, `--migrate`, `--seed`, `--probe <url>`.

- [ ] **Step 1: Write the Dockerfile.**
  ```dockerfile
  # syntax=docker/dockerfile:1.7
  ARG DOTNET_REGISTRY=mcr.microsoft.com
  FROM ${DOTNET_REGISTRY}/dotnet/sdk:10.0 AS build
  WORKDIR /src
  COPY global.json nuget.config Directory.Build.props Directory.Build.targets Directory.Packages.props EwaysVNext.slnx ./
  COPY EwaysVNext.Modules.Catalog.Contracts/ EwaysVNext.Modules.Catalog.Contracts/
  COPY src/ src/
  RUN dotnet restore src/Host/EwaysVNext.Host/EwaysVNext.Host.csproj
  RUN dotnet publish src/Host/EwaysVNext.Host/EwaysVNext.Host.csproj -c Release --no-restore -o /app /p:UseAppHost=false

  FROM ${DOTNET_REGISTRY}/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
  WORKDIR /app
  COPY --from=build --chown=$APP_UID:$APP_UID /app ./
  USER $APP_UID
  ENV ASPNETCORE_HTTP_PORTS=8080 ASPNETCORE_ENVIRONMENT=Production
  EXPOSE 8080
  ENTRYPOINT ["dotnet", "EwaysVNext.Host.dll"]
  ```
  (`-extra` keeps ICU and tzdata; chiseled has no shell, so health checks use `--probe`.)
  `api.Dockerfile.dockerignore`: `**/bin/`, `**/obj/`, `tests/`, `docs/`, `build/`, `.git/`, `.github/`,
  `.vs/`, `.idea/`, `**/*.user`, `**/.env`, `**/.env.*`, `**/TestResults/`.
- [ ] **Step 2: Verify the image.**
  ```bash
  docker build -f build/docker/api.Dockerfile -t eways-api:local .
  docker image inspect eways-api:local --format '{{.Config.User}}'      # expected: 1654
  docker run --rm eways-api:local --probe http://127.0.0.1:1/health; echo $?   # expected: 1
  docker run --rm -e ASPNETCORE_ENVIRONMENT=Production eways-api:local --seed; echo $?  # expected: 2 and the refusal message
  ```
- [ ] **Step 3: CI.** In `ci.yml` add, after the Release build, `docker build -f build/docker/api.Dockerfile -t eways-api:ci .`
  and note in a comment that integration tests need Docker (available on `ubuntu-latest`).
- [ ] **Step 4: Commit** `git add -A && git commit -m "build: chiseled non-root api image in build/docker; remove root Docker files" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 16: Compose stacks, Caddy, Keycloak stubs, env template

**Files:** Create: `build/compose/{docker-compose.yml,docker-compose.dev.yml,.env.example,ci.env}`,
`build/compose/caddy/Caddyfile`, `build/keycloak/{shop-realm.json,backoffice-realm.json}`,
`tests/ArchitectureTests/BuildDirectoryTests.cs`. Delete: root `docker-compose.yml`, root `.env.example`.
Modify: `EwaysVNext.slnx`, `.github/workflows/ci.yml`.

**Interfaces:**
- `.env.example` keys (all values empty): `PUBLIC_HOST, AUTH_HOST, STORAGE_HOST, ACME_EMAIL,
  POSTGRES_PASSWORD, APP_DB_PASSWORD, MIGRATOR_DB_PASSWORD, MINIO_ROOT_USER, MINIO_ROOT_PASSWORD,
  MINIO_APP_ACCESS_KEY, MINIO_APP_SECRET_KEY, KC_DB_PASSWORD, KC_BOOTSTRAP_ADMIN_USERNAME,
  KC_BOOTSTRAP_ADMIN_PASSWORD, KC_SMTP_HOST, KC_SMTP_PORT, KC_SMTP_FROM, KC_SMTP_USER, KC_SMTP_PASSWORD,
  SHOP_REDIRECT_URI, BACKOFFICE_REDIRECT_URI, API_KEYS_PEPPER, BRAVE_API_KEY, ANTHROPIC_API_KEY`.
  `ci.env` has the same keys with the value `ci-placeholder` (ports `587`), used only for `config` validation.
- Production `docker-compose.yml` services (every one has a healthcheck; every secret uses `${VAR:?…}`):
  - `postgres`: `postgres:17-alpine`, `POSTGRES_DB=eways`, `POSTGRES_USER=postgres`, `POSTGRES_PASSWORD`,
    `APP_DB_PASSWORD`, `MIGRATOR_DB_PASSWORD`, `../postgres/init:/docker-entrypoint-initdb.d:ro`,
    volume `pg_data`, health `pg_isready -U postgres -d eways`.
  - `minio`: `minio/minio:RELEASE.2025-04-22T22-12-26Z`, `server /data --console-address :9001`, volume
    `minio_data`, health `["CMD", "mc", "ready", "local"]`.
  - `minio-init`: `minio/mc:RELEASE.2025-04-16T18-13-26Z`, depends on minio healthy, `restart: "no"`;
    entrypoint script: `mc alias set local http://minio:9000 "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD"`,
    `mc mb --ignore-existing local/catalog-media local/import-staging local/documents`,
    `mc anonymous set download local/catalog-media`, `mc admin user add local "$MINIO_APP_ACCESS_KEY" "$MINIO_APP_SECRET_KEY"`,
    `mc admin policy attach local readwrite --user "$MINIO_APP_ACCESS_KEY" || true`.
  - `migrations`: image built from `../docker/api.Dockerfile` (context `../..`), `command: ["--migrate"]`,
    `ConnectionStrings__Default` with user `eways_migrator`, depends on postgres healthy, `restart: "no"`.
  - `api`: same image, no command, `Jobs__Mode=Disabled`, `ConnectionStrings__Default` with user `eways_app`,
    `Minio__Endpoint=http://minio:9000`, `Minio__PublicEndpoint=https://${STORAGE_HOST}`,
    `Minio__AccessKey/SecretKey` = app user, `ApiKeys__Pepper`, `Brave__ApiKey`, `Anthropic__ApiKey`,
    `Keycloak__InternalUrl=http://keycloak:8080`, `Keycloak__ExternalUrl=https://${AUTH_HOST}`,
    depends on `migrations: service_completed_successfully`, `minio-init: service_completed_successfully`,
    `keycloak: service_healthy`; health `["CMD", "dotnet", "EwaysVNext.Host.dll", "--probe", "http://localhost:8080/health/ready"]`.
  - `worker`: same image and env as `api` except `command: ["--worker"]`, no `Jobs__Mode`, health probe `/health/live`.
  - `keycloak-db`: `postgres:17-alpine`, `POSTGRES_DB=keycloak`, `POSTGRES_USER=keycloak`, `POSTGRES_PASSWORD=${KC_DB_PASSWORD:?…}`.
  - `keycloak`: `quay.io/keycloak/keycloak:26.4`, `command: ["start", "--import-realm"]`, `KC_DB=postgres`,
    `KC_DB_URL=jdbc:postgresql://keycloak-db:5432/keycloak`, `KC_DB_USERNAME=keycloak`, `KC_DB_PASSWORD`,
    `KC_HOSTNAME=https://${AUTH_HOST}`, `KC_HTTP_ENABLED=true`, `KC_PROXY_HEADERS=xforwarded`,
    `KC_HEALTH_ENABLED=true`, `KC_BOOTSTRAP_ADMIN_USERNAME/PASSWORD`, the `KC_SMTP_*`, redirect URIs,
    `../keycloak:/opt/keycloak/data/import:ro`; health via bash `/dev/tcp` GET `localhost:9000/health/ready` expecting `200`.
  - `caddy`: `caddy:2.10-alpine`, ports `80`, `443`, `./caddy/Caddyfile:/etc/caddy/Caddyfile:ro`,
    volumes `caddy_data`, `caddy_config`, health `["CMD", "wget", "-qO-", "http://localhost:2019/config/"]`.
- `Caddyfile`:
  ```
  { email {$ACME_EMAIL} }
  {$PUBLIC_HOST} {
      encode zstd gzip
      handle_path /media/* {
          rewrite * /catalog-media{uri}
          reverse_proxy minio:9000
      }
      handle { reverse_proxy api:8080 }
  }
  {$AUTH_HOST} { reverse_proxy keycloak:8080 }
  {$STORAGE_HOST} { reverse_proxy minio:9000 }
  ```
- `docker-compose.dev.yml`: `postgres` (port 5432, defaults `${POSTGRES_PASSWORD:-dev-postgres}`,
  `${APP_DB_PASSWORD:-dev-app-password}`, `${MIGRATOR_DB_PASSWORD:-dev-migrator-password}`, same init
  mount), `minio` (9000/9001, `${MINIO_ROOT_USER:-dev-minio}`/`${MINIO_ROOT_PASSWORD:-dev-minio-password}`),
  `minio-init` (as production, app user = root credentials), `keycloak` (`start-dev --import-realm`,
  port `8081:8080`, H2, realm env defaulted to `localhost` values). No api/worker: the API runs from the IDE.
- Realm stubs (phase 1 fills them): `shop-realm.json` = realm `Shop`, `registrationAllowed`,
  `verifyEmail`, `resetPasswordAllowed`, `loginWithEmailAllowed` true; `smtpServer` with
  `host/port/from/user/password` = `${KC_SMTP_HOST}` … `${KC_SMTP_PASSWORD}`, `auth`/`starttls` `"true"`;
  client `modular-monolith` (public, standard flow, `directAccessGrantsEnabled: false`,
  `redirectUris: ["${SHOP_REDIRECT_URI}"]`, attribute `pkce.code.challenge.method: S256`).
  `backoffice-realm.json` = realm `Backoffice`, `registrationAllowed: false`, realm roles
  `super_admin`, `staff`, same `smtpServer`, client `backoffice-console` (public, PKCE,
  `redirectUris: ["${BACKOFFICE_REDIRECT_URI}"]`).

- [ ] **Step 1: Write the failing tests** (`BuildDirectoryTests`):
  ```csharp
  [Fact] public void Spec_Build_Layout_Exists() => new[] {
      "build/docker/api.Dockerfile", "build/compose/docker-compose.yml", "build/compose/docker-compose.dev.yml",
      "build/compose/.env.example", "build/compose/caddy/Caddyfile", "build/keycloak/shop-realm.json",
      "build/keycloak/backoffice-realm.json", "build/postgres/init/01-roles.sql" }
      .Where(p => !File.Exists(Path.Combine(Root, p))).Should().BeEmpty();
  [Fact] public void Root_Docker_Files_Are_Gone() => new[] { "Dockerfile", "Dockerfile.ci", "docker-compose.yml", ".env.example" }
      .Where(p => File.Exists(Path.Combine(Root, p))).Should().BeEmpty();
  [Fact] public void Env_Example_Has_No_Values() => EnvLines("build/compose/.env.example").Should().OnlyContain(l => l.EndsWith('='));
  [Theory] [InlineData("build/keycloak/shop-realm.json")] [InlineData("build/keycloak/backoffice-realm.json")]
  public void Realm_Secrets_Are_Placeholders(string file) =>    // every JSON string value under a key containing "password" or "secret" (case-insensitive)
      SecretValues(file).Should().OnlyContain(v => Regex.IsMatch(v, @"^\$\{[A-Z0-9_]+\}$"));
  [Fact] public void Roles_Script_Contains_No_Literal_Password() =>
      File.ReadAllText(Path.Combine(Root, "build/postgres/init/01-roles.sql")).Should().NotMatchRegex(@"PASSWORD\s+'");
  ```
- [ ] **Step 2: Run, expect FAIL:** `dotnet test --project tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj --filter-class "*.BuildDirectoryTests"`
- [ ] **Step 3: Write the files** per the interfaces; delete the root files.
- [ ] **Step 4: Run, expect PASS**, then verify the stacks:
  ```bash
  docker compose -f build/compose/docker-compose.yml --env-file build/compose/.env.example config --quiet
  # expected: non-zero exit, "required variable ... is missing a value" (fail fast)
  docker compose -f build/compose/docker-compose.yml --env-file build/compose/ci.env config --quiet   # expected: exit 0
  docker compose -f build/compose/docker-compose.dev.yml up -d --wait                                 # expected: postgres, minio, keycloak healthy; minio-init exited 0
  ConnectionStrings__Default="Host=localhost;Port=5432;Database=eways;Username=eways_migrator;Password=dev-migrator-password" \
    dotnet run --project src/Host/EwaysVNext.Host -- --migrate                                        # expected: exit 0, ProgramMessages.MigrateDone printed
  curl -fsS http://localhost:8081/realms/Shop/.well-known/openid-configuration > /dev/null && echo REALM_OK   # expected: REALM_OK
  docker compose -f build/compose/docker-compose.dev.yml down -v
  ```
  Add the `ci.env` `config --quiet` line to `ci.yml`.
- [ ] **Step 5: Commit** `git add -A && git commit -m "build: production and dev compose, Caddy, Keycloak realm stubs, env template" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 17: ADRs and documentation match the code

**Files:** Create: `docs/adr/0018-sales-aggregate-owns-order-lifecycle.md`,
`docs/adr/0020-storefront-materialized-views.md`, `docs/adr/0021-partner-api-key-authentication.md`,
`tests/ArchitectureTests/DocumentationTests.cs`. Modify: `docs/adr/0016-queryapplication-is-the-read-side-implementation.md`
(Status → `Accepted — 2026-10-28`), `docs/adr/0015-payment-gateway.md` (Status → `Superseded by ADR-0018`),
`AGENTS.md`, `docs/ONBOARDING.md`, `docs/TESTING-GUARANTEES.md`, `docs/DDD-GUIDELINES.md`, `README.md`.

**Interfaces:** none (docs). Content requirements:
- ADR-0018: spec §5.6 "Order" + "Invariants"; atomicity by aggregate design; the one documented
  non-atomic edge (§10.3 step 5); rules 7/8 intact; supersedes ADR-0015.
- ADR-0020: spec §8; amends ADR-0008 for storefront browse reads only; cross-schema SQL dependency;
  purchasing re-validates live; the MV-ownership/refresh-role consequence from ADR-0017.
- ADR-0021: spec §5.1 key format, HMAC-SHA256 + pepper rationale, scopes, `PartnerPrincipal`,
  404-not-403 tenancy, per-partner in-process rate limit and the ADR-0010 multi-replica caveat, usage log.
- AGENTS.md: module list = **Catalog**, **Customer** (shell, rebuilt in phase 1), plus a non-bold
  "Planned" table (Partners, Settings, Pricing, Sales, Sourcing, Admin with their phases); Postgres,
  Testcontainers, `build/` commands, the new building blocks, `Jobs:Mode`, host flags, env table
  from `.env.example`, the real test suites (four projects) and filter syntax; delete every Identity/
  Orders/Supplier/Brand/Payment/Location/Notification/SQL Server/LocalDB/JWT-signing-key statement;
  rule 3 exception rows replaced by "see `HexagonalBoundaryTests.SanctionedReferences`"; rule 8 now
  names `No_User_Initiated_Transactions_In_Src`.
- ONBOARDING: same module list and run instructions (`docker compose -f build/compose/docker-compose.dev.yml up -d`).
- TESTING-GUARANTEES: LocalDB prerequisite → Docker; describe PostgresFixture, PersistenceContractTests
  (Npgsql refused port + InMemory), what is verified for jobs/audit/storage; drop the saga section.
- DDD-GUIDELINES "Known inconsistencies": `RowVersion` is `uint` mapped to `xmin` on every entity.

- [ ] **Step 1: Write the failing test.**
  ```csharp
  [Fact] public void Agents_Md_Bolds_Exactly_The_Existing_Modules()
  {
      var agents = File.ReadAllText(Path.Combine(Root, "AGENTS.md"));
      HexagonalBoundaryTests.ModuleNames.Should().OnlyContain(m => agents.Contains($"**{m}**"));
      new[] { "Identity", "Orders", "Supplier", "Brand", "Payment", "Location", "Notification" }
          .Should().OnlyContain(m => !agents.Contains($"**{m}**"));
      agents.Should().NotContain("LocalDB").And.NotContain("SQL Server");
  }
  [Fact] public void Adr_0016_Is_Accepted_And_0017_To_0022_Exist()
  ```
- [ ] **Step 2: Run, expect FAIL:** `dotnet test --project tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj --filter-class "*.DocumentationTests"`
- [ ] **Step 3: Write the documents** per the content requirements.
- [ ] **Step 4: Run, expect PASS**, then
  `grep -nE "LocalDB|SQL Server|MSSQL|Shahkar|IRR" AGENTS.md docs/ONBOARDING.md docs/TESTING-GUARANTEES.md README.md`
  → expected: no output.
- [ ] **Step 5: Commit** `git add -A && git commit -m "docs: ADRs 0018, 0020, 0021; accept ADR-0016; AGENTS/ONBOARDING/TESTING match the real modules" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 18: Phase gate

**Files:** Create: `docs/reports/phase-0.md`.

- [ ] **Step 1: Full build (Release, warnings as errors).** `dotnet build EwaysVNext.slnx -c Release` → 0 errors, 0 warnings.
- [ ] **Step 2: Full test run with Docker up.** `dotnet test EwaysVNext.slnx` → all four suites
  (Architecture, Unit, PersistenceContract, Integration) pass. Record each suite's count.
- [ ] **Step 3: Migration drift check.**
  `dotnet ef migrations has-pending-model-changes -p src/Database -s src/Host/EwaysVNext.Host -c EwaysVNextDbContext`
  and the same for `AuthorizationDbContext` → both report no pending changes.
- [ ] **Step 4: Image and stack.** Re-run the Task 15 Step 2 and Task 16 Step 4 verifications.
- [ ] **Step 5: Write `docs/reports/phase-0.md`** with sections *What works* (per task), *Incomplete*
  (Customer is a shell until phase 1; realms are stubs; no `IDataSeeder` registered yet; GraphQL
  composition unchanged), *Risks* (private NuGet feed required for Docker builds; MV refresh needs a
  view-owning role in phase 3; job enqueue is not atomic with the business save, nightly MV refresh
  is the backstop; audit seal 60s lag; pinned MinIO/Keycloak image tags must be re-checked before
  go-live), *Reproduction commands* (build, each suite's `dotnet test --project …`, dev compose up,
  `--migrate`, `docker build`), and the suite counts from Step 2. Update the test count in AGENTS.md
  and TESTING-GUARANTEES.md to match.
- [ ] **Step 6: Commit** `git add -A && git commit -m "docs: phase 0 report" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

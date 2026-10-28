# Phases 6 & 8 — Admin & Ops Package Implementation Plan

> Part of docs/superpowers/plans/2026-10-28-ecommerce-mvp.md — read its Global Constraints, Review Focus and Cross-phase contracts first.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking. Run every command from the repository root
> `C:\Users\Daniyal\Documents\mkbj`. Docker must be running for integration tests and for every
> phase 8 stack step.

**Goal:**
- **Phase 6 (Admin):** a data-less `Admin` composition module (ADR-0006/0007). It provides the
  dashboard, customer 360, order 360 and audit verification over REST `/api/v1/admin/...` and over
  GraphQL on the one `/graphql` endpoint. Also in this phase:
  - GraphQL depth, cost and admin rate limits.
  - An executable proof that every admin write is audited.
  - An executable PRD §10 coverage matrix, with the gaps it finds filled.
  - Acceptance 13 completed across every super-only area.
  - The "every endpoint is authorized unless allowlisted" guard.
- **Phase 8 (Ops package):**
  - OpenAPI v1 with examples for every partner endpoint, plus OpenAPI and GraphQL SDL snapshot tests.
  - A Bruno collection.
  - An end-to-end smoke script against `build/compose`.
  - The coverage and load report, produced from the simulator and k6 on the compose stack.
  - `docs/RUNBOOK.md`, `docs/SECURITY-CHECKLIST.md`, and the final doc counts.

**Spec sections:** §5.8 (Admin), §6.4 (audit: every admin service calls `IAuditTrail`;
verification exposed), §9.3 (admin REST + GraphQL for every PRD §10 view; depth 10, complexity,
`admin` rate limit), §9.4 (OpenAPI examples, Bruno, SDL snapshot), §10.5 (simulator + k6 report),
§12 (RUNBOOK, SECURITY-CHECKLIST), §13 (ArchitectureTests rows, IntegrationTests), §14 phases 6
and 8, §16 (release gates in the runbook). PRD §10, §12 acceptance 13 and 16.

**Assumed from phases 0–5 and 7** (names exactly as in the master's Cross-phase contracts and
"additions" blocks; nothing here redefines them):
- **Phase 0:**
  - `IAuditTrail`, `IAuditVerifier`, `AuditVerificationResult`, `AuditActor`, `AdminActor.ToAudit()`
  - `ICorrelationContext`, `CorrelationHeaders`, `ErrorResponse`, `EndpointResult.Fail`
  - `Error.RateLimited`, `DatabaseMigrator`
  - host `--migrate`, `--seed`, `--worker`, `--probe`
  - `HexagonalBoundaryTests.{ModuleNames, SanctionedReferences}`, `BuildDirectoryTests`,
    `DocumentationTests`
  - the `build/compose` stacks and `ci.env`
  - `audit.audit_events` (columns `action, target_type, target_id, actor_user_id, actor_role, correlation_id`)
- **Phase 1:**
  - `AuthPolicies`, `AdminPermissions`, `AdminActor.TryFrom`, `.RequirePermission(...)` / `[RequirePermission]`
  - `AdminJwtFactory.{CreateSuperAdminToken, CreateStaffToken, CreateShopToken}`
  - `RateLimitingSetup.AddEwaysRateLimiting` (UseAuthentication runs before UseRateLimiter)
  - `IPartnerAdminService`, `ICustomerAdminQueries` (`CustomerAdminDetail`, `ContactChangeView`)
  - `Acceptance13StaffPermissionTests`, the partner/settings/customer admin routes
- **Phases 2–3:**
  - Catalog and Pricing admin routes and GraphQL fields (Tasks 10–15 and 25–27)
  - `ICatalogAdminQueries`, `MoneyText`
  - storefront `shop*` GraphQL fields and anonymous `/api/v1/shop/*` routes, the `--seed` catalog
  - `PostgresFixture.CreateFactory()` with `[Collection(PostgresSuite.Name)]`
- **Phases 4–5:**
  - `ISalesAdminQueries` and its DTOs (`EwaysVNext.Modules.Sales.QueryApplication.Admin`)
  - `IOrderAdminService`, `IInvoiceService`, `DocumentAccess.Admin`
  - `SalesApiFactory` (stub partner keys `pk_partnerA_…`), `SalesTestData.EnsureSeededAsync`
  - `PartnerOpenApiExamples`, `tools/CompositionSimulator`, `tests/load/partner-orders.js`
- **Phase 7:**
  - `ISourcingAdminQueries.GetDashboardCountsAsync(DateTimeOffset since)` → `SourcingDashboardCounts`
  - the `/api/v1/admin/imports/...` routes and GraphQL fields
  - the staged-record seeding helper used by `SourcingAdminQueriesTests`

**Conventions:**
- Unit: `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "*.<Class>"`.
- Integration: `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "*.<Class>"`.
- Architecture: `dotnet test --project tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj --filter-class "*.<Class>"`.
- Every commit is `git commit -m "<subject>" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`.
  Each step below names only the subject.
- Admin wire format follows Sales' admin DTOs. Money is `*Minor` `long` plus `currency`. Every id
  that crosses the wire is `[Snowflake]`. Success bodies are `EndpointResult`'s `{ message, data }`.

**Design calls made here (for review):**
1. **Admin has four projects:** Application, Adapter/Outbound, Adapter/Inbound/{Rest,GraphQL} and
   Installer. It has no QueryApplication and no DbContext (ADR-0006 "lean").
   - Admin.Application keeps its own view records (AGENTS.md rule 2).
   - Gateways in Admin.Adapter.Outbound map provider DTOs into those view records.
2. **The "order detail with documents" composition is a new route,** `GET /api/v1/admin/orders/{id}/360`.
   Sales already owns `GET /api/v1/admin/orders/{id}`. The 360 adds the customer profile and the
   admin PDF path of each document.
3. **The phase-0 built-in Admin rule is tightened.** Phase 0 let `Admin.Adapter.Outbound` reference
   any `.QueryApplication` or `.Application`. It is replaced by the explicit
   `AdminReadReferences` rows `Sales`, `Sourcing` and `Customer`. These are QueryApplication only,
   and no Application of another module may be referenced.
4. **The `admin` rate-limit policy is made usable for an admin UI.** Its limit becomes
   `RateLimiting:AdminPermitPerMinute`, default 120 (was a hard-coded 10). Its partition becomes
   `admin:{sub}`.
   - The `/graphql` endpoint gets a `graphql` policy. It applies the admin partition to Backoffice
     principals and no extra limit to everyone else; the phase-1 global limiter still applies to them.
   - The spec requires one endpoint, so the limit cannot be bound to a separate admin endpoint.
5. **The GraphQL depth and cost limits apply to the whole schema.** Depth is 10, and introspection
   fields are skipped. Cost uses `MaxFieldCost` = `MaxTypeCost` = 20 000, enforced. One executor
   cannot hold per-field depth limits, and the storefront queries are shallower than 10.
6. **New permission `AdminPermissions.AuditVerify = "audit.verify"`,** super_admin only.
7. **Admin writes that live outside `EwaysVNextDbContext` are audited by a separate save.** These
   are the RBAC GraphQL mutations, which use `AuthorizationDbContext`. They write the audit row
   after the RBAC save, through `IStandaloneAuditWriter`, as a documented non-atomic edge.
8. **`/openapi/v1.json` is served in every environment** (`OpenApi:Expose`, default true), because
   the partner contract is public. Scalar stays Development-only.
9. **The smoke and load stack is production compose plus `build/compose/docker-compose.smoke.yml`.**
   - The override sets `ASPNETCORE_ENVIRONMENT=Staging`, so `--seed` is allowed.
   - It publishes the api, keycloak, minio and postgres ports on 127.0.0.1.
   - The smoke script gets a Backoffice token without weakening the committed realm. It uses the
     Keycloak admin REST API to create a temporary super_admin user and turn on direct grants for
     `backoffice-console` for the run, then restores both in a `trap`.

---

## File Structure

```
src/Modules/Admin/                                              (new; ./New-EwaysModule.ps1 -ModuleName Admin, then delete its QueryApplication project)
├── Application/
│   ├── Views/{DashboardViews,CustomerViews,OrderViews,AuditViews}.cs   local read models (no provider types)
│   ├── Ports/Outbound/{ISalesReadPort,ISourcingReadPort,ICustomerReadPort}.cs
│   ├── Ports/Inbound/{IAdminDashboardService,ICustomer360Service,IOrder360Service,IAuditVerificationService}.cs
│   ├── Services/{AdminDashboardService,Customer360Service,Order360Service,AuditVerificationService}.cs
│   ├── AdminErrors.cs, DependencyInjection.cs                         AddAdminApplication()
├── Adapter/Outbound/Gateways/{SalesReadGateway,SourcingReadGateway,CustomerReadGateway}.cs, DependencyInjection.cs
├── Adapter/Inbound/Rest/{AdminCompositionEndpoints,AuditVerificationEndpoints}.cs
├── Adapter/Inbound/GraphQL/{AdminCompositionQueries,AdminGraphQLTypes,DependencyInjection}.cs
└── Installer/ModuleInstaller.cs                                     AddAdminModule(IServiceCollection), AddAdminGraphQL(IRequestExecutorBuilder)

src/BuildingBlocks/Presentation.Endpoint/
├── Security/RateLimitPolicies.cs                                   (new) "admin", "graphql"
├── Security/AdminEndpointConventions.cs                            (new) RequireAdmin(staff|super, permission) = policy + permission + rate limit
└── GraphQL/{DependencyInjection.cs (modify), GraphQLLimits.cs (new)}
src/BuildingBlocks/Audit/Application/IStandaloneAuditWriter.cs      (new) + Implementation/StandaloneAuditWriter.cs
src/BuildingBlocks/Core/Application/Security/IAdminActorAccessor.cs (new) + Presentation.Endpoint/Security/HttpAdminActorAccessor.cs
src/Authorization/Authorization.Abstractions/Policies/AdminPermissions.cs, .../Seeding/AdminPermissionMatrix.cs (modify: AuditVerify)
src/Authorization/Authorization.Endpoints/**/*Mutations.cs          (modify: audit via IStandaloneAuditWriter)
src/Host/EwaysVNext.Host/{Program.cs, RateLimiting/RateLimitingSetup.cs, OpenApi/PartnerApiKeyOpenApiTransformer.cs}
src/Modules/Catalog/QueryApplication/Products/{ICatalogAdminQueries,CatalogAdminQueries}.cs  (modify: duplicate candidates)
src/Modules/Catalog/Adapter/Inbound/{Rest/Admin/CatalogAdminEndpoints.cs, GraphQL/Products/ProductAdminQueries.cs} (modify)
src/Modules/Sourcing/Adapter/Inbound/{Rest/StagedRecordImageEndpoints.cs (new), GraphQL/*Queries.cs (modify)}
src/Modules/Sales/Adapter/Inbound/{GraphQL/Admin/AdminOrderGraph.cs, Rest/Partner/PartnerOpenApiExamples.cs} (modify)
tools/smoke/smoke.sh, build/compose/docker-compose.smoke.yml        (new)
tests/load/partner-orders.js                                         (modify: summaryTrendStats, currency tag)
docs/api/bruno/{bruno.json,package.json,package-lock.json,environments/{local,smoke}.bru,partner/*.bru,shop/*.bru,admin/*.bru}
docs/RUNBOOK.md, docs/SECURITY-CHECKLIST.md, docs/reports/{coverage-and-load.md,phase-6.md,phase-8.md}
AGENTS.md, docs/TESTING-GUARANTEES.md, docs/ONBOARDING.md          (modify)

tests/UnitTests/Admin/{AdminGatewayMappingTests,AdminDashboardServiceTests,Admin360ServiceTests,AuditVerificationServiceTests}.cs
tests/UnitTests/GraphQLLimitsTests.cs
tests/ArchitectureTests/{HexagonalBoundaryTests.cs (modify), AdminArchitectureTests.cs, OpsPackageTests.cs, BrunoCollectionTests.cs}
tests/IntegrationTests/Admin/{AdminCompositionApiTests,AdminGraphQLTests,AdminPrd10CoverageTests}.cs
tests/IntegrationTests/Security/{EndpointAuthorizationTests,AdminRateLimitTests,AdminAuditCoverageTests,AdminWriteCases,AuditWorld}.cs
tests/IntegrationTests/Identity/Acceptance13StaffPermissionTests.cs (extend)
tests/IntegrationTests/Contracts/{OpenApiSnapshotTests,GraphQLSchemaSnapshotTests,PartnerOpenApiExampleTests}.cs
tests/IntegrationTests/Contracts/Snapshots/{openapi-v1.json,schema.graphql}
```

---

# Phase 6 — Admin

### Task 1: Admin module scaffold and its boundary rules

**Files:**
- Create:
  - the Admin projects (run `./New-EwaysModule.ps1 -ModuleName Admin`, then delete
    `src/Modules/Admin/QueryApplication` and its `.slnx` entry)
  - `src/Modules/Admin/Adapter/Inbound/GraphQL/EwaysVNext.Modules.Admin.Adapter.Inbound.GraphQL.csproj`,
    copying Catalog's GraphQL csproj references
  - `tests/ArchitectureTests/AdminArchitectureTests.cs`
- Modify:
  - `tests/ArchitectureTests/HexagonalBoundaryTests.cs`
  - `EwaysVNext.slnx`
  - `src/Modules/Admin/Adapter/Outbound/*.csproj`: reference
    `EwaysVNext.Modules.{Sales,Sourcing,Customer}.QueryApplication`
  - `src/Modules/Admin/Application/*.csproj`: reference `EwaysVNext.Domain`, `EwaysVNext.Outcomes`,
    `EwaysVNext.Core.Application` and `EwaysVNext.Audit.Application` only
  - `src/Host/EwaysVNext.Host/Program.cs` (`AddAdminModule()`)

**Interfaces:**
- Consumes: `HexagonalBoundaryTests.ModuleNames`, `ForbiddenCrossModuleReferences` (phase 0).
- Produces:
```csharp
public static TheoryData<string> AdminReadReferences => new() { "Sales", "Sourcing", "Customer" }; // Admin.Adapter.Outbound → {x}.QueryApplication
public static IServiceCollection AddAdminModule(this IServiceCollection services);              // Installer; registers Application + Outbound + endpoints
```

- [ ] **Step 1: Write the failing tests.**
  1. In `HexagonalBoundaryTests`, add `"Admin"` to `ModuleNames`.
  2. Replace phase 0's rule (b) in `ForbiddenCrossModuleReferences` with: `module == "Admin"`, the
     assembly is `EwaysVNext.Modules.Admin.Adapter.Outbound`, and the target is
     `EwaysVNext.Modules.{p}.QueryApplication` with `p` in `AdminReadReferences`.
  3. `QueryApplication_Never_Writes` must tolerate a missing directory, so `SourceFiles` returns
     empty for one.
  4. Add these tests:
```csharp
[Theory, MemberData(nameof(AdminReadReferences))]
public void Admin_Read_Row_Is_Well_Formed(string provider)
{
    ModuleNames.Should().Contain(provider);
    provider.Should().NotBeOneOf("Admin", "Partners");
}

// AdminArchitectureTests
[Fact] public void Admin_Composition_Layers_Must_Be_Aggregate_Free() =>
    Types.InAssemblies([Load("EwaysVNext.Modules.Admin.Application"), Load("EwaysVNext.Modules.Admin.Adapter.Inbound.Rest"),
                        Load("EwaysVNext.Modules.Admin.Adapter.Inbound.GraphQL")])
        .ShouldNot().HaveDependencyOnAny("EwaysVNext.Domain.Common.AggregateRoot", "EwaysVNext.Domain.Common.Entity",
            "EwaysVNext.Domain.Common.IAggregateRoot", "EwaysVNext.Domain.Common.ValueObject", "Microsoft.EntityFrameworkCore",
            "EwaysVNext.Persistence", "EwaysVNext.Database")
        .GetResult().IsSuccessful.Should().BeTrue();
[Fact] public void Admin_Owns_No_DbContext_And_No_QueryApplication()
{
    Directory.Exists(Path.Combine(Root, "src/Modules/Admin/QueryApplication")).Should().BeFalse();
    Directory.Exists(Path.Combine(Root, "src/Database/Admin")).Should().BeFalse();
    AllModuleTypes("Admin").Should().NotContain(t => typeof(DbContext).IsAssignableFrom(t));
}
[Fact] public void Admin_References_No_Other_Modules_Application() =>
    Load("EwaysVNext.Modules.Admin.Adapter.Outbound").GetReferencedAssemblies().Select(a => a.Name!)
        .Where(n => n.StartsWith("EwaysVNext.Modules.") && !n.StartsWith("EwaysVNext.Modules.Admin.") && !n.EndsWith(".QueryApplication"))
        .Should().BeEmpty();
```
- [ ] **Step 2: Run, expect FAIL.**
  `dotnet test --project tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj --filter-class "*.AdminArchitectureTests"`.
  The Admin assemblies do not exist yet.
- [ ] **Step 3: Implement.**
  - Keep the scaffolded projects empty except `DependencyInjection.cs` / `ModuleInstaller.cs`.
  - Add the csproj references listed under Files and the Program.cs registration.
- [ ] **Step 4: Run, expect PASS.** Run the full architecture suite:
  `dotnet test --project tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj`.
  Mutation check:
  1. Add a reference from `Admin.Adapter.Outbound` to `EwaysVNext.Modules.Catalog.Application`
     and use one type from it.
  2. Expect `Module_References_No_Other_Module_Unless_Sanctioned(Admin)` and
     `Admin_References_No_Other_Modules_Application` to fail.
  3. Revert.
- [ ] **Step 5: Commit.** `feat(admin): data-less Admin composition module scaffold with explicit read-reference rows`

---

### Task 2: Admin views, read ports and gateways

**Files:**
- Create:
  - `src/Modules/Admin/Application/Views/*.cs`
  - `src/Modules/Admin/Application/Ports/Outbound/*.cs`
  - `src/Modules/Admin/Adapter/Outbound/Gateways/*.cs` and `Adapter/Outbound/DependencyInjection.cs`
    (`AddAdminOutboundAdapters()`)
- Test: `tests/UnitTests/Admin/AdminGatewayMappingTests.cs`

**Interfaces:**
- Consumes:
  - `ISalesAdminQueries.{GetDashboardAsync, ListCustomerOrdersAsync, GetOrderAsync}` and its DTOs
  - `ISourcingAdminQueries.GetDashboardCountsAsync`
  - `ICustomerAdminQueries.GetAsync` → `Result<CustomerAdminDetail>`
- Produces (namespace `EwaysVNext.Modules.Admin.Application.Views`; ids are `[Snowflake]`):
```csharp
public sealed record OrderTotalsRow(string Window, string Channel, string Currency, int OrderCount, long TotalMinor);
public sealed record CurrencyAmount(string Currency, long AmountMinor);
public sealed record PendingPayments(int Count, IReadOnlyList<CurrencyAmount> AmountsByCurrency, DateTimeOffset? OldestCreatedAt);
public sealed record CompositionWindow(string Window, int Attempts, int Succeeded, decimal SuccessRate, long? P95DurationMs);
public sealed record CompositionFailure(DateTimeOffset At, string ErrorCode, string Currency, long TargetMinor, long DurationMs, long? PartnerId);
public sealed record SalesSnapshot(IReadOnlyList<OrderTotalsRow> OrderTotals, PendingPayments PendingWebsitePayments,
    CompositionWindow Composition24h, CompositionWindow Composition7d, IReadOnlyList<CompositionFailure> RecentFailures);
public sealed record ImportSnapshot(int JobsQueued, int JobsRunning, int JobsPaused, int JobsFailed7d, int JobsCompleted7d,
    int RecordsNeedsReview, int RecordsValidUnpublished, int RecordsInvalid, int RecordsPublished7d);
public sealed record FailureCodeCount(string ErrorCode, int Count);
public sealed record AdminDashboard(DateTimeOffset GeneratedAt, SalesSnapshot Sales, ImportSnapshot Imports, IReadOnlyList<FailureCodeCount> RecentFailureCodes);

public sealed record OrderStatusView(string Order, string Payment, string? PaymentSource, string Fulfillment, string? FulfillmentSource,
    string FulfillmentStatement, string Documents);
public sealed record OrderRow(long Id, string Number, string Channel, long? PartnerId, string? ExternalOrderId, long CustomerId,
    string Currency, long TotalMinor, long RefundedMinor, OrderStatusView Status, DateTimeOffset CreatedAt);
public sealed record DocumentView(long Id, string Type, string Number, DateTimeOffset IssuedAt, long? RelatedDocumentId, string PdfPath); // "/api/v1/admin/documents/{id}/pdf"
public sealed record PaymentView(long Id, string Source, string? Method, string? Reference, Guid? VerifiedBy, DateTimeOffset RecordedAt, long AmountMinor);
public sealed record RefundView(long Id, long AmountMinor, string Reason, Guid RecordedBy, DateTimeOffset RecordedAt, string? ExternalReference, long CreditNoteDocumentId);
public sealed record OrderSheet(OrderRow Summary, string? ExternalCustomerId, long UsdEquivalentMinor, string CorrelationId, int LineCount,
    IReadOnlyList<PaymentView> Payments, IReadOnlyList<DocumentView> Documents, IReadOnlyList<RefundView> Refunds);
public sealed record AddressView(string Line1, string? Line2, string City, string PostalCode, string CountryIso2);
public sealed record ContactChangeEntry(string Field, string? OldValue, string? NewValue, DateTimeOffset ChangedAt, long? SourcePartnerId, string CorrelationId);
public sealed record CustomerProfile(long Id, string Kind, long? PartnerId, string? ExternalCustomerId, string? Email, string? Phone,
    string? FullName, string? CompanyName, string Status, bool EmailVerified, AddressView? BillingAddress, string? RegistrationNumber,
    DateTimeOffset CreatedAt, IReadOnlyList<ContactChangeEntry> ContactHistory);
public sealed record Customer360(CustomerProfile Customer, IReadOnlyList<OrderRow> RecentOrders);
public sealed record Order360(OrderSheet Order, CustomerProfile? Customer);
public sealed record AuditVerificationView(bool Intact, long SealedCount, long? FirstMismatchEventId, long UnsealedBelowWatermark, DateTimeOffset VerifiedAt);

// EwaysVNext.Modules.Admin.Application.Ports.Outbound
public interface ISalesReadPort {
  Task<SalesSnapshot> GetDashboardAsync(DateTimeOffset now, CancellationToken ct = default);
  Task<IReadOnlyList<OrderRow>> ListCustomerOrdersAsync(long customerId, int limit, CancellationToken ct = default);
  Task<Result<OrderSheet>> GetOrderAsync(long orderId, CancellationToken ct = default);
}
public interface ISourcingReadPort { Task<ImportSnapshot> GetDashboardCountsAsync(DateTimeOffset since, CancellationToken ct = default); }
public interface ICustomerReadPort { Task<Result<CustomerProfile>> GetAsync(long customerId, CancellationToken ct = default); }
```
- Gateways (`internal sealed`, scoped):
  - They map field by field by hand (DDD-GUIDELINES §9).
  - `LineCount = dto.Lines.Count`.
  - `PdfPath = $"/api/v1/admin/documents/{id}/pdf"`.
  - `ImportSnapshot` maps `JobsFailedSince → JobsFailed7d`, `JobsCompletedSince → JobsCompleted7d`
    and `RecordsPublishedSince → RecordsPublished7d`.
  - A provider `Result` error passes through unchanged.

- [ ] **Step 1: Write the failing tests** (NSubstitute for the three provider query interfaces):
```csharp
[Fact] public async Task Sales_Dashboard_Maps_Every_Field()
{
    sales.GetDashboardAsync(Now, default).Returns(FullDashboardDto());   // every list has 2 items, every nullable set
    var s = await new SalesReadGateway(sales).GetDashboardAsync(Now);
    s.Should().BeEquivalentTo(FullDashboardDto(), o => o.ExcludingMissingMembers());
    s.Composition24h.SuccessRate.Should().Be(0.8000m);
}
[Fact] public async Task Order_Sheet_Counts_Lines_And_Builds_Admin_Pdf_Paths()
{
    var sheet = (await new SalesReadGateway(sales).GetOrderAsync(42)).Value!;
    sheet.LineCount.Should().Be(3);
    sheet.Documents.Select(d => d.PdfPath).Should().Equal("/api/v1/admin/documents/7/pdf", "/api/v1/admin/documents/8/pdf");
}
[Fact] public async Task Sales_Not_Found_Passes_Through() =>
    (await new SalesReadGateway(sales).GetOrderAsync(404)).Error!.HttpStatus.Should().Be(404);
[Fact] public async Task Sourcing_Counts_Map_Since_Fields_To_7d() =>
    (await new SourcingReadGateway(sourcing).GetDashboardCountsAsync(Now.AddDays(-7)))
        .Should().Be(new ImportSnapshot(1, 2, 3, 4, 5, 6, 7, 8, 9));    // source: new SourcingDashboardCounts(1,2,3,4,5,6,7,8,9)
[Fact] public async Task Customer_Detail_Maps_History_Newest_First_And_Address()
```
- [ ] **Step 2: Run, expect FAIL.**
  `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "*.AdminGatewayMappingTests"`.
- [ ] **Step 3: Implement** the views, ports and gateways. Register them in `AddAdminOutboundAdapters()`.
- [ ] **Step 4: Run, expect PASS.** Run the same command, then the full architecture suite.
- [ ] **Step 5: Commit.** `feat(admin): local admin views and read gateways over Sales, Sourcing and Customer read sides`

---

### Task 3: Dashboard composition service

**Files:**
- Create:
  - `src/Modules/Admin/Application/Ports/Inbound/IAdminDashboardService.cs`
  - `Services/AdminDashboardService.cs`
  - `AdminErrors.cs`
  - `DependencyInjection.cs`
- Test: `tests/UnitTests/Admin/AdminDashboardServiceTests.cs`

**Interfaces:**
```csharp
public interface IAdminDashboardService { Task<AdminDashboard> GetAsync(CancellationToken ct = default); }
public static class AdminErrors {
  public static readonly Error PageSizeInvalid = new("ADMIN_PAGE_SIZE_INVALID", "orderLimit must be 1–100.", 400);   // ADR-0007 paging contract
  public static readonly Error Forbidden = Error.Forbidden;
}
```
- Rules:
  - Read `IClock` once, as `now`.
  - Call `sales.GetDashboardAsync(now)`, **then** `sourcing.GetDashboardCountsAsync(now - 7 days)`.
    The calls are sequential, never `Task.WhenAll`, because both gateways share the scoped
    `EwaysVNextDbContext`.
  - `RecentFailureCodes` groups `Sales.RecentFailures` by `ErrorCode`, ordered by count
    descending, then code ascending.

- [ ] **Step 1: Write the failing tests** (`FixedClock` at `2026-10-28T12:00:00Z`):
```csharp
[Fact] public async Task Composes_Sales_And_Imports_With_Seven_Day_Window()
{
    var d = await Svc().GetAsync();
    d.GeneratedAt.Should().Be(Now);
    await sourcing.Received(1).GetDashboardCountsAsync(Now.AddDays(-7), Arg.Any<CancellationToken>());
    await salesPort.Received(1).GetDashboardAsync(Now, Arg.Any<CancellationToken>());
}
[Fact] public async Task Failure_Codes_Are_Counted_And_Ordered()
{   // RecentFailures: TIMEOUT, UNAVAILABLE, TIMEOUT, FX_RATE_UNAVAILABLE
    (await Svc().GetAsync()).RecentFailureCodes.Should().Equal(
        new FailureCodeCount("COMPOSITION_TIMEOUT", 2), new FailureCodeCount("EXACT_COMPOSITION_UNAVAILABLE", 1), new FailureCodeCount("FX_RATE_UNAVAILABLE", 1));
}
[Fact] public async Task Ports_Are_Called_Sequentially()
{   // salesPort completes only after a TaskCompletionSource is released; sourcing must not be called before
    var pending = Svc().GetAsync();
    await sourcing.DidNotReceiveWithAnyArgs().GetDashboardCountsAsync(default, default);
    gate.SetResult(); await pending;
    await sourcing.ReceivedWithAnyArgs(1).GetDashboardCountsAsync(default, default);
}
```
- [ ] **Step 2: Run, expect FAIL.**
  `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "*.AdminDashboardServiceTests"`.
- [ ] **Step 3: Implement** `sealed class AdminDashboardService(ISalesReadPort, ISourcingReadPort, IClock)`.
  `AddAdminApplication()` registers it as scoped.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit.** `feat(admin): dashboard composition (sales totals, pending payments, composition 24h/7d, imports, failure codes)`

---

### Task 4: Customer 360 and order 360

**Files:**
- Create:
  - `Ports/Inbound/{ICustomer360Service,IOrder360Service}.cs`
  - `Services/{Customer360Service,Order360Service}.cs`
- Test: `tests/UnitTests/Admin/Admin360ServiceTests.cs`

**Interfaces:**
```csharp
public interface ICustomer360Service { Task<Result<Customer360>> GetAsync(long customerId, int orderLimit = 20, CancellationToken ct = default); }
public interface IOrder360Service { Task<Result<Order360>> GetAsync(long orderId, CancellationToken ct = default); }
```
- Customer 360:
  - An `orderLimit` outside 1..100 returns `AdminErrors.PageSizeInvalid` before any IO.
  - If the customer is not found, its error is returned and orders are not read.
  - Otherwise orders come from `ListCustomerOrdersAsync(customerId, orderLimit)`.
- Order 360:
  - The order error passes through.
  - Then the customer is loaded by `Summary.CustomerId`.
  - A customer failure yields `Customer = null` and logs a Warning, so the order is still shown.
    It never becomes a 404 for an order that exists.

- [ ] **Step 1: Write the failing tests:**
  - `Customer360_Returns_Profile_And_Orders`: the order list equals the port's list, in order.
  - `Customer360_Unknown_Customer_Skips_Orders`: `salesPort.DidNotReceiveWithAnyArgs().ListCustomerOrdersAsync`
    is checked, and status is 404.
  - `Customer360_OrderLimit_0_And_101_Are_ADMIN_PAGE_SIZE_INVALID`: an `[InlineData]` theory.
  - `Order360_Includes_Customer`.
  - `Order360_Missing_Customer_Still_Returns_Order`: `Customer` is null and `IsSuccess` is true.
  - `Order360_Unknown_Order_Is_404_Without_Customer_Read`.
- [ ] **Step 2: Run, expect FAIL.**
  `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "*.Admin360ServiceTests"`.
- [ ] **Step 3: Implement** both sealed services and register them.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit.** `feat(admin): customer 360 and order 360 composition`

---

### Task 5: Audit verification service and the `audit.verify` permission

**Files:**
- Create:
  - `Ports/Inbound/IAuditVerificationService.cs`
  - `Services/AuditVerificationService.cs`
  - Authorization migration `<ts>_AdminAuditVerifyPermission`
- Modify:
  - `src/Authorization/Authorization.Abstractions/Policies/AdminPermissions.cs`
  - `src/Authorization/Authorization.Implementation/Seeding/AdminPermissionMatrix.cs`
  - `tests/UnitTests/Identity/AdminPermissionMatrixTests.cs`
- Test: `tests/UnitTests/Admin/AuditVerificationServiceTests.cs`

**Interfaces:**
```csharp
public interface IAuditVerificationService { Task<Result<AuditVerificationView>> VerifyAsync(AdminActor actor, CancellationToken ct = default); }
// AdminPermissions (extend): public const string AuditVerify = "audit.verify";   // super_admin only, new fixed GUID literals in the matrix row
```

- [ ] **Step 1: Write the failing tests:**
  - `Staff_Is_Forbidden_Before_Verifying`: the result is 403, and
    `verifier.DidNotReceiveWithAnyArgs().VerifyAsync` is checked.
  - `Super_Admin_Gets_The_Verifier_Result_Stamped_With_Now`: the verifier returns
    `new AuditVerificationResult(false, 12, 9, 1)`, and the view is `(false, 12, 9, 1, Now)`.
  - In `AdminPermissionMatrixTests`, add
    `[InlineData("staff", AdminPermissions.AuditVerify, false)]` and
    `[InlineData("super_admin", AdminPermissions.AuditVerify, true)]`.
- [ ] **Step 2: Run, expect FAIL.**
  `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "*.AuditVerificationServiceTests"`.
  Repeat with `--filter-class "*.AdminPermissionMatrixTests"`.
- [ ] **Step 3: Implement** the service and the matrix row. Then create the migration:
  `dotnet ef migrations add AdminAuditVerifyPermission -p src/Authorization/Authorization.Implementation/Authorization.Implementation.csproj -s src/Host/EwaysVNext.Host/EwaysVNext.Host.csproj -c AuthorizationDbContext -o Persistence/Migrations`.
- [ ] **Step 4: Run, expect PASS** (both classes, including `Matrix_Covers_Every_AdminPermissions_Constant`).
- [ ] **Step 5: Commit.** `feat(admin): super-admin audit verification and the audit.verify permission`

---

### Task 6: Admin REST endpoints and the shared admin endpoint convention

**Files:**
- Create:
  - `src/BuildingBlocks/Presentation.Endpoint/Security/{RateLimitPolicies,AdminEndpointConventions}.cs`
  - `src/Modules/Admin/Adapter/Inbound/Rest/{AdminCompositionEndpoints,AuditVerificationEndpoints}.cs`
- Test: `tests/IntegrationTests/Admin/AdminCompositionApiTests.cs`

**Interfaces:**
```csharp
public static class RateLimitPolicies { public const string Admin = "admin"; public const string GraphQL = "graphql"; }
public static class AdminEndpointConventions {
  // RequireAuthorization(super ? AdminSuper : AdminStaff) + RequirePermission(permission) + RequireRateLimiting(RateLimitPolicies.Admin)
  public static TBuilder RequireAdmin<TBuilder>(this TBuilder builder, string permission, bool superOnly = false) where TBuilder : IEndpointConventionBuilder;
}
```

| Method & path | Gate | Port | 200 `data` |
|---|---|---|---|
| `GET /api/v1/admin/dashboard` | `RequireAdmin(OrdersManage)` | `IAdminDashboardService.GetAsync` | `AdminDashboard` |
| `GET /api/v1/admin/customers/{id:long}/360?orderLimit=20` | `RequireAdmin(CustomersManage)` | `ICustomer360Service.GetAsync` | `Customer360` |
| `GET /api/v1/admin/orders/{id:long}/360` | `RequireAdmin(OrdersManage)` | `IOrder360Service.GetAsync` | `Order360` |
| `GET /api/v1/admin/audit/verify` | `RequireAdmin(AuditVerify, superOnly: true)` | `IAuditVerificationService.VerifyAsync(actor)` | `AuditVerificationView` |

The audit handler builds the actor with `AdminActor.TryFrom` and returns `EndpointResult.Forbidden()`
when that fails.

- [ ] **Step 1: Write the failing tests.** The class uses `[Collection(PostgresSuite.Name)]` and
  phase 4–5's `SalesApiFactory`. Partner orders use `pk_partnerA_…`.
```csharp
[Fact] public async Task Dashboard_Counts_A_New_Partner_Order()
{
    var before = await Dashboard(Staff);
    (await _partnerA.PostOrderAsync(Ext(), "500.00", "USD", NewCustomer("c-dash", "dash@example.test", "+6591230101"))).StatusCode.Should().Be(HttpStatusCode.Created);
    var after = await Dashboard(Staff);
    Row(after, "24h", "PartnerApi", "USD").OrderCount.Should().Be(Row(before, "24h", "PartnerApi", "USD").OrderCount + 1);
    Row(after, "24h", "PartnerApi", "USD").TotalMinor.Should().Be(Row(before, "24h", "PartnerApi", "USD").TotalMinor + 50_000);
    after.Sales.Composition24h.Attempts.Should().Be(before.Sales.Composition24h.Attempts + 1);
    after.Imports.Should().NotBeNull();
}
[Fact] public async Task Customer360_Shows_Profile_Contact_History_And_Orders_Newest_First()
{   // order 1: c-360 email a@x.test; order 2: same externalId, email b@x.test (sync after create)
    var c = await Get<Customer360>(Staff, $"/api/v1/admin/customers/{customerId}/360");
    c.Customer.ExternalCustomerId.Should().Be("c-360");
    c.Customer.ContactHistory.Should().ContainSingle(h => h.Field == "Email" && h.OldValue == "a@x.test" && h.NewValue == "b@x.test");
    c.RecentOrders.Select(o => o.Id).Should().Equal(order2Id, order1Id);
}
[Fact] public async Task Order360_Has_Commercial_Invoice_With_Admin_Pdf_Path_And_Customer()
{
    var o = await Get<Order360>(Staff, $"/api/v1/admin/orders/{orderId}/360");
    o.Order.Documents.Should().ContainSingle(d => d.Type == "Commercial" && d.PdfPath == $"/api/v1/admin/documents/{d.Id}/pdf");
    o.Customer!.Id.Should().Be(o.Order.Summary.CustomerId);
    (await Staff.GetAsync(o.Order.Documents[0].PdfPath)).StatusCode.Should().Be(HttpStatusCode.Redirect);
}
[Theory, InlineData("/api/v1/admin/customers/1/360"), InlineData("/api/v1/admin/orders/1/360")]
public async Task Unknown_Id_Is_404(string path) => (await Staff.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.NotFound);
[Fact] public async Task OrderLimit_101_Is_400_ADMIN_PAGE_SIZE_INVALID() { }
[Fact] public async Task Audit_Verify_Is_Super_Only_And_Reports_An_Intact_Chain()
{   // staff → 403; super → 200, data.intact == true, data.unsealedBelowWatermark == 0
}
[Fact] public async Task Shop_And_Anonymous_Are_Refused() { /* shop token → 403, no token → 401 on /api/v1/admin/dashboard */ }
```
- [ ] **Step 2: Run, expect FAIL** (routes return 404).
  `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "*.AdminCompositionApiTests"`.
- [ ] **Step 3: Implement** the conventions and endpoints (`IEndpoint` classes, `ToHttpResult()`).
  Register `AddEndpoints(typeof(AdminCompositionEndpoints).Assembly)` in `AddAdminModule`.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit.** `feat(admin): REST dashboard, customer 360, order 360 and audit verification`

---

### Task 7: Admin GraphQL fields

**Files:**
- Create: `src/Modules/Admin/Adapter/Inbound/GraphQL/{AdminCompositionQueries,AdminGraphQLTypes,DependencyInjection}.cs`
- Modify: `src/Host/EwaysVNext.Host/Program.cs`, by adding `.AddAdminGraphQL()` to the GraphQL builder
- Test: `tests/IntegrationTests/Admin/AdminGraphQLTests.cs`

**Interfaces:** These are fields on the root `Query`. Each one carries `[Authorize(Policy = …)]` and
`[RequirePermission(…)]` as in Task 6, and calls the same inbound port.
- `adminDashboard: AdminDashboard!`
- `adminCustomer360(id: ID!, orderLimit: Int = 20): Customer360!`
- `adminOrder360(id: ID!): Order360!`
- `adminAuditVerification: AuditVerification!` (super)

`AdminGraphQLTypes` binds every `long` id property to `IdType`, following the `SnowflakeIdGraphQLTests`
convention. Errors go through `ResultGraphQLExtensions.ValueOrThrow`.

- [ ] **Step 1: Write the failing tests** (`AdminGraphQLClient`):
  - `GraphQL_And_REST_Dashboards_Agree`: both are read back to back with no write in between. They
    compare equal except for `generatedAt`.
  - `GraphQL_Customer360_Matches_REST`.
  - `GraphQL_Order360_Matches_REST`.
  - `Staff_AdminAuditVerification_Is_AUTH_NOT_AUTHORIZED`.
  - `Unknown_Order_Has_Error_Code_From_Sales`: `errors[0].extensions.code` equals the REST 404 body's `code`.
- [ ] **Step 2: Run, expect FAIL.**
  `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "*.AdminGraphQLTests"`.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit.** `feat(admin): GraphQL adminDashboard, adminCustomer360, adminOrder360, adminAuditVerification`

---

### Task 8: GraphQL depth, cost and admin rate limits

**Files:**
- Create: `src/BuildingBlocks/Presentation.Endpoint/GraphQL/GraphQLLimits.cs`
- Modify:
  - `src/BuildingBlocks/Presentation.Endpoint/GraphQL/DependencyInjection.cs`
  - `src/Host/EwaysVNext.Host/RateLimiting/RateLimitingSetup.cs`
  - the admin endpoint groups of every module. Replace each group's `RequireAuthorization(...)` /
    `RequirePermission(...)` pair with `RequireAdmin(...)`. The groups are in:
    - `Partners/.../PartnerAdminEndpoints.cs`
    - `Settings/.../SettingsAdminEndpoints.cs`
    - `Customer/.../CustomerAdminEndpoints.cs`
    - `Catalog/.../{CatalogAdminEndpoints,BrandAdminEndpoints,CategoryAdminEndpoints,AttributeAdminEndpoints}.cs`
    - `Pricing/.../PricingAdminEndpoints.cs`
    - `Sales/.../AdminOrderEndpoints.cs`
    - `Sourcing/Adapter/Inbound/Rest/*.cs`
- Test:
  - `tests/UnitTests/GraphQLLimitsTests.cs`
  - `tests/IntegrationTests/Security/AdminRateLimitTests.cs`

**Interfaces:**
```csharp
public static class GraphQLLimits { public const int MaxDepth = 10; public const double MaxFieldCost = 20_000; public const double MaxTypeCost = 20_000; }
// AddGraphQLBootstrap: .AddMaxExecutionDepthRule(GraphQLLimits.MaxDepth, skipIntrospectionFields: true)
//                      .ModifyCostOptions(o => { o.MaxFieldCost = GraphQLLimits.MaxFieldCost; o.MaxTypeCost = GraphQLLimits.MaxTypeCost; o.EnforceCostLimits = true; })
// MapGraphQLEndpoint: app.MapGraphQL().RequireRateLimiting(RateLimitPolicies.GraphQL)
// RateLimitingSetup: "admin"   → partition "admin:{sub}", FixedWindow, PermitLimit = RateLimiting:AdminPermitPerMinute (default 120), 1 min
//                    "graphql" → Backoffice tenant: the same "admin:{sub}" partition and limit; otherwise RateLimitPartition.GetNoLimiter("shop")
```

- [ ] **Step 1: Write the failing tests.**
  - Unit tests use a schema built with `AddGraphQLBootstrap()`. They add a test type
    `Node { child: Node, children(first: Int!): [Node!]! @cost(weight: "1") @listSize(slicingArguments: ["first"]) }`.
    The resolvers increment a counter.
```csharp
[Fact] public async Task Depth_10_Executes() { (await Run(NestedChild(9))).Errors.Should().BeNullOrEmpty(); }   // query{node{child×9}} = depth 10
[Fact] public async Task Depth_11_Is_Rejected_Before_Any_Resolver() { var r = await Run(NestedChild(10)); r.Errors.Should().NotBeEmpty(); r.Data.Should().BeNull(); counter.Should().Be(0); }
[Fact] public async Task Introspection_Is_Not_Depth_Limited() => (await Run(IntrospectionQuery)).Errors.Should().BeNullOrEmpty();
[Fact] public async Task Cost_Over_Limit_Is_Rejected_Before_Any_Resolver()
{   // node{children(first:100){children(first:100){children(first:100){id}}}} → estimated 10^6 > 20 000
    var r = await Run(Wide(100, 3)); r.Errors.Should().NotBeEmpty(); r.Data.Should().BeNull(); counter.Should().Be(0);
}
[Fact] public async Task Cost_Under_Limit_Executes() => (await Run(Wide(10, 2))).Errors.Should().BeNullOrEmpty();
```
  - Integration tests use the factory settings `RateLimiting:AdminPermitPerMinute = 3`.
```csharp
[Fact] public async Task Admin_Rest_Fourth_Request_In_A_Minute_Is_429_RATE_LIMITED()
{   // staff token GET /api/v1/admin/dashboard ×4 → 200,200,200,429; body code RATE_LIMITED, retryable true
}
[Fact] public async Task Admin_GraphQL_Fourth_Request_Is_429_But_Anonymous_Shop_GraphQL_Is_Not()
{   // staff POST /graphql { adminDashboard { generatedAt } } ×4 → 4th 429; then anonymous { currencies { code } } ×4 → all 200
}
[Fact] public async Task Two_Admins_Have_Separate_Buckets() { /* staff A ×3, staff B (other sub) 1 → 200 */ }
[Fact] public void Every_Admin_Rest_Endpoint_Uses_The_Admin_Rate_Limit_Policy()
{
    AdminRestEndpoints(factory).Where(e => e.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName != RateLimitPolicies.Admin)
        .Select(e => e.RoutePattern.RawText).Should().BeEmpty();     // AdminRestEndpoints = RouteEndpoints whose pattern starts with "/api/v1/admin"
}
```
- [ ] **Step 2: Run, expect FAIL.** Run `--filter-class "*.GraphQLLimitsTests"` against the unit project and
  `--filter-class "*.AdminRateLimitTests"` against the integration project.
- [ ] **Step 3: Implement** per the interfaces. Apply `RequireAdmin` in each listed group,
  keeping the group's existing policy (`AdminStaff` or `AdminSuper`) and permission.
- [ ] **Step 4: Run, expect PASS.** Then run the whole integration suite, since every admin route
  gained a limiter. The factory default for `RateLimiting:AdminPermitPerMinute` is set to
  `100000` in `PostgresFixture` settings, so other suites are not throttled.
  Mutation check:
  1. Remove `.AddMaxExecutionDepthRule(...)` and expect `Depth_11_Is_Rejected_Before_Any_Resolver` to fail.
  2. Set `EnforceCostLimits = false` and expect `Cost_Over_Limit_Is_Rejected_Before_Any_Resolver` to fail.
  3. Restore both.
- [ ] **Step 5: Commit.** `feat(graphql): depth 10, enforced cost limits, admin rate limit on admin REST and admin GraphQL callers`

---

### Task 9: Every endpoint and GraphQL field is authorized unless allowlisted

**Files:** Create `tests/IntegrationTests/Security/EndpointAuthorizationTests.cs`.

**Interfaces:** None produced. The test reads `EndpointDataSource` from `PostgresFixture.CreateFactory().Services`.
It also reads the schema via `IRequestExecutorResolver.GetRequestExecutorAsync()`.

- [ ] **Step 1: Write the tests.**
```csharp
// Patterns are compared after Regex.Replace(raw, @"\{[^}]+\}", "{}")
private static readonly (string Method, string Pattern, string Why)[] AnonymousAllowlist =
[
    ("GET", "/health", "probe"), ("GET", "/health/live", "probe"), ("GET", "/health/ready", "probe"),
    ("GET", "/openapi/{}.json", "public partner contract (design call 8)"),
    ("GET", "/api/v1/shop/home", "storefront browse"), ("GET", "/api/v1/shop/categories", "storefront browse"),
    ("GET", "/api/v1/shop/products", "PLP"), ("GET", "/api/v1/shop/products/{}", "PDP"),
    ("GET", "/api/v1/shop/facets", "PLP facets"), ("GET", "/api/v1/shop/search", "search"),
    ("GET", "/api/v1/shop/currencies", "currency picker"),
    ("*", "/graphql/{}", "field-level authorization; see Every_Non_Shop_GraphQL_Field_Requires_Authorization"),
];
private static readonly string[] AnonymousGraphQLFields =
    ["shopHome", "shopCategories", "shopProducts", "shopFacets", "shopProduct", "shopSearch", "currencies"];

[Fact] public void Every_Mapped_Endpoint_Requires_Authorization_Except_An_Explicit_Allowlist()
{
    var offenders = RouteEndpoints().Where(e => !e.RoutePattern.RawText!.StartsWith("/scalar"))          // Development-only UI
        .Where(e => !RequiresAuthorization(e) && !Allowlisted(e)).Select(Describe).ToList();
    offenders.Should().BeEmpty();
}
[Fact] public void Allowlist_Has_No_Stale_Entries() => AnonymousAllowlist.Should().OnlyContain(a => RouteEndpoints().Any(e => Matches(e, a)));
[Theory, InlineData("/api/v1/shop/home?currency=USD"), InlineData("/api/v1/shop/categories"), InlineData("/api/v1/shop/currencies"),
 InlineData("/api/v1/shop/products?currency=USD"), InlineData("/api/v1/shop/search?q=demo&currency=USD")]
public async Task Allowlisted_Shop_Routes_Answer_Anonymously(string path) => (await Anon.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.OK);
[Fact] public async Task Every_Non_Shop_GraphQL_Field_Requires_Authorization()
{
    var schema = (await Executor()).Schema;
    var offenders = new[] { schema.QueryType, schema.MutationType! }.SelectMany(t => t.Fields)
        .Where(f => !f.IsIntrospectionField && !AnonymousGraphQLFields.Contains(f.Name))
        .Where(f => !f.Directives.ContainsDirective("authorize")).Select(f => $"{f.DeclaringType.Name}.{f.Name}").ToList();
    offenders.Should().BeEmpty();
}
```
  - `RequiresAuthorization(e)` means: `e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any()` and
    `e.Metadata.GetMetadata<IAllowAnonymous>() is null`.
- [ ] **Step 2: Run.**
  `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "*.EndpointAuthorizationTests"`.
  - Every failure is a real gap. Fix it in the owning adapter by adding the missing
    `RequireAdmin` / `RequireAuthorization` / `[Authorize]`. Never fix it by adding an allowlist row.
  - The expected first-run offenders are:
    - the legacy `measurementUnits` field, if phase 2–3 left it ungated (gate it `AdminStaff`)
    - any `Catalog` GraphQL field still carrying the phase-0 commented-out `//[Authorize]`
- [ ] **Step 3: Run, expect PASS.** Mutation check:
  1. Remove `[Authorize]` from `adminDashboard` and expect
     `Every_Non_Shop_GraphQL_Field_Requires_Authorization` to fail.
  2. Add `.AllowAnonymous()` to `GET /api/v1/admin/dashboard` and expect the endpoint test to fail.
  3. Restore both.
- [ ] **Step 4: Commit.** `test(security): every endpoint and GraphQL field is authorized unless explicitly allowlisted`

---

### Task 10: Every admin write is audited (REST theory, GraphQL samples, RBAC)

**Files:**
- Create:
  - `tests/IntegrationTests/Security/{AdminWriteCases,AuditWorld,AdminAuditCoverageTests}.cs`
  - `src/BuildingBlocks/Audit/Application/IStandaloneAuditWriter.cs`, `src/BuildingBlocks/Audit/Implementation/StandaloneAuditWriter.cs`
  - `src/BuildingBlocks/Core/Application/Security/IAdminActorAccessor.cs`, `src/BuildingBlocks/Presentation.Endpoint/Security/HttpAdminActorAccessor.cs`
- Modify: each service that a failing row names (see Step 3), plus `src/Authorization/Authorization.Endpoints/**/*Mutations.cs`.

**Interfaces:**
```csharp
public interface IStandaloneAuditWriter {   // own DI scope + own SaveChangesAsync; for writers outside EwaysVNextDbContext only
  Task RecordAsync(AuditActor actor, string action, string targetType, string targetId, object? before, object? after, CancellationToken ct = default);
}
public interface IAdminActorAccessor { AdminActor? Current { get; } }   // from HttpContext.User via AdminActor.TryFrom; null outside a request
public sealed record AdminWriteCase(string Method, string Pattern, bool SuperOnly, Func<AuditWorld, Task<HttpRequestMessage>> Build);
public static class AdminWriteCases {
  public static IReadOnlyList<AdminWriteCase> All { get; }
  public static IReadOnlyDictionary<(string Method, string Pattern), string> Exempt { get; }   // value = reason
}
```
`AuditWorld` is an `IAsyncLifetime` class fixture over `SalesApiFactory`. It creates its
prerequisites lazily and caches each:
- through the admin API with a super-admin token: a partner and key, a brand, a Draft product with
  one variant and a PNG media, a category, an attribute, an import job of mode `File`, and a source
  policy
- through `_partnerA`: a paid partner order
- through the shop REST cart and checkout, with `CreateShopToken(sub, email, true)` and a demo
  variant: a pending website order
- through phase 7's staged-record seeding helper: one `NeedsReview` and one `Valid` staged record

Each `Build` returns a request that is valid for the world. Its `X-Correlation-Id` is
`audit-{Guid:N}`.

**Exempt** (with reason):
- `("POST", "/api/v1/admin/imports/{}/mapping/preview")`: a read-only preview that writes nothing.

**`All`**: one case per (method, normalized pattern):

| Area | Cases |
|---|---|
| Partners | `POST /api/v1/admin/partners`; `POST /api/v1/admin/partners/{}/suspend`, `/reactivate`; `PUT /api/v1/admin/partners/{}/scopes`, `/limits`; `POST /api/v1/admin/partners/{}/keys`; `POST /api/v1/admin/partners/{}/keys/{}/rotate`, `/revoke` |
| Settings | `PUT /api/v1/admin/settings/seller`; `POST /api/v1/admin/settings/payment-instructions`; `POST /api/v1/admin/settings/payment-instructions/{}/activate`; `PUT /api/v1/admin/settings/commerce-limits` |
| Customers | `POST /api/v1/admin/customers/{}/suspend`, `/reactivate` |
| Catalog | `POST /api/v1/admin/catalog/brands`; `PUT /api/v1/admin/catalog/brands/{}`, `/aliases`, `/status`; `POST /api/v1/admin/catalog/products`; `PUT /api/v1/admin/catalog/products/{}`, `/secondary-categories`, `/status`; `POST /api/v1/admin/catalog/products/{}/variants`; `PUT /api/v1/admin/catalog/products/{}/variants/{}/status`; `POST /api/v1/admin/catalog/products/{}/identifiers`; `DELETE /api/v1/admin/catalog/products/{}/identifiers/{}`; `POST /api/v1/admin/catalog/products/{}/media`; `DELETE /api/v1/admin/catalog/products/{}/media/{}`; `POST /api/v1/admin/catalog/products/bulk`; `POST /api/v1/admin/catalog/products/{}/merge-into/{}`; `POST /api/v1/admin/catalog/categories`; `PUT /api/v1/admin/catalog/categories/{}`, `/status`, `/move`, `/aliases`; `POST /api/v1/admin/catalog/attributes`; `PUT /api/v1/admin/catalog/attributes/{}`, `/scope`, `/aliases` |
| Pricing | `PUT /api/v1/admin/pricing/variants/{}/price`; `PUT /api/v1/admin/pricing/variants/{}/overrides/{}`; `DELETE /api/v1/admin/pricing/variants/{}/overrides/{}`; super: `POST /api/v1/admin/pricing/fx/overrides`, `DELETE /api/v1/admin/pricing/fx/overrides/{}/{}`, `PUT /api/v1/admin/pricing/currencies/{}`, `POST /api/v1/admin/pricing/fx/refresh` |
| Sales | `POST /api/v1/admin/orders/{}/confirm-payment`; `POST /api/v1/admin/orders/{}/cancel`; super: `POST /api/v1/admin/orders/{}/refunds` |
| Sourcing | `POST /api/v1/admin/imports`; `POST /api/v1/admin/imports/{}/file`; `PUT /api/v1/admin/imports/{}/mapping`; `POST /api/v1/admin/imports/{}/start`, `/pause`, `/resume`, `/cancel`, `/publish-valid`; `POST /api/v1/admin/imports/records/{}/approve`, `/merge`, `/create-new`, `/reject`; `POST /api/v1/admin/imports/records/bulk-approve`; `POST /api/v1/admin/imports/source-policies`; `PUT /api/v1/admin/imports/source-policies/{}`; `DELETE /api/v1/admin/imports/source-policies/{}` |

- [ ] **Step 1: Write the failing tests.**
```csharp
[Fact] public void Every_Admin_Write_Endpoint_Has_A_Case_Or_An_Exemption()
{
    var mapped = AdminRestEndpoints(factory).SelectMany(e => Methods(e).Where(m => m is "POST" or "PUT" or "PATCH" or "DELETE")
        .Select(m => (m, Normalize(e.RoutePattern.RawText!)))).ToHashSet();
    var covered = AdminWriteCases.All.Select(c => (c.Method, c.Pattern)).Concat(AdminWriteCases.Exempt.Keys).ToHashSet();
    mapped.Except(covered).Should().BeEmpty("every new admin write must be added to AdminWriteCases");
    covered.Except(mapped).Should().BeEmpty("no stale cases");
}
public static TheoryData<string, string> Cases => new(AdminWriteCases.All.Select(c => (c.Method, c.Pattern)));
[Theory, MemberData(nameof(Cases))]
public async Task Admin_Write_Records_An_Audit_Event_In_The_Same_Request(string method, string pattern)
{
    var c = AdminWriteCases.All.Single(x => x.Method == method && x.Pattern == pattern);
    var request = await c.Build(_world); var correlationId = request.Headers.GetValues("X-Correlation-Id").Single();
    var response = await _world.Super.SendAsync(request);
    ((int)response.StatusCode).Should().BeInRange(200, 299, await response.Content.ReadAsStringAsync());
    var rows = await _world.AuditRowsAsync(correlationId);   // SELECT action, actor_user_id, actor_role FROM audit.audit_events WHERE correlation_id = @c
    rows.Should().NotBeEmpty();
    rows.Should().OnlyContain(r => r.ActorUserId == _world.SuperSub && r.ActorRole == "super_admin"
                                && Regex.IsMatch(r.Action, @"^[a-z_]+(\.[a-z_\-]+)+$"));
}
[Theory]
[InlineData("mutation { createPartner(input:{name:\"gql-audit\", scopes:[\"orders:read\"]}) { id } }")]
[InlineData("mutation { updateCommerceLimits(input:{minUsd:\"10.00\", maxUsd:\"1000000.00\", bulkDefaultMax:100, compositionDeadlineMs:8000, compositionMaxLines:200}) { minUsd } }")]
[InlineData("mutation { suspendCustomer(id:\"{customerId}\") { id } }")]
[InlineData("mutation { createBrand(input:{displayName:\"Gql Audit Brand\", aliases:[]}) { id } }")]
[InlineData("mutation { setVariantPrice(input:{variantId:\"{variantId}\", amount:\"12.34\", currency:\"USD\"}) { variantId } }")]
[InlineData("mutation { cancelOrder(id:\"{pendingOrderId}\") { summary { id } } }")]
[InlineData("mutation { createSourcePolicy(input:{domain:\"gql-audit.example\", crawlAllowed:true, contentUseAllowed:true, imageUseAllowed:true}) }")]
[InlineData("mutation { createRole(input:{code:\"gql_audit\", name:\"Gql Audit\", rank:1}) { role { id } } }")]
public async Task GraphQL_Admin_Mutation_Records_An_Audit_Event(string template) { /* placeholders resolved from _world; same assertions as REST */ }
```
- [ ] **Step 2: Run, expect FAIL.**
  `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "*.AdminAuditCoverageTests"`.
  The run lists every unaudited row, and the `createRole` row fails by design.
- [ ] **Step 3: Close each gap in the owning module, never in the test.**
  - **Service with an `AdminActor` parameter:** add `auditTrail.Record(actor.ToAudit(), "<module>.<target>.<verb>", target, id, before, after)`
    before its single save. `before`/`after` hold the changed fields only (spec §6.4).
  - **Service without an actor.** The expected ones are `ICategoryService` and
    `IAttributeBlueprintService`, which phase 2–3 reused.
    - Inject `IAdminActorAccessor`.
    - Record `category.created|updated|status_set|moved|aliases_set` and
      `attribute.created|updated|scope_set|aliases_set`.
    - When `Current` is null (seed, import publishing), record with the actor
      `new AuditActor(Guid.Empty, "system")`.
  - **`RequestFxRefreshAsync`:** record `fx.refresh_requested` and `SaveChangesAsync`, then
    enqueue. A refresh request is then audited even if the enqueue coalesces.
  - **RBAC GraphQL mutations** (`src/Authorization/Authorization.Endpoints/**/*Mutations.cs`):
    after a successful result, call `IStandaloneAuditWriter.RecordAsync(actor.ToAudit(), "rbac.<entity>.<verb>", ...)`
    (design call 7). Note this non-atomic edge in `src/Authorization/README.md`.
  - Register `HttpAdminActorAccessor` (scoped) in `AddUserProfileService()` and
    `StandaloneAuditWriter` in `AddAudit()`.
- [ ] **Step 4: Run, expect PASS.** Then run the full unit suite, because the services gained
  constructor parameters and their unit-test builders must pass a substitute.
  Mutation check:
  1. Delete the `Record` call in `OrderAdminService.CancelAsync` and expect the
     `POST /api/v1/admin/orders/{}/cancel` row to fail.
  2. Restore it.
- [ ] **Step 5: Commit.** `test(audit): every admin write endpoint and GraphQL mutation records an audit event; close audit gaps`

---

### Task 11: PRD §10 coverage matrix and the gaps it exposes

**Files:**
- Create: `tests/IntegrationTests/Admin/AdminPrd10CoverageTests.cs`, `src/Modules/Sourcing/Adapter/Inbound/Rest/StagedRecordImageEndpoints.cs`
- Modify:
  - `src/Modules/Catalog/QueryApplication/Products/{ICatalogAdminQueries,CatalogAdminQueries,CatalogAdminDtos}.cs`
  - `src/Modules/Catalog/Adapter/Inbound/Rest/Admin/CatalogAdminEndpoints.cs`
  - `src/Modules/Catalog/Adapter/Inbound/GraphQL/Products/ProductAdminQueries.cs`
  - the Sourcing GraphQL queries file
  - `src/Modules/Sales/Adapter/Inbound/GraphQL/Admin/AdminOrderGraph.cs`
  - `tests/IntegrationTests/Security/AdminWriteCases.cs`, only if a new write route appears (none does)

**Interfaces (the three gap fills; each is single-module, so it lives in its owning module per ADR-0007):**
```csharp
// Catalog.QueryApplication — duplicate resolution needs a way to FIND duplicates; phase 2–3 only shipped merge.
public sealed record DuplicateCandidateDto([Snowflake] long ProductId, string Title, string Status, double Similarity);
Task<Result<IReadOnlyList<DuplicateCandidateDto>>> ListDuplicateCandidatesAsync(long productId, int limit, CancellationToken ct = default);
//   same brand_id, id <> productId, merged_into_product_id IS NULL, similarity(title, @title) >= 0.6 (pg_trgm, index from phase 7), order by similarity desc;
//   limit 1..50 else ADMIN_PAGE_SIZE_INVALID; unknown product → PRODUCT_NOT_FOUND 404
// REST  GET /api/v1/admin/catalog/products/{id:long}/duplicate-candidates?limit=10   RequireAdmin(CatalogManage)
// GraphQL adminProductDuplicateCandidates(id: ID!, limit: Int = 10): [DuplicateCandidate!]!

// Sourcing — "discovered products with photos": staged image keys are private (import-staging); admins need short-lived URLs.
// REST  GET /api/v1/admin/imports/records/{recordId:long}/images/{index:int} → 302 Location = IObjectStorage.GetPresignedUrlAsync(StorageBuckets.ImportStaging, key, 5 min);
//       index outside ImageKeys → 404 NOT_FOUND.   RequireAdmin(ImportsManage)
// GraphQL stagedRecordImageUrls(id: ID!): [String!]!   (same presigned URLs, in ImageKeys order)

// Sales — admin PDFs exist only over REST (GET /api/v1/admin/documents/{id}/pdf).
// GraphQL adminDocumentPdfUrl(documentId: ID!): String!  → IInvoiceService.GetPdfUrlAsync(new DocumentAccess.Admin(), id)   [Authorize AdminStaff] + [RequirePermission(OrdersManage)]
```

**The matrix.** Every row is executable. The REST pattern must exist in `EndpointDataSource`
(normalized as in Task 9) and the GraphQL field must exist on `Query` or `Mutation`.

| PRD §10 view | REST | GraphQL | Owner |
|---|---|---|---|
| Dashboard: counts, amounts by currency, pending website payments, import/validation queues, failure indicators, composition metric | `GET /api/v1/admin/dashboard` | `adminDashboard` | P6 T6–7 |
| Catalog: category tree | `GET /api/v1/admin/catalog/categories/tree` | `categoryHierarchy` | P2 T14 |
| Catalog: brands | `GET /api/v1/admin/catalog/brands` | `adminBrands` | P2 T10 |
| Catalog: attributes | `GET /api/v1/admin/catalog/attributes` | `attributeBlueprints` | P2 T14 |
| Catalog: products/variants | `GET /api/v1/admin/catalog/products`, `POST /api/v1/admin/catalog/products/{}/variants` | `adminProducts`, `addVariant` | P2 T11–15 |
| Catalog: images | `POST /api/v1/admin/catalog/products/{}/media` | `uploadProductMedia` | P2 T13 |
| Catalog: status toggles | `PUT /api/v1/admin/catalog/products/{}/status`, `PUT /api/v1/admin/catalog/products/{}/variants/{}/status` | `setProductActive`, `setVariantActive` | P2 T11 |
| Catalog: prices/overrides | `PUT /api/v1/admin/pricing/variants/{}/price`, `PUT /api/v1/admin/pricing/variants/{}/overrides/{}` | `setVariantPrice`, `setCurrencyOverride` | P2 T27 |
| Catalog: bulk editing | `POST /api/v1/admin/catalog/products/bulk` | `bulkEditProducts` | P2 T12 |
| Catalog: duplicate resolution (find) | `GET /api/v1/admin/catalog/products/{}/duplicate-candidates` | `adminProductDuplicateCandidates` | **P6 T11 (gap)** |
| Catalog: duplicate resolution (merge) | `POST /api/v1/admin/catalog/products/{}/merge-into/{}` | `mergeDuplicateProduct` | P2 T12 |
| Imports: source selection, auto-publish toggle | `POST /api/v1/admin/imports` | `createImportJob` | P7 T22–23 |
| Imports: download template | `GET /api/v1/admin/imports/templates/{}` | — (binary download; REST only) | P7 T22 |
| Imports: file + mapping | `POST /api/v1/admin/imports/{}/file`, `PUT /api/v1/admin/imports/{}/mapping` | `uploadImportFile`, `setImportMapping` | P7 |
| Imports: staging preview | `POST /api/v1/admin/imports/{}/mapping/preview` | `importMappingPreview` | P7 |
| Imports: discovered products with source prices, errors | `GET /api/v1/admin/imports/records`, `GET /api/v1/admin/imports/records/{}` | `stagedRecords`, `stagedRecord` | P7 |
| Imports: photos | `GET /api/v1/admin/imports/records/{}/images/{}` | `stagedRecordImageUrls` | **P6 T11 (gap)** |
| Imports: duplicate actions | `POST /api/v1/admin/imports/records/{}/merge`, `GET /api/v1/admin/imports/records/{}/diff` | `mergeStagedRecord`, `stagedRecordDiff` | P7 |
| Imports: job progress | `GET /api/v1/admin/imports/{}` | `importJob` | P7 |
| Imports: bulk approve/publish | `POST /api/v1/admin/imports/records/bulk-approve`, `POST /api/v1/admin/imports/{}/publish-valid` | `bulkApproveStagedRecords`, `publishValidImportRecords` | P7 |
| Orders: website/API list, statuses | `GET /api/v1/admin/orders` | `adminOrders` | P5 T29 |
| Orders: detail, customer details | `GET /api/v1/admin/orders/{}`, `GET /api/v1/admin/orders/{}/360` | `adminOrder`, `adminOrder360` | P5 T29, P6 |
| Orders: PDFs | `GET /api/v1/admin/documents/{}/pdf` | `adminDocumentPdfUrl` | P5 T28, **P6 T11 (gap: GraphQL)** |
| Orders: payment acceptance, cancellation | `POST /api/v1/admin/orders/{}/confirm-payment`, `POST /api/v1/admin/orders/{}/cancel` | `confirmOrderPayment`, `cancelOrder` | P5 T28 |
| Customers: records | `GET /api/v1/admin/customers` | `adminCustomers` | P1 T14 |
| Customers: contact-change history | `GET /api/v1/admin/customers/{}`, `GET /api/v1/admin/customers/{}/360` | `adminCustomer`, `adminCustomer360` | P1 T14, P6 |
| Payments/refunds: offline confirmation | `POST /api/v1/admin/orders/{}/confirm-payment` | `confirmOrderPayment` | P5 T28 |
| Payments/refunds: refunds + credit notes | `POST /api/v1/admin/orders/{}/refunds` | `refundOrder` | P5 T28 |
| Partners: create/suspend | `POST /api/v1/admin/partners`, `POST /api/v1/admin/partners/{}/suspend` | `createPartner`, `suspendPartner` | P1 T8 |
| Partners: scopes, rate limits | `PUT /api/v1/admin/partners/{}/scopes`, `PUT /api/v1/admin/partners/{}/limits` | `setPartnerScopes`, `setPartnerLimits` | P1 T8 |
| Partners: issue/rotate/revoke keys | `POST /api/v1/admin/partners/{}/keys`, `POST /api/v1/admin/partners/{}/keys/{}/rotate`, `POST /api/v1/admin/partners/{}/keys/{}/revoke` | `issuePartnerApiKey`, `rotatePartnerApiKey`, `revokePartnerApiKey` | P1 T8 |
| Partners: key usage logs | `GET /api/v1/admin/partners/{}/usage` | `adminPartnerUsage` | P1 T8 |
| Settings: seller identity | `GET /api/v1/admin/settings/seller`, `PUT /api/v1/admin/settings/seller` | `adminSettings`, `updateSeller` | P1 T10 |
| Settings: payment instruction | `POST /api/v1/admin/settings/payment-instructions` | `publishPaymentInstruction` | P1 T10 |
| Settings: enabled currencies | `PUT /api/v1/admin/pricing/currencies/{}` | `setCurrencyEnabled` | P2 T27 |
| Settings: FX provider/refresh | `GET /api/v1/admin/pricing/fx/rates`, `POST /api/v1/admin/pricing/fx/refresh` | `adminFxRates`, `requestFxRefresh` | P2 T27 |
| Settings: FX overrides | `POST /api/v1/admin/pricing/fx/overrides` | `setFxOverride` | P2 T27 |
| Settings: order bounds | `PUT /api/v1/admin/settings/commerce-limits` | `updateCommerceLimits` | P1 T10 |
| Audit verification (spec §9.3) | `GET /api/v1/admin/audit/verify` | `adminAuditVerification` | P6 T6–7 |

- [ ] **Step 1: Write the failing tests.**
  - `Prd10_Row_Has_Its_Rest_Route(view, method, pattern)` is a theory over every REST cell.
  - `Prd10_Row_Has_Its_GraphQL_Field(view, field)` is a theory over every non-"—" GraphQL cell.
  - The gap behaviours:
    - `Duplicate_Candidates_Lists_Same_Brand_Similar_Titles`: brand B has "Acme Cordless Drill 18V"
      and "Acme Cordless Drill 18 V". A different-brand product has the same title. Only the
      same-brand one is returned, with `Similarity >= 0.6`. A merged product is excluded.
    - `Duplicate_Candidates_Limit_51_Is_400`.
    - `Staged_Image_Url_Redirects_To_A_Presigned_Import_Staging_Url`: the 302 `Location` host is
      the MinIO endpoint, the path contains `/import-staging/images/`, and index 99 gives 404.
    - `AdminDocumentPdfUrl_Matches_The_Rest_Redirect_Target_Bucket`: the URL path contains `/documents/`.
- [ ] **Step 2: Run, expect FAIL** on exactly the three gap rows and the behaviours.
  `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "*.AdminPrd10CoverageTests"`.
  - Any other failing row means a name differs from the owning plan. Change the **row** only
    after `grep -rn` confirms the shipped name (for example, a single `{id}/{action}` route for
    job transitions). Record the final name in `docs/reports/phase-6.md`.
- [ ] **Step 3: Implement** the three gap fills per the interfaces.
- [ ] **Step 4: Run, expect PASS.** Re-run `*.EndpointAuthorizationTests` and `*.AdminRateLimitTests`
  (the new routes are covered by both), then `*.AdminAuditCoverageTests` (the new routes are GETs,
  so no new cases).
- [ ] **Step 5: Commit.** `feat(admin): PRD §10 coverage matrix as tests; duplicate candidates, staged image URLs, adminDocumentPdfUrl`

---

### Task 12: Acceptance 13 across every super-only area

**Files:** Modify `tests/IntegrationTests/Identity/Acceptance13StaffPermissionTests.cs` (phase 1).

**Interfaces:**
- Consumes the phase 1 helpers `Send`, `Resolve`, `ValidBodyFor` and `CreateStaffToken`.
- `Resolve` gains the placeholders `{orderId}` (a paid partner order), `{version}` (an existing
  instruction version) and `{pendingOrderId}`.
- The class switches to `SalesApiFactory`, so partner orders can be placed.

- [ ] **Step 1: Add the rows and tests.**
```csharp
// appended to StaffForbiddenRest
{ "GET",    "/api/v1/admin/partners/{partnerId}" },        { "GET",    "/api/v1/admin/partners/{partnerId}/usage" },
{ "PUT",    "/api/v1/admin/partners/{partnerId}/scopes" }, { "PUT",    "/api/v1/admin/partners/{partnerId}/limits" },
{ "POST",   "/api/v1/admin/partners/{partnerId}/reactivate" },
{ "GET",    "/api/v1/admin/settings/seller" },             { "POST",   "/api/v1/admin/settings/payment-instructions/{version}/activate" },
{ "POST",   "/api/v1/admin/pricing/fx/overrides" },        { "DELETE", "/api/v1/admin/pricing/fx/overrides/USD/SGD" },
{ "PUT",    "/api/v1/admin/pricing/currencies/SGD" },      { "POST",   "/api/v1/admin/pricing/fx/refresh" },
{ "POST",   "/api/v1/admin/orders/{orderId}/refunds" },    { "GET",    "/api/v1/admin/audit/verify" },
// appended to Staff_Is_Forbidden_On_GraphQL
[InlineData("mutation { setFxOverride(input:{base:\"USD\", quote:\"SGD\", rate:\"1.3500000000\"}) }")]
[InlineData("mutation { clearFxOverride(base:\"USD\", quote:\"SGD\") }")]
[InlineData("mutation { setCurrencyEnabled(code:\"SGD\", enabled:false) }")]
[InlineData("mutation { requestFxRefresh }")]
[InlineData("mutation { refundOrder(id:\"{orderId}\", input:{amount:\"1.00\", reason:\"x\"}) { summary { id } } }")]
[InlineData("mutation { activatePaymentInstruction(version:1) }")]
[InlineData("mutation { issuePartnerApiKey(partnerId:\"{partnerId}\", input:{}) { key } }")]
[InlineData("query { adminPartners(page:1, pageSize:10) { totalCount } }")]
[InlineData("query { adminAuditVerification { intact } }")]

[Fact] public async Task Staff_Refusals_Leave_Super_Only_State_Unchanged()
{   // snapshot before/after all staff attempts above:
    //  SGD enabled == true; active FX override for USD/SGD unchanged (none, or the seeded one);
    //  order {orderId}: refunds count 0, payment "Paid"; active payment instruction version unchanged; no fx-refresh job queued by staff
}
[Fact] public async Task Staff_Can_Do_The_Staff_Side_Of_Orders_And_Pricing()
{   // staff: confirm-payment on {pendingOrderId} → 200; PUT /api/v1/admin/pricing/variants/{variantId}/price → 200; GET /api/v1/admin/dashboard → 200
}
```
  Extend `Super_Admin_Is_Allowed_On_The_Same_Routes` with the new rows. Each one gets a fresh
  world value, so a refund or a rotation does not consume shared state.
- [ ] **Step 2: Run, expect PASS** (the gates exist since phases 1, 2–3, 5 and Task 6).
  `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "*.Acceptance13StaffPermissionTests"`.
  Mutation check:
  1. In `PricingAdminEndpoints`, change the `fx/overrides` POST to `RequireAdmin(FxManage)`
     without `superOnly`, **and** remove the service's `IsSuperAdmin` guard.
  2. Expect the `POST /api/v1/admin/pricing/fx/overrides` row to fail.
  3. Restore. Repeat the same check for refunds (`RefundsManage`).
- [ ] **Step 3: Commit.** `test(acceptance-13): staff refused on FX overrides, currencies, refunds, settings, partners and audit verification`

---

### Task 13: Phase 6 gate

**Files:**
- Create: `docs/reports/phase-6.md`
- Modify:
  - `AGENTS.md`: **Admin** bolded in the module list; API table rows for `/api/v1/admin/dashboard`,
    `/360`, `/audit/verify` and `/graphql` limits; rule 3 now names `AdminReadReferences`
  - `docs/TESTING-GUARANTEES.md`: new sections for the authorization allowlist, audit coverage,
    the PRD §10 matrix and the GraphQL limits

- [ ] **Step 1:** `dotnet build EwaysVNext.slnx -c Release`. Expected: 0 errors and 0 warnings.
- [ ] **Step 2:** `dotnet test EwaysVNext.slnx` with Docker running. Expected: all suites pass.
  Record the count per suite.
- [ ] **Step 3:** `dotnet ef migrations has-pending-model-changes -p src/Authorization/Authorization.Implementation/Authorization.Implementation.csproj -s src/Host/EwaysVNext.Host -c AuthorizationDbContext`.
  Do the same for `EwaysVNextDbContext` (`-p src/Database`). Expected: no pending changes.
- [ ] **Step 4:** Write `docs/reports/phase-6.md`:
  - **What works:** per task, naming the tests.
  - **Incomplete:** OpenAPI and SDL snapshots are phase 8; there is no admin UI (spec non-goal).
  - **Risks:**
    - the in-process admin rate limiter under replicas > 1 (ADR-0010)
    - the RBAC audit is a separate save (design call 7)
    - the depth and cost limits are global
    - the coverage matrix proves the routes exist, not their UX
  - **Matrix rows renamed:** any row renamed in Task 11.
  - **Reproduction commands.**
- [ ] **Step 5: Commit.** `docs: phase 6 report; AGENTS and testing guarantees for the Admin module`

---

# Phase 8 — Ops package

### Task 14: OpenAPI v1 — partner security scheme and examples for every partner operation

**Files:**
- Create:
  - `src/Host/EwaysVNext.Host/OpenApi/PartnerApiKeyOpenApiTransformer.cs`
  - `tests/IntegrationTests/Contracts/PartnerOpenApiExampleTests.cs`
- Modify:
  - `src/Modules/Sales/Adapter/Inbound/Rest/Partner/PartnerOpenApiExamples.cs` (phase 4–5)
  - `src/Host/EwaysVNext.Host/Program.cs`: `MapOpenApi()` when `OpenApi:Expose` (default `true`);
    Scalar stays `IsDevelopment()`

**Interfaces:**
- `PartnerApiKeyOpenApiTransformer` is both an `IOpenApiDocumentTransformer` and an
  `IOpenApiOperationTransformer`:
  - It adds `components.securitySchemes.PartnerApiKey = { type: http, scheme: bearer, bearerFormat: "pk_<prefix>_<secret>" }`.
  - On every operation whose endpoint metadata has a policy starting `partner:`, it sets
    `security: [{ PartnerApiKey: [] }]` and the tag `Partner Orders` or `Partner Invoices`.
- `PartnerOpenApiExamples` adds named examples. Every example body is built from the real DTO
  types (`PartnerOrderResponse`, `ErrorResponse`, the bulk and search records) and serialized
  with the API's `JsonOptions`.

| Operation | Request example | Response examples |
|---|---|---|
| `POST /api/v1/orders` | `create`: the PRD body (`500.00 USD`, customer with email + phone) | `201 created`, `200 replay` (with `Idempotent-Replay: true` header doc), `400 VALIDATION_FAILED` (`amount: "500"`), `409 ORDER_IDEMPOTENCY_CONFLICT`, `422 EXACT_COMPOSITION_UNAVAILABLE`, `503 COMPOSITION_TIMEOUT` (`retryable: true`) |
| `POST /api/v1/orders/bulk` | `mixed`: 3 items | `200 mixed`: items `created`, `replayed`, `failed` (`CUSTOMER_CONTACT_REQUIRED`); `400` over `BulkMaxItems` |
| `GET /api/v1/orders` | — | `200 page` with `nextCursor` |
| `GET /api/v1/orders/{id}` | — | `200`, `404 NOT_FOUND` |
| `GET /api/v1/customers/{id}/orders` | — | `200 page`, `404` |
| `GET /api/v1/invoices/{id}` | — | `200 invoice` (title `Invoice`, `taxStatement: "Not GST registered"`, fulfillment statement), `404` |
| `GET /api/v1/invoices/{id}/pdf` | — | `302` with a `Location` header doc |
| `POST /api/v1/invoices/search` | `search`: `["173", "999"]` | `200` with `found` + `not_found` |

Every partner operation also documents `401 UNAUTHORIZED`, `403 INSUFFICIENT_SCOPE` and
`429 RATE_LIMITED`, each with an `X-Correlation-Id` response header.

- [ ] **Step 1: Write the failing tests.**
```csharp
public static TheoryData<string, string, string[]> PartnerOps => new()
{
    { "post", "/api/v1/orders", ["201", "200", "400", "409", "422", "503", "401", "403", "429"] },
    { "post", "/api/v1/orders/bulk", ["200", "400", "401", "403", "429"] },
    { "get", "/api/v1/orders", ["200", "401", "403", "429"] },
    { "get", "/api/v1/orders/{id}", ["200", "404", "401", "403", "429"] },
    { "get", "/api/v1/customers/{id}/orders", ["200", "404", "401", "403", "429"] },
    { "get", "/api/v1/invoices/{id}", ["200", "404", "401", "403", "429"] },
    { "get", "/api/v1/invoices/{id}/pdf", ["302", "404", "401", "403", "429"] },
    { "post", "/api/v1/invoices/search", ["200", "400", "401", "403", "429"] },
};
[Theory, MemberData(nameof(PartnerOps))]
public async Task Partner_Operation_Is_Secured_And_Has_Examples(string method, string path, string[] statuses)
{
    var op = (await OpenApi())["paths"]![path]![method]!;
    op["security"]![0]!["PartnerApiKey"].Should().NotBeNull();
    foreach (var s in statuses.Where(s => s != "302"))
        op["responses"]![s]!["content"]!["application/json"]!["examples"]!.AsObject().Count.Should().BeGreaterThan(0, $"{method} {path} {s}");
    op["responses"]!["200"]?["headers"]?["X-Correlation-Id"]?.Should().NotBeNull();
    if (method == "post") op["requestBody"]!["content"]!["application/json"]!["examples"]!.AsObject().Count.Should().BeGreaterThan(0);
}
[Fact] public async Task Create_Example_Shape_Matches_A_Live_201()
{   // live: _partnerA POST /api/v1/orders 500.00 USD → 201; JsonPaths(x) = set of property paths with array indices collapsed to [*]
    JsonPaths(Example("post", "/api/v1/orders", "201", "created")).Should().BeEquivalentTo(JsonPaths(liveBody));
}
[Fact] public async Task Bulk_Example_Shape_Matches_A_Live_Mixed_Response() { }
[Fact] public async Task Search_Example_Shape_Matches_A_Live_Response() { }
[Fact] public async Task Error_Examples_Use_Spec_Codes_And_Retryable_Flags()
{   // every error example's code ∈ master "Error codes" list ∪ {UNAUTHORIZED, FORBIDDEN}; retryable true only for COMPOSITION_TIMEOUT, RATE_LIMITED
}
```
- [ ] **Step 2: Run, expect FAIL.**
  `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "*.PartnerOpenApiExampleTests"`.
- [ ] **Step 3: Implement** the transformer and the examples. Register them with
  `options.AddDocumentTransformer<PartnerApiKeyOpenApiTransformer>()` and
  `options.AddOperationTransformer<PartnerApiKeyOpenApiTransformer>()`.
- [ ] **Step 4: Run, expect PASS.** Also run phase 4–5's `OpenApi_Has_Partner_Order_Example`, which must still pass.
- [ ] **Step 5: Commit.** `feat(openapi): PartnerApiKey scheme and examples for every partner operation`

---

### Task 15: Contract snapshots — OpenAPI JSON and GraphQL SDL

**Files:**
- Create:
  - `tests/IntegrationTests/Contracts/{OpenApiSnapshotTests,GraphQLSchemaSnapshotTests,SnapshotFile}.cs`
  - `tests/IntegrationTests/Contracts/Snapshots/{openapi-v1.json,schema.graphql}`, written on first run
- Modify: `tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj` (`<None Update="Contracts/Snapshots/**" CopyToOutputDirectory="Never" />`)

**Interfaces:**
```csharp
internal static class SnapshotFile {
  // Path is the SOURCE file (CallerFilePath-relative), not bin/. Mismatch → writes "<name>.received.<ext>" beside it and fails with a unified diff.
  // UPDATE_SNAPSHOTS=1 → overwrite and pass. Line endings normalised to "\n".
  public static void AssertMatches(string name, string actual, [CallerFilePath] string caller = "");
}
```
- The OpenAPI JSON is normalized before comparison:
  - re-serialized with sorted object keys and 2-space indent
  - `servers` removed, since it depends on the host
- The SDL comes from `(await resolver.GetRequestExecutorAsync()).Schema.ToString()`.

- [ ] **Step 1: Write the tests.**
```csharp
[Fact] public async Task OpenApi_V1_Matches_Snapshot() => SnapshotFile.AssertMatches("openapi-v1.json", Normalize(await Anon.GetStringAsync("/openapi/v1.json")));
[Fact] public async Task GraphQL_Schema_Matches_Snapshot() => SnapshotFile.AssertMatches("schema.graphql", (await Executor()).Schema.ToString());
[Fact] public void Snapshot_Mismatch_Writes_Received_File_And_Fails() { /* AssertMatches("probe.txt","a") then "b" in a temp copy → throws, .received exists */ }
```
- [ ] **Step 2: Run, expect FAIL** (no snapshot exists).
  `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "*.OpenApiSnapshotTests"`,
  then the same for `*.GraphQLSchemaSnapshotTests`.
- [ ] **Step 3: Create the snapshots.** Run once with `UPDATE_SNAPSHOTS=1`, then read both files
  in full before committing. Check that:
  - every partner path from Task 14 is present
  - no admin route appears without `security`
  - the SDL contains `adminDashboard` and the `shop*` fields
  - there is no field from deleted modules (`grep -nE "Payment|Location|Notification" tests/IntegrationTests/Contracts/Snapshots/*` is empty)
- [ ] **Step 4: Run, expect PASS** with no environment variable set. Mutation check:
  1. Rename one `PartnerDtos` property and expect `OpenApi_V1_Matches_Snapshot` to fail with the
     diff naming it.
  2. Revert.
  Phase 7 Task 23 says "update the phase-0 SDL snapshot test", but that test never existed before
  this task. Note in the phase report that this snapshot now covers Sourcing.
- [ ] **Step 5: Commit.** `test(contracts): OpenAPI v1 and GraphQL SDL snapshot tests`

---

### Task 16: Bruno collection

**Files:**
- Create:
  - `docs/api/bruno/{bruno.json,package.json,package-lock.json,README.md}`
  - `docs/api/bruno/environments/{local,smoke}.bru`
  - `docs/api/bruno/partner/{01-create-order,02-replay-order,03-conflict-409,04-bulk-mixed,05-get-order,06-get-order-by-ext,07-list-orders,08-customer-orders,09-get-invoice,10-invoice-pdf,11-invoice-search}.bru`
  - `docs/api/bruno/shop/{01-currencies,02-home,03-products,04-product,05-search,06-me,07-add-to-cart,08-cart,09-checkout,10-my-orders,11-document-pdf-url}.bru`
  - `docs/api/bruno/admin/{01-dashboard,02-create-partner,03-issue-key,04-partner-usage,05-orders,06-order-360,07-confirm-payment,08-refund,09-customer-360,10-set-fx-override,11-audit-verify,12-graphql-dashboard}.bru`
  - `tests/ArchitectureTests/BrunoCollectionTests.cs`

**Interfaces:**
- `environments/local.bru`:
  ```
  vars {
    baseUrl: http://localhost:5000
  }
  vars:secret [
    partnerApiKey,
    backofficeToken,
    shopToken
  ]
  ```
  `smoke.bru` is the same with `baseUrl: http://127.0.0.1:8080`.
- Every request sets `auth: bearer` with a `{{...}}` secret, except the anonymous shop browse files.
  Every request has an `assert` block on status. Partner `01` stores `orderId`, `invoiceId` and
  `externalOrderId` via `script:post-response` (`bru.setVar`). Later files use them.
- `package.json` pins `@usebruno/cli` with `--save-exact`. `README.md` says how to set secrets
  (`bru run --env local --env-var partnerApiKey=...`), and that the collection is a client, not a test suite.

- [ ] **Step 1: Write the failing tests.**
```csharp
[Fact] public void Every_Partner_OpenApi_Operation_Has_A_Bruno_Request()
{   // parse the snapshot JSON (Task 15) for partner-tagged operations; each "METHOD path" (path params → {{var}}) appears in some partner/*.bru "url:" line
}
[Fact] public void Environment_Files_Hold_No_Secret_Values()
{   // every line inside "vars {}" is "baseUrl: http..."; every secret name is only listed inside "vars:secret [ ]"
    BruFiles("environments").SelectMany(VarsBlock).Should().OnlyContain(kv => kv.Key == "baseUrl");
}
[Fact] public void No_Bruno_File_Contains_A_Literal_Key_Or_Jwt() =>
    BruFiles("").Where(f => Regex.IsMatch(File.ReadAllText(f), @"pk_[A-Za-z0-9]{8}_[A-Za-z0-9]{20,}|eyJ[A-Za-z0-9_-]{10,}\.")).Should().BeEmpty();
[Fact] public void Collection_Has_Partner_Shop_And_Admin_Folders()
```
- [ ] **Step 2: Run, expect FAIL.**
  `dotnet test --project tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj --filter-class "*.BrunoCollectionTests"`.
- [ ] **Step 3: Write the collection.** Take the bodies from the Task 14 request examples and use
  snake_case field names for the partner folder (`external_order_id`, `external_customer_id`,
  `customer.full_name`, …, matching PRD §9) and camelCase for the shop and admin folders. Then run `npm --prefix docs/api/bruno install --save-exact @usebruno/cli`.
- [ ] **Step 4: Run, expect PASS.** Then run
  `npx --prefix docs/api/bruno bru run docs/api/bruno/shop --env local -r 01-currencies.bru`
  against a dev host (`dotnet run --project src/Host/EwaysVNext.Host` after `--migrate` and
  `--seed`). Expected output: `1 passed`. The full run happens in Task 17.
- [ ] **Step 5: Commit.** `docs(api): Bruno collection for partner, shop and admin with secret-free environments`

---

### Task 17: Smoke stack and the end-to-end smoke script

**Files:**
- Create: `build/compose/docker-compose.smoke.yml`, `tools/smoke/smoke.sh`
- Modify: `tests/ArchitectureTests/BuildDirectoryTests.cs`, `.github/workflows/ci.yml`
  (add `docker compose -f build/compose/docker-compose.yml -f build/compose/docker-compose.smoke.yml --env-file build/compose/ci.env config --quiet`)

**Interfaces:**
- `docker-compose.smoke.yml` (an override; never used in production):
  - `api`, `worker`, `migrations`: `ASPNETCORE_ENVIRONMENT: Staging`.
  - `api`: `ports: ["127.0.0.1:8080:8080"]`, `Minio__PublicEndpoint: http://127.0.0.1:9000`,
    `RateLimiting__AdminPermitPerMinute: "1000"`.
  - `worker`: `Minio__PublicEndpoint: http://127.0.0.1:9000`.
  - `keycloak`: `ports: ["127.0.0.1:8081:8080"]`, `KC_HOSTNAME_BACKCHANNEL_DYNAMIC: "true"`.
  - `minio`: `ports: ["127.0.0.1:9000:9000"]`.
  - `postgres`: `ports: ["127.0.0.1:55432:5432"]`.
- `tools/smoke/smoke.sh` uses `set -euo pipefail` and needs `docker`, `curl`, `jq` and `openssl`.
  `KEEP_STACK=1` skips the teardown. It exits 0 and prints `SMOKE_OK` as its last line.
  **Safety guard:** the script always starts its own throwaway stack (project name `eways-smoke`,
  fresh volumes, generated secrets). Before any Keycloak admin call it refuses to run (exit 2,
  `REFUSING: smoke only runs against its own local stack`) unless the Keycloak URL is
  `http://127.0.0.1:8081`. So the temporary user and the direct-grants flag can never touch a real
  realm. The steps,
  each printed as `==> <step>`:
  1. **Write a temporary env file** (`mktemp`).
     - Every `.env.example` key gets a value. Secrets come from `openssl rand -hex 24`.
     - The hosts are `api.localhost` / `auth.localhost` / `storage.localhost`, and
       `ACME_EMAIL=smoke@example.test`.
     - SMTP values point at `smtp.invalid:587`. The smoke never sends mail.
     - Without `KEEP_STACK=1`, the `trap` runs `docker compose … down -v` and removes the file.
     - With `KEEP_STACK=1`, the stack and the file are kept, and the script prints `ENV=<path>`.
       Task 18 needs both.
  2. **Bring the stack up:**
     `C="docker compose -p eways-smoke -f build/compose/docker-compose.yml -f build/compose/docker-compose.smoke.yml --env-file $ENV"`, then
     `$C up -d --build --wait` (`migrations` and `minio-init` must exit 0).
  3. **Seed:** `$C run --rm api --seed`, expecting exit 0. `Staging` permits it, and Production
     would return 2.
  4. **Get a Backoffice token** (design call 9):
     - Get a master token: `POST http://127.0.0.1:8081/realms/master/protocol/openid-connect/token`
       (`grant_type=password`, `client_id=admin-cli`, bootstrap admin from the env file).
     - Create user `smoke-<epoch>` in `Backoffice` with `enabled`, `emailVerified`,
       `requiredActions: []` and a random 24-char non-temporary password.
     - Map the realm role `super_admin` to the user.
     - Get the `backoffice-console` client and save its JSON. Then `PUT` it with
       `directAccessGrantsEnabled: true`.
     - Get the token: `POST /realms/Backoffice/protocol/openid-connect/token`
       (`grant_type=password`, `client_id=backoffice-console`, `scope=openid`).
     - The `trap` restores the saved client JSON and deletes the user before the teardown.
  5. **Create a partner:** `POST http://127.0.0.1:8080/api/v1/admin/partners` with
     `{"name":"smoke-<epoch>","scopes":["customers:read","customers:write","orders:create","orders:read","invoices:read"],"rateLimitPerMinute":100000}`.
     Expect 201, then `PID=$(jq -r .data.id)`.
  6. **Issue a key:** `POST /api/v1/admin/partners/$PID/keys` with `{}`. Expect 201,
     `KEY=$(jq -r .data.key)`, and a key matching `^pk_[A-Za-z0-9]{8}_`.
  7. **Order:** `POST /api/v1/orders` with
     `{"externalOrderId":"smoke-<epoch>","amount":"500.00","currency":"USD","customer":{"externalId":"smoke-c-<epoch>","email":"smoke@example.test","phone":"+6591234567","fullName":"Smoke Test"}}`.
     Check:
     - 201
     - `.data.total.amount == "500.00"`
     - the sum of `.data.lines[].lineTotal.amount` in integer cents is 50000
     - `.data.invoice.number` starts with `INV-`
     - `.data.status.payment == "Paid"`
     - the `X-Correlation-Id` header equals `.data.correlationId`
  8. **Replay:** the same body returns 200 and `Idempotent-Replay: true`.
  9. **PDF:** `GET` `.data.invoice.pdfUrl` with `curl -s -o /dev/null -w '%{http_code} %{redirect_url}'`
     returns 302 and a URL starting with `http://127.0.0.1:9000/documents/`. Fetching that URL
     returns bytes whose first 5 are `%PDF-`.
  10. **Bruno:** run `npx --prefix docs/api/bruno bru run docs/api/bruno/partner --env smoke --env-var partnerApiKey=$KEY`
      and expect every request to pass.
  11. Print `SMOKE_OK`.

- [ ] **Step 1: Write the failing test.** In `BuildDirectoryTests`, add
  `Smoke_Override_Never_Sets_Production_And_Publishes_Only_Loopback`. It reads
  `docker-compose.smoke.yml` and asserts that:
  - every `ports` entry starts with `127.0.0.1:`
  - `ASPNETCORE_ENVIRONMENT` is never `Production`
  - no key named like `*PASSWORD*` / `*SECRET*` / `*KEY*` has a literal value
- [ ] **Step 2: Run, expect FAIL.**
  `dotnet test --project tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj --filter-class "*.BuildDirectoryTests"`.
- [ ] **Step 3: Write** the override and the script, then `chmod +x tools/smoke/smoke.sh`.
- [ ] **Step 4: Verify.**
  - Re-run Step 2 and expect PASS.
  - Run `bash -n tools/smoke/smoke.sh` and expect no output.
  - Run `bash tools/smoke/smoke.sh`. Expected: the step lines, then `SMOKE_OK`, exit 0. Afterwards
    `docker compose -p eways-smoke ps -q` prints nothing.
  - A failure is fixed in code or config, never by loosening a check.
- [ ] **Step 5: Commit.** `test(smoke): compose smoke stack and end-to-end script (partner + key via admin API, $500 order, invoice PDF)`

---

### Task 18: Coverage and load report on the compose stack

**Files:**
- Create: `docs/reports/coverage-and-load.md`, `docs/reports/composition-coverage-compose.{md,json}`, `docs/reports/k6-partner-orders.json`
- Modify: `tests/load/partner-orders.js`, `tests/ArchitectureTests/OpsPackageTests.cs` (create the class here if Task 19 has not run yet; Task 19 adds to it)

**Interfaces:**
- `partner-orders.js`:
  - Add `options.summaryTrendStats = ['avg','min','med','p(95)','p(99)','max']`.
  - Tag each request `{ name: 'create_order', currency }`.
  - Add a threshold `checks{currency:X}` for each currency in `CURRENCIES`, using `rate>0.99`.
  - Phase 4–5's thresholds are unchanged.
- The run uses the smoke stack (`KEEP_STACK=1 bash tools/smoke/smoke.sh`), which leaves a seeded
  stack and a partner. A dedicated load partner is created with the same admin calls as smoke
  steps 4–6, with `rateLimitPerMinute: 100000` and `bulkMaxItems: 100`.

**Commands (exact):**
```bash
KEEP_STACK=1 bash tools/smoke/smoke.sh                                   # → SMOKE_OK; env file path printed as "ENV=<path>"
export $(grep -E '^(APP_DB_PASSWORD)=' "$ENV" | xargs)
dotnet run --project tools/CompositionSimulator -c Release -- --source db \
  --connection "Host=127.0.0.1;Port=55432;Database=eways;Username=eways_app;Password=$APP_DB_PASSWORD" \
  --samples 2000 --seed 20261028 --deadline-ms 8000 --out docs/reports/composition-coverage-compose
NET=$(docker network ls --filter label=com.docker.compose.project=eways-smoke -q | head -1)
docker run --rm --network "$NET" -v "$PWD:/work" -w /work -e BASE_URL=http://api:8080 -e API_KEY="$LOAD_KEY" \
  -e VUS=10 -e DURATION=5m -e CURRENCIES=USD,EUR,SGD,AED,GBP grafana/k6:0.54.0 run tests/load/partner-orders.js
docker compose -p eways-smoke exec -T postgres psql -U postgres -d eways -At -F '|' -c \
  "SELECT currency, count(*), count(*) FILTER (WHERE succeeded), round(percentile_cont(0.5) WITHIN GROUP (ORDER BY duration_ms)),
          round(percentile_cont(0.95) WITHIN GROUP (ORDER BY duration_ms)), round(percentile_cont(0.99) WITHIN GROUP (ORDER BY duration_ms))
   FROM sales.composition_attempts WHERE partner_id = $LOAD_PID GROUP BY currency ORDER BY currency"
docker compose -p eways-smoke exec -T postgres psql -U postgres -d eways -At -F '|' -c \
  "SELECT error_code, count(*) FROM sales.composition_attempts WHERE partner_id = $LOAD_PID AND NOT succeeded GROUP BY error_code ORDER BY 2 DESC"
docker compose -p eways-smoke down -v
```

**`docs/reports/coverage-and-load.md`** has these sections, in this order:
1. **Environment:** machine, CPU and RAM, Docker version, git SHA, image digest, catalog size
   (variants per currency from `catalog.mv_storefront_variant`).
2. **Representative distribution:** copied from the simulator report's definition. It is the
   PRD's documented distribution (spec §10.5).
3. **Simulator coverage per currency:** in-bounds success rate, out-of-bounds mandated values,
   failure codes with counts, p50/p95/p99 ms, mean lines, mean |deviation| bps.
4. **Uncovered ranges:** every log-decade bucket below 99%, or "none".
5. **Live load (k6):** VUs, duration, request count, `http_req_duration{name:create_order}`
   p50/p95/p99, `checks` per currency, and whether thresholds passed.
6. **Live composition telemetry:** the two SQL results above as tables.
7. **Verdict against PRD targets:** 99% success on the distribution; p95 < 10 s under the declared
   load. Each is stated as met or not met, with the gap. Release gate §16, "agreement on the load
   profile", stays open.
8. **Reproduction:** the commands block above.

- [ ] **Step 1: Write the failing test.** In `OpsPackageTests`, add
  `Coverage_And_Load_Report_Has_Every_Section_And_Real_Numbers`:
  - the 8 headings, in order
  - a row for each of `USD, EUR, SGD, AED, GBP` in sections 3 and 6
  - `p95`/`p99` cells that are numbers, not `TBD` or blank
  - section 7 contains `met` or `not met` for both targets
- [ ] **Step 2: Run, expect FAIL.**
  `dotnet test --project tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj --filter-class "*.OpsPackageTests"`.
- [ ] **Step 3: Run the commands** and write the report from their outputs. Copy numbers only;
  do not round success rates up.
- [ ] **Step 4: Run, expect PASS.** Confirm that `docs/reports/k6-partner-orders.json` exists and
  that its `metrics.http_req_duration{name:create_order}` has a `p(99)` value.
- [ ] **Step 5: Commit.** `docs(reports): composition coverage and k6 load results on the compose stack`

---

### Task 19: `docs/RUNBOOK.md`

**Files:** Create `docs/RUNBOOK.md`. Create or modify `tests/ArchitectureTests/OpsPackageTests.cs`.

**Content.** The headings are exact, and every command is copy-paste runnable from the
repository root:
1. **Deploy:**
   - `docker compose -f build/compose/docker-compose.yml --env-file build/compose/.env up -d --build --wait`
   - `.env` from `.env.example`, with every secret generated (`openssl rand -hex 32`)
   - the health checks to watch
2. **Migrate:**
   - the `migrations` init container
   - manual `docker compose … run --rm migrations`
   - never automatic on startup
   - `--migrate` is idempotent
3. **Seed (non-production only):**
   - `docker compose … run --rm -e ASPNETCORE_ENVIRONMENT=Staging api --seed`
   - Production refuses with exit 2
   - production starts with an empty catalog, so partner orders return
     `EXACT_COMPOSITION_UNAVAILABLE` until an import publishes products (spec §10.5)
4. **Key rotation:**
   - **partner keys:** `POST /api/v1/admin/partners/{id}/keys/{keyId}/rotate` with `overlapHours`,
     then revoke the old key
   - **`ApiKeys__Pepper`:** rotating the pepper invalidates every key. Re-issue all keys in a
     maintenance window. The order is: issue under the new pepper (a second deployment), notify
     partners, then revoke.
   - **MinIO app user, DB passwords, Keycloak client secrets and SMTP:** the exact `mc admin user`,
     `ALTER ROLE … PASSWORD`, `.env` and `up -d` steps
5. **Keycloak realm import:**
   - `--import-realm` runs at start and skips realms that already exist
   - updating a realm uses the admin console export/diff, or `kc.sh import --override true` in a
     maintenance window
   - `${VAR}` placeholders, SMTP fail-fast, creating the first super_admin in the console,
     enforced TOTP
6. **FX override:**
   - `POST /api/v1/admin/pricing/fx/overrides {base, quote, rate, validUntil}` (super_admin);
     clear with `DELETE …`
   - when to use it (ECB outage beyond `Fx:MaxRateAge` 72h; the AED peg)
   - verify with `GET /api/v1/admin/pricing/fx/overrides`
   - `POST /fx/refresh`
7. **Backup and restore:**
   - `docker compose exec -T postgres pg_dump -U postgres -Fc eways > eways-$(date +%F).dump`
   - restore: `pg_restore --clean --if-exists -d eways`, with api and worker stopped
   - Keycloak DB: the same steps against `keycloak-db`
   - MinIO: `mc mirror --overwrite local/documents backup/documents` and the same for
     `catalog-media`. `import-staging` is optional.
   - a restore drill checklist, then `GET /api/v1/admin/audit/verify` must be `intact: true` after
     a restore
8. **Incident checks:**
   - `/health/ready`
   - `docker compose ps`
   - the job backlog: `SELECT type, status, count(*) FROM jobs.job GROUP BY 1,2`
   - stuck leases: `locked_until < now()`
   - composition failure codes: `GET /api/v1/admin/dashboard`
   - the FX age: `GET /api/v1/admin/pricing/fx/rates`
   - rate-limit 429s
   - audit seal lag
   - MinIO reachability (PDF 302 but 5xx at the URL)
   - logs by `X-Correlation-Id`
9. **Smoke and load:**
   - `bash tools/smoke/smoke.sh`
   - the Task 18 commands
10. **Go-live release gates (spec §16, not resolved by the build):** each of the six gates as an
    unchecked item, with its owner role and the evidence required:
    - real supply and delivery evidence
    - end-customer authorization
    - legal invoice recipient fields per jurisdiction (`RequiredBuyerFields`)
    - content and image rights per crawled domain (`SourcePolicy`)
    - accounting review of non-GST invoicing and credit notes
    - an agreed load profile behind p95 (link the coverage-and-load report)

- [ ] **Step 1: Write the failing test.** `Runbook_Has_Every_Section_And_Real_Commands` asserts:
  - the 10 headings appear in order
  - the text contains `pg_dump`, `pg_restore`, `mc mirror`, `--import-realm`, `--migrate`, `--seed`,
    `fx/overrides`, `/rotate` and `audit/verify`
  - section 10 has exactly 6 `- [ ]` items
  - no line contains a literal secret, using the same regex as `No_Bruno_File_Contains_A_Literal_Key_Or_Jwt`
    plus `password=\S{6,}`
- [ ] **Step 2: Run, expect FAIL.**
  `dotnet test --project tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj --filter-class "*.OpsPackageTests"`.
- [ ] **Step 3: Write `docs/RUNBOOK.md`.** Execute the backup-and-restore commands once against
  the smoke stack (`KEEP_STACK=1`), and record "drill performed <date>, audit verify intact" in
  section 7.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit.** `docs: operations runbook with release gates`

---

### Task 20: `docs/SECURITY-CHECKLIST.md`

**Files:** Create `docs/SECURITY-CHECKLIST.md`. Modify `tests/ArchitectureTests/OpsPackageTests.cs`.

**Content.** One `- [ ]` item per control. Each item names the test, file or command that proves
it. Sections:
1. **Secrets:**
   - only from env or `.env`
   - `.env` is git-ignored
   - `Env_Example_Has_No_Values`, `Realm_Secrets_Are_Placeholders`, `Environment_Files_Hold_No_Secret_Values`
   - `git log -p | grep -E "pk_[A-Za-z0-9]{8}_"` is empty
2. **Partner API keys:**
   - HMAC-SHA256 with a pepper, constant-time compare, shown once, `Cache-Control: no-store`
   - prefix-only logging; never logged
   - rotation, revocation and suspension
3. **Tenancy:**
   - `PartnerPrincipal` only
   - `No_Inbound_Request_Dto_Has_A_PartnerId_Property`
   - cross-partner 404 (`Acceptance04_…`)
4. **Admin authorization:**
   - realm gate plus permission
   - `Acceptance13StaffPermissionTests`
   - `Every_Mapped_Endpoint_Requires_Authorization_Except_An_Explicit_Allowlist`
   - `Every_Non_Shop_GraphQL_Field_Requires_Authorization`
   - Backoffice TOTP
5. **Audit:**
   - same-save recording, `AdminAuditCoverageTests`, the append-only grant
     (`App_Role_Cannot_Rewrite_Audit_History`), hash-chain seals, verification
   - the RBAC separate-save edge
6. **Abuse limits:**
   - global, per-partner and admin rate limits
   - GraphQL depth 10 and cost
   - bulk caps, upload size caps, the SSRF guard (`Sourcing:Crawler:AllowPrivateNetworks=false`)
7. **Transport and storage:**
   - TLS at Caddy
   - private `documents` and `import-staging` buckets
   - 5-minute presigned URLs
   - least-privilege DB roles (`eways_app` / `eways_migrator`)
   - MinIO app user is not root
8. **PII and retention:**
   - audit `before`/`after` hold changed fields only
   - usage log retention is 90 days
   - logs carry no keys
   - `Seq`/OTLP redaction
   - Keycloak password policy
9. **Supply chain:**
   - pinned image tags
   - chiseled non-root image
   - `dotnet list package --vulnerable --include-transitive` is clean (record the output date)
   - Bruno CLI pinned with `--save-exact`
10. **Pre-go-live:** link RUNBOOK §10.

- [ ] **Step 1: Write the failing test.** `Security_Checklist_Names_Existing_Tests` asserts:
  - the 10 headings appear
  - every backticked identifier matching `^[A-Z][A-Za-z0-9]+_[A-Za-z0-9_]+$` is the name of a test
    method that exists in `tests/**/*.cs` (grep the sources)
- [ ] **Step 2: Run, expect FAIL.**
  `dotnet test --project tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj --filter-class "*.OpsPackageTests"`.
- [ ] **Step 3: Write the checklist.** Run `dotnet list EwaysVNext.slnx package --vulnerable --include-transitive`
  and paste the summary line with its date.
- [ ] **Step 4: Run, expect PASS.** Mutation check:
  1. Rename one cited test name in the checklist to a non-existent one and expect FAIL.
  2. Revert.
- [ ] **Step 5: Commit.** `docs: security checklist bound to the tests that prove each control`

---

### Task 21: AGENTS.md, TESTING-GUARANTEES.md and ONBOARDING.md match the delivered system

**Files:** Modify `AGENTS.md`, `docs/TESTING-GUARANTEES.md` and `docs/ONBOARDING.md`.
Modify `tests/ArchitectureTests/DocumentationTests.cs`.

**Content:**
- **AGENTS.md:**
  - The "N tests, four suites" line and each suite count come from the Task 22 run.
  - The API table rows: `/openapi/v1.json` (all environments), `/graphql` (limits), and
    `/api/v1/admin/*` (rate limit `admin`).
  - A pointer to `docs/RUNBOOK.md`, `docs/SECURITY-CHECKLIST.md`, `docs/api/bruno` and
    `tools/smoke/smoke.sh`.
  - "Load and coverage are not part of `dotnet test`".
- **TESTING-GUARANTEES.md:**
  - the per-suite table with counts
  - new rows for the contract snapshots (what a snapshot proves: the shape is unchanged; what it
    does not prove: that the shape is right)
  - the audit coverage (proves a row per admin write; does not prove `before`/`after` content)
  - the authorization allowlist
  - the GraphQL limits
  - smoke (manual, Docker, not in CI)
  - k6 and the simulator (reports, not tests)
- **ONBOARDING.md:** the smoke and Bruno commands.

- [ ] **Step 1: Write the failing test.** `Agents_Md_Test_Count_Matches_Testing_Guarantees` checks
  that the integer before " tests" in AGENTS.md equals the sum of the per-suite counts in the
  TESTING-GUARANTEES table, and that each suite name appears in both files.
- [ ] **Step 2: Run, expect FAIL** (the counts are stale).
  `dotnet test --project tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj --filter-class "*.DocumentationTests"`.
- [ ] **Step 3: Edit the docs.** Take the counts from
  `dotnet test EwaysVNext.slnx 2>&1 | grep -E "^(Passed|Failed)!|total:"`.
- [ ] **Step 4: Run, expect PASS**, together with `Agents_Md_Bolds_Exactly_The_Existing_Modules`.
- [ ] **Step 5: Commit.** `docs: AGENTS, testing guarantees and onboarding reflect the final suites and ops package`

---

### Task 22: Phase 8 gate (final)

**Files:** Create `docs/reports/phase-8.md`.

- [ ] **Step 1:** `dotnet build EwaysVNext.slnx -c Release`. Expected: 0 errors and 0 warnings.
- [ ] **Step 2:** `dotnet test EwaysVNext.slnx` with Docker running. Expected: every suite
  passes, and the counts equal those written in Task 21. If they differ, redo Task 21 Step 3.
- [ ] **Step 3:** Run the snapshots with no `UPDATE_SNAPSHOTS`, and check that
  `git status --porcelain tests/IntegrationTests/Contracts/Snapshots` is empty and that no
  `*.received.*` file exists.
- [ ] **Step 4:** Run `bash tools/smoke/smoke.sh`. Expected: `SMOKE_OK`.
- [ ] **Step 5:** Run `docker compose -f build/compose/docker-compose.yml --env-file build/compose/ci.env config --quiet`
  and the same with `-f build/compose/docker-compose.smoke.yml` added. Expected: exit 0 for both.
- [ ] **Step 6:** Write `docs/reports/phase-8.md`:
  - **What works:** OpenAPI, snapshots, Bruno, smoke, the report, the runbook and the checklist.
  - **Incomplete:** for example, the smoke is not in CI because it needs Docker-in-CI and about
    10 minutes.
  - **Risks:** the coverage gaps and the p95 verdict from Task 18; the release gates still open.
  - **Reproduction commands.**
  - **Run-wide summary:** the 16 PRD acceptance scenarios, each mapped to its test name or report
    and its result.
- [ ] **Step 7: Commit.** `docs: phase 8 report and end-of-run summary`

---

## Contract additions produced here

```csharp
// EwaysVNext.Presentation.Endpoint.Security
public static class RateLimitPolicies { Admin = "admin"; GraphQL = "graphql"; }
public static TBuilder RequireAdmin<TBuilder>(this TBuilder b, string permission, bool superOnly = false); // policy + permission + admin rate limit
// EwaysVNext.Presentation.Endpoint.GraphQL
public static class GraphQLLimits { MaxDepth = 10; MaxFieldCost = 20_000; MaxTypeCost = 20_000; }
// EwaysVNext.Audit.Application
public interface IStandaloneAuditWriter { Task RecordAsync(AuditActor, string action, string targetType, string targetId, object? before, object? after, CancellationToken ct = default); }
// EwaysVNext.Core.Application.Security
public interface IAdminActorAccessor { AdminActor? Current { get; } }
// AdminPermissions
AuditVerify = "audit.verify"   // super_admin
// Admin module: IAdminDashboardService, ICustomer360Service, IOrder360Service, IAuditVerificationService (+ views in Task 2)
// Catalog.QueryApplication: ICatalogAdminQueries.ListDuplicateCandidatesAsync(long productId, int limit) → Result<IReadOnlyList<DuplicateCandidateDto>>
// Config: RateLimiting:AdminPermitPerMinute (120), OpenApi:Expose (true)
// HexagonalBoundaryTests.AdminReadReferences = { Sales, Sourcing, Customer } (QueryApplication only) — replaces phase 0's built-in Admin rule
```

## Conflicts with other plans and how this plan resolves them

1. **Test fixture name.** Resolved in plan review: phase 0 now creates `PostgresFixture` /
   `PostgresSuite.Name`, the name every phase uses.
2. **The phase-0 SDL snapshot test does not exist.** Phase 7 Task 23 updates a "phase-0 SDL
   snapshot test", but phase 0 never creates one. Task 15 creates it. Phase 7's step is a no-op
   if phase 7 runs first.
3. **Admin policy literals.** Phases 2–3, 5 and 7 gate their admin groups with the literal
   `"admin:staff"` and no `RequirePermission`. Phase 1's convention is policy plus permission.
   Task 8 moves every admin group to `RequireAdmin(...)`.
4. **The Sales and Sourcing `AdminActor` helpers.** Phase 5's `CallerClaims.AdminActorOf` (from
   `identity_id ?? sub`) and phase 7's `AdminActorFactory.From` duplicate phase 1's
   `AdminActor.TryFrom`. They are left in place. `IAdminActorAccessor` uses `TryFrom`. The phase
   reports should note the duplication for later cleanup.
5. **The partner request field casing.** Resolved in plan review: the partner API (the bare
   `/api/v1/orders`, `/api/v1/customers/...` and `/api/v1/invoices/...` routes) uses **snake_case**
   JSON for requests and responses, exactly as in PRD §9 (`external_order_id`,
   `external_customer_id`, `customer.full_name`, `payment_status`, …). The shop and admin APIs
   stay camelCase. OpenAPI examples, Bruno and the smoke script use snake_case for partner calls.
6. **The Backoffice realm has no direct grants and requires TOTP** (phase 1). The smoke script
   therefore uses the temporary-user and temporary-client-flag procedure (design call 9), not a
   committed test client.

# Phases 4–5 — Sales Implementation Plan

> Part of docs/superpowers/plans/2026-10-28-ecommerce-mvp.md — read its Global Constraints, Review Focus and Cross-phase contracts first.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the `Sales` module. Phase 4 delivers amount-driven partner orders:
- a pure composition engine, its simulator and a k6 script
- an atomic `Order` aggregate
- partner REST create, bulk, get, list and invoices
- lazy QuestPDF invoices

Phase 5 delivers the website channel:
- cart and checkout with a pro forma invoice
- admin payment confirmation, cancel, and refunds with credit notes
- the read side that the phase 6 Admin dashboard consumes

**Spec sections:** §3.5–§3.6 (Sales row and four exception rows), §4 (money, band, snapshots, tax),
§5.6 (Sales, all of it), §6.2–§6.4 (partner principal, tenancy, errors, audit), §8 (popularity
columns of `mv_storefront_product` / `mv_home` only), §9.1, §9.2 (cart / checkout / orders /
documents only), §9.3 (orders only), §10, §11, §13 (Sales rows), §14 phases 4–5.

**Assumptions about phases 0–3.** They are named once here, and every task relies on them:

| # | Assumption | Where it is used |
|---|---|---|
| A1 | `PostgresFixture` has `CreateFactory()` and the xUnit collection `PostgresSuite.Name`, with migrations applied (same shape as today's `LocalDbFixture`/`LocalDbSuite`). | all integration tests |
| A2 | Phase 0's error writer is public: `EndpointResult.Fail(Error error, object? details = null, object? items = null)`. It writes `{code,message,retryable,correlationId,details?,items?}`. | `PRICE_CHANGED` details, bulk |
| A3 | Phase 0's correlation middleware sets `HttpContext.TraceIdentifier` to the `X-Correlation-Id` value. | `correlationId` everywhere |
| A4 | Phase 0 maps aggregate roots with Postgres `xmin` through `builder.UseXminConcurrency()`, a phase-0 `EntityTypeBuilder` extension. Phase 0 also uses snake_case naming. | Order, Cart configs |
| A5 | Phase 1's `PartnerApiKey` handler adds a `bulk_max_items` claim from `ApiKeyAuthResult.BulkMaxItems`. The master contract's `PartnerPrincipal` does not carry it. | bulk |
| A6 | Phase 1 registers `admin:staff`, which passes for staff **or** super_admin, and `admin:super`. The per-realm policy `Shop` still exists, as it does in today's `KeycloakAuthenticationExtensions`. | shop/admin endpoints |
| A7 | **Confirmed against phase 1:** `ICustomerResolutionPort.CreatePartnerCustomerAsync` is idempotent under a race. When it loses on `UNIQUE(partner_id, external_customer_id)` it reloads and returns `Success(existing ref)`, not a failure. Sales therefore needs no conflict fallback. | concurrency |
| A8 | Phase 2–3's `--seed` code path is a public service. Integration tests call it once per fixture: `SalesTestData.EnsureSeededAsync`. Find it with `grep -rn '"--seed"' src/Host`. | integration tests |
| A9 | Variant deactivation and FX overrides use the phase 2–3 admin service methods that their admin REST endpoints call. | Acceptance 8, review focus 3 |

---

## File Structure

```
src/Modules/Sales/
├── Application/EwaysVNext.Modules.Sales.Application.csproj
│   ├── DependencyInjection.cs                 AddSalesApplication()
│   ├── SalesErrors.cs                         every Error the module returns (codes, HTTP, retryable)
│   ├── SalesOptions.cs                        Sales:Bulk:Parallelism (4), Sales:Bulk:DeadlineSeconds (120), Sales:CandidateCacheSeconds (30)
│   ├── SalesMetrics.cs                        Meter "EwaysVNext.Sales": customer_contact_sync_failures, composition_duration_ms
│   ├── Money/AmountText.cs                    "500.00" ⇄ minor units, ^\d+\.\d{2}$ only
│   ├── Money/RequestFingerprint.cs            SHA-256(external_customer_id|amount_minor|currency)
│   ├── Composition/                           PURE: no IO, no ports, no EF (arch-tested)
│   │   ├── CompositionCandidate.cs  CompositionOptions.cs  CompositionPlan.cs (ComposedLine, CompositionStats)
│   │   ├── ICompositionBudget.cs              + StopwatchBudget, IterationBudget
│   │   ├── SplitMix64.cs                      version-stable PRNG (seed is persisted, so System.Random is not used)
│   │   ├── ExactHit.cs                        step 4: price assignment + gcd-aware residual search
│   │   ├── CompositionEngine.cs               steps 1–7 (strategy "v1")
│   │   ├── CompositionScoring.cs              step 5 score
│   │   └── CompositionVerifier.cs             step 8, independent checker
│   ├── Domain/
│   │   ├── Orders/  Order.cs OrderLine.cs PaymentRecord.cs OrderDocument.cs Refund.cs CompositionTrace.cs
│   │   │            OrderEnums.cs OrderInputs.cs OrderNumberFormat.cs
│   │   ├── Documents/ DocumentContent.cs (+ SellerBlock, BuyerBlock, DocumentLine, PaymentInstructionBlock)
│   │   │              DocumentWording.cs  BuyerFieldRequirements.cs  DocumentContentJson.cs
│   │   ├── Carts/   Cart.cs CartLine.cs                                  (phase 5)
│   │   └── Telemetry/CompositionAttempt.cs                               append-only row, not an aggregate
│   ├── Ports/Inbound/  IPartnerOrderService.cs IInvoiceService.cs ICartService.cs ICheckoutService.cs
│   │                   IOrderAdminService.cs  + command/result records beside each
│   ├── Ports/Outbound/ IOrderRepository.cs ICartRepository.cs IDocumentNumberAllocator.cs
│   │                   ISalesCatalogGateway.cs ISalesPricingGateway.cs ISalesCustomerGateway.cs
│   │                   ISalesSettingsGateway.cs ICandidatePoolGateway.cs ICompositionAttemptRecorder.cs
│   │                   IDocumentPdfRenderer.cs IDocumentStore.cs ITaxPolicy.cs
│   │                   ICompositionBudgetFactory.cs ICompositionSeedSource.cs
│   └── Services/ PartnerOrderService.cs LiveRequoter.cs InvoiceService.cs NonGstTaxPolicy.cs
│                 CartService.cs CheckoutService.cs OrderAdminService.cs              (last three: phase 5)
├── Adapter/Outbound/  (references Catalog/Pricing/Customer/Settings .Application — 4 exception rows)
│   ├── DependencyInjection.cs                 AddSalesOutboundAdapters()
│   ├── Persistence/ OrderRepository.cs CartRepository.cs DocumentNumberAllocator.cs CompositionAttemptRecorder.cs
│   ├── Gateways/ SalesCatalogGateway.cs SalesPricingGateway.cs SalesCustomerGateway.cs SalesSettingsGateway.cs
│   ├── Candidates/ CandidatePoolCache.cs (singleton) CachedCandidatePoolGateway.cs (scoped)
│   └── Documents/ QuestPdfDocumentRenderer.cs ObjectStorageDocumentStore.cs
├── Adapter/Inbound/Rest/
│   ├── Common/ MoneyJson.cs (MoneyDto, AmountJson) CallerClaims.cs (PartnerPrincipal / ShopCaller / AdminActor from claims)
│   ├── Partner/ PartnerOrderEndpoints.cs PartnerInvoiceEndpoints.cs PartnerDtos.cs PartnerOpenApiExamples.cs
│   ├── Shop/    ShopCartEndpoints.cs ShopCheckoutEndpoints.cs ShopOrderEndpoints.cs ShopDtos.cs  (phase 5)
│   └── Admin/   AdminOrderEndpoints.cs AdminDtos.cs                                              (phase 5)
├── Adapter/Inbound/GraphQL/ (phase 5) Shop/ShopCartGraph.cs Shop/ShopOrderGraph.cs Admin/AdminOrderGraph.cs DependencyInjection.cs
├── QueryApplication/
│   ├── Partner/ IPartnerOrderQueries.cs PartnerOrderQueries.cs PartnerOrderDtos.cs
│   ├── Shop/    IShopOrderQueries.cs ShopOrderQueries.cs ShopOrderDtos.cs                        (phase 5)
│   ├── Admin/   ISalesAdminQueries.cs SalesAdminQueries.cs SalesAdminDtos.cs                     (phase 5)
│   └── DependencyInjection.cs                 AddSalesQueryApplication()
└── Installer/ModuleInstaller.cs               AddSalesModule(IConfiguration)

src/Database/Sales/Configurations/  OrderConfiguration.cs (+ owned/child configs) CartConfiguration.cs
                                    CompositionAttemptConfiguration.cs SalesSequences.cs
src/Database/Migrations/            *_AddSalesOrders, *_StorefrontPopularityFromSales, *_AddSalesCarts
tools/CompositionSimulator/         CompositionSimulator.csproj Program.cs SeedLikeCatalog.cs AmountDistribution.cs
                                    CatalogSources.cs CoverageReport.cs
tests/load/partner-orders.js
tests/UnitTests/Sales/              Composition/*, Orders/*, Carts/*, Services/*, Documents/*
tests/IntegrationTests/Sales/       SalesApiFactory.cs SalesTestData.cs InMemoryDocumentStore.cs + test classes
tests/ArchitectureTests/SalesArchitectureTests.cs
docs/reports/                       composition-coverage.md/.json, phase-4.md, phase-5.md
```

**Composition-attempt recording (decision).** Each run of the engine appends one row to
`sales.composition_attempts`, whether it succeeds or fails. The row holds `at, partner_id,
currency, target_minor, succeeded, error_code, duration_ms, lines, iterations,
candidate_pool_size, seed, strategy_version, recompositions, order_id`.
- It is written through `ICompositionAttemptRecorder`, in its **own DI scope and save**, and only
  on a best-effort basis. Telemetry never fails or rolls back an order. A failed composition
  writes no business row at all; the attempt row is the only thing it leaves behind.
- The dashboard's success rate, p95 and recent failure codes read only this table.
- The table is append-only. It is not an aggregate, so the separate save does not breach rule 7.

---

## Phase 4 — Sales: partner

### Task 1: Sales module scaffold and boundary rows

**Files:**
- Create: `src/Modules/Sales/**` using `./New-EwaysModule.ps1 -ModuleName Sales`. Then add
  `Adapter/Inbound/GraphQL/EwaysVNext.Modules.Sales.Adapter.Inbound.GraphQL.csproj` by hand,
  copying the Catalog GraphQL csproj.
- Modify: `src/Host/EwaysVNext.Host/Program.cs`, `EwaysVNext.slnx`
- Create: `tests/ArchitectureTests/SalesArchitectureTests.cs`
- Modify: `tests/ArchitectureTests/HexagonalBoundaryTests.cs` (`ModuleNames`, exception theory)

**Interfaces:**
- Produces:
  - `public static IServiceCollection AddSalesModule(this IServiceCollection services, IConfiguration configuration)`
  - Sales in `ModuleNames`
  - Four rows in phase 0's `SanctionedReferences` (`TheoryData<string,string>` of
    `(consumerModule, providerModule)`):
    `{ "Sales", "Catalog" }`, `{ "Sales", "Pricing" }`, `{ "Sales", "Customer" }`, `{ "Sales", "Settings" }`.

- [ ] **Step 1: Write failing tests.** Put them in `SalesArchitectureTests.cs`:
```csharp
[Fact] public void Sales_Composition_Has_No_IO_Or_Port_Dependencies() =>
    Types.InAssembly(typeof(CompositionEngine).Assembly)
        .That().ResideInNamespace("EwaysVNext.Modules.Sales.Application.Composition")
        .ShouldNot().HaveDependencyOnAny(
            "Microsoft.EntityFrameworkCore", "System.Net", "System.IO", "System.Data",
            "EwaysVNext.Modules.Sales.Application.Ports", "EwaysVNext.Persistence",
            "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.Logging")
        .GetResult().IsSuccessful.Should().BeTrue();

[Fact] public void Sales_Domain_Never_References_Ports_Or_Services() =>
    Types.InAssembly(typeof(CompositionEngine).Assembly)
        .That().ResideInNamespace("EwaysVNext.Modules.Sales.Application.Domain")
        .ShouldNot().HaveDependencyOnAny("EwaysVNext.Modules.Sales.Application.Ports",
            "EwaysVNext.Modules.Sales.Application.Services")
        .GetResult().IsSuccessful.Should().BeTrue();

[Fact] public void Sales_Source_Never_Calls_An_Invoice_A_Tax_Invoice() =>
    Directory.EnumerateFiles(Path.Combine(SourceRoot(), "Modules", "Sales"), "*.cs", SearchOption.AllDirectories)
        .Where(f => File.ReadAllText(f).Contains("Tax Invoice", StringComparison.OrdinalIgnoreCase))
        .Should().BeEmpty();
```
  Also add the four exception rows and `"Sales"` to `ModuleNames`.

- [ ] **Step 2: Run the tests to see them fail.** Command:
  `dotnet test --project tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj`.
  Expected: build FAILS, because `CompositionEngine` does not exist yet.

- [ ] **Step 3: Implement the scaffold.**
  - Run the scaffold script.
  - Add project references:
    - Application → Domain, Outcomes, Core.Application, Events.Application, Persistence (for
      `IUnitOfWork`, as Catalog does), Audit.
    - Adapter.Outbound → Application, Database, Storage, plus the four provider Application
      projects.
    - QueryApplication → Database, Outcomes, Queries.Application, Core.Application.
  - Create an empty `public static class CompositionEngine` in `Composition/CompositionEngine.cs`.
  - Add `ModuleInstaller.AddSalesModule`. It calls `AddSalesApplication`,
    `AddSalesOutboundAdapters`, `AddSalesRestAdapter`, `AddSalesGraphQL` and
    `AddSalesQueryApplication`, then
    `.AddPersistenceInterceptors().AddUserProfileService().AddUnitOfWork<EwaysVNextDbContext>().AddEvents(typeof(CompositionEngine).Assembly)`.
  - In `Program.cs`, call `.AddSalesModule(builder.Configuration)` next to the other modules.
  - The Application folder is `Application/Domain/**` (top level), so the existing
    `DeterministicTimeTests` scan covers the Sales aggregates.

- [ ] **Step 4: Run the tests to see them pass.** Command:
  `dotnet test --project tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj`.
  Expected: PASS. That includes `HexagonalBoundaryTests` with the four new rows.

- [ ] **Step 5: Commit.**
```bash
git add src/Modules/Sales src/Host EwaysVNext.slnx tests/ArchitectureTests
git commit -m "feat(sales): scaffold Sales module with boundary rows and composition purity test" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 2: Sales errors, amount text and request fingerprint

**Files:**
- Create:
  - `src/Modules/Sales/Application/SalesErrors.cs`
  - `Money/AmountText.cs`
  - `Money/RequestFingerprint.cs`
- Test: `tests/UnitTests/Sales/Money/AmountTextTests.cs`, `RequestFingerprintTests.cs`. Add
  `ProjectReference`s from UnitTests to every Sales project.

**Interfaces:**
- Produces:
```csharp
public static class SalesErrors {
  public static Error ValidationFailed(string message);                    // VALIDATION_FAILED 400
  public static readonly Error AmountOutOfRange;                           // AMOUNT_OUT_OF_RANGE 422
  public static readonly Error ExactCompositionUnavailable;                // EXACT_COMPOSITION_UNAVAILABLE 422
  public static readonly Error CompositionTimeout;                         // COMPOSITION_TIMEOUT 503, Retryable = true
  public static readonly Error OrderIdempotencyConflict;                   // ORDER_IDEMPOTENCY_CONFLICT 409
  public static readonly Error InvoiceBuyerFieldsMissing;                  // INVOICE_BUYER_FIELDS_MISSING 422
  public static readonly Error CustomerContactRequired;                    // CUSTOMER_CONTACT_REQUIRED 422
  public static readonly Error PriceChanged;                               // PRICE_CHANGED 409
  public static readonly Error NotFound;                                   // NOT_FOUND 404
  public static readonly Error InsufficientScope;                          // INSUFFICIENT_SCOPE 403
  public static readonly Error BatchDeadlineExceeded;                      // BATCH_DEADLINE_EXCEEDED 503, Retryable = true
  public static readonly Error OrderStateConflict;                         // ORDER_STATE_CONFLICT 409       (phase 5)
  public static readonly Error RefundExceedsRefundable;                    // REFUND_EXCEEDS_REFUNDABLE 422  (phase 5)
  public static readonly Error EmailNotVerified;                           // EMAIL_NOT_VERIFIED 403         (phase 5)
  public static readonly Error CustomerSuspended;                          // CUSTOMER_SUSPENDED 403         (phase 5)
  public static Error Internal(string message);                            // INTERNAL_SALES 500
  public const string ContactSyncFailedWarning = "CONTACT_SYNC_FAILED";
}
public static class AmountText {
  public static bool TryParseMinor(string? text, out long minor);   // only ^\d+\.\d{2}$; false on overflow
  public static string Format(long minor);                          // 50000 → "500.00", invariant culture
}
public static class RequestFingerprint {
  public static string Compute(string externalCustomerId, long amountMinor, string currency); // lowercase hex SHA-256 of UTF-8 "{ext}|{minor}|{CUR}"
}
```

- [ ] **Step 1: Write failing tests.**
```csharp
[Theory]
[InlineData("500"), InlineData("500.1"), InlineData("500.123"), InlineData("1e3"), InlineData("-5.00"),
 InlineData(" 500.00"), InlineData("500.00 "), InlineData("500,00"), InlineData(""), InlineData(null),
 InlineData("99999999999999999999.00")]
public void Rejects_Anything_But_Two_Decimal_Digits(string? text) => AmountText.TryParseMinor(text, out _).Should().BeFalse();

[Theory]
[InlineData("10.00", 1000L), InlineData("17.43", 1743L), InlineData("500.00", 50000L),
 InlineData("12845.99", 1284599L), InlineData("1000000.00", 100000000L), InlineData("0.01", 1L)]
public void Parses_Two_Decimal_Strings(string text, long expected)
{ AmountText.TryParseMinor(text, out var m).Should().BeTrue(); m.Should().Be(expected); AmountText.Format(m).Should().Be(text); }

[Fact] public void Fingerprint_Is_Sha256_Of_Pipe_Joined_Fields() =>
    RequestFingerprint.Compute("C-77", 50000, "USD").Should().Be(
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("C-77|50000|USD"))));

[Fact] public void Fingerprint_Ignores_Contact_And_Changes_With_Amount() =>
    RequestFingerprint.Compute("C-77", 50001, "USD").Should().NotBe(RequestFingerprint.Compute("C-77", 50000, "USD"));

[Fact] public void Retryable_Errors_Are_Exactly_Timeout_And_Batch_Deadline() =>
    typeof(SalesErrors).GetFields().Select(f => f.GetValue(null)).OfType<Error>()
        .Where(e => e.Retryable).Select(e => e.Code)
        .Should().BeEquivalentTo("COMPOSITION_TIMEOUT", "BATCH_DEADLINE_EXCEEDED");
```

- [ ] **Step 2: Run the tests to see them fail.** Command:
  `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-namespace "EwaysVNext.UnitTests.Sales.Money"`.
  Expected: FAIL (compile).

- [ ] **Step 3: Implement.**
  - `AmountText.TryParseMinor` uses
    `[GeneratedRegex(@"^\d+\.\d{2}$", RegexOptions.CultureInvariant)]`, then `long.TryParse`
    on the digits with the dot removed.
  - `SalesErrors` uses the phase-0 `Error(Code, Message, HttpStatus, Retryable)`.

- [ ] **Step 4: Run the tests to see them pass.** Same command. Expected: PASS.

- [ ] **Step 5: Commit.**
```bash
git commit -am "feat(sales): error catalogue, strict two-decimal amounts, request fingerprint" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 3: Composition types, PRNG, budgets and the independent verifier

**Files:**
- Create: `Composition/{CompositionCandidate,CompositionOptions,CompositionPlan,ICompositionBudget,SplitMix64,CompositionVerifier}.cs`
- Test: `tests/UnitTests/Sales/Composition/CompositionVerifierTests.cs`, `SplitMix64Tests.cs`

**Interfaces:**
- Produces (namespace `EwaysVNext.Modules.Sales.Application.Composition`):
```csharp
public sealed record CompositionCandidate(long VariantId, long CategoryId, long ReferenceMinor, long LoMinor, long HiMinor);
public sealed record CompositionOptions(int MaxLines, long TargetUsdMinor, int MaxQuantityPerLine = 500,
    int SolutionsToScore = 6, int MaxAttempts = 96, string StrategyVersion = CompositionEngine.StrategyVersion);
public sealed record ComposedLine(long VariantId, long CategoryId, int Quantity, long UnitPriceMinor, long ReferenceMinor)
{ public long LineTotalMinor => checked(Quantity * UnitPriceMinor); }
public sealed record CompositionStats(long Seed, string StrategyVersion, int Iterations, int CandidatePoolSize,
    IReadOnlyList<string> AttemptSummaries);                              // ≤ 20 most recent, e.g. "a7:k=4:residual"
public sealed record CompositionPlan(IReadOnlyList<ComposedLine> Lines, CompositionStats Stats);
public interface ICompositionBudget { bool IsExhausted { get; } }
public sealed class StopwatchBudget(TimeSpan limit) : ICompositionBudget;  // Stopwatch.GetTimestamp, not the wall clock
public sealed class IterationBudget(int reads) : ICompositionBudget;       // exhausted after `reads` reads of IsExhausted: deterministic
public sealed class ExpiredBudget : ICompositionBudget;                    // always exhausted (tests, A7)
internal struct SplitMix64(long seed) { public ulong NextULong(); public int Next(int minInclusive, int maxExclusive); }
public static class CompositionVerifier {
  public static Result Verify(long targetMinor, IReadOnlyList<CompositionCandidate> pool, IReadOnlyList<ComposedLine> lines, int maxLines);
}
```

- [ ] **Step 1: Write failing tests.**
  - The verifier rejects each of the following, each with code `COMPOSITION_VERIFICATION_FAILED`:
    - Σ ≠ T, off by 1 minor unit
    - `unit = lo − 1`
    - `unit = hi + 1`
    - `quantity = 0`
    - a variant missing from the pool
    - a duplicate variant
    - `lines.Count > maxLines`
    - zero lines
  - It accepts `unit == lo` and `unit == hi`.
  - `SplitMix64(42)` gives the published first output `0xBDD732262FEB6E95` and never changes.
```csharp
[Fact] public void Accepts_Band_Edges()
{
    var pool = new[] { new CompositionCandidate(1, 10, 1000, 950, 1050), new CompositionCandidate(2, 11, 2000, 1900, 2100) };
    CompositionVerifier.Verify(950 + 2 * 2100, pool, [new(1, 10, 1, 950, 1000), new(2, 11, 2, 2100, 2000)], 200)
        .IsSuccess.Should().BeTrue();
}
```

- [ ] **Step 2: Run the tests to see them fail.** Command:
  `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-namespace "EwaysVNext.UnitTests.Sales.Composition"`.
  Expected: FAIL (compile).

- [ ] **Step 3: Implement.**
  - The verifier recomputes everything from `pool` keyed by `VariantId`. It must not trust
    `ComposedLine.ReferenceMinor`, and it shares no code with the engine.
  - It returns `Error("COMPOSITION_VERIFICATION_FAILED", <which rule>, 500)`.
  - `SplitMix64` is the standard algorithm:
    - increment `0x9E3779B97F4A7C15`
    - mix constants `0xBF58476D1CE4E5B9` and `0x94D049BB133111EB`
    - shifts 30 / 27 / 31
  - `Next(min, max)` is `min + (int)(NextULong() % (ulong)(max - min))`.

- [ ] **Step 4: Run the tests to see them pass.** Same command. Expected: PASS.

- [ ] **Step 5: Commit.**
```bash
git commit -am "feat(sales): composition types, deterministic PRNG, budgets, independent verifier" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 4: Exact-hit price assignment (strategy step 4)

**Files:**
- Create: `Composition/ExactHit.cs`
- Test: `tests/UnitTests/Sales/Composition/ExactHitTests.cs`. Add
  `[assembly: InternalsVisibleTo("EwaysVNext.UnitTests")]` to the Sales Application project.

**Interfaces:**
- Produces:
```csharp
internal readonly record struct DraftLine(int CandidateIndex, long Quantity, long Reference, long Lo, long Hi);
internal static class ExactHit {
  internal const long MaxResidualWindow = 4_000;
  public static long[]? TryAssign(long target, IReadOnlyList<DraftLine> lines, ICompositionBudget budget);
}
```

- [ ] **Step 1: Write failing tests.**
```csharp
[Fact] public void Q1_Line_Absorbs_Any_Residual()
{
    DraftLine[] lines = [new(0, 7, 1999, 1900, 2098), new(1, 1, 50000, 47500, 52500)];
    var p = ExactHit.TryAssign(7 * 1999 + 50000 + 3, lines, new IterationBudget(10_000))!;
    (7 * p[0] + p[1]).Should().Be(7 * 1999 + 50000 + 3);
    p[0].Should().BeInRange(1900, 2098); p[1].Should().BeInRange(47500, 52500);
}
[Fact] public void Target_Outside_Sum_Of_Bands_Is_Null() =>
    ExactHit.TryAssign(3 * 1050 + 1, [new(0, 3, 1000, 950, 1050)], new IterationBudget(100)).Should().BeNull();
[Fact] public void Gcd_That_Does_Not_Divide_Residual_Is_Null() =>   // q = 2 and 4 cannot make an odd total
    ExactHit.TryAssign(2 * 1000 + 4 * 1000 + 1, [new(0, 2, 1000, 950, 1050), new(1, 4, 1000, 950, 1050)], new IterationBudget(10_000)).Should().BeNull();
[Fact] public void Residual_Search_Finds_Multi_Line_Moves()        // q = 3 and 5, no q = 1 line, residual 1
{
    DraftLine[] lines = [new(0, 3, 1000, 950, 1050), new(1, 5, 1000, 950, 1050)];
    var p = ExactHit.TryAssign(8000 + 1, lines, new IterationBudget(10_000))!;
    (3 * p[0] + 5 * p[1]).Should().Be(8001);
}
[Fact] public void Starts_At_Reference_When_Target_Equals_Reference_Sum() =>
    ExactHit.TryAssign(2 * 1234 + 999, [new(0, 2, 1234, 1173, 1295), new(1, 1, 999, 950, 1048)], new IterationBudget(100))
        .Should().Equal(1234, 999);
```

- [ ] **Step 2: Run the tests to see them fail.** Command:
  `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "EwaysVNext.UnitTests.Sales.Composition.ExactHitTests"`.
  Expected: FAIL (compile).

- [ ] **Step 3: Implement.** The tests do not determine this algorithm, so the body is given in
  full:
```csharp
public static long[]? TryAssign(long target, IReadOnlyList<DraftLine> lines, ICompositionBudget budget)
{
    Int128 low = 0, high = 0, atRef = 0, weight = 0;
    foreach (var l in lines)
    {
        low += (Int128)l.Quantity * l.Lo; high += (Int128)l.Quantity * l.Hi;
        atRef += (Int128)l.Quantity * l.Reference; weight += (Int128)l.Quantity * (l.Hi - l.Lo);
    }
    if (target < low || target > high) return null;

    var p = new long[lines.Count];
    for (var i = 0; i < p.Length; i++) p[i] = lines[i].Reference;
    var delta = (long)(target - atRef);

    // (a) spread Δ in proportion to q_i·(hi_i−lo_i), in whole steps of q_i (truncation toward zero).
    if (delta != 0 && weight > 0)
    {
        var spread = delta;
        for (var i = 0; i < p.Length; i++)
        {
            var l = lines[i];
            var share = (long)((Int128)spread * l.Quantity * (l.Hi - l.Lo) / weight);
            delta -= Move(ref p[i], l, share / l.Quantity);
        }
    }
    // (b) greedy, largest quantity first: leaves |Δ| below the smallest q that still has slack.
    foreach (var i in Enumerable.Range(0, p.Length).OrderByDescending(i => lines[i].Quantity).ThenBy(i => i))
    {
        if (delta == 0) return p;
        delta -= Move(ref p[i], lines[i], delta / lines[i].Quantity);
    }
    if (delta == 0) return p;
    // (c) a q = 1 line with room absorbs any integer residual.
    for (var i = 0; i < p.Length; i++)
        if (lines[i].Quantity == 1 && p[i] + delta >= lines[i].Lo && p[i] + delta <= lines[i].Hi) { p[i] += delta; return p; }
    // (d) gcd-aware bounded search over the remaining slack.
    return SolveResidual(delta, lines, p, budget) ? p : null;
}

private static long Move(ref long price, DraftLine line, long steps)
{
    var clamped = Math.Clamp(steps, line.Lo - price, line.Hi - price);
    price += clamped;
    return clamped * line.Quantity;
}

// Find moves d_i with Lo_i ≤ p_i + d_i ≤ Hi_i and Σ q_i·d_i == residual. Each line is used at most once
// on a path: a line extends only states that existed before that line was processed.
private static bool SolveResidual(long residual, IReadOnlyList<DraftLine> lines, long[] p, ICompositionBudget budget)
{
    long g = 0, m = 0;
    for (var i = 0; i < p.Length; i++)
        if (lines[i].Hi > lines[i].Lo) { g = (long)BigInteger.GreatestCommonDivisor(g, lines[i].Quantity); m = Math.Max(m, lines[i].Quantity); }
    if (g == 0 || residual % g != 0) return false;
    var window = 2 * m;
    if (window > MaxResidualWindow) return false;

    var reach = new Dictionary<long, (int Line, long Move, long Prev)> { [0] = (-1, 0, 0) };
    for (var i = 0; i < p.Length && !reach.ContainsKey(residual); i++)
    {
        if (budget.IsExhausted) return false;
        var l = lines[i];
        var down = Math.Max(l.Lo - p[i], -(window / l.Quantity + 1));
        var up = Math.Min(l.Hi - p[i], window / l.Quantity + 1);
        foreach (var offset in reach.Keys.Order().ToArray())
            for (var d = down; d <= up; d++)
            {
                if (d == 0) continue;
                var next = offset + d * l.Quantity;
                if (Math.Abs(next) <= window) reach.TryAdd(next, (i, d, offset));
            }
    }
    if (!reach.ContainsKey(residual)) return false;
    for (var at = residual; at != 0;) { var (line, move, prev) = reach[at]; p[line] += move; at = prev; }
    return true;
}
```

- [ ] **Step 4: Run the tests to see them pass.** Same command. Expected: PASS.

- [ ] **Step 5: Commit.**
```bash
git commit -am "feat(sales): exact-hit price assignment with gcd-aware residual search" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 5: Composition engine, strategy v1

**Files:**
- Create: `Composition/CompositionEngine.cs`, `Composition/CompositionScoring.cs`
- Test: `tests/UnitTests/Sales/Composition/CompositionEngineTests.cs`

**Interfaces:**
- Consumes: Tasks 3 and 4.
- Produces:
```csharp
public static class CompositionEngine {
  public const string StrategyVersion = "v1";
  public static Result<CompositionPlan> Compose(long targetMinor, IReadOnlyList<CompositionCandidate> candidates,
      CompositionOptions options, long seed, ICompositionBudget deadline);
}
internal static class CompositionScoring { public static long Score(long targetMinor, long targetUsdMinor, IReadOnlyList<ComposedLine> lines); }
```

- [ ] **Step 1: Write failing tests.**
  - An empty pool gives `EXACT_COMPOSITION_UNAVAILABLE`.
  - `targetMinor <= 0` gives `VALIDATION_FAILED`.
  - `ExpiredBudget` gives `COMPOSITION_TIMEOUT` with `Retryable == true`.
  - A pool `{ r = 1000 }` with T = 1000 returns one line `(q = 1, p = 1000)`.
  - A pool where no sum is reachable gives `EXACT_COMPOSITION_UNAVAILABLE`. Example: one candidate
    `lo = hi = 1000`, T = 1500.
  - The same seed returns identical lines. Shuffling the input order of `candidates` does not
    change the result.
  - Every success passes `CompositionVerifier.Verify`.
  - Variety: with 12 categories and T = $12,845.99 USD, `Lines.Select(l => l.CategoryId)` are
    distinct.
```csharp
[Fact] public void Is_Deterministic_Per_Seed_And_Independent_Of_Input_Order()
{
    var pool = SeedLikeCatalog.For("USD");
    var a = CompositionEngine.Compose(1_284_599, pool, Options(1_284_599), 7, new IterationBudget(50_000));
    var b = CompositionEngine.Compose(1_284_599, pool.Reverse().ToArray(), Options(1_284_599), 7, new IterationBudget(50_000));
    a.Value!.Lines.Should().Equal(b.Value!.Lines);
}
```
  `SeedLikeCatalog` is introduced in Task 6. For this task, write the test with a hand-built
  60-candidate pool in `CompositionEngineTests`, and switch it to `SeedLikeCatalog` in Task 6.

- [ ] **Step 2: Run the tests to see them fail.** Command:
  `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "EwaysVNext.UnitTests.Sales.Composition.CompositionEngineTests"`.
  Expected: FAIL.

- [ ] **Step 3: Implement spec §10.2 exactly in this order.** No `float` or `double` anywhere.
  Shares are integers. Use `Int128` wherever a product can overflow.
  1. **Normalize.**
     - Drop candidates with `Lo < 1 || Lo > Hi`.
     - Deduplicate by `VariantId`; the first one wins after sorting.
     - Sort by `(ReferenceMinor, VariantId)`.
     - Group by `CategoryId`, with the groups ordered by id. Input order then has no effect.
  2. **Line count per attempt.** `kBase = clamp(round(log10(TargetUsdMinor / 100)) + rng.Next(0, 3), 1, min(MaxLines, pool.Count))`.
     The log10 is integer: count the decimal digits of the USD major amount minus 1, rounding up
     when the leading digits are ≥ 316…, i.e. `√10`.
     Broadening: from attempt 32, `k = kBase ± 1`, alternating every 8 attempts.
  3. **Shares and candidates for lines 1..k−1.**
     - Draw weights `w_i = rng.Next(1000, 3001)` and set `T_i = T·w_i / Σw`.
     - Visit categories in a rng-shuffled order. Do not repeat a category until all have been
       used. That rule is dropped from attempt 64.
     - In the category, take the largest `r ≤ T_i` and its neighbourhood: 5 candidates, or 20
       from attempt 48. Pick one uniformly with rng. If the category has none, use the whole pool.
     - `q_i = max(1, (T_i + r_i/2) / r_i)`, capped at `cap = max(MaxQuantityPerLine, ceil(T / (k · maxReference)))`.
  4. **Anchor line k (q = 1).**
     - `R = T − Σ_{i<k} q_i·r_i`.
     - Pick the unused candidate whose band contains `R`, preferring an unused category. If none
       does, pick the one whose `r` is nearest to `R`.
     - If `R < min Lo`, lower the largest `q_i` by `ceil((minLo − R) / r_i)` (never below 1) and
       recompute.
  5. **Feasibility** (§10.2 step 3). `L = Σ q·lo`, `H = Σ q·hi`. Make at most 3 adjust rounds:
     - `T > H`: raise the q of the non-anchor line with the largest `hi` by `ceil((T−H)/hi)`, up
       to `cap`.
     - `T < L`: lower the largest non-anchor q by `ceil((L−T)/lo)`, but not below 1.
     - Otherwise swap the anchor for its neighbour in the needed direction.
     - If it is still infeasible, record `a{n}:k={k}:infeasible` and go to the next attempt.
  6. **Exact hit.** `ExactHit.TryAssign`. If it returns null, record `residual` and continue.
     Otherwise build the `ComposedLine`s and call `CompositionVerifier.Verify`. A failure there
     is a bug: record `verify` and continue. Otherwise keep `(score, attemptIndex, lines)`.
  7. **Stop** when `SolutionsToScore` solutions exist, `MaxAttempts` is reached, or
     `deadline.IsExhausted` (checked once per attempt, and inside `ExactHit`).
  8. **Pick.** Lowest `CompositionScoring.Score`; ties go to the lower attempt index.
     `Score = Σ q·|p−r|·10_000 / T` (total deviation in bps of target)
     `+ 200·distance(lines, [2,8])` (only when `TargetUsdMinor ≥ 5_000`)
     `+ 100·(lines − distinctCategories)`
     `+ Σ max(0, q−100)/10`.
  9. **Fail.**
     - No solution and the deadline is exhausted: `COMPOSITION_TIMEOUT`.
     - No solution otherwise: `EXACT_COMPOSITION_UNAVAILABLE`.
     - Solutions exist: return the best one, even if the deadline is exhausted.

- [ ] **Step 4: Run the tests to see them pass.** Same command. Expected: PASS.

- [ ] **Step 5: Commit.**
```bash
git commit -am "feat(sales): composition engine strategy v1 (variety, anchor q=1 line, scoring, broadening)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 6: Composition property tests and mandated amounts

**Files:**
- Create: `tools/CompositionSimulator/SeedLikeCatalog.cs`. Task 19 adds the csproj, and this
  file is linked into UnitTests.
- Modify: `tests/UnitTests/EwaysVNext.UnitTests.csproj`. Add
  `<Compile Include="..\..\tools\CompositionSimulator\SeedLikeCatalog.cs" Link="Sales\Composition\SeedLikeCatalog.cs" />`,
  `PackageReference FsCheck.Xunit.v3`, and a `ProjectReference` to Pricing.Application.
- Modify: `Directory.Packages.props`. Add `FsCheck` and `FsCheck.Xunit.v3`, both 3.3.x.
- Test: `tests/UnitTests/Sales/Composition/CompositionPropertyTests.cs`, `MandatedAmountTests.cs`

**Interfaces:**
- Produces:
```csharp
public static class SeedLikeCatalog {
  // 400 variants, 12 categories, base USD log-uniform $0.50–$50,000 (fixed internal seed 20261028),
  // converted with fixed rates USD 1, EUR 0.92, SGD 1.35, AED 3.6725, GBP 0.79, round-half-even to 2dp,
  // band lo = (95r + 99)/100, hi = 105r/100 (integer; equals PriceBand.For).
  public static readonly IReadOnlyList<string> Currencies; // USD, EUR, SGD, AED, GBP
  public static IReadOnlyList<CompositionCandidate> For(string currency);
  public static long ToUsdMinor(long amountMinor, string currency);
}
```

- [ ] **Step 1: Write failing tests.**
```csharp
[Property(MaxTest = 300)]
public Property Every_Success_Is_Exact_Within_Band_With_Positive_Quantities(PositiveInt cents, long seed)
{
    var target = 1_000L + cents.Get % 99_999_000L;              // $10.00 – $1,000,000.00
    var result = Compose(target, "USD", seed);
    return (result.IsFailure
            ? result.Error!.Code is "EXACT_COMPOSITION_UNAVAILABLE" or "COMPOSITION_TIMEOUT"
            : CompositionVerifier.Verify(target, SeedLikeCatalog.For("USD"), result.Value!.Lines, 200).IsSuccess
              && result.Value.Lines.Sum(l => l.LineTotalMinor) == target
              && result.Value.Lines.All(l => l.Quantity >= 1)).ToProperty();
}
[Property(MaxTest = 100)]
public bool Same_Seed_Same_Lines(PositiveInt cents, long seed) =>
    Compose(1_000 + cents.Get % 9_999_000, "EUR", seed) is var a && Compose(1_000 + cents.Get % 9_999_000, "EUR", seed) is var b
    && a.IsSuccess == b.IsSuccess && (a.IsFailure || a.Value!.Lines.SequenceEqual(b.Value!.Lines));
[Property(MaxTest = 300)]
public bool Arbitrary_Pools_Never_Produce_An_Invalid_Success(NonEmptyArray<PositiveInt> refs, PositiveInt t, long seed)
{ /* pool from refs (r = 50 + ref % 1_000_000, category = index % 5), T = 100 + t % 5_000_000; success ⇒ Verify succeeds */ }
[Property(MaxTest = 200)]
public bool A_Candidate_Whose_Band_Contains_T_Guarantees_Success(PositiveInt idx, long seed)
{ /* pick candidate c from SeedLikeCatalog.For("SGD"); T = c.Lo + (idx % (c.Hi - c.Lo + 1)); Compose must succeed */ }
[Fact] public void SeedLikeCatalog_Band_Equals_PriceBand_For() =>
    SeedLikeCatalog.Currencies.SelectMany(SeedLikeCatalog.For)
        .Should().OnlyContain(c => PriceBand.For(c.ReferenceMinor) == (c.LoMinor, c.HiMinor));
```
  In `MandatedAmountTests`:
```csharp
public static TheoryData<string, long> Mandated => new(
    from c in new[] { "USD", "EUR", "SGD", "AED", "GBP" }
    from a in new[] { 1_000L, 1_743L, 50_000L, 1_284_599L, 100_000_000L }
    select (c, a));
[Theory, MemberData(nameof(Mandated))]
public void Mandated_Amount_Composes_Exactly(string currency, long target)
{
    foreach (var seed in new long[] { 1, 2, 3 })
    {
        var r = CompositionEngine.Compose(target, SeedLikeCatalog.For(currency),
            new CompositionOptions(200, SeedLikeCatalog.ToUsdMinor(target, currency)), seed, new IterationBudget(200_000));
        r.IsSuccess.Should().BeTrue($"{currency} {AmountText.Format(target)} seed {seed}: {r.Error?.Code}");
        CompositionVerifier.Verify(target, SeedLikeCatalog.For(currency), r.Value!.Lines, 200).IsSuccess.Should().BeTrue();
    }
}
```
  The mandated amounts are face values in each currency. The engine does not apply order bounds;
  the service does (Task 12).

- [ ] **Step 2: Run the tests to see them fail.** Command:
  `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-namespace "EwaysVNext.UnitTests.Sales.Composition"`.
  Expected: FAIL. `SeedLikeCatalog` is missing, which is a compile error.

- [ ] **Step 3: Implement `SeedLikeCatalog`.**
  - Use `SplitMix64` semantics, inlined so the file stays dependency-free apart from
    `CompositionCandidate`.
  - Draw the log-uniform price in integer arithmetic over a 5-decade grid of 400 points with
    jitter.
  - If a mandated case fails, tune the Task 5 heuristics, not the test.
  - Replace the hand-built pool in `CompositionEngineTests` with `SeedLikeCatalog.For("USD")`.

- [ ] **Step 4: Run the tests to see them pass.** Same command. Expected: PASS, with all 25
  mandated cases green on 3 seeds each.

- [ ] **Step 5: Commit.**
```bash
git add tools/CompositionSimulator/SeedLikeCatalog.cs tests/UnitTests Directory.Packages.props
git commit -m "test(sales): FsCheck composition properties and mandated amounts x 5 currencies" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 7: Document content, wording, tax policy, buyer-field requirements

**Files:**
- Create:
  - `Domain/Documents/{DocumentContent,DocumentWording,BuyerFieldRequirements,DocumentContentJson}.cs`
  - `Ports/Outbound/ITaxPolicy.cs`
  - `Services/NonGstTaxPolicy.cs`
- Test: `tests/UnitTests/Sales/Documents/DocumentRulesTests.cs`

**Interfaces:**
- Produces:
```csharp
public static class DocumentWording {
  public const string FulfillmentStatement = "Fulfillment is an operational assertion, not verified proof of delivery";
  public const string NotGstRegistered = "Not GST registered";
  public static string TitleFor(DocumentType type); // Commercial → "Invoice", ProForma → "Pro Forma Invoice", CreditNote → "Credit Note"
}
public sealed record SellerBlock(string LegalName, string Uen, string Address, string Email, string Footer);
public sealed record BuyerBlock(long CustomerId, string? ExternalCustomerId, string? FullName, string? CompanyName, string? Email,
    string? Phone, string? BillingAddress, string? RegistrationNumber);
public sealed record DocumentLine(int LineNo, string Sku, string Description, int Quantity, long UnitPriceMinor, long LineTotalMinor);
public sealed record PaymentInstructionBlock(long InstructionId, int Version, string Text);
public sealed record DocumentContent(string Title, string DocumentType, string DocumentNumber, DateTimeOffset IssuedAt,
    string OrderNumber, string Channel, string? ExternalOrderId, SellerBlock Seller, BuyerBlock Buyer,
    IReadOnlyList<DocumentLine> Lines, string Currency, long SubtotalMinor, long TaxMinor, long TotalMinor,
    string TaxStatement, string FulfillmentStatement, string PaymentStatus, string? PaymentSource,
    PaymentInstructionBlock? PaymentInstruction, string? RelatedDocumentNumber, long? RefundAmountMinor, string? RefundReason);
public static class DocumentContentJson { public static readonly JsonSerializerOptions Options; } // camelCase, no indentation, enums as strings
public static class BuyerFieldRequirements {
  // Each entry is a group of '|'-separated alternatives (email, phone, fullName, companyName, billingAddress, registrationNumber); every group must be satisfied.
  public static IReadOnlyList<string> Missing(IReadOnlyList<string> required, BuyerBlock buyer);
}
public sealed record TaxAssessment(long TaxMinor, string Statement);
public interface ITaxPolicy { TaxAssessment Assess(long subtotalMinor, string currency); }   // NonGstTaxPolicy: (0, "Not GST registered")
```

- [ ] **Step 1: Write failing tests.**
  - `TitleFor(Commercial) == "Invoice"`. No title contains "Tax".
  - `Missing(["email|phone"], buyer with phone only)` is empty.
  - `Missing(["email|phone"], buyer with neither)` equals `["email|phone"]`.
  - `Missing(["email", "billingAddress"], email only)` equals `["billingAddress"]`.
  - `NonGstTaxPolicy.Assess(50000, "USD") == new TaxAssessment(0, "Not GST registered")`.
  - `JsonSerializer.Serialize(content, DocumentContentJson.Options)` round-trips to an equal
    record, and serializing twice gives identical strings.

- [ ] **Step 2: Run the tests to see them fail.** Command:
  `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "EwaysVNext.UnitTests.Sales.Documents.DocumentRulesTests"`.
  Expected: FAIL.

- [ ] **Step 3: Implement** the signatures above. `DocumentContent` equality over its lists:
  override `Equals` to use `SequenceEqual`, or compare serialized JSON in the test. Pick one and
  keep it consistent.

- [ ] **Step 4: Run the tests to see them pass.** Same command. Expected: PASS.

- [ ] **Step 5: Commit.**
```bash
git commit -am "feat(sales): document content snapshot, invoice wording, non-GST tax policy, buyer-field rules" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 8: Order aggregate — CreatePartnerOrder

**Files:**
- Create: `Domain/Orders/*.cs`
- Test: `tests/UnitTests/Sales/Orders/PartnerOrderAggregateTests.cs`, `OrderMother.cs`

**Interfaces:**
- Produces:
```csharp
public enum OrderChannel { Website, PartnerApi }
public enum OrderStatus { PendingPayment, Completed, Cancelled }
public enum PaymentStatus { Unpaid, Paid, PartiallyRefunded, Refunded }
public enum PaymentSource { PartnerReported, AdminConfirmed }
public enum FulfillmentStatus { Unfulfilled, Fulfilled }
public enum FulfillmentSource { SystemAutomatic }
public enum DocumentType { ProForma, Commercial, CreditNote }
public enum DocumentStatus { None, ProFormaIssued, Invoiced, CreditNoted }
public enum LinePriceSource { Override, Fx, Base }

public sealed record PriceSnapshot(long ReferenceMinor, LinePriceSource Source, decimal? FxRate, long? FxRateId, long? FxOverrideId,
    DateTimeOffset? FxRateAt, string? FxProvenance, long BaseAmountMinor, string BaseCurrency);
public sealed record VariantSnapshot(long VariantId, long ProductId, string Sku, string ProductTitle, string BrandName,
    IReadOnlyList<string> CategoryPath, IReadOnlyDictionary<string, string> Attributes);
public sealed record OrderLineInput(long LineId, VariantSnapshot Variant, int Quantity, long UnitPriceMinor, PriceSnapshot Price);
public sealed record IssuedDocumentRef(long DocumentId, string Number);
public sealed record CompositionTraceInput(long Seed, string StrategyVersion, int Iterations, long DurationMs, int CandidatePoolSize, string AttemptsJson);
public sealed record PartnerOrderHeader(long OrderId, string Number, long PartnerId, string ExternalOrderId, string ExternalCustomerId,
    string RequestFingerprint, long CustomerId, string Currency, long RequestedTotalMinor, long UsdEquivalentMinor, string CorrelationId);

public sealed class Order : AggregateRoot<long> {
  public string Number; public OrderChannel Channel; public long? PartnerId; public string? ExternalOrderId;
  public string? ExternalCustomerId; public string? RequestFingerprint; public long CustomerId; public string Currency;
  public long TotalMinor, SubtotalMinor, TaxMinor, UsdEquivalentMinor; public DateTimeOffset CreatedAt; public string CorrelationId;
  public OrderStatus Status; public PaymentStatus PaymentStatus; public PaymentSource? PaymentSource;
  public FulfillmentStatus FulfillmentStatus; public FulfillmentSource? FulfillmentSource;
  public BuyerBlock Buyer;                                  // buyer snapshot reused by later documents
  public DocumentStatus DocumentStatus { get; }             // derived, not mapped
  public IReadOnlyList<OrderLine> Lines; IReadOnlyList<PaymentRecord> Payments; IReadOnlyList<OrderDocument> Documents;
  public IReadOnlyList<Refund> Refunds; public CompositionTrace? CompositionTrace;
  public static Order CreatePartnerOrder(PartnerOrderHeader header, IReadOnlyList<OrderLineInput> lines, CompositionTraceInput trace,
      IssuedDocumentRef invoice, long paymentRecordId, SellerBlock seller, IReadOnlyList<string> requiredBuyerFields,
      BuyerBlock buyer, TaxAssessment tax, DateTimeOffset now);
  public OrderDocument CommercialInvoice { get; }           // throws if absent; used by refunds
}
public sealed class OrderLine : Entity<long> { /* every field of the snapshot + Quantity, UnitPriceMinor, LineTotalMinor, DeviationBps, BandEnforced, LineNo */ }
public sealed class PaymentRecord : Entity<long> { PaymentSource Source; string? Method; string? Reference; Guid? VerifiedBy; DateTimeOffset RecordedAt; long AmountMinor; }
public sealed class OrderDocument : Entity<long> { DocumentType Type; string Number; DateTimeOffset IssuedAt; DocumentContent Content; long? RelatedDocumentId; string PdfObjectKey; }
public sealed class Refund : Entity<long> { long AmountMinor; string Reason; Guid RecordedBy; DateTimeOffset RecordedAt; string? ExternalReference; long CreditNoteDocumentId; }
public sealed class CompositionTrace { long Seed; string StrategyVersion; int Iterations; long DurationMs; int CandidatePoolSize; string Attempts; } // owned
public static class OrderNumberFormat { public static string Order(long n); public static string Document(DocumentType t, long n); } // ORD-000123, PF-/INV-/CN- D6
```

- [ ] **Step 1: Write failing tests** (with `OrderMother.PartnerOrder(...)` defaults: USD, lines
  from a small fixed pool, `now = TestTime.Now`).
  - Happy path:
    - `TotalMinor == Σ LineTotal == RequestedTotalMinor`; `TaxMinor == 0`; `SubtotalMinor == TotalMinor`
    - `Status == Completed`; `PaymentStatus == Paid`; `PaymentSource == PartnerReported`
    - `FulfillmentStatus == Fulfilled`; `FulfillmentSource == SystemAutomatic`
    - one `PaymentRecord` with `AmountMinor == total`
    - one Commercial document `INV-…` whose `Content.Title == "Invoice"`,
      `TaxStatement == "Not GST registered"`, `FulfillmentStatement == DocumentWording.FulfillmentStatement`,
      Buyer equal to the supplied buyer, and `PdfObjectKey == $"{orderId}/{number}.pdf"`
    - `DocumentStatus == Invoiced`
    - every line has `BandEnforced == true` and `DeviationBps == (unit − r)·10_000 / r`
      (truncated)
  - Each of these throws `DomainException` with the code shown:
    - Σ lines ≠ requested total: `ORDER_LINES_TOTAL_MISMATCH`
    - a unit at `lo − 1`: `ORDER_LINE_OUTSIDE_BAND` (band from `PriceSnapshot.ReferenceMinor`
      with the integer `95/105` rule)
    - quantity 0: `ORDER_LINE_QUANTITY_INVALID`
    - no lines: `ORDER_LINES_EMPTY`
    - a duplicate variant: `ORDER_LINE_DUPLICATE_VARIANT`
    - a buyer with neither email nor phone under the default `["email|phone"]`:
      `INVOICE_BUYER_FIELDS_MISSING`
  - The aggregate raises `PartnerOrderCreatedDomainEvent(OrderId, PartnerId)`.

- [ ] **Step 2: Run the tests to see them fail.** Command:
  `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "EwaysVNext.UnitTests.Sales.Orders.PartnerOrderAggregateTests"`.
  Expected: FAIL.

- [ ] **Step 3: Implement.**
  - The aggregate builds the Commercial `DocumentContent` itself from its own lines and totals,
    so the document cannot disagree with the order.
  - Shared private helpers: `IssueDocument(DocumentType, IssuedDocumentRef, SellerBlock, IReadOnlyList<string> requiredBuyerFields, TaxAssessment, DateTimeOffset, PaymentInstructionBlock?, OrderDocument? related, Refund? refund)`
    and `static void AssertBand(long unit, long reference)` (`100·unit ≥ 95·r ∧ 100·unit ≤ 105·r`).
  - Lists are private backing fields (Catalog `Product` style).

- [ ] **Step 4: Run the tests to see them pass.** Same command. Expected: PASS.

- [ ] **Step 5: Commit.**
```bash
git commit -am "feat(sales): Order aggregate with atomic CreatePartnerOrder (lines, payment, fulfillment, invoice)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 9: Sales persistence — mapping, sequences, constraints, repository, numbers

**Files:**
- Create:
  - `src/Database/Sales/Configurations/{OrderConfiguration,CompositionAttemptConfiguration,SalesSequences}.cs`
  - `Domain/Telemetry/CompositionAttempt.cs`
  - `Ports/Outbound/{IOrderRepository,IDocumentNumberAllocator}.cs`
  - `Adapter/Outbound/Persistence/{OrderRepository,DocumentNumberAllocator}.cs`
  - migration `AddSalesOrders`
- Modify:
  - `src/Database/EwaysVNextDbContext.cs`: add `DbSet<Order> Orders` and
    `DbSet<CompositionAttempt> CompositionAttempts`, and call `SalesSequences.Configure(modelBuilder)`
  - `src/Database/EwaysVNext.Database.csproj`: reference Sales.Application
- Test:
  - `tests/IntegrationTests/Sales/SalesSchemaTests.cs`
  - `tests/IntegrationTests/Sales/SalesOrderMother.cs`
  - `tests/UnitTests/Sales/Orders/OrderModelTests.cs` (EF model metadata, no DB)

**Interfaces:**
- Produces:
```csharp
public interface IOrderRepository {
  Task<Order?> GetByIdAsync(long orderId, CancellationToken ct = default);
  Task<PartnerOrderKey?> FindPartnerOrderKeyAsync(long partnerId, string externalOrderId, CancellationToken ct = default);
  Task<bool> TryInsertPartnerOrderAsync(Order order, CancellationToken ct = default); // false ⇔ 23505 on ux_orders_partner_external; order detached
  Task AddAsync(Order order, CancellationToken ct = default);
  Task<DocumentForRendering?> FindDocumentAsync(long documentId, DocumentAccess access, CancellationToken ct = default);
}
public sealed record PartnerOrderKey(long OrderId, string RequestFingerprint);
public abstract record DocumentAccess { public sealed record Partner(long PartnerId) : DocumentAccess; public sealed record WebsiteCustomer(long CustomerId) : DocumentAccess; public sealed record Admin : DocumentAccess; }
public sealed record DocumentForRendering(long OrderId, long DocumentId, DocumentType Type, string Number, DocumentContent Content, string PdfObjectKey);
public enum DocumentNumberKind { Order, ProForma, Commercial, CreditNote }
public interface IDocumentNumberAllocator { Task<string> NextAsync(DocumentNumberKind kind, CancellationToken ct = default); }
public sealed class CompositionAttempt : Entity<long> { /* fields from "Composition-attempt recording" above */ public static CompositionAttempt Record(long id, CompositionAttemptRecord r); }
public sealed record CompositionAttemptRecord(DateTimeOffset At, long? PartnerId, string Currency, long TargetMinor, bool Succeeded,
    string? ErrorCode, long DurationMs, int Lines, int Iterations, int CandidatePoolSize, long Seed, string StrategyVersion, int Recompositions, long? OrderId);
```

**Schema** (`sales` schema, snake_case, enums stored as `varchar(32)` strings):

| Table | Key constraints / indexes |
|---|---|
| `orders` | PK `id` (`ValueGeneratedNever`), `xmin` (A4). `ux_orders_number UNIQUE(number)`. `ux_orders_partner_external UNIQUE(partner_id, external_order_id) WHERE channel = 'PartnerApi'`. `ck_orders_total_positive CHECK (total_minor > 0)`. `ck_orders_totals_add_up CHECK (subtotal_minor + tax_minor = total_minor)`. `ck_orders_partner_fields CHECK ((channel = 'PartnerApi') = (partner_id IS NOT NULL AND external_order_id IS NOT NULL AND request_fingerprint IS NOT NULL AND external_customer_id IS NOT NULL))`. `ix_orders_partner (partner_id, id DESC) WHERE channel='PartnerApi'`. `ix_orders_customer (customer_id, channel, id DESC)`. `ix_orders_pending (created_at) WHERE order_status='PendingPayment'`. `ix_orders_created (created_at, channel, currency)`. Columns: `currency char(3)`, `buyer jsonb`. |
| `order_lines` | FK `order_id`. `ck_order_lines_quantity_positive CHECK (quantity > 0)`. `ck_order_lines_total CHECK (line_total_minor = quantity * unit_price_minor)`. **`ck_order_lines_partner_band CHECK (NOT band_enforced OR (unit_price_minor * 100 >= reference_price_minor * 95 AND unit_price_minor * 100 <= reference_price_minor * 105))`**. `category_path text[]`, `attributes jsonb`, `fx_rate numeric(18,10)`. `ix_order_lines_product (product_id)` (popularity). |
| `order_payments` | `ck_order_payments_amount_positive CHECK (amount_minor > 0)` |
| `order_documents` | `ux_order_documents_type_number UNIQUE(type, number)`. `content jsonb` via `DocumentContentJson` converter. `ck_order_documents_credit_note_link CHECK ((type = 'CreditNote') = (related_document_id IS NOT NULL))` |
| `order_refunds` | `ck_order_refunds_amount_positive CHECK (amount_minor > 0)` |
| `order_composition_traces` | PK/FK `order_id` (owned one-to-one), `attempts jsonb` |
| `composition_attempts` | PK `id`. `ix_composition_attempts_at (at DESC)`. `ix_composition_attempts_failures (at DESC) WHERE NOT succeeded` |
| sequences | `sales.order_number_seq`, `sales.pf_number_seq`, `sales.inv_number_seq`, `sales.cn_number_seq` (start 1, no cycle) |

- [ ] **Step 1: Write failing tests.** In `SalesSchemaTests`, with `[Collection(PostgresSuite.Name)]`:
```csharp
[Fact] public async Task Partner_Line_Outside_Band_Is_Rejected_By_Check_Constraint()
{
    var (orderId, lineId, r, q) = await SalesOrderMother.InsertPartnerOrderAsync(_factory.Services);   // r = 10_000, q = 1
    await SetUnitAsync(lineId, q, (95 * r + 99) / 100);          // ceil(0.95r) = 9500: accepted
    await SetUnitAsync(lineId, q, 105 * r / 100);                // floor(1.05r) = 10500: accepted
    var low = () => SetUnitAsync(lineId, q, (95 * r + 99) / 100 - 1);
    var high = () => SetUnitAsync(lineId, q, 105 * r / 100 + 1);
    (await low.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("ck_order_lines_partner_band");
    (await high.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("ck_order_lines_partner_band");
}   // SetUnitAsync: UPDATE sales.order_lines SET unit_price_minor = {u}, line_total_minor = quantity * {u} WHERE id = {lineId}
[Fact] public async Task Website_Line_Is_Not_Band_Checked() { /* insert row with band_enforced = false and unit = 2r: succeeds */ }
[Fact] public async Task Same_Partner_External_Id_Twice_Is_A_Unique_Violation() { /* 2nd insert ⇒ SqlState 23505, ConstraintName ux_orders_partner_external */ }
[Fact] public async Task Two_Partners_May_Share_An_External_Order_Id() { /* partner 1 and 2, same ext: both insert */ }
[Fact] public async Task Repository_TryInsert_Returns_False_On_Duplicate_And_Detaches() { /* second TryInsertPartnerOrderAsync ⇒ false; ChangeTracker has no Added Order */ }
[Fact] public async Task Zero_Total_And_Bad_Line_Total_Are_Rejected() { /* ck_orders_total_positive, ck_order_lines_total */ }
[Fact] public async Task Document_Numbers_Come_From_Separate_Sequences()
{ /* allocator.NextAsync(Commercial) twice ⇒ "INV-000001"-style increasing; ProForma starts independently with "PF-" */ }
```
  In `OrderModelTests` (model metadata from `EwaysVNextDbContextDesignTimeFactory`, no
  connection), assert:
  - `Order.Id` has `ValueGenerated.Never`
  - the filter `"channel = 'PartnerApi'"` on `ux_orders_partner_external`
  - the presence of every check constraint named in the table

- [ ] **Step 2: Run the tests to see them fail.** Commands:
  - `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "EwaysVNext.UnitTests.Sales.Orders.OrderModelTests"`
  - `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "EwaysVNext.IntegrationTests.Sales.SalesSchemaTests"`

  Expected: FAIL (compile).

- [ ] **Step 3: Implement.**
  - Write the configurations from the table. Children are mapped with
    `OwnsMany`/`HasMany(...).WithOne()` through backing fields.
  - `CompositionTrace` uses `OwnsOne(...).ToTable("order_composition_traces")`.
  - `SalesSequences.Configure` uses `modelBuilder.HasSequence<long>("order_number_seq", "sales")`
    and the same for the other three.
  - `DocumentNumberAllocator`:
    - Runs `db.Database.SqlQuery<long>($"SELECT nextval('sales.inv_number_seq') AS \"Value\"").SingleAsync()`.
    - Formats the result with `OrderNumberFormat`.
    - When `db.Database.ProviderName` is not Npgsql (dev InMemory), it uses a process-local
      `Interlocked` counter.
  - `OrderRepository.TryInsertPartnerOrderAsync`:
    - `db.Orders.Add(order)`, then `await unitOfWork.SaveChangesAsync(ct)`.
    - Catch `DbUpdateException { InnerException: PostgresException { SqlState: "23505", ConstraintName: "ux_orders_partner_external" } }`.
    - In the catch, detach every entry belonging to the order graph and return `false`.
  - `FindDocumentAsync` is an `AsNoTracking` projection. It filters `Partner` on
    `o.PartnerId == id && o.Channel == PartnerApi`, and `WebsiteCustomer` on
    `o.CustomerId == id && o.Channel == Website`.
  - Generate the migration:
    `dotnet ef migrations add AddSalesOrders -p src/Database -s src/Host/EwaysVNext.Host`.

- [ ] **Step 4: Run the tests to see them pass.** Same commands. Expected: PASS.

- [ ] **Step 5: Mutation-test the band CHECK.**
  1. Delete the `ck_order_lines_partner_band` `HasCheckConstraint` line and its `AddCheckConstraint`
     in the migration.
  2. Rerun `Partner_Line_Outside_Band_Is_Rejected_By_Check_Constraint`. Expected: FAIL.
  3. Restore both lines and rerun. Expected: PASS.
  4. Repeat for `ux_orders_partner_external`: delete the index, and
     `Same_Partner_External_Id_Twice_Is_A_Unique_Violation` FAILS. Restore it.

- [ ] **Step 6: Commit.**
```bash
git add src/Database src/Modules/Sales tests
git commit -m "feat(sales): order persistence, number sequences, band/total/idempotency constraints" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 10: Outbound gateways and the candidate pool cache

**Files:**
- Create:
  - `Ports/Outbound/{ISalesCatalogGateway,ISalesPricingGateway,ISalesCustomerGateway,ISalesSettingsGateway,ICandidatePoolGateway}.cs`
  - `Adapter/Outbound/Gateways/*.cs`
  - `Adapter/Outbound/Candidates/{CandidatePoolCache,CachedCandidatePoolGateway}.cs`
- Test:
  - `tests/UnitTests/Sales/Gateways/CandidatePoolCacheTests.cs`
  - `tests/UnitTests/Sales/Gateways/GatewayMappingTests.cs`

**Interfaces:**
- Consumes (master contracts): `ISellableVariantPort` (+`GetVersionAsync`), `IPriceQuotePort`
  (+`GetVersionAsync`), `IFxSnapshotPort`, `ICustomerResolutionPort`, `IWebsiteCustomerPort`,
  `ISettingsPort`.
- Produces (Application-local copies; never the provider's types):
```csharp
public sealed record SalesVariant(long VariantId, long ProductId, string Sku, string ProductTitle, long BrandId, string BrandName,
    long PrimaryCategoryId, IReadOnlyList<string> CategoryPath, IReadOnlyDictionary<string, string> Attributes);
public interface ISalesCatalogGateway {
  Task<IReadOnlyList<SalesVariant>> GetSellableAsync(IReadOnlyCollection<long>? variantIds, CancellationToken ct = default);
  Task<long> GetVersionAsync(CancellationToken ct = default);
}
public sealed record SalesPriceQuote(long VariantId, long ReferenceMinor, long LoMinor, long HiMinor, PriceSnapshot Snapshot);
public sealed record SalesUsdConversion(long UsdMinor);
public interface ISalesPricingGateway {
  Task<Result<IReadOnlyList<SalesPriceQuote>>> QuoteAsync(IReadOnlyCollection<long> variantIds, string currency, DateTimeOffset now, CancellationToken ct = default);
  Task<Result<SalesUsdConversion>> ToUsdAsync(long amountMinor, string currency, DateTimeOffset now, CancellationToken ct = default);
  Task<long> GetVersionAsync(CancellationToken ct = default);
}
public sealed record PartnerContact(string? Email, string? Phone, string? FullName);
public sealed record SalesPartnerCustomer(long CustomerId, string? Email, string? Phone, string? FullName);
public sealed record SalesInvoiceParty(long CustomerId, string? FullName, string? CompanyName, string? Email, string? Phone,
    string? BillingAddress, string? RegistrationNumber);                   // address flattened to one line
public sealed record SalesWebsiteCustomer(long CustomerId, bool EmailVerified, bool IsActive);
public interface ISalesCustomerGateway {
  Task<SalesPartnerCustomer?> FindPartnerCustomerAsync(long partnerId, string externalCustomerId, CancellationToken ct = default);
  Task<Result<SalesPartnerCustomer>> CreatePartnerCustomerAsync(long partnerId, string externalCustomerId, PartnerContact contact, string correlationId, CancellationToken ct = default);
  Task<Result> SyncPartnerContactAsync(long customerId, long partnerId, PartnerContact contact, string correlationId, CancellationToken ct = default);
  Task<Result<SalesInvoiceParty>> GetInvoicePartyAsync(long customerId, CancellationToken ct = default);
  Task<Result<SalesWebsiteCustomer>> EnsureWebsiteCustomerAsync(Guid identityUserId, string email, bool emailVerified, CancellationToken ct = default);
}
public sealed record SalesCommerceLimits(long MinUsdMinor, long MaxUsdMinor, int BulkDefaultMax, int CompositionDeadlineMs, int CompositionMaxLines);
public interface ISalesSettingsGateway {
  Task<(SellerBlock Seller, IReadOnlyList<string> RequiredBuyerFields, bool GstRegistered)> GetSellerAsync(CancellationToken ct = default);
  Task<PaymentInstructionBlock> GetActivePaymentInstructionAsync(CancellationToken ct = default);
  Task<SalesCommerceLimits> GetCommerceLimitsAsync(CancellationToken ct = default);
}
public sealed record CandidatePool(string Currency, long CatalogVersion, long PricingVersion, IReadOnlyList<CompositionCandidate> Candidates);
public interface ICandidatePoolGateway {
  Task<Result<CandidatePool>> GetAsync(string currency, DateTimeOffset now, CancellationToken ct = default); // CURRENCY_NOT_ENABLED passes through
  void Invalidate(string currency);
}
```

- [ ] **Step 1: Write failing tests.** NSubstitute stands in for the provider ports.
  `FixedClock` drives the cache.
```csharp
[Fact] public async Task Second_Call_Within_30s_With_Same_Versions_Hits_Cache()
{ await gw.GetAsync("USD", T0); await gw.GetAsync("USD", T0.AddSeconds(29));
  await sellable.Received(1).GetSellableAsync(null, Arg.Any<CancellationToken>()); }
[Fact] public async Task Expires_After_30s() { /* T0, T0+30s ⇒ 2 loads (30s is the boundary: expired) */ }
[Fact] public async Task Catalog_Or_Pricing_Version_Bump_Reloads_Immediately() { /* version 1→2 at T0+1s ⇒ 2 loads */ }
[Fact] public async Task Invalidate_Forces_Reload() { }
[Fact] public async Task Cache_Is_Per_Currency() { /* USD then EUR ⇒ 2 loads */ }
[Fact] public async Task Variants_Without_Quote_Are_Not_Candidates() { /* quote omits variant 3 ⇒ pool lacks 3 */ }
[Fact] public async Task Candidate_Category_Is_Primary_Category_And_Band_Comes_From_Quote() { }
[Fact] public async Task Currency_Not_Enabled_Is_Returned_Not_Cached() { }
```
  `GatewayMappingTests` maps each provider record to its Sales copy field by field, including
  `PriceSource.Base` → `LinePriceSource.Base` and `AddressSnapshot` flattened to
  `"Line1, Line2, City PostalCode, CC"`.

- [ ] **Step 2: Run the tests to see them fail.** Command:
  `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-namespace "EwaysVNext.UnitTests.Sales.Gateways"`.
  Expected: FAIL.

- [ ] **Step 3: Implement.**
  - `CandidatePoolCache` is a singleton
    `ConcurrentDictionary<string, (long Cv, long Pv, DateTimeOffset LoadedAt, CandidatePool Pool)>`.
    Its TTL comes from `SalesOptions.CandidateCacheSeconds` (default 30).
  - `CachedCandidatePoolGateway` is scoped. It reads both versions on every call, which is two
    cheap queries, then reuses the pool only when both versions match and `now − LoadedAt < TTL`.
    Otherwise it runs `GetSellableAsync(null)` plus one `QuoteAsync(allIds)`.
  - Register everything in `AddSalesOutboundAdapters`.

- [ ] **Step 4: Run the tests to see them pass.** Same command. Run the architecture tests too.
  Expected: PASS.

- [ ] **Step 5: Commit.**
```bash
git commit -am "feat(sales): ACL gateways to Catalog/Pricing/Customer/Settings and 30s versioned candidate cache" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 11: Composition attempt recorder

**Files:**
- Create:
  - `Ports/Outbound/{ICompositionAttemptRecorder,ICompositionBudgetFactory,ICompositionSeedSource}.cs`
  - `Adapter/Outbound/Persistence/CompositionAttemptRecorder.cs`
- Test: `tests/IntegrationTests/Sales/CompositionAttemptRecorderTests.cs`

**Interfaces:**
- Produces:
```csharp
public interface ICompositionAttemptRecorder { Task RecordAsync(CompositionAttemptRecord record, CancellationToken ct = default); } // never throws
public interface ICompositionBudgetFactory { ICompositionBudget Start(TimeSpan deadline); }   // default: StopwatchBudget
public interface ICompositionSeedSource { long NextSeed(); }                                    // default: RandomNumberGenerator
```

- [ ] **Step 1: Write failing tests.**
  - Recording through a factory scope inserts one row.
  - The caller's `EwaysVNextDbContext` has no tracked entries afterwards: the recorder opens its
    own scope.
  - When the database throws (simulated by disposing the provider), the recorder logs and does
    not throw.

- [ ] **Step 2: Run the tests to see them fail.** Command:
  `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "EwaysVNext.IntegrationTests.Sales.CompositionAttemptRecorderTests"`.
  Expected: FAIL.

- [ ] **Step 3: Implement.** The recorder takes `IServiceScopeFactory`, opens a new scope, adds
  `CompositionAttempt.Record(idGen.Generate(), record)` and saves. It catches every exception
  except `OperationCanceledException` and logs it at Warning.

- [ ] **Step 4: Run the tests to see them pass.** Same command. Expected: PASS.

- [ ] **Step 5: Commit.**
```bash
git commit -am "feat(sales): best-effort composition attempt recorder in its own scope" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 12: PartnerOrderService.CreateAsync (spec §10.3)

**Files:**
- Create:
  - `Ports/Inbound/IPartnerOrderService.cs`
  - `Services/PartnerOrderService.cs`, `Services/LiveRequoter.cs`
  - `SalesMetrics.cs`, `SalesOptions.cs`
- Test: `tests/UnitTests/Sales/Services/PartnerOrderServiceTests.cs`

**Interfaces:**
- Consumes: Tasks 2–11, `PartnerPrincipal`, `PartnerScopes`, `IClock`, `IIdGenerator`.
- Produces:
```csharp
public sealed record CreatePartnerOrderCommand(string ExternalOrderId, string ExternalCustomerId, long AmountMinor, string Currency, PartnerContact? Contact);
public sealed record PartnerOrderCreated(long OrderId, bool Replayed, IReadOnlyList<string> Warnings);
public enum BulkItemStatus { Created, Replayed, Failed }
public sealed record BulkItem(int Index, CreatePartnerOrderCommand Command);
public sealed record BulkItemOutcome(int Index, string ExternalOrderId, BulkItemStatus Status, long? OrderId, Error? Error, IReadOnlyList<string> Warnings);
public interface IPartnerOrderService {
  Task<Result<PartnerOrderCreated>> CreateAsync(PartnerPrincipal principal, CreatePartnerOrderCommand command, string correlationId, CancellationToken ct = default);
  Task<IReadOnlyList<BulkItemOutcome>> CreateBulkAsync(PartnerPrincipal principal, IReadOnlyList<BulkItem> items, string correlationId, CancellationToken ct = default); // Task 16
}
internal sealed class LiveRequoter { Task<Result<IReadOnlyList<OrderLineInput>>> ResolveAsync(...); } // see step 3
```

- [ ] **Step 1: Write failing tests.** Use NSubstitute for every port and the real engine over a
  10-candidate pool. Each test name is the rule it pins.
  - `Replay_With_Same_Fingerprint_Returns_Existing_Order_Without_Composing`: the key exists with
    the same fingerprint. Result `(OrderId, Replayed: true)`. The candidate pool, customer
    gateway and recorder receive no calls.
  - `Different_Fingerprint_Is_ORDER_IDEMPOTENCY_CONFLICT_With_No_Calls`.
  - `Amount_Below_MinUsd_Is_AMOUNT_OUT_OF_RANGE_Before_Customer_Lookup`: `ToUsd` gives 999 when
    `Min = 1000`. Also check `Max + 1`, and that exactly `Min` and exactly `Max` are accepted.
  - `New_Customer_Without_Phone_Is_CUSTOMER_CONTACT_REQUIRED_And_Nothing_Is_Composed_Or_Created`.
  - `New_Customer_Without_customers_write_Is_INSUFFICIENT_SCOPE`.
  - `Existing_Customer_Changed_Contact_Without_customers_write_Is_INSUFFICIENT_SCOPE`.
  - `Missing_Buyer_Fields_Fails_Before_Composition`: `RequiredBuyerFields = ["billingAddress"]`
    gives `INVOICE_BUYER_FIELDS_MISSING`. Nothing is composed or created.
  - `Failed_Composition_Creates_No_Customer_And_Records_A_Failed_Attempt`: use `ExpiredBudget`.
    The result is `COMPOSITION_TIMEOUT`. `CreatePartnerCustomerAsync` and `IUnitOfWork` are not
    called. The recorder is called once with `Succeeded == false` and
    `ErrorCode == "COMPOSITION_TIMEOUT"`.
  - `New_Customer_Is_Created_After_Composition_And_Before_Save`, checked with `Received.InOrder`.
  - `Concurrent_New_Customer_Requests_Share_One_Customer` (A7): two different external order ids for the same new external customer, run in parallel ⇒ both orders created, both with the same `CustomerId`.
  - `Duplicate_On_Insert_Takes_Replay_Path`: `TryInsertPartnerOrderAsync` returns false. The
    reload gives the same fingerprint, so the result is replayed; with a different fingerprint
    it is a 409.
  - `Contact_Sync_Runs_Only_After_Successful_New_Order_For_Existing_Customer`: called once, after
    the insert.
  - `Replay_And_Conflict_Never_Sync_Contacts`.
  - `Sync_Failure_Still_Returns_Created_With_CONTACT_SYNC_FAILED_Warning`: the sync returns a
    failure, or throws. The result is Success with
    `Warnings == ["CONTACT_SYNC_FAILED"]`, and `customer_contact_sync_failures` is incremented
    (check with a `MeterListener`).
  - `Invoice_Buyer_Uses_Supplied_Contact_Even_When_Stored_Differs`: the stored email is `old@x`
    and `new@x` is supplied, so the invoice buyer email is `new@x`. Omitted fields fall back to
    the stored values.
  - `Unsellable_Chosen_Variant_Is_Dropped_Pool_Invalidated_And_Recomposed`: the live
    `GetSellableAsync(ids)` omits the chosen variant. `Invalidate("USD")` is called, the reload
    excludes it, and the final lines do not contain it. The trace attempts contain
    `requote:dropped=1`.
  - `Requote_Band_Shift_Excluding_Chosen_Price_Recomposes`.
  - `Three_Failed_Requotes_Is_EXACT_COMPOSITION_UNAVAILABLE`.
  - `Line_Snapshot_Uses_Live_Quote_Not_Cached_Quote`: the cached `r` is 1000 and the live `r` is
    1010, with p inside both bands. The line has `ReferencePriceMinor == 1010`.

- [ ] **Step 2: Run the tests to see them fail.** Command:
  `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "EwaysVNext.UnitTests.Sales.Services.PartnerOrderServiceTests"`.
  Expected: FAIL.

- [ ] **Step 3: Implement `CreateAsync`.** Read `now` once. These steps map one-to-one to §10.3:
  1. **Idempotency.** `FindPartnerOrderKeyAsync`. A match returns a replay; a mismatch returns
     `OrderIdempotencyConflict`.
  2. **Fingerprint.** `RequestFingerprint.Compute(...)`. Load `limits` and `seller`.
  3. **Currency, then bounds.**
     - `ICandidatePoolGateway.GetAsync(currency, now)`. A failure is returned as is; this is the
       currency-enabled check.
     - `ToUsdAsync`, then `AmountOutOfRange` unless `Min ≤ usd ≤ Max`.
  4. **Customer pre-check, no writes.**
     - `FindPartnerCustomerAsync`.
     - New customer: needs `customers:write` (`InsufficientScope`), then email **and** phone
       (`CustomerContactRequired`).
     - Existing customer: `GetInvoicePartyAsync`. `syncNeeded` = any supplied non-null field
       differs from the stored value. If so, it needs `customers:write`.
     - `buyer` = the stored party overlaid with the supplied non-null values. A new customer
       uses the supplied values alone.
     - `BuyerFieldRequirements.Missing(...)` non-empty means `InvoiceBuyerFieldsMissing`.
  5. **Compose with live re-quote.** Budget is `budgetFactory.Start(limits.CompositionDeadlineMs)`
     and seed is `seedSource.NextSeed()`. Loop at most 3 times:
     - `CompositionEngine.Compose(T, pool, new(limits.CompositionMaxLines, usd), seed, budget)`.
       On failure, record the attempt and return.
     - `LiveRequoter`: run `catalog.GetSellableAsync(chosenIds)` and
       `pricing.QuoteAsync(chosenIds, currency, now)`.
     - Drop a line if its variant is missing, its quote is missing, or `p ∉ [lo', hi']`. If
       nothing was dropped, build `OrderLineInput`s from the live `VariantSnapshot` and live
       `PriceSnapshot` and stop.
     - Otherwise call `pool.Invalidate(currency)`, reload, remove the dropped ids, and retry
       with the same budget and `seed + attempt`.
     - Record the attempt with `Recompositions`.
  6. **Create the customer** if it is new (its own save inside Customer). A lost creation race
     already resolves to the existing customer inside phase 1's port (A7).
  7. **Build and save.**
     - Allocate `Order` and `Commercial` numbers.
     - `tax = taxPolicy.Assess(T, currency)`.
     - `Order.CreatePartnerOrder(...)`. Catch `DomainException`: `INVOICE_BUYER_FIELDS_MISSING`
       maps to that error, anything else to `SalesErrors.Internal`.
     - `TryInsertPartnerOrderAsync`. On `false`, take the step-1 replay path again.
  8. **Sync contacts** for an existing customer with `syncNeeded`:
     `SyncPartnerContactAsync(customerId, partnerId, supplied, correlationId)`.
     - On failure or exception: `LogWarning`, increment the counter, and add the warning.
     - Never undo the order.
  9. Return `PartnerOrderCreated(order.Id, false, warnings)`.

  Register `IPartnerOrderService`, `LiveRequoter`, `NonGstTaxPolicy`, the default budget factory
  and the default seed source in `AddSalesApplication`. Bind `SalesOptions` from
  `configuration.GetSection("Sales")`.

- [ ] **Step 4: Run the tests to see them pass.** Same command. Expected: PASS.

- [ ] **Step 5: Mutation-test.** Move the contact sync from step 8 to before step 7. Expected:
  `Contact_Sync_Runs_Only_After_Successful_New_Order_For_Existing_Customer` FAILS. Restore it.
  Then move the customer creation before composition. Expected:
  `Failed_Composition_Creates_No_Customer_And_Records_A_Failed_Attempt` FAILS. Restore it.

- [ ] **Step 6: Commit.**
```bash
git commit -am "feat(sales): partner order flow with idempotency, live re-quote, ordered customer writes, contact sync" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 13: Partner order queries (read side)

**Files:**
- Create: `QueryApplication/Partner/{IPartnerOrderQueries,PartnerOrderQueries,PartnerOrderDtos}.cs`, `QueryApplication/DependencyInjection.cs`
- Test: `tests/IntegrationTests/Sales/PartnerOrderQueriesTests.cs`

**Interfaces:**
- Produces:
```csharp
public sealed record MoneyMinor(long Minor, string Currency);
public sealed record OrderLineDto(int LineNo, long VariantId, long ProductId, string Sku, string ProductTitle, string BrandName,
    IReadOnlyList<string> CategoryPath, IReadOnlyDictionary<string, string> Attributes, int Quantity, long UnitPriceMinor,
    long LineTotalMinor, long ReferencePriceMinor, int DeviationBps, string PriceSource);
public sealed record DocumentDto(long Id, string Type, string Number, DateTimeOffset IssuedAt, long? RelatedDocumentId, JsonElement Content);
public sealed record OrderStatusDto(string Order, string Payment, string? PaymentSource, string Fulfillment, string? FulfillmentSource,
    string FulfillmentStatement, string Documents);
public sealed record PartnerOrderDto(long Id, string Number, string ExternalOrderId, long CustomerId, string ExternalCustomerId,
    string Currency, long TotalMinor, long SubtotalMinor, long TaxMinor, OrderStatusDto Status, IReadOnlyList<OrderLineDto> Lines,
    DocumentDto? Invoice, IReadOnlyList<DocumentDto> CreditNotes, DateTimeOffset CreatedAt, string CorrelationId);
public sealed record PartnerOrderSummaryDto(long Id, string Number, string ExternalOrderId, long CustomerId, string ExternalCustomerId,
    string Currency, long TotalMinor, OrderStatusDto Status, long? InvoiceId, string? InvoiceNumber, DateTimeOffset CreatedAt);
public sealed record PartnerOrderListQuery(string? ExternalOrderId, string? ExternalCustomerId, long? CustomerId, string? Currency,
    DateTimeOffset? From, DateTimeOffset? To, long? Cursor, int Limit = 50);                     // Limit 1..200
public sealed record CursorPage<T>(IReadOnlyList<T> Items, long? NextCursor);
public abstract record CustomerKey { public sealed record ById(long CustomerId) : CustomerKey; public sealed record ByExternalId(string ExternalCustomerId) : CustomerKey; }
public sealed record PartnerInvoiceDto(long Id, string Type, string Number, long OrderId, string ExternalOrderId, DateTimeOffset IssuedAt, JsonElement Content);
public sealed record InvoiceSearchItem(string ExternalOrderId, bool Found, IReadOnlyList<PartnerInvoiceDto> Invoices);
public interface IPartnerOrderQueries {
  Task<Result<PartnerOrderDto>> GetAsync(PartnerPrincipal principal, long orderId, CancellationToken ct = default);
  Task<Result<PartnerOrderDto>> GetByExternalIdAsync(PartnerPrincipal principal, string externalOrderId, CancellationToken ct = default);
  Task<IReadOnlyList<PartnerOrderDto>> GetManyAsync(PartnerPrincipal principal, IReadOnlyCollection<long> orderIds, CancellationToken ct = default);
  Task<Result<CursorPage<PartnerOrderSummaryDto>>> ListAsync(PartnerPrincipal principal, PartnerOrderListQuery query, CancellationToken ct = default);
  Task<Result<CursorPage<PartnerOrderSummaryDto>>> ListForCustomerAsync(PartnerPrincipal principal, CustomerKey customer, long? cursor, int limit, CancellationToken ct = default);
  Task<Result<PartnerInvoiceDto>> GetInvoiceAsync(PartnerPrincipal principal, long documentId, CancellationToken ct = default);
  Task<Result<IReadOnlyList<InvoiceSearchItem>>> SearchInvoicesAsync(PartnerPrincipal principal, IReadOnlyList<string> externalOrderIds, CancellationToken ct = default); // 1..100 distinct
}
```

- [ ] **Step 1: Write failing tests.** Seed through `SalesOrderMother` with partners 1 and 2,
  then check:
  - Partner 2 calling `GetAsync`, `GetByExternalIdAsync`, `GetInvoiceAsync` or
    `ListForCustomerAsync` for partner 1's records gets `NOT_FOUND`.
  - `ListAsync` pages by `id DESC` with `NextCursor`, and filters on every field.
  - A customer that has no orders for this partner gives `NOT_FOUND`.
  - Searching 101 ids gives `VALIDATION_FAILED`. Mixed found and not-found ids keep their input
    order.
  - `Status.FulfillmentStatement == DocumentWording.FulfillmentStatement`.
  - `Invoice.Content` deserializes to the stored `DocumentContent`.
  - Website orders never appear in any partner query.

- [ ] **Step 2: Run the tests to see them fail.** Command:
  `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "EwaysVNext.IntegrationTests.Sales.PartnerOrderQueriesTests"`.
  Expected: FAIL.

- [ ] **Step 3: Implement.**
  - Write single-statement `AsNoTracking` projections. Every one starts with
    `db.Orders.Where(o => o.Channel == OrderChannel.PartnerApi && o.PartnerId == principal.PartnerId)`.
  - Map `Content` to `JsonSerializer.SerializeToElement(content, DocumentContentJson.Options)`
    after materialization. That serializes a small record; it does not hydrate the aggregate.
  - The derived `Documents` status string is computed in the projection from document types.

- [ ] **Step 4: Run the tests to see them pass.** Same command. Expected: PASS.

- [ ] **Step 5: Commit.**
```bash
git commit -am "feat(sales): partner-scoped order and invoice queries (404 across partners)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 14: Partner REST — POST /orders and reads, OpenAPI examples

**Files:**
- Create:
  - `Adapter/Inbound/Rest/Common/{MoneyJson,CallerClaims}.cs`
  - `Partner/{PartnerOrderEndpoints,PartnerDtos,PartnerOpenApiExamples}.cs`
  - `DependencyInjection.cs` (`AddSalesRestAdapter`)
- Test:
  - `tests/IntegrationTests/Sales/{SalesApiFactory,SalesTestData,SalesHttpExtensions}.cs`
  - `PartnerOrderAcceptanceTests.cs`, `PartnerOrderFormatTests.cs`
  - `tests/ArchitectureTests/SalesArchitectureTests.cs` (add one test)

**Interfaces:**
- Produces:
  - Routes:
    - `POST /api/v1/orders` (policy `partner:orders:create`)
    - `GET /api/v1/orders` (`partner:orders:read`)
    - `GET /api/v1/orders/{id}` (`partner:orders:read`; `{id}` is a long or `ext:{externalOrderId}`)
    - `GET /api/v1/customers/{id}/orders` (`partner:customers:read`; long or `ext:`)
  - Request shapes:
```csharp
// Partner wire format = PRD §9, snake_case (JsonNamingPolicy.SnakeCaseLower), applied ONLY to the partner route
// group via a dedicated JsonSerializerOptions (PartnerJson.Options) used for both binding and responses
// (incl. bulk and error bodies). Shop/admin stay camelCase.
// { "external_order_id", "external_customer_id", "customer": { "email", "phone", "full_name" }, "amount", "currency" }
public sealed record CreatePartnerOrderRequest(string? ExternalOrderId, string? ExternalCustomerId, JsonElement Amount, string? Currency, PartnerCustomerRequest? Customer);
public sealed record PartnerCustomerRequest(string? Email, string? Phone, string? FullName);
// Test (this task): Partner_Api_Uses_Snake_Case — POST with the PRD §9 sample body verbatim ⇒ 201; response has
// "external_order_id", "payment_status":"PAID", "payment_source":"PARTNER_REPORTED", "fulfillment_status":"FULFILLED",
// "fulfillment_source":"SYSTEM_AUTOMATIC"; a camelCase body ("externalOrderId") ⇒ 400 VALIDATION_FAILED.
public sealed record MoneyDto(string Amount, string Currency);           // amount from AmountText.Format
public sealed record PartnerOrderResponse(/* PartnerOrderDto with MoneyDto money, [Snowflake] ids, invoice {id, number, json, pdfUrl}, correlationId, warnings */);
public static class CallerClaims {
  public static string CorrelationIdOf(HttpContext ctx);                 // ctx.TraceIdentifier (A3)
  public static int BulkMaxItemsOf(ClaimsPrincipal user, int fallback);  // "bulk_max_items" claim (A5)
}
```
  - Validation, all of which gives 400 `VALIDATION_FAILED`:
    - `Amount.ValueKind == String` and `AmountText.TryParseMinor`
    - `Currency` matches `^[A-Z]{3}$`
    - `ExternalOrderId` and `Customer.ExternalId` match `^[A-Za-z0-9._:\-]{1,100}$`
    - `Customer` is not null
  - Responses: create is 201 with `Location: /api/v1/orders/{id}`. A replay is 200 with
    `Idempotent-Replay: true`. Both bodies are reloaded through
    `IPartnerOrderQueries.GetAsync`, so create, replay and get return the same shape (guardrail 3).
  - `pdfUrl` is `/api/v1/invoices/{invoiceId}/pdf`.

- [ ] **Step 1: Write the test infrastructure and failing tests.**
  - `SalesApiFactory`:
    - Wraps `PostgresFixture.CreateFactory().WithWebHostBuilder(...)`.
    - Replaces `IApiKeyAuthenticator` with `StubApiKeyAuthenticator`:
      - `pk_partnerA_…` gives partner 9001 with all scopes and `BulkMaxItems = 100`.
      - `pk_partnerB_…` gives partner 9002 with all scopes.
      - `pk_nocustw_…` gives partner 9003 without `customers:write`.
    - Adds `TestIdentityHandler` (header `X-Test-Identity`) for Shop and Backoffice principals
      (phase 5).
    - Replaces `IDocumentStore` with `InMemoryDocumentStore`.
    - `SalesTestData.EnsureSeededAsync` runs once (A8).
  - In `PartnerOrderFormatTests`:
```csharp
[Theory]
[InlineData("\"500\""), InlineData("\"500.1\""), InlineData("\"500.123\""), InlineData("\"1e3\""),
 InlineData("\"-5.00\""), InlineData("\" 500.00\""), InlineData("500.00"), InlineData("500")]
public async Task Amount_Must_Be_A_Two_Decimal_String(string amountJson)
{
    var before = await _db.CountOrdersAsync(PartnerA);
    var body = $$"""{"externalOrderId":"fmt-{{Guid.NewGuid():N}}","amount":{{amountJson}},"currency":"USD","customer":{"externalId":"c-fmt","email":"fmt@example.test","phone":"+6591234567"}}""";
    var response = await _partnerA.PostAsync("/api/v1/orders", new StringContent(body, Encoding.UTF8, "application/json"));
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await response.ReadErrorAsync()).Code.Should().Be("VALIDATION_FAILED");
    (await _db.CountOrdersAsync(PartnerA)).Should().Be(before);
}
```
  - In `PartnerOrderAcceptanceTests`, with `[Collection(PostgresSuite.Name)]`:
```csharp
[Fact] public async Task Acceptance01_New_Customer_500_Usd_Order_Is_Exact_Paid_Fulfilled_And_Invoiced()
{
    var sw = Stopwatch.StartNew();
    var res = await _partnerA.PostOrderAsync(Ext(), "500.00", "USD", NewCustomer("c-a1", "a1@example.test", "+6591230001"));
    sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    res.StatusCode.Should().Be(HttpStatusCode.Created);
    var o = await res.ReadDataAsync<PartnerOrderResponse>();
    o.Total.Should().Be(new MoneyDto("500.00", "USD"));
    o.Lines.Sum(l => AmountText.ParseMinor(l.LineTotal.Amount)).Should().Be(50_000);
    o.Lines.Should().OnlyContain(l => l.Quantity >= 1 && 100 * Minor(l.UnitPrice) >= 95 * Minor(l.ReferencePrice) && 100 * Minor(l.UnitPrice) <= 105 * Minor(l.ReferencePrice));
    o.Status.Payment.Should().Be("Paid"); o.Status.PaymentSource.Should().Be("PartnerReported");
    o.Status.Fulfillment.Should().Be("Fulfilled"); o.Status.FulfillmentSource.Should().Be("SystemAutomatic");
    o.Status.FulfillmentStatement.Should().Be("Fulfillment is an operational assertion, not verified proof of delivery");
    o.Invoice!.Number.Should().StartWith("INV-"); o.Invoice.PdfUrl.Should().Be($"/api/v1/invoices/{o.Invoice.Id}/pdf");
    o.Invoice.Json.GetProperty("title").GetString().Should().Be("Invoice");
    o.Invoice.Json.GetProperty("taxStatement").GetString().Should().Be("Not GST registered");
    res.Headers.GetValues("X-Correlation-Id").Single().Should().Be(o.CorrelationId);
}
[Fact] public async Task Acceptance02_Resubmitted_Order_Returns_Identical_Order_And_Invoice()
{ /* same body twice ⇒ 201 then 200 + Idempotent-Replay: true; Data JSON equal except correlationId and warnings; DB has 1 order, 1 payment, 1 Commercial document */ }
[Fact] public async Task Acceptance03_Same_External_Order_Different_Amount_Returns_409()
{ /* 500.00 then 501.00 ⇒ 409 ORDER_IDEMPOTENCY_CONFLICT; GET ext:{id} still 500.00 with same invoice number */ }
[Fact] public async Task Acceptance04_Partners_Are_Isolated_And_Cross_Partner_Invoice_Is_404()
{ /* A and B post identical externalOrderId + customer externalId ⇒ both 201, different order ids and customer ids;
     B: GET /orders/{A.id}, /orders/ext:{ext} (returns B's own), /invoices/{A.invoiceId}, /invoices/{A.invoiceId}/pdf,
     /customers/{A.customerId}/orders ⇒ 404 NOT_FOUND (except ext: which returns B's own order) */ }
[Fact] public async Task Get_By_Ext_Id_And_List_Filters_Work() { /* GET /orders?externalCustomerId=…&currency=USD&limit=1 ⇒ nextCursor; ext: form equals by-id body */ }
[Fact] public async Task Missing_Scope_Is_403_INSUFFICIENT_SCOPE() { /* pk_nocustw new customer ⇒ 403 INSUFFICIENT_SCOPE, no customer row */ }
```
  - In `SalesArchitectureTests`, add `Partner_Request_Dtos_Never_Accept_A_PartnerId`. Every
    type in namespace `EwaysVNext.Modules.Sales.Adapter.Inbound.Rest.Partner` whose name ends
    with `Request` has no property whose name contains `PartnerId`, case-insensitive.

- [ ] **Step 2: Run the tests to see them fail.** Commands:
  - `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "EwaysVNext.IntegrationTests.Sales.PartnerOrderFormatTests"`
  - `... --filter-class "EwaysVNext.IntegrationTests.Sales.PartnerOrderAcceptanceTests"`

  Expected: FAIL (404 route).

- [ ] **Step 3: Implement.**
  - Endpoint handlers:
    - Derive `PartnerPrincipal.TryFrom(ctx.User, out var p)`. Failing that, return
      `EndpointResult.Unauthorized()`.
    - Validate, then call the service, then reload through the query.
    - Failures use `EndpointResult.Fail(error)`. The replay header is set on `ctx.Response`
      before the result is returned.
    - Log the `Idempotency-Key` header and do not use it otherwise (spec §5.6).
  - `GET {id}`: an `ext:` prefix calls `GetByExternalIdAsync`. `long.TryParse` calls `GetAsync`.
    Anything else is `NOT_FOUND`.
  - `PartnerOpenApiExamples` is an `IOpenApiOperationTransformer`. It adds request and response
    examples (201, 200 replay, 400, 409, 422, 503) to the operations tagged `Partner Orders`.
    Register it in `AddSalesRestAdapter`, using the phase 0 OpenAPI configuration hook.
  - Add the test `OpenApi_Has_Partner_Order_Example`. `GET /openapi/v1.json` must have
    `paths./api/v1/orders.post.requestBody.content.application/json.example.amount == "500.00"`.

- [ ] **Step 4: Run the tests to see them pass.** Same commands, plus the architecture tests.
  Expected: PASS.

- [ ] **Step 5: Commit.**
```bash
git commit -am "feat(sales): partner order REST (create/replay/get/ext:/list/customer orders) with OpenAPI examples" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 15: Partner flow integration — acceptance 5–8, concurrency, stale candidate

**Files:**
- Test: `tests/IntegrationTests/Sales/PartnerOrderFlowTests.cs`
- Test doubles in `SalesApiFactory`:
  - `StaleOnceCandidatePoolGateway`: a decorator whose first call returns a captured pool and
    later calls delegate to the real gateway.
  - `PoolFilter`: restricts the pool to the given variant ids.
  - `ExpiredBudgetFactory`.

**Interfaces:** Consumes Tasks 12–14. Produces no new production code; any fix it forces goes in
the owning task's files.

- [ ] **Step 1: Write failing tests.**
```csharp
[Fact] public async Task Acceptance05_Existing_Customer_Contact_Change_Is_Audited_And_Omission_Keeps_Values()
{ /* order 1 new customer (e1@, +6591110001, "Ann"); order 2 same customer with email e2@ only ⇒ 201, no warnings;
     db: customer email e2@, phone +6591110001, name "Ann"; ContactChanges = [Email e1@→e2@] with SourcePartnerId = A;
     invoice 2 buyer email e2@, phone +6591110001; replaying order 2 adds no ContactChange */ }
[Fact] public async Task Acceptance06_New_Customer_Without_Phone_Is_Rejected_With_No_Writes()
{ /* 422 CUSTOMER_CONTACT_REQUIRED; no order, document, payment, customer for that external id; no composition_attempts row */ }
[Fact] public async Task Acceptance07_Composition_Failure_Leaves_No_Partial_Records_And_No_Line_Breaks_Band()
{ /* (a) ExpiredBudgetFactory ⇒ 503 COMPOSITION_TIMEOUT retryable:true;
     (b) PoolFilter to one variant v with r and T = 3·hi(v) + 1 ⇒ 422 EXACT_COMPOSITION_UNAVAILABLE;
     both: no customer created, counts of orders/order_lines/order_payments/order_documents unchanged;
     composition_attempts has a failed row per case;
     finally SELECT count(*) FROM sales.order_lines WHERE band_enforced AND (unit_price_minor*100 < reference_price_minor*95 OR unit_price_minor*100 > reference_price_minor*105) == 0 */ }
[Fact] public async Task Acceptance08_Multi_Currency_Totals_Are_Exact_And_Snapshot_Survives_Rate_Change()
{ /* for EUR, SGD, AED, GBP: post "12845.99" (AED: "50000.00") ⇒ Σ lines == amount, every line PriceSource Fx or Base;
     override path: set CurrencyOverride(GBP) on variant v (A9), PoolFilter {v}, amount = override ⇒ line PriceSource "Override", unit == override;
     snapshot: GET EUR order JSON; set FX override EUR+10% (A9); GET again ⇒ byte-identical Data (lines, fx rate, fxRateId, totals, invoice json) */ }
[Fact] public async Task Concurrent_Same_External_Id_Creates_Exactly_One_Order()
{
    var ext = Ext();
    var same = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
        _partnerA.PostOrderAsync(ext, "500.00", "USD", NewCustomer($"c-{ext}", "conc@example.test", "+6591239999"))));
    same.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
    same.Where(r => r.StatusCode == HttpStatusCode.OK)
        .Should().HaveCount(9).And.OnlyContain(r => r.Headers.GetValues("Idempotent-Replay").Single() == "true");
    (await Task.WhenAll(same.Select(r => r.ReadDataAsync<PartnerOrderResponse>()))).Select(o => o.Id).Distinct().Should().ContainSingle();
    (await _db.CountOrdersAsync(PartnerA, ext)).Should().Be(1);

    var ext2 = Ext();
    var diff = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
        _partnerA.PostOrderAsync(ext2, $"{500 + i}.00", "USD", NewCustomer($"c-{ext2}", "conc2@example.test", "+6591238888"))));
    diff.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
    diff.Where(r => r.StatusCode == HttpStatusCode.Conflict).Should().HaveCount(9);
    (await Task.WhenAll(diff.Where(r => r.StatusCode == HttpStatusCode.Conflict).Select(r => r.ReadErrorAsync())))
        .Should().OnlyContain(e => e.Code == "ORDER_IDEMPOTENCY_CONFLICT");
    (await _db.CountOrdersAsync(PartnerA, ext2)).Should().Be(1);
    (await _db.CountPartnerCustomersAsync(PartnerA, $"c-{ext2}")).Should().Be(1);
}
[Fact] public async Task Deactivated_Variant_After_Cache_Is_Never_Ordered()
{ /* pick seed variant v (USD); capture pool {v} via StaleOnceCandidatePoolGateway; deactivate v (A9);
     post amount = r(v) ⇒ 201; lines do not contain v; order's composition trace attempts contain "requote:dropped=1";
     then StaleOnce with pool {v} only and the real reload filtered to {v} ⇒ 422 EXACT_COMPOSITION_UNAVAILABLE;
     SELECT count(*) FROM sales.order_lines WHERE variant_id = v AND order created after deactivation == 0 */ }
[Fact] public async Task Contact_Sync_Failure_Returns_201_With_Warning() { /* decorator: SyncPartnerContactAsync fails ⇒ 201, warnings ["CONTACT_SYNC_FAILED"], order persisted, invoice buyer = supplied */ }
```

- [ ] **Step 2: Run the tests.** Command:
  `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "EwaysVNext.IntegrationTests.Sales.PartnerOrderFlowTests"`.
  Expected: FAIL first, because the test doubles are not yet in `SalesApiFactory`.

- [ ] **Step 3: Implement the test doubles** in `SalesApiFactory` and fix any defect they expose
  in the owning Task 9–14 code.

- [ ] **Step 4: Run the tests to see them pass.** Same command, run 3 times in a row for the
  concurrency test. Expected: PASS every time.

- [ ] **Step 5: Commit.**
```bash
git commit -am "test(sales): PRD acceptance 5-8, concurrent idempotency, stale-candidate re-quote" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 16: Bulk orders (spec §10.4)

**Files:**
- Modify: `Services/PartnerOrderService.cs` (`CreateBulkAsync`), `Partner/PartnerOrderEndpoints.cs`, `PartnerDtos.cs`
- Test:
  - `tests/UnitTests/Sales/Services/PartnerBulkOrderTests.cs`
  - `tests/IntegrationTests/Sales/PartnerBulkAcceptanceTests.cs`

**Interfaces:**
- Produces:
  - Route `POST /api/v1/orders/bulk` with policy `partner:orders:create`.
  - Body `{ "items": [CreatePartnerOrderRequest, …] }`.
  - The response is 200: `{ items: [{ index, externalOrderId, status: "created"|"replayed"|"failed", order?, error?, warnings }] }`.
  - Over `BulkMaxItems` returns 400 `VALIDATION_FAILED` for the whole batch.
  - Malformed items fail on their own with `VALIDATION_FAILED`.

- [ ] **Step 1: Write failing tests.**
  - Unit tests:
    - Each item runs in its own `IServiceScope`. Use a substitute `IServiceScopeFactory` and count
      the `CreateScope` calls.
    - At most `Sales:Bulk:Parallelism` (4) run at once, measured with a gate in the stub service.
    - Items not started before the deadline report `BATCH_DEADLINE_EXCEEDED` with
      `retryable: true`. Use a 0-second deadline plus a gate.
    - One item's exception becomes that item's `Failed` with `INTERNAL_SALES` and does not touch
      the others.
    - The output order equals the input `Index`.
  - Integration:
```csharp
[Fact] public async Task Acceptance14_Bulk_100_Items_Commits_Successes_And_Isolates_Failures()
{ /* 100 items: 94 valid distinct; #10 amount "5.00" (AMOUNT_OUT_OF_RANGE), #20 new customer without phone (CUSTOMER_CONTACT_REQUIRED),
     #30 amount "500.1" (VALIDATION_FAILED), #40 currency "XXX" (CURRENCY_NOT_ENABLED), #50 duplicate of #49 same body (replayed),
     #60 duplicate of #59 different amount (failed ORDER_IDEMPOTENCY_CONFLICT or created-then-#59-conflict: assert exactly one of the pair created);
     ⇒ 200; 100 outcomes in index order; failures carry exact codes; DB orders for the batch == created count; every created order has its invoice;
     101 items ⇒ 400 VALIDATION_FAILED and zero orders */ }
```

- [ ] **Step 2: Run the tests to see them fail.** Commands:
  - `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "EwaysVNext.UnitTests.Sales.Services.PartnerBulkOrderTests"`
  - `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "EwaysVNext.IntegrationTests.Sales.PartnerBulkAcceptanceTests"`

  Expected: FAIL.

- [ ] **Step 3: Implement.**
  - `CreateBulkAsync` creates `using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(options.Bulk.DeadlineSeconds))`.
  - It runs `Parallel.ForEachAsync(items, new ParallelOptions { MaxDegreeOfParallelism = options.Bulk.Parallelism, CancellationToken = ct }, ...)`.
  - Per item:
    - If `deadline.IsCancellationRequested`, the item gets `BatchDeadlineExceeded`.
    - Otherwise `await using var scope = scopeFactory.CreateAsyncScope()`, resolve
      `IPartnerOrderService`, and call `CreateAsync`.
    - Started items run to completion under `ct`.
  - The endpoint:
    - Reads `BulkMaxItemsOf(user, limits.BulkDefaultMax)`.
    - Validates each item into a `BulkItem` or a `Failed` outcome.
    - Calls the service and loads views with one `GetManyAsync(principal, createdIds)`.

- [ ] **Step 4: Run the tests to see them pass.** Same commands. Expected: PASS.

- [ ] **Step 5: Commit.**
```bash
git commit -am "feat(sales): bounded-parallel bulk partner orders with per-item outcomes and deadline" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 17: Invoice PDF and partner invoice endpoints

**Files:**
- Create:
  - `Ports/Outbound/{IDocumentPdfRenderer,IDocumentStore}.cs`
  - `Ports/Inbound/IInvoiceService.cs`, `Services/InvoiceService.cs`
  - `Adapter/Outbound/Documents/{QuestPdfDocumentRenderer,ObjectStorageDocumentStore}.cs`
  - `Partner/PartnerInvoiceEndpoints.cs`
- Modify: `Directory.Packages.props` (`QuestPDF` 2025.x; `PdfPig` for tests)
- Test:
  - `tests/UnitTests/Sales/Documents/QuestPdfRendererTests.cs`
  - `tests/UnitTests/Sales/Services/InvoiceServiceTests.cs`
  - `tests/IntegrationTests/Sales/PartnerInvoiceTests.cs`

**Interfaces:**
- Produces:
```csharp
public interface IDocumentPdfRenderer { byte[] Render(DocumentContent content); }        // pure, deterministic
public interface IDocumentStore {                                                        // bucket "documents"
  Task<bool> ExistsAsync(string key, CancellationToken ct = default);
  Task PutPdfAsync(string key, byte[] pdf, CancellationToken ct = default);
  Task<Uri> GetDownloadUrlAsync(string key, CancellationToken ct = default);             // presigned, 5 minutes
}
public interface IInvoiceService { Task<Result<Uri>> GetPdfUrlAsync(DocumentAccess access, long documentId, CancellationToken ct = default); }
```
- Routes, all with `partner:invoices:read`:
  - `GET /api/v1/invoices/{id}` returns `PartnerInvoiceDto` with a `pdfUrl`.
  - `GET /api/v1/invoices/{id}/pdf` returns **302** `Location: <presigned>`.
  - `POST /api/v1/invoices/search` takes `{ externalOrderIds: [...] }` (1..100) and returns 200
    `{ items: [{ externalOrderId, status: "found"|"not_found", invoices: [...] }] }`.

- [ ] **Step 1: Write failing tests.**
  - Renderer:
    - `Render(c)` twice is byte-equal.
    - The PdfPig text contains `"Invoice"`, the seller legal name and UEN, `"Not GST registered"`,
      `DocumentWording.FulfillmentStatement`, every SKU, and `"500.00"`.
    - The text never matches `/tax invoice/i`.
    - A ProForma contains its payment instruction text.
    - A CreditNote contains `RelatedDocumentNumber`.
  - Service:
    - `Missing → NOT_FOUND`, without rendering.
    - First call: `ExistsAsync` is false, so it renders once, then `PutPdfAsync("{orderId}/{number}.pdf", bytes)`.
    - Second call: it exists, so there is no render.
    - It returns the presigned URL.
  - Integration:
    - `GET /invoices/{id}/pdf` returns 302 with `Location` from `InMemoryDocumentStore`, and the
      store holds `{orderId}/INV-….pdf`.
    - Partner B gets 404 on A's invoice.
    - Search with 101 ids returns 400.
    - Search for `[A1, unknown]` returns `found` and `not_found` in that order.
    - **A broken `IDocumentStore`, whose every call throws, still lets `POST /orders` return
      201.** MinIO is off the order-write path.

- [ ] **Step 2: Run the tests to see them fail.** Commands:
  - `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-namespace "EwaysVNext.UnitTests.Sales.Documents"`
  - `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "EwaysVNext.IntegrationTests.Sales.PartnerInvoiceTests"`

  Expected: FAIL.

- [ ] **Step 3: Implement.**
  - `QuestPdfDocumentRenderer`:
    - `QuestPDF.Settings.License = LicenseType.Community` is set once in the static ctor.
    - Metadata `CreationDate = ModifiedDate = content.IssuedAt`, and `Producer = Creator = "EwaysVNext"`.
    - Layout:
      - the seller block
      - title from `content.Title`
      - number and date
      - buyer
      - the lines table
      - totals with the tax statement
      - the fulfillment statement
      - the payment instruction or related document when present
      - the footer
    - No other text comes from code. Every string comes from `DocumentContent` or `DocumentWording`.
  - `ObjectStorageDocumentStore` wraps `IObjectStorage` with `StorageBuckets.Documents` and a TTL
    of 5 minutes.

- [ ] **Step 4: Run the tests to see them pass.** Same commands. Expected: PASS.

- [ ] **Step 5: Commit.**
```bash
git commit -am "feat(sales): deterministic QuestPDF invoices rendered lazily to MinIO; partner invoice endpoints" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 18: Storefront popularity from sales lines

**Files:**
- Create: migration `StorefrontPopularityFromSales`
- Test: `tests/IntegrationTests/Sales/StorefrontPopularityTests.cs`

**Interfaces:** Consumes the phase 2–3 MV definitions of `catalog.mv_storefront_product` and
`catalog.mv_home`, and the phase 3 refresh entry point. Produces the same view columns, with
`popularity` now real.

- [ ] **Step 1: Write a failing test.**
  `Best_Sellers_Rank_By_Units_Sold_In_Last_30_Days`:
  - Create partner orders whose lines cover products P1 and P2. Then sum per product
    `SELECT product_id, sum(quantity) FROM sales.order_lines …` for the expectation.
  - Run the phase 3 refresh (the same call the `RefreshStorefrontViews` job handler makes).
  - `mv_storefront_product.popularity` for P1 and P2 equals those sums, in USD rows.
  - The `mv_home` best-sellers section orders by popularity DESC.
  - A cancelled order's lines are excluded: cancel through SQL `UPDATE sales.orders SET order_status='Cancelled'`.
  - A 31-day-old order is excluded: `UPDATE … created_at = now() - interval '31 days'`.

- [ ] **Step 2: Run the test to see it fail.** Command:
  `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "EwaysVNext.IntegrationTests.Sales.StorefrontPopularityTests"`.
  Expected: FAIL, because popularity is still the placeholder.

- [ ] **Step 3: Implement.**
  - Create the migration with
    `dotnet ef migrations add StorefrontPopularityFromSales -p src/Database -s src/Host/EwaysVNext.Host`.
  - **Up:**
    1. `DROP MATERIALIZED VIEW catalog.mv_home;` then `DROP MATERIALIZED VIEW catalog.mv_storefront_product;`.
       Use the reverse dependency order from phase 2–3, and drop `mv_category_facets` too if it
       reads `mv_storefront_product`.
    2. Recreate each one with its phase 2–3 SQL, copied verbatim. In `mv_storefront_product`,
       replace only the placeholder popularity expression with `COALESCE(pop.units_30d, 0) AS popularity`
       and add:
```sql
LEFT JOIN (
  SELECT ol.product_id, SUM(ol.quantity)::bigint AS units_30d
  FROM sales.order_lines ol
  JOIN sales.orders o ON o.id = ol.order_id
  WHERE o.order_status <> 'Cancelled' AND o.created_at >= now() - interval '30 days'
  GROUP BY ol.product_id) pop ON pop.product_id = p.id
```
    3. In `mv_home`, the best-sellers section ranks by `popularity DESC, product_id`.
    4. Recreate every unique, GIN, trigram and b-tree index phase 2–3 declared on the dropped
       views, by name.
  - **Down:** restore the phase 2–3 definitions.

- [ ] **Step 4: Run the tests to see them pass.** Same command, plus phase 3's
  "every view refreshes" test. Expected: PASS.

- [ ] **Step 5: Commit.**
```bash
git commit -am "feat(sales): storefront popularity and best sellers from 30-day sales lines" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 19: Composition coverage simulator and report

**Files:**
- Create: `tools/CompositionSimulator/{CompositionSimulator.csproj,Program.cs,AmountDistribution.cs,CatalogSources.cs,CoverageReport.cs}`
- Modify: `EwaysVNext.slnx` (add a `/tools/` folder)
- Test: `tests/UnitTests/Sales/Composition/SimulatorDistributionTests.cs` (links
  `AmountDistribution.cs` the same way as `SeedLikeCatalog.cs`)

**Interfaces:**
- Produces:
  - CLI: `CompositionSimulator --source synthetic|db|file [--connection <pg>] [--catalog <json>] [--samples 2000] [--seed 20261028] [--deadline-ms 8000] [--out docs/reports/composition-coverage]`.
  - `synthetic` uses `SeedLikeCatalog`.
  - `db` reads `catalog.mv_storefront_variant`: `variant_id, primary_category_id, currency, price_minor`.
    Bands are computed with `PriceBand.For`, and FX to USD comes from the latest `pricing` rows.
  - `file` reads `{ "USD": [{variantId, categoryId, r, lo, hi}], ... }`.
```csharp
public static class AmountDistribution {
  public static readonly long[] MandatedFaceValues = [1_000, 1_743, 50_000, 1_284_599, 100_000_000];
  // Per currency: `samples` USD amounts drawn log-uniform over [$10, $1,000,000] (integer log grid, SplitMix64),
  // converted to the currency, cents replaced by a uniform 00–99; plus the 5 mandated face values.
  public static IReadOnlyList<SimAmount> For(string currency, int samples, long seed, Func<long, string, long> toUsdMinor);
}
public sealed record SimAmount(long TargetMinor, long UsdMinor, bool Mandated, bool WithinBounds);   // bounds $10–$1M
```

- [ ] **Step 1: Write failing tests.**
  - `For("USD", 1000, 1, …)` gives 1,005 amounts.
  - All non-mandated amounts are within bounds.
  - Each decade from $10 to $1M holds 20% ± 4% of the samples.
  - The same seed gives the same list.
  - The mandated values that are out of bounds for a currency are flagged
    `WithinBounds == false`. Example: SGD 10.00 is about $7.40.

- [ ] **Step 2: Run the tests to see them fail.** Command:
  `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "EwaysVNext.UnitTests.Sales.Composition.SimulatorDistributionTests"`.
  Expected: FAIL.

- [ ] **Step 3: Implement.**
  - `Program` runs every enabled currency through
    `CompositionEngine.Compose(…, new StopwatchBudget(deadline))` and verifies each success with
    `CompositionVerifier`.
  - `CoverageReport` writes `<out>.md` and `<out>.json` with:
    - **the distribution definition**, worded as in the `AmountDistribution` doc comment above
      (this text is the PRD's "representative distribution")
    - per currency and overall: success rate, where the denominator is in-bounds amounts only;
      out-of-bounds mandated values are listed separately
    - failure codes with counts
    - p50/p95/p99 duration in ms
    - mean line count
    - mean |deviation| in bps
    - **uncovered ranges**: every log-decade bucket whose success rate is below 99%
    - the engine strategy version, seed and machine info

- [ ] **Step 4: Run.**
  - Test command: as in Step 2. Expected: PASS.
  - Then run
    `dotnet run --project tools/CompositionSimulator -c Release -- --source synthetic --samples 2000 --seed 20261028 --out docs/reports/composition-coverage`.
    Expected: it writes both files. The overall in-bounds success rate should be ≥ 99%. If it is
    lower, the report states the gap and the uncovered ranges (spec §15), and the gap is carried
    to `phase-4.md`.

- [ ] **Step 5: Commit.**
```bash
git add tools/CompositionSimulator tests/UnitTests EwaysVNext.slnx docs/reports/composition-coverage.*
git commit -m "feat(tools): composition coverage simulator defining the representative distribution" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 20: k6 partner-order load script

**Files:**
- Create: `tests/load/partner-orders.js`, `tests/load/README.md` (how to run against compose;
  results go to `docs/reports/`)

**Interfaces:**
- Env vars:
  - `BASE_URL` (default `http://localhost:8080`)
  - `API_KEY` (required)
  - `VUS` (10)
  - `DURATION` (`5m`)
  - `CURRENCIES` (`USD,EUR,SGD,AED,GBP`)
- Thresholds: `http_req_duration{name:create_order}: ['p(95)<10000']` and `checks: ['rate>0.99']`.

- [ ] **Step 1: Write the script.**
  - Each iteration POSTs `/api/v1/orders`:
    - `externalOrderId` is `` `k6-${__VU}-${__ITER}-${Date.now()}` ``.
    - `externalId` is `` `k6c-${__VU}` ``, with email and phone.
    - The amount is log-uniform between $10 and $1M, formatted with `toFixed(2)`, in a random
      currency from `CURRENCIES`.
  - Tag the request `{ name: 'create_order' }`.
  - Check `status === 201` and that `data.lines` sum equals the amount. Use integer cents
    arithmetic in JS: `Math.round(parseFloat(x)*100)`, applied to fixed 2dp strings.
  - `handleSummary` writes `docs/reports/k6-partner-orders.json` and stdout.

- [ ] **Step 2: Validate the script.** Command:
  `docker run --rm -i grafana/k6 inspect - < tests/load/partner-orders.js`.
  Expected: exit 0, printing `vus: 10` and `duration: 5m`. The actual load run against compose is
  phase 8's task; this phase does not claim any p95.

- [ ] **Step 3: Commit.**
```bash
git add tests/load
git commit -m "test(load): k6 partner-order script (10 VUs, 5 min, p95<10s threshold)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 21: Phase 4 gate

**Files:**
- Create: `docs/reports/phase-4.md`
- Modify:
  - `AGENTS.md` API table: the partner rows
  - `docs/TESTING-GUARANTEES.md`: the Sales rows, including what InMemory cannot see — the band
    CHECK and the 23505 path

- [ ] **Step 1: Build.** `dotnet build EwaysVNext.slnx -c Release`. Expected: 0 errors, 0
  warnings-as-errors.
- [ ] **Step 2: Test.** `dotnet test EwaysVNext.slnx`, with Docker running. Expected: all PASS.
  Report counts per suite.
- [ ] **Step 3: Simulator.** Rerun the Task 19 command. Expected: report regenerated, with the
  success rate copied into the phase report.
- [ ] **Step 4: Write `docs/reports/phase-4.md`.** It has four sections:
  - **What works:** the endpoints, invariants and tests by name.
  - **What is incomplete:** the k6 run is deferred to phase 8; the website channel is phase 5.
  - **Risks:**
    - simulator gaps, if any
    - QuestPDF Community licence revenue cap
    - the in-process candidate cache and rate limiter under replicas > 1 (ADR-0010)
    - a new customer persists if the order save fails (spec §10.3)
    - assumptions A1–A9 and whether each held
  - **Reproduction commands:** every command in this phase.
- [ ] **Step 5: Commit.**
```bash
git add docs/reports/phase-4.md docs/reports/composition-coverage.* AGENTS.md docs/TESTING-GUARANTEES.md
git commit -m "docs(reports): phase 4 gate (partner sales)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Phase 5 — Sales: website

### Task 22: Cart aggregate

**Files:**
- Create: `Domain/Carts/{Cart,CartLine}.cs`
- Test: `tests/UnitTests/Sales/Carts/CartTests.cs`

**Interfaces:**
- Produces:
```csharp
public sealed class Cart : AggregateRoot<long> {
  public long CustomerId; public string Currency; public DateTimeOffset UpdatedAt; public IReadOnlyList<CartLine> Lines;
  public const int MinQuantity = 1, MaxQuantity = 999, MaxLines = 100;
  public static Cart Create(long id, long customerId, string currency, DateTimeOffset now);
  public void AddOrIncrement(long lineId, long variantId, int quantity, long displayedUnitPriceMinor, DateTimeOffset now);
  public void SetQuantity(long variantId, int quantity, long displayedUnitPriceMinor, DateTimeOffset now);
  public void Remove(long variantId, DateTimeOffset now);
  public void Reprice(string currency, IReadOnlyDictionary<long, long> livePrices, DateTimeOffset now); // drops lines absent from livePrices
  public void Clear(DateTimeOffset now);
  public long TotalMinor { get; }                       // Σ quantity × displayed
}
public sealed class CartLine : Entity<long> { long VariantId; int Quantity; long DisplayedUnitPriceMinor; DateTimeOffset PriceQuotedAt; }
```

- [ ] **Step 1: Write failing tests.**
  - Each of these throws `DomainException("VALIDATION_FAILED")`:
    - quantity 0
    - quantity 1000
    - an increment past 999 (998 + 2)
    - a 101st line
  - Quantities 1 and 999 are accepted.
  - `AddOrIncrement` on an existing variant adds to the quantity and refreshes the displayed
    price and `PriceQuotedAt`.
  - `Reprice` sets the currency and prices and drops absent variants.
  - `Clear` empties the cart.
  - `TotalMinor` is correct.

- [ ] **Step 2: Run the tests to see them fail.** Command:
  `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "EwaysVNext.UnitTests.Sales.Carts.CartTests"`.
  Expected: FAIL.

- [ ] **Step 3: Implement** the aggregate as specified.

- [ ] **Step 4: Run the tests to see them pass.** Same command. Expected: PASS.

- [ ] **Step 5: Commit.**
```bash
git commit -am "feat(sales): Cart aggregate (qty 1-999, displayed unit prices)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 23: Cart persistence and ICartService

**Files:**
- Create:
  - `src/Database/Sales/Configurations/CartConfiguration.cs`
  - migration `AddSalesCarts`
  - `Ports/Outbound/ICartRepository.cs`, `Adapter/Outbound/Persistence/CartRepository.cs`
  - `Ports/Inbound/ICartService.cs`, `Services/CartService.cs`
- Test:
  - `tests/UnitTests/Sales/Services/CartServiceTests.cs`
  - `tests/IntegrationTests/Sales/CartSchemaTests.cs`

**Interfaces:**
- Produces:
```csharp
public sealed record ShopCaller(Guid IdentityUserId, string Email, bool EmailVerified);
public sealed record CartLineView(long VariantId, string Sku, string ProductTitle, int Quantity, long UnitPriceMinor, long LineTotalMinor);
public sealed record CartChange(long VariantId, string Kind, long? OldUnitPriceMinor, long? NewUnitPriceMinor); // Kind: "repriced" | "removed"
public sealed record CartView(string Currency, IReadOnlyList<CartLineView> Lines, long TotalMinor, IReadOnlyList<CartChange> Changes);
public interface ICartService {
  Task<Result<CartView>> GetAsync(ShopCaller caller, CancellationToken ct = default);               // re-quotes live; persists refreshed displayed prices
  Task<Result<CartView>> AddAsync(ShopCaller caller, long variantId, int quantity, CancellationToken ct = default);
  Task<Result<CartView>> UpdateLineAsync(ShopCaller caller, long variantId, int quantity, CancellationToken ct = default);
  Task<Result<CartView>> RemoveLineAsync(ShopCaller caller, long variantId, CancellationToken ct = default);
  Task<Result<CartView>> SetCurrencyAsync(ShopCaller caller, string currency, CancellationToken ct = default);
}
public interface ICartRepository { Task<Cart?> GetByCustomerAsync(long customerId, CancellationToken ct = default); Task AddAsync(Cart cart, CancellationToken ct = default); }
```
- Schema:
  - `sales.carts`: `ux_carts_customer UNIQUE(customer_id)`, `xmin`.
  - `sales.cart_lines`: `ux_cart_lines_cart_variant UNIQUE(cart_id, variant_id)` and
    `ck_cart_lines_quantity CHECK (quantity BETWEEN 1 AND 999)`.

- [ ] **Step 1: Write failing tests.** Service tests use substitutes:
  - `EnsureWebsiteCustomerAsync` is called with the caller's identity.
  - An inactive customer gets `CUSTOMER_SUSPENDED`.
  - Adding an unsellable variant gives `NOT_FOUND`.
  - Adding with no quote gives `PRICE_MISSING`.
  - Adding in a disabled currency gives `CURRENCY_NOT_ENABLED`.
  - A new cart takes `USD` as its default currency.
  - `GetAsync` after a price change returns `Changes = [repriced]` and saves the new displayed
    price.
  - `GetAsync` after a deactivation returns `[removed]` and drops the line.
  - `SetCurrencyAsync("EUR")` re-quotes every line.

  Schema tests check:
  - the CHECK rejects 0 and 1000
  - a duplicate (cart, variant) hits `ux_cart_lines_cart_variant`
  - a second cart for the same customer hits `ux_carts_customer`

- [ ] **Step 2: Run the tests to see them fail.** Commands:
  - `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "EwaysVNext.UnitTests.Sales.Services.CartServiceTests"`
  - `... IntegrationTests ... --filter-class "EwaysVNext.IntegrationTests.Sales.CartSchemaTests"`

  Expected: FAIL.

- [ ] **Step 3: Implement.**
  - Every operation follows load → live quote (`ISalesCatalogGateway.GetSellableAsync(ids)` plus
    `ISalesPricingGateway.QuoteAsync(ids, currency, now)`) → mutate → one save.
  - The displayed price is the live `ReferenceMinor`.
  - The cart never reads the MVs.
  - Generate migration `AddSalesCarts`.

- [ ] **Step 4: Run the tests to see them pass.** Same commands. Expected: PASS.

- [ ] **Step 5: Commit.**
```bash
git commit -am "feat(sales): cart persistence and live-quoting cart service" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 24: Shop cart REST and GraphQL

**Files:**
- Create:
  - `Rest/Shop/{ShopCartEndpoints,ShopDtos}.cs`
  - `Rest/Common/CallerClaims.cs` (add `ShopCaller ShopCallerOf(ClaimsPrincipal)`)
  - `GraphQL/Shop/ShopCartGraph.cs`, `GraphQL/DependencyInjection.cs` (`AddSalesGraphQL`)
- Test:
  - `tests/IntegrationTests/Sales/ShopCartTests.cs`
  - `tests/UnitTests/Sales/GraphQL/SalesGraphQLSchemaTests.cs`

**Interfaces:**
- REST, all with policy `Shop`:
  - `GET /api/v1/shop/cart`
  - `POST /api/v1/shop/cart/lines {variantId, quantity}`
  - `PUT /api/v1/shop/cart/lines/{variantId} {quantity}`
  - `DELETE /api/v1/shop/cart/lines/{variantId}`
  - `PUT /api/v1/shop/cart/currency {currency}`
- GraphQL `[Authorize(Policy = "Shop")]`:
  - Query `cart: Cart!`
  - Mutations `addToCart(input: AddToCartInput!): Cart!`, `updateCartLine(input: UpdateCartLineInput!): Cart!`,
    `removeCartLine(input: RemoveCartLineInput!): Cart!`, `setCartCurrency(input: SetCartCurrencyInput!): Cart!`.
  - Money is `{ amount: String!, currency: String! }`.
- `ShopCallerOf` reads `identity_id` (falling back to `sub`), `email`, and `email_verified == "true"`.

- [ ] **Step 1: Write failing tests.**
  - The schema test asserts each field signature above, built as in `GraphQLBootstrapTests`.
  - REST and GraphQL execution, with `X-Test-Identity: shop;{guid};buyer@example.test;true`:
    - Add → update → remove round trip.
    - Quantity 1000 gives 400 `VALIDATION_FAILED`.
    - Anonymous gets 401.
    - A Backoffice identity gets 403.
    - Currency switch to EUR reprices.
    - The GraphQL and REST views of the same cart are equal.

- [ ] **Step 2: Run the tests to see them fail.** Commands:
  - `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "EwaysVNext.UnitTests.Sales.GraphQL.SalesGraphQLSchemaTests"`
  - `... IntegrationTests ... --filter-class "EwaysVNext.IntegrationTests.Sales.ShopCartTests"`

  Expected: FAIL.

- [ ] **Step 3: Implement** the thin adapters over `ICartService`. Domain errors map through
  `ResultGraphQLExtensions`.

- [ ] **Step 4: Run the tests to see them pass.** Same commands. Expected: PASS.

- [ ] **Step 5: Commit.**
```bash
git commit -am "feat(sales): shop cart REST and GraphQL" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 25: Website order aggregate methods

**Files:**
- Modify: `Domain/Orders/Order.cs`, `OrderInputs.cs`
- Test: `tests/UnitTests/Sales/Orders/WebsiteOrderAggregateTests.cs`, `RefundTests.cs`

**Interfaces:**
- Produces:
```csharp
public sealed record WebsiteOrderHeader(long OrderId, string Number, long CustomerId, string Currency, long UsdEquivalentMinor, string CorrelationId);
public static Order CheckoutWebsite(WebsiteOrderHeader header, IReadOnlyList<OrderLineInput> lines, IssuedDocumentRef proForma,
    SellerBlock seller, IReadOnlyList<string> requiredBuyerFields, BuyerBlock buyer, TaxAssessment tax,
    PaymentInstructionBlock instruction, DateTimeOffset now);
public void ConfirmExternalPayment(long paymentRecordId, IssuedDocumentRef invoice, Guid verifiedBy, string method, string? reference,
    SellerBlock seller, IReadOnlyList<string> requiredBuyerFields, TaxAssessment tax, DateTimeOffset now);
public void Cancel(DateTimeOffset now);
public Refund RecordRefund(long refundId, IssuedDocumentRef creditNote, long amountMinor, string reason, Guid recordedBy,
    string? externalReference, SellerBlock seller, IReadOnlyList<string> requiredBuyerFields, DateTimeOffset now);
public long RefundedMinor { get; }   // Σ refunds
```

- [ ] **Step 1: Write failing tests.**
  - `CheckoutWebsite`:
    - `PendingPayment` / `Unpaid` / `Unfulfilled`, with no `PaymentSource` or `FulfillmentSource`.
    - One ProForma `PF-…` titled `"Pro Forma Invoice"` whose `PaymentInstruction` equals the
      input. `DocumentStatus == ProFormaIssued`.
    - No commercial invoice.
    - Lines have `BandEnforced == false` and `UnitPriceMinor == ReferencePriceMinor`. Otherwise
      it throws `ORDER_LINE_PRICE_NOT_REFERENCE`.
  - `ConfirmExternalPayment`:
    - From `PendingPayment`: `Paid` / `AdminConfirmed`, `Fulfilled` / `SystemAutomatic`,
      `Completed`.
    - One `PaymentRecord(AdminConfirmed, method, reference, verifiedBy, amount = total)`.
    - A Commercial `INV-…` whose buyer is the stored `Order.Buyer`.
    - The ProForma is unchanged: compare its serialized content before and after.
    - From `Completed` or `Cancelled` it throws `ORDER_STATE_CONFLICT`.
  - `Cancel`:
    - From `Unpaid`: `Cancelled`.
    - A second `Cancel` is a no-op (guardrail 2).
    - On `Paid` it throws `ORDER_STATE_CONFLICT`.
  - `RecordRefund`:
    - On a `Paid` order of 10,000: refunding 3,000 gives `PartiallyRefunded` and a CreditNote
      `CN-…` with `RelatedDocumentId == CommercialInvoice.Id`, `RefundAmountMinor == 3000`, and
      `RelatedDocumentNumber == "INV-…"`.
    - Then 7,000 gives `Refunded`.
    - Then 1 throws `REFUND_EXCEEDS_REFUNDABLE`.
    - On `Unpaid` it throws `ORDER_STATE_CONFLICT`.
    - An amount ≤ 0 throws `VALIDATION_FAILED`.
    - The commercial invoice content is byte-identical before and after.
    - Partner orders can be refunded too.

- [ ] **Step 2: Run the tests to see them fail.** Command:
  `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-namespace "EwaysVNext.UnitTests.Sales.Orders"`.
  Expected: FAIL.

- [ ] **Step 3: Implement** by reusing `IssueDocument`.

- [ ] **Step 4: Run the tests to see them pass.** Same command. Expected: PASS.

- [ ] **Step 5: Commit.**
```bash
git commit -am "feat(sales): website checkout, admin payment confirmation, cancel, refunds with credit notes" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 26: Checkout service and endpoints

**Files:**
- Create:
  - `Ports/Inbound/ICheckoutService.cs`, `Services/CheckoutService.cs`
  - `Rest/Shop/ShopCheckoutEndpoints.cs`
  - add `checkout` to `GraphQL/Shop/ShopCartGraph.cs`
- Test:
  - `tests/UnitTests/Sales/Services/CheckoutServiceTests.cs`
  - `tests/IntegrationTests/Sales/ShopCheckoutTests.cs`
  - `SalesArchitectureTests` (one more test)

**Interfaces:**
- Produces:
```csharp
public abstract record CheckoutOutcome {
  public sealed record Placed(long OrderId) : CheckoutOutcome;
  public sealed record PriceChanged(CartView FreshCart) : CheckoutOutcome;
}
public interface ICheckoutService {
  Task<Result<CheckoutOutcome>> CheckoutAsync(ShopCaller caller, long expectedTotalMinor, string expectedCurrency, string correlationId, CancellationToken ct = default);
}
```
- REST: `POST /api/v1/shop/checkout { expectedTotal: { amount: "123.45", currency: "USD" } }`.
  - It returns 201 with the shop order view (Task 27 shape).
  - `PriceChanged` returns **409** via `EndpointResult.Fail(SalesErrors.PriceChanged, details: new { cart })`.
- GraphQL: `checkout(input: CheckoutInput!): CheckoutPayload!` with
  `CheckoutPayload { order: ShopOrder, priceChanged: Cart, code: String }`.

- [ ] **Step 1: Write failing tests.**
  - Service tests:
    - Unverified email gives `EMAIL_NOT_VERIFIED`, with no quote call.
    - An empty cart gives `VALIDATION_FAILED`.
    - A live price differing from the displayed price gives `PriceChanged`. Nothing is written:
      no `IUnitOfWork` call and no allocator call.
    - A total mismatch gives `PriceChanged`.
    - A currency mismatch gives `PriceChanged`.
    - FX unavailable gives `FX_RATE_UNAVAILABLE` (retryable).
    - On success, `Received.InOrder`: allocate `Order` and `ProForma` numbers, one
      `IOrderRepository.AddAsync` + save, then `cart.Clear` + a second save.
    - A failure of the cart-clear save still returns `Placed` and logs a warning.
  - Integration:
```csharp
[Fact] public async Task Checkout_With_Deactivated_Line_Returns_Price_Changed()
{ /* add v1, v2; expectedTotal = cart total; deactivate v2 (A9); POST checkout ⇒ 409 PRICE_CHANGED;
     details.cart.lines contains only v1, details.cart.changes = [{variantId: v2, kind: "removed"}];
     no order for this customer; GET cart ⇒ only v1; checkout with new total ⇒ 201, order lines = [v1] */ }
[Fact] public async Task Unverified_Email_Cannot_Check_Out() { /* email_verified=false ⇒ 403 EMAIL_NOT_VERIFIED, no order */ }
[Fact] public async Task Price_Change_Between_View_And_Checkout_Returns_Price_Changed() { /* set currency override on v1 (A9) after add ⇒ 409; no writes */ }
```
  - Architecture: `Shop_Request_Dtos_Accept_No_Money_Except_ExpectedTotal`. Across the
    `…Rest.Shop` request types and the `…GraphQL.Shop` input types, a property whose name
    contains `Price`, `Amount`, `Total` or `Money` is allowed only if it is `ExpectedTotal`.

- [ ] **Step 2: Run the tests to see them fail.** Commands:
  - `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "EwaysVNext.UnitTests.Sales.Services.CheckoutServiceTests"`
  - the `ShopCheckoutTests` integration class
  - the architecture tests

  Expected: FAIL.

- [ ] **Step 3: Implement** spec §11 step 2. Read `now` once.
  1. Ensure the customer exists, is verified and is active.
  2. Load the cart.
  3. Re-quote live and re-check sellability.
  4. Compare per line and in total. Any difference: return `PriceChanged(fresh view)`, with no
     write.
  5. Convert to USD. Load the invoice party, then the buyer block, then check the required
     fields.
  6. Load the active payment instruction.
  7. Allocate numbers, then `Order.CheckoutWebsite`, then `AddAsync`, then save.
  8. Clear the cart in its own save, inside `try`/`catch`.
  9. Return `Placed`.

- [ ] **Step 4: Run the tests to see them pass.** Same commands. Expected: PASS.

- [ ] **Step 5: Commit.**
```bash
git commit -am "feat(sales): checkout(expectedTotal) with PRICE_CHANGED, pro forma snapshot, cart clear" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 27: Shop order reads — myOrders, order, documentPdfUrl

**Files:**
- Create:
  - `QueryApplication/Shop/{IShopOrderQueries,ShopOrderQueries,ShopOrderDtos}.cs`
  - `Rest/Shop/ShopOrderEndpoints.cs`
  - `GraphQL/Shop/ShopOrderGraph.cs`
- Test: `tests/IntegrationTests/Sales/ShopOrderTests.cs`

**Interfaces:**
- Produces:
```csharp
public sealed record ShopOrderDto(long Id, string Number, string Currency, long TotalMinor, OrderStatusDto Status,
    IReadOnlyList<OrderLineDto> Lines, IReadOnlyList<DocumentDto> Documents, DateTimeOffset CreatedAt);
public interface IShopOrderQueries {
  Task<CursorPage<ShopOrderDto>> ListMyOrdersAsync(long customerId, long? cursor, int limit, CancellationToken ct = default); // channel = Website only
  Task<Result<ShopOrderDto>> GetMyOrderAsync(long customerId, long orderId, CancellationToken ct = default);
}
```
- REST, policy `Shop`:
  - `GET /api/v1/shop/orders?cursor&limit`
  - `GET /api/v1/shop/orders/{id}`
  - `GET /api/v1/shop/documents/{id}/pdf-url` returns `{ url }` via `IInvoiceService` with
    `DocumentAccess.WebsiteCustomer`.
- GraphQL: `myOrders(cursor: ID, limit: Int = 20)`, `order(id: ID!)`, `documentPdfUrl(documentId: ID!): String!`.

- [ ] **Step 1: Write failing tests.**
  - Another customer's order or document gives 404.
  - `myOrders` returns only the caller's Website orders, newest first, with a cursor.
  - The pro forma document is present, with the instruction text in `content.paymentInstruction.text`.
  - `documentPdfUrl` returns the store URL.
  - REST and GraphQL bodies agree.

- [ ] **Step 2: Run the tests to see them fail.** Command:
  `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "EwaysVNext.IntegrationTests.Sales.ShopOrderTests"`.
  Expected: FAIL.

- [ ] **Step 3: Implement.** Projections filter
  `o.Channel == OrderChannel.Website && o.CustomerId == customerId` in SQL. The customer id comes
  from `ISalesCustomerGateway.EnsureWebsiteCustomerAsync`, called in the adapter through a
  one-method `IShopCustomerResolver` in Application.

- [ ] **Step 4: Run the tests to see them pass.** Same command. Expected: PASS.

- [ ] **Step 5: Commit.**
```bash
git commit -am "feat(sales): shop order history, order detail and document pdf url (REST + GraphQL)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 28: Order admin service, endpoints and mutations

**Files:**
- Create:
  - `Ports/Inbound/IOrderAdminService.cs`, `Services/OrderAdminService.cs`
  - `Rest/Admin/{AdminOrderEndpoints,AdminDtos}.cs`
  - `GraphQL/Admin/AdminOrderGraph.cs` (mutations)
  - `Rest/Common/CallerClaims.cs` (add `AdminActor AdminActorOf(ClaimsPrincipal)`)
- Test:
  - `tests/UnitTests/Sales/Services/OrderAdminServiceTests.cs`
  - `tests/IntegrationTests/Sales/WebsiteOrderAcceptanceTests.cs`

**Interfaces:**
- Produces (the master contract, exactly):
```csharp
public sealed record RefundResult(long RefundId, long CreditNoteDocumentId, string CreditNoteNumber, string PaymentStatus, long RefundedMinor);
public interface IOrderAdminService {
  Task<Result> ConfirmExternalPaymentAsync(long orderId, string method, string? reference, AdminActor actor, CancellationToken ct = default);
  Task<Result> CancelAsync(long orderId, AdminActor actor, CancellationToken ct = default);
  Task<Result<RefundResult>> RecordRefundAsync(long orderId, long amountMinor, string reason, string? externalReference, AdminActor actor, CancellationToken ct = default);
}
```
- `AdminActorOf` is `new AdminActor(Guid.Parse(identity_id ?? sub), user.IsInRole("super_admin"))`.
- REST:
  - `POST /api/v1/admin/orders/{id}/confirm-payment {method, reference?}` (`admin:staff`)
  - `POST /api/v1/admin/orders/{id}/cancel` (`admin:staff`)
  - `POST /api/v1/admin/orders/{id}/refunds {amount: "12.00", reason, externalReference?}` (`admin:super`)
  - `GET /api/v1/admin/documents/{id}/pdf` returns 302 (`admin:staff`)
- GraphQL mutations `confirmOrderPayment`, `cancelOrder` and `refundOrder`. Each returns
  `AdminOrderDetail`, reloaded through `ISalesAdminQueries.GetOrderAsync` (Task 29; guardrail 3).

- [ ] **Step 1: Write failing tests.**
  - Service tests:
    - `RecordRefundAsync` with `IsSuperAdmin == false` gives `FORBIDDEN`. The repository is not
      called. The service check holds even if the endpoint policy is missing.
    - Unknown order gives `NOT_FOUND`.
    - Domain codes pass through: `ORDER_STATE_CONFLICT`, `REFUND_EXCEEDS_REFUNDABLE`.
    - Each successful operation calls `IAuditTrail.Record(actor.ToAudit(), action, "Order", id, before, after)`
      **before** the single `SaveChangesAsync`, with exactly one save. Actions:
      `sales.order.confirm_payment`, `sales.order.cancel`, `sales.order.refund`.
      `before`/`after` are the status dimensions and `RefundedMinor` only (no PII).
    - The refund amount is checked against the order currency.
  - Integration:
```csharp
[Fact] public async Task Acceptance09_Website_Checkout_Is_ProForma_Until_Admin_Confirms_And_History_Is_Website_Only()
{ /* shop checkout ⇒ PendingPayment, documents = [ProForma], no INV-; partner A order for a partner customer exists;
     myOrders shows only the website order; staff POST confirm-payment {method:"bank-transfer", reference:"TT-1"} ⇒ 200;
     order: Paid/AdminConfirmed, Fulfilled/SystemAutomatic, Completed, documents = [ProForma, Commercial] with Commercial title "Invoice";
     second confirm ⇒ 409 ORDER_STATE_CONFLICT; cancel on Paid ⇒ 409; a second unpaid website order: staff cancel ⇒ 200 Cancelled */ }
[Fact] public async Task Acceptance12_Refunds_Create_Audited_Linked_Credit_Notes_And_Keep_Invoice()
{ /* paid order total T; capture commercial content JSON; super refund "T/4" ⇒ 200 PartiallyRefunded, CN-… linked to INV id;
     refund rest ⇒ Refunded; refund "0.01" more ⇒ 422 REFUND_EXCEEDS_REFUNDABLE; staff refund ⇒ 403;
     audit.audit_events has 2 rows action sales.order.refund for the order with the super admin's user id;
     commercial content JSON unchanged; partner order refund works the same way */ }
```

- [ ] **Step 2: Run the tests to see them fail.** Commands:
  - `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "EwaysVNext.UnitTests.Sales.Services.OrderAdminServiceTests"`
  - the `WebsiteOrderAcceptanceTests` integration class

  Expected: FAIL.

- [ ] **Step 3: Implement.** Each service method does load → allocate number (INV/CN) →
  domain call (`DomainException` → `Result`) → `IAuditTrail.Record` → one save.
  `CancelAsync` allocates no number.

- [ ] **Step 4: Run the tests to see them pass.** Same commands. Expected: PASS.

- [ ] **Step 5: Mutation-test.** Remove the `IsSuperAdmin` check from `RecordRefundAsync` and
  run the tests. Expected: the unit test fails (the endpoint policy still blocks over HTTP).
  Restore the check.

- [ ] **Step 6: Commit.**
```bash
git commit -am "feat(sales): admin confirm payment, cancel and super-admin refunds with audit (REST + GraphQL)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 29: ISalesAdminQueries — list, detail, documents, dashboard

**Files:**
- Create: `QueryApplication/Admin/{ISalesAdminQueries,SalesAdminQueries,SalesAdminDtos}.cs`
- Modify: `Rest/Admin/AdminOrderEndpoints.cs` (GET routes), `GraphQL/Admin/AdminOrderGraph.cs` (queries)
- Test: `tests/IntegrationTests/Sales/SalesAdminQueriesTests.cs`

**Interfaces:**
- Produces. **These are the names phase 6 consumes; they are fixed.**
```csharp
namespace EwaysVNext.Modules.Sales.QueryApplication.Admin;
public enum AdminChannelFilter { Website, PartnerApi }
public sealed record AdminOrderFilter(AdminChannelFilter? Channel = null, string? OrderStatus = null, string? PaymentStatus = null,
    string? Currency = null, long? CustomerId = null, long? PartnerId = null, string? ExternalOrderId = null, string? Number = null,
    DateTimeOffset? From = null, DateTimeOffset? To = null, long? Cursor = null, int PageSize = 50);           // PageSize 1..200
public sealed record AdminOrderListItemDto(long Id, string Number, string Channel, long? PartnerId, string? ExternalOrderId,
    long CustomerId, string Currency, long TotalMinor, long RefundedMinor, OrderStatusDto Status, DateTimeOffset CreatedAt);
public sealed record AdminPaymentDto(long Id, string Source, string? Method, string? Reference, Guid? VerifiedBy, DateTimeOffset RecordedAt, long AmountMinor);
public sealed record AdminRefundDto(long Id, long AmountMinor, string Reason, Guid RecordedBy, DateTimeOffset RecordedAt, string? ExternalReference, long CreditNoteDocumentId);
public sealed record AdminCompositionTraceDto(long Seed, string StrategyVersion, int Iterations, long DurationMs, int CandidatePoolSize, JsonElement Attempts);
public sealed record AdminOrderDetailDto(AdminOrderListItemDto Summary, string? ExternalCustomerId, long UsdEquivalentMinor,
    string CorrelationId, JsonElement Buyer, IReadOnlyList<OrderLineDto> Lines, IReadOnlyList<AdminPaymentDto> Payments,
    IReadOnlyList<DocumentDto> Documents, IReadOnlyList<AdminRefundDto> Refunds, AdminCompositionTraceDto? CompositionTrace);
public sealed record ChannelCurrencyTotalsDto(string Window, string Channel, string Currency, int OrderCount, long TotalMinor);  // Window "24h" | "7d"; excludes Cancelled
public sealed record CurrencyAmountDto(string Currency, long AmountMinor);
public sealed record PendingWebsitePaymentsDto(int Count, IReadOnlyList<CurrencyAmountDto> AmountsByCurrency, DateTimeOffset? OldestCreatedAt);
public sealed record CompositionStatsDto(string Window, int Attempts, int Succeeded, decimal SuccessRate, long? P95DurationMs); // SuccessRate 0..1, 4dp; null p95 when 0 attempts
public sealed record CompositionFailureDto(DateTimeOffset At, string ErrorCode, string Currency, long TargetMinor, long DurationMs, long? PartnerId);
public sealed record SalesDashboardDto(IReadOnlyList<ChannelCurrencyTotalsDto> OrderTotals, PendingWebsitePaymentsDto PendingWebsitePayments,
    CompositionStatsDto Composition24h, CompositionStatsDto Composition7d, IReadOnlyList<CompositionFailureDto> RecentFailures); // last 20 failures
public interface ISalesAdminQueries {
  Task<Result<CursorPage<AdminOrderListItemDto>>> ListOrdersAsync(AdminOrderFilter filter, CancellationToken ct = default);
  Task<Result<AdminOrderDetailDto>> GetOrderAsync(long orderId, CancellationToken ct = default);
  Task<Result<DocumentDto>> GetDocumentAsync(long documentId, CancellationToken ct = default);
  Task<IReadOnlyList<AdminOrderListItemDto>> ListCustomerOrdersAsync(long customerId, int limit, CancellationToken ct = default); // customer 360, newest first
  Task<SalesDashboardDto> GetDashboardAsync(DateTimeOffset now, CancellationToken ct = default);
}
```
- REST, policy `admin:staff`: `GET /api/v1/admin/orders` (query string = `AdminOrderFilter`),
  `GET /api/v1/admin/orders/{id}`.
- GraphQL `[Authorize(Policy = "admin:staff")]`: `adminOrders(filter: AdminOrderFilterInput): AdminOrderPage!`,
  `adminOrder(id: ID!): AdminOrderDetail!`.
- The dashboard has no endpoint here; phase 6 composes it.

- [ ] **Step 1: Write failing tests.**
  - The list covers both channels, and every filter narrows the result. Cursor paging works.
  - `PageSize` 0 or 201 gives `VALIDATION_FAILED`.
  - The detail includes payments, refunds, documents in issue order, and the composition trace
    for partner orders (null for website orders).
  - Dashboard:
    - Seed 3 USD partner orders, 1 EUR website pending, and 1 cancelled website order, all now.
    - Add a partner order with `created_at = now − 3 days`.
    - Insert composition attempts: 8 successes with durations 100..800, and 2 failures
      (`COMPOSITION_TIMEOUT`, `EXACT_COMPOSITION_UNAVAILABLE`) within 24h. Add one success 3
      days ago.
    - `Composition24h = (10, 8, 0.8000, p95 = percentile_cont(0.95) over the 10 durations, rounded)`.
    - `Composition7d.Attempts == 11`.
    - `RecentFailures` holds the 2 codes, newest first.
    - `OrderTotals` has `("24h", "PartnerApi", "USD", 3, Σ)` and `("7d", "PartnerApi", "USD", 4, Σ)`,
      and no cancelled order.
    - `PendingWebsitePayments.Count == 1`, amount by EUR.
  - REST and GraphQL list/detail agree. A Shop identity gets 403.

- [ ] **Step 2: Run the tests to see them fail.** Command:
  `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "EwaysVNext.IntegrationTests.Sales.SalesAdminQueriesTests"`.
  Expected: FAIL.

- [ ] **Step 3: Implement.**
  - List and detail are `AsNoTracking` projections.
  - In the dashboard, totals and pending are grouped LINQ queries.
  - Composition stats use one raw `db.Database.SqlQuery<CompositionStatsRow>` per window:
```sql
SELECT count(*)::int AS attempts, count(*) FILTER (WHERE succeeded)::int AS succeeded,
       round(percentile_cont(0.95) WITHIN GROUP (ORDER BY duration_ms))::bigint AS p95
FROM sales.composition_attempts WHERE at > {now - window}
```
  - Failures: `CompositionAttempts.Where(!Succeeded).OrderByDescending(At).Take(20)`.
  - These statements are Postgres-specific, so they are tested only in the integration suite.

- [ ] **Step 4: Run the tests to see them pass.** Same command. Expected: PASS.

- [ ] **Step 5: Commit.**
```bash
git commit -am "feat(sales): admin order list/detail and sales dashboard aggregates for phase 6" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 30: Phase 5 gate

**Files:**
- Create: `docs/reports/phase-5.md`
- Modify:
  - `AGENTS.md` API table: the `/api/v1/shop/{cart,checkout,orders,documents}` and
    `/api/v1/admin/orders` rows
  - `docs/TESTING-GUARANTEES.md`

- [ ] **Step 1: Build.** `dotnet build EwaysVNext.slnx -c Release`. Expected: 0 errors.
- [ ] **Step 2: Test.** `dotnet test EwaysVNext.slnx`. Expected: all PASS. List the PRD
  acceptance tests 1, 2, 3, 4, 5, 6, 7, 8, 9, 12 and 14 by name with their result.
- [ ] **Step 3: Write `docs/reports/phase-5.md`.** It covers:
  - **What works.**
  - **What is incomplete:** the admin dashboard endpoint is phase 6; the k6 run is phase 8.
  - **Risks:**
    - `GET cart` writes the refreshed displayed prices
    - a failed cart clear leaves a stale cart
    - assumptions A1–A9 and whether each held
  - **Reproduction commands.**
- [ ] **Step 4: Commit.**
```bash
git add docs/reports/phase-5.md AGENTS.md docs/TESTING-GUARANTEES.md
git commit -m "docs(reports): phase 5 gate (website sales)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Spec conflicts and resolutions (for the reviewer)

1. **Reads on `IPartnerOrderService` and `IInvoiceService`.** §5.6 lists get, list, history and
   search on these Application ports. DDD §7 and ADR-0016 put all reads in QueryApplication.
   - **Resolution:** reads live in `IPartnerOrderQueries`. Application keeps create and bulk on
     `IPartnerOrderService`, and the PDF on `IInvoiceService`.
   - Create and replay bodies are reloaded through the query side, so the shapes are identical
     (acceptance 2).
2. **The `Composition` type name.** It would collide with the `Composition` namespace.
   **Resolution:** the result type is `CompositionPlan`, so the signature is
   `Result<CompositionPlan>`.
3. **`PriceSource` values.** §5.6 lists `{Override, Fx}`; the master contract adds `Base`.
   **Resolution:** line snapshots store `Override | Fx | Base`.
4. **"Write nothing" on `PRICE_CHANGED` versus re-showing the cart.**
   - **Resolution:** checkout writes nothing. `GET cart` is itself a cart operation: it re-quotes
     and persists the refreshed displayed prices, so the customer's next checkout compares
     against what was shown.
5. **Codes the spec does not list.** The resolution adds:
   - `ORDER_STATE_CONFLICT` (409)
   - `REFUND_EXCEEDS_REFUNDABLE` (422)
   - `EMAIL_NOT_VERIFIED` (403)
   - `CUSTOMER_SUSPENDED` (403)
   - the internal `INTERNAL_SALES` and `COMPOSITION_VERIFICATION_FAILED` (500)
6. **Order header additions.**
   - `ExternalCustomerId` is denormalized for the partner list filters and `ext:` customer
     history.
   - `Buyer` is a jsonb snapshot, so the commercial invoice and credit note reuse the
     checkout-time buyer.
   - `PdfObjectKey` is set at issuance to its deterministic key. PDF rendering then never writes
     to the aggregate.
7. **The `PartnerPrincipal` contract has no `BulkMaxItems`.** It is read from a `bulk_max_items`
   claim (A5), falling back to `CommerceLimits.BulkDefaultMax`.
8. **Best-seller popularity.** It counts units on non-cancelled lines from both channels over
   30 days. Partner compositions therefore influence best sellers. That is a design call; flip
   the `WHERE` to `o.channel = 'Website'` if the product owner disagrees.

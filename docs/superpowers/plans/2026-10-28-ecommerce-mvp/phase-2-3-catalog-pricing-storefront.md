# Phases 2–3 — Catalog, Pricing & Storefront Implementation Plan

> Part of docs/superpowers/plans/2026-10-28-ecommerce-mvp.md — read its Global Constraints, Review Focus and Cross-phase contracts first.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:**
- **Phase 2:** extend the kept Catalog aggregates (canonical keys, aliases), add `Brand`, rewrite
  `Product` (identifiers, media in MinIO, `Variant` children, statuses, `DeactivatedByAdmin`), ship
  the Catalog admin, sellable-variant and publishing ports, build the new **Pricing** module (prices,
  currency overrides, currencies, append-only FX, FX overrides, ECB provider, quotes, ±5% band), and
  a `--seed` demo catalog.
- **Phase 3:** storefront materialized views, their refresh job, and the anonymous shop browse API
  (REST `/api/v1/shop/...` + GraphQL).

**Spec sections:** §4 (money, currency, FX), §5.3 (Catalog), §5.4 (Pricing), §8 (materialized
views), §9.2 anonymous part, §10.5 "Seed catalog" paragraph. Also §3.3 (snake_case, `xmin`,
`pg_trgm`/`unaccent`), §3.6 (sanctioned references), §6.3 (errors), §6.4 (audit), §7 (jobs).

**Assumed from phases 0–1 (do not re-implement; use as named in the master Cross-phase contracts):**
Npgsql + `UseSnakeCaseNamingConvention()` + `xmin` concurrency + `pg_trgm`/`unaccent` extensions;
Catalog `ProductReservation`, the reaper and stock fields removed; `IObjectStorage`/`StorageBuckets`;
`IJobQueue`/`IJobHandler<T>`/`AddJobHandler`/`AddRecurringJob`/`JobTypes`; `IAuditTrail`;
`AdminActor`; `Error(Code, Message, HttpStatus, Retryable)`; `MinorUnits`; authorization policies
`"admin:staff"` (staff **or** super admin) and `"admin:super"`; `ISettingsPort`
(`CommerceLimitsSnapshot.FxMaxRateAge`); `tests/IntegrationTests/PostgresFixture.cs` with
collection `PostgresSuite.Name` and `CreateFactory()`; `tests/ArchitectureTests/HexagonalBoundaryTests.cs`
with `ModuleNames` and the sanctioned-exception `[Theory]`; `AdminJwtFactory.CreateToken(realm, role)`
reachable from tests (make it `internal static` if phase 1 left it `private`).

**Conventions used by every task:**
- Unit test command: `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "*.<Class>"`.
- Integration: `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "*.<Class>"` (Docker running).
- Architecture: `dotnet test --project tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj`.
- Migration: `dotnet ef migrations add <Name> -p src/Database -s src/Host/EwaysVNext.Host`.
- Every new table gets an explicit snake_case `ToTable("<name>")`, so the raw SQL in phase 3
  never depends on how the naming convention treats explicit names.
- Enums that raw SQL reads (`ProductStatus`, `MediaUsageRights`, `ProductIdentifierType`,
  `PriceSource`) are stored as text (`HasConversion<string>()`); existing int-stored enums stay int.
- Aggregates throw `DomainException(code, message)`; services translate to `Result` with
  `new Error(code, message, status)` (rule 9). Admin services call `IAuditTrail.Record(actor.ToAudit(), ...)`
  before their single `SaveChangesAsync`.
- Phase 0/1 names used verbatim: `IUnitOfWork.TrySaveChangesAsync()` → `SaveOutcome { Saved, UniqueViolation }`
  (every "maps `23505` to …" below means: call `TrySaveChangesAsync` and map `UniqueViolation`);
  Storage/Jobs/Audit abstractions live in `EwaysVNext.{Storage,Jobs,Audit}.Application` — Application
  projects reference only those, adapters/installers reference `.Implementation`; policies are referenced as
  `AuthPolicies.AdminStaff` (`"admin:staff"`) and `AuthPolicies.AdminSuper` (`"admin:super"`), never as literals.
- Room left for phase 7: it adds `ICatalogImportLookupPort` and `ICatalogMatchPort` to
  `Catalog.Application/Products/Ports/Inbound` and `MeasurementUnit.Dimension`/`ToBaseFactor`; this plan
  defines neither and does not touch `MeasurementUnit`.
- Commit trailer: every `git commit` passes a second `-m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`.

---

## File Structure

```
src/Modules/Catalog/
├── Application/
│   ├── Common/CanonicalKey.cs                                  (new) shared normalizer
│   ├── Categories/Domain/Category.cs                           (modify) CanonicalKey, Aliases
│   ├── AttributeBlueprints/Domain/AttributeBlueprint.cs        (modify) CanonicalKey, Scope, Aliases, Option, UnitId
│   ├── Brands/{Domain/Brand.cs, Domain/BrandAlias.cs, Ports/Inbound/IBrandAdminService.cs,
│   │          Ports/Outbound/IBrandRepository.cs, Services/BrandAdminService.cs, DependencyInjection.cs} (new)
│   ├── Products/Domain/{Product.cs (rewrite), ProductStatus.cs, Variant.cs, AttributeSignature.cs,
│   │                    ProductMedia.cs, MediaUsageRights.cs, ProductIdentifier.cs, ProductIdentifierType.cs, Gs1.cs}
│   ├── Products/Ports/Inbound/{ICatalogAdminService.cs (rewrite), CatalogAdminCommands.cs,
│   │                          ISellableVariantPort.cs, IVariantLookupPort.cs, ICatalogPublishingPort.cs, PublishProductCommand.cs}
│   ├── Products/Ports/Outbound/{IProductRepository.cs (rewrite), ICatalogMediaStore.cs,
│   │                           ISellableVariantReader.cs, ICatalogChangeNotifier.cs}
│   ├── Products/Services/{CatalogAdminService.cs (rewrite), SellableVariantService.cs, CatalogPublishingService.cs}
│   └── Storefront/Ports/Inbound/IStorefrontRefreshTrigger.cs   (new)
│   (delete) Products/Domain/{ProductMessages.cs, ProductEvents.cs→rewritten}, Ports/Inbound/{IProductService.cs, ReservationSnapshot.cs},
│            Services/ProductService.cs, Validation/{ChangePriceValidator,CreateProductValidator,ReserveStockValidator}.cs
├── Adapter/Outbound/
│   ├── Persistence/Brands/BrandRepository.cs, Persistence/Products/ProductRepository.cs (rewrite),
│   ├── Persistence/Products/SellableVariantReader.cs, Media/ObjectStorageCatalogMediaStore.cs,
│   └── Storefront/{CatalogChangeNotifier.cs, StorefrontRefreshTrigger.cs, StorefrontViewRefresher.cs,
│                   RefreshStorefrontViewsJobHandler.cs}
├── Adapter/Inbound/Rest/Admin/{CatalogAdminEndpoints.cs, BrandAdminEndpoints.cs, CategoryAdminEndpoints.cs,
│                              AttributeAdminEndpoints.cs, CatalogAdminDtos.cs}
├── Adapter/Inbound/Rest/Shop/{ShopEndpoints.cs, ShopDtos.cs}
├── Adapter/Inbound/GraphQL/{Products/ProductAdminMutations.cs, Products/ProductAdminQueries.cs,
│                            Brands/BrandAdminResolvers.cs, Shop/ShopQueries.cs, Shop/ShopTypes.cs}
└── QueryApplication/{Products/ICatalogAdminQueries.cs, Products/CatalogAdminQueries.cs,
                      Storefront/IStorefrontQueries.cs, Storefront/StorefrontQueries.cs, Storefront/StorefrontSql.cs,
                      Storefront/StorefrontCursor.cs, Storefront/StorefrontDtos.cs}
    (delete) QueryApplication/Products/{IProductQueryService,ProductQueryService}.cs,
             EwaysVNext.Modules.Catalog.Contracts/Products/*

src/Modules/Pricing/                                            (new module, scaffolded by New-EwaysModule.ps1)
├── Application/
│   ├── Bands/PriceBand.cs
│   ├── Currencies/{Domain/Currency.cs, Ports/Outbound/ICurrencyRepository.cs}
│   ├── Fx/Domain/{FxRate.cs, FxRateOverride.cs, FxRateBook.cs, FxRateResolver.cs, FxResolution.cs, UsdPeg.cs}
│   ├── Fx/Ports/Inbound/{IFxSnapshotPort.cs, IFxRefreshService.cs, FxProviderBatch.cs}
│   ├── Fx/Ports/Outbound/{IFxRateProvider.cs, IFxRateRepository.cs, IFxRateOverrideRepository.cs, IResolvedRateProjection.cs}
│   ├── Fx/Services/{FxRefreshService.cs, FxSnapshotService.cs, ResolvedRateProjectionService.cs}
│   ├── Prices/Domain/{VariantPrice.cs, CurrencyOverride.cs, PriceProvenance.cs, PriceResolver.cs}
│   ├── Prices/Ports/Inbound/{IPriceQuotePort.cs, PriceQuote.cs, IPricePublishingPort.cs, IPricingAdminService.cs}
│   ├── Prices/Ports/Outbound/{IVariantPriceRepository.cs, ICatalogVariantGateway.cs, IPricingPolicyGateway.cs, IPricingChangeNotifier.cs}
│   ├── Prices/Services/{PriceQuoteService.cs, PricePublishingService.cs, PricingAdminService.cs}
│   └── PricingErrors.cs, DependencyInjection.cs
├── Adapter/Outbound/{Persistence/*Repository.cs, Persistence/ResolvedRateProjection.cs, Fx/Ecb/EcbFxRateProvider.cs,
│                     Fx/FxRefreshJobHandler.cs, Gateways/CatalogVariantGateway.cs, Gateways/PricingPolicyGateway.cs,
│                     Gateways/PricingChangeNotifier.cs, DependencyInjection.cs}
├── Adapter/Inbound/Rest/{PricingAdminEndpoints.cs, ShopCurrencyEndpoints.cs, PricingDtos.cs}
├── Adapter/Inbound/GraphQL/{PricingAdminResolvers.cs, CurrencyQueries.cs, DependencyInjection.cs}
├── QueryApplication/{IPricingAdminQueries.cs, PricingAdminQueries.cs, ICurrencyQueries.cs, CurrencyQueries.cs, PricingDtos.cs}
└── Installer/ModuleInstaller.cs  (AddPricingModule)

src/Database/
├── EwaysVNextDbContext.cs                                       (modify) DbSets
├── Catalog/Configurations/{BrandConfiguration.cs, Products/{ProductConfiguration.cs (rewrite), VariantConfiguration.cs,
│                           ProductMediaConfiguration.cs, ProductIdentifierConfiguration.cs}}
├── Catalog/Views/StorefrontViewSql.cs                           (new) V1 view SQL constants
├── Pricing/Configurations/{CurrencyConfiguration.cs, FxRateConfiguration.cs, FxRateOverrideConfiguration.cs,
│                           VariantPriceConfiguration.cs, CurrencyOverrideConfiguration.cs}
└── Migrations/<timestamp>_{CatalogCanonicalKeys, CatalogBrands, CatalogProductsRewrite, CatalogSellabilityVersion,
                            PricingInit, PricingPrices, StorefrontViewsV1}.cs

src/BuildingBlocks/Presentation.Endpoint/Money/MoneyText.cs     (new) "^\d+\.\d{2}$" <-> minor units
src/Host/EwaysVNext.Host/Seeding/{DemoSeedGuard.cs, DemoCatalogData.cs, PlaceholderPng.cs, DemoCatalogSeeder.cs}
src/Host/EwaysVNext.Host/Program.cs                              (modify) --seed, AddPricingModule

tests/UnitTests/Catalog/{CanonicalKeyTests, CategoryCanonicalKeyTests, AttributeBlueprintCanonicalTests, BrandTests,
                         ProductIdentifierTests, AttributeSignatureTests, ProductLifecycleTests, CatalogAdminServiceTests,
                         CatalogBulkAndMergeTests, CatalogMediaUploadTests, CatalogPublishingServiceTests}.cs
tests/UnitTests/Pricing/{PriceBandTests, CurrencyAndFxDomainTests, FxRateResolverTests, PriceResolverTests,
                         PriceQuoteServiceTests, PricingAdminServiceTests, EcbFxRateProviderTests, FxRefreshServiceTests}.cs
tests/UnitTests/Pricing/Fixtures/eurofxref-daily-2026-10-27.xml
tests/UnitTests/Seeding/{PlaceholderPngTests, DemoCatalogDataTests, DemoSeedGuardTests}.cs
tests/UnitTests/MoneyTextTests.cs
tests/IntegrationTests/Support/{InMemoryObjectStorage.cs, CatalogTestHost.cs, StorefrontScenario.cs}
tests/IntegrationTests/Catalog/{CatalogConstraintTests, CatalogAdminRestTests, SellableVariantPortTests}.cs
tests/IntegrationTests/Pricing/{PricingRestTests, PricingPersistenceTests}.cs
tests/IntegrationTests/Seeding/DemoSeedTests.cs
tests/IntegrationTests/Storefront/{StorefrontViewTests, StorefrontPriceParityTests, ShopRestTests, ShopGraphQLTests}.cs
docs/reports/phase-2.md, docs/reports/phase-3.md
```

---

# Phase 2 — Catalog, Pricing, seed

### Task 1: Canonical key normalizer and snake_case Catalog table names

**Files:**
- Create: `src/Modules/Catalog/Application/Common/CanonicalKey.cs`
- Modify: every `src/Database/Catalog/Configurations/**/*.cs` (explicit `ToTable`)
- Test: `tests/UnitTests/Catalog/CanonicalKeyTests.cs`

**Interfaces:**
- Produces: `public static class CanonicalKey { public static string From(string input); public static bool TryFrom(string? input, out string key); public const int MaxLength = 120; }`
- Produces table names: `catalog.categories`, `catalog.category_products`, `catalog.attribute_blueprints`,
  `catalog.attribute_blueprint_options`, `catalog.measurement_units`, `catalog.category_attribute_assignments`,
  `catalog.category_attribute_exclusions`.

- [ ] **Step 1: Write the failing test**

```csharp
public sealed class CanonicalKeyTests
{
    [Theory]
    [InlineData("  Café  Crème ", "cafe-creme")]
    [InlineData("USB-C / Thunderbolt 4", "usb-c-thunderbolt-4")]
    [InlineData("Kitchen & Dining", "kitchen-dining")]
    [InlineData("ÉCRAN_LCD", "ecran-lcd")]
    [InlineData("لوازم خانگی", "لوازم-خانگی")]
    public void From_Normalizes(string input, string expected) =>
        CanonicalKey.From(input).Should().Be(expected);

    [Theory]
    [InlineData(" -/ ")]
    [InlineData("")]
    public void From_Rejects_Keys_With_No_Letters_Or_Digits(string input) =>
        FluentActions.Invoking(() => CanonicalKey.From(input)).Should().Throw<DomainException>()
            .Which.Code.Should().Be("CANONICAL_KEY_EMPTY");

    [Fact]
    public void From_Rejects_Over_120_Chars() =>
        FluentActions.Invoking(() => CanonicalKey.From(new string('a', 121))).Should().Throw<DomainException>()
            .Which.Code.Should().Be("CANONICAL_KEY_TOO_LONG");
}
```

- [ ] **Step 2: Run** `... --filter-class "*.CanonicalKeyTests"`. Expected: FAIL (type missing).
- [ ] **Step 3: Implement** `CanonicalKey.From`: `Normalize(FormKD)` → drop `UnicodeCategory.NonSpacingMark` →
  `ToLowerInvariant()` → keep `char.IsLetterOrDigit`, map every other run to one `-` → trim `-` →
  `Normalize(FormC)`; empty → `CANONICAL_KEY_EMPTY`; length > 120 → `CANONICAL_KEY_TOO_LONG`.
- [ ] **Step 4: Rename tables.** Add the explicit `builder.ToTable("<snake>")` calls listed above and
  rewrite every `HasFilter("[Status] <> 2")` to `HasFilter("status <> 2")`. Run
  `dotnet ef migrations add CatalogSnakeTableNames -p src/Database -s src/Host/EwaysVNext.Host`
  (empty diff is acceptable if phase 0 already produced these names; then delete the empty migration).
- [ ] **Step 5: Run** the Step 2 command and `dotnet build EwaysVNext.slnx`. Expected: PASS.
- [ ] **Step 6: Commit** `git commit -m "feat(catalog): canonical key normalizer and snake_case catalog tables" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 2: Category canonical key and aliases

**Files:**
- Modify: `src/Modules/Catalog/Application/Categories/Domain/Category.cs`,
  `src/Modules/Catalog/Application/Categories/Services/CategoryService.cs`,
  `src/Modules/Catalog/Application/Categories/Ports/Inbound/ICategoryService.cs`,
  `src/Modules/Catalog/Application/Categories/Ports/Outbound/ICategoryRepository.cs`,
  `src/Modules/Catalog/Adapter/Outbound/Persistence/Categories/CategoryRepository.cs`,
  `src/Database/Catalog/Configurations/Categories/CategoryConfiguration.cs`
- Test: `tests/UnitTests/Catalog/CategoryCanonicalKeyTests.cs`, `tests/IntegrationTests/Catalog/CatalogConstraintTests.cs`

**Interfaces:**
- Produces on `Category`: `string CanonicalKey { get; }` (derived from `Name` in `Create`/`Update`),
  `IReadOnlyList<string> Aliases { get; }`, `void SetAliases(IEnumerable<string> aliases, DateTimeOffset now)` (each alias stored as `CanonicalKey.From(alias)`, de-duplicated, own key removed, max 20 → `CATEGORY_ALIASES_TOO_MANY`).
- Produces: `Task<Result> SetAliasesAsync(long categoryId, IReadOnlyList<string> aliases, CancellationToken ct = default)` on `ICategoryService`.
- Produces on `ICategoryRepository`: `Task<Category?> FindByCanonicalPathAsync(IReadOnlyList<string> keyPath, CancellationToken ct = default)` (each segment matches `canonical_key` **or** an alias of a child of the previous match; deleted categories never match).
- Produces DB: column `canonical_key varchar(120) not null`, `aliases text[] not null default '{}'`,
  `UNIQUE (parent_id, canonical_key) NULLS NOT DISTINCT WHERE status <> 2` named `ux_categories_parent_canonical_key`
  (replaces `IX_Category_ParentId_NormalizedName`), `GIN (aliases)`.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public void Create_Derives_CanonicalKey_From_Name()
{
    var c = Category.Create(1, "Kitchen & Dining", "kitchen-dining", null, null, 0, T0);
    c.CanonicalKey.Should().Be("kitchen-dining");
    c.Update("Kitchen and Dining", "kitchen-dining", null, 0, T1);
    c.CanonicalKey.Should().Be("kitchen-and-dining");
}

[Fact]
public void SetAliases_Canonicalizes_Dedupes_And_Drops_Own_Key()
{
    var c = Category.Create(1, "Kettles", "kettles", null, null, 0, T0);
    c.SetAliases(["Electric Kettles", "electric-kettles", "KETTLES"], T1);
    c.Aliases.Should().Equal("electric-kettles");
}
```

Integration (`CatalogConstraintTests`, `[Collection(PostgresSuite.Name)]`):

```csharp
[Fact]
public async Task Root_Categories_With_Same_CanonicalKey_Are_Rejected()
{
    // two root categories "Garden Tools" / "garden  tools" inserted directly through EwaysVNextDbContext
    var ex = await act.Should().ThrowAsync<DbUpdateException>();
    ex.Which.InnerException.Should().BeOfType<PostgresException>()
        .Which.SqlState.Should().Be("23505");
}
```

- [ ] **Step 2: Run** both classes. Expected: FAIL.
- [ ] **Step 3: Implement** the properties, `SetAliases`, the service method (one save; audit
  `"category.aliases.set"`), the repository lookup, the configuration
  (`.HasIndex(c => new { c.ParentId, c.CanonicalKey }).IsUnique().AreNullsDistinct(false).HasFilter("status <> 2")`),
  and the service's pre-check that maps a sibling key clash to `CATEGORY_NAME_DUPLICATE` (409).
- [ ] **Step 4: Migration** `CatalogCanonicalKeys`.
- [ ] **Step 5: Run** Step 2 commands plus `--filter-class "*.CategoryServiceTests"`. Expected: PASS.
- [ ] **Step 6: Commit** `git commit -m "feat(catalog): category canonical keys and aliases" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 3: Attribute blueprint canonical key, scope, aliases, Option datatype, unit

**Files:**
- Modify: `src/Modules/Catalog/Application/AttributeBlueprints/Domain/AttributeBlueprint.cs`,
  `.../Ports/Inbound/IAttributeBlueprintService.cs`, `.../Services/AttributeBlueprintService.cs`,
  `.../Validation/AttributeBlueprintValidators.cs`, `src/Database/Catalog/Configurations/AttributeBlueprintConfiguration.cs`,
  `EwaysVNext.Modules.Catalog.Contracts/AttributeBlueprints/AttributeBlueprintSnapshot.cs`,
  `src/Modules/Catalog/Adapter/Inbound/GraphQL/AttributeBlueprints/{AttributeBlueprintInputs,AttributeBlueprintTypes}.cs`
- Test: `tests/UnitTests/Catalog/AttributeBlueprintCanonicalTests.cs`

**Interfaces:**
- Produces: `AttributeBlueprintDataType.Option = 5` (existing values unchanged).
- Produces: `public enum AttributeScope { Global = 1, Category = 2 }`; on the aggregate
  `string CanonicalKey`, `AttributeScope Scope`, `long? ScopeCategoryId`, `IReadOnlyList<string> Aliases`,
  `long? UnitId`; `void SetScope(AttributeScope scope, long? categoryId, DateTimeOffset now)`
  (`Category` requires a positive id, `Global` requires null → `ATTRIBUTE_SCOPE_INVALID`),
  `void SetAliases(IEnumerable<string> aliases, DateTimeOffset now)`, `void SetUnit(long? unitId, DateTimeOffset now)`.
- Produces DB: `UNIQUE (scope_category_id, canonical_key) NULLS NOT DISTINCT WHERE deleted_at IS NULL`
  (`ux_attribute_blueprints_scope_key`) — the spec's `UNIQUE(scope, canonical_key)` made exact (a
  category-scoped key is unique per category; `NULL` = global).

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public void Create_Is_Global_With_CanonicalKey()
{
    var a = AttributeBlueprint.Create(1, "Screen Size", AttributeBlueprintDataType.Number, T0);
    a.CanonicalKey.Should().Be("screen-size");
    a.Scope.Should().Be(AttributeScope.Global);
    a.ScopeCategoryId.Should().BeNull();
}

[Fact]
public void Category_Scope_Requires_Category_Id() =>
    FluentActions.Invoking(() => AttributeBlueprint.Create(1, "Colour", AttributeBlueprintDataType.Option, T0)
        .SetScope(AttributeScope.Category, null, T1))
        .Should().Throw<DomainException>().Which.Code.Should().Be("ATTRIBUTE_SCOPE_INVALID");
```

- [ ] **Step 2: Run** `--filter-class "*.AttributeBlueprintCanonicalTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** domain members, service methods `SetScopeAsync`, `SetAliasesAsync`, `SetUnitAsync`
  (unit must exist via `IMeasurementUnitRepository`, else `MEASUREMENT_UNIT_NOT_FOUND`), snapshot fields,
  GraphQL input/type fields, configuration (aliases `text[]`, scope as int).
- [ ] **Step 4: Migration** `CatalogAttributeCanonicalKeys`.
- [ ] **Step 5: Run** Step 2 plus `--filter-class "*.AttributeBlueprintServiceTests"`. Expected: PASS.
- [ ] **Step 6: Commit** `git commit -m "feat(catalog): attribute canonical key, scope, aliases, option type" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 4: Brand aggregate and alias table

**Files:**
- Create: `src/Modules/Catalog/Application/Brands/Domain/{Brand.cs, BrandAlias.cs}`,
  `src/Modules/Catalog/Application/Brands/Ports/Outbound/IBrandRepository.cs`,
  `src/Modules/Catalog/Adapter/Outbound/Persistence/Brands/BrandRepository.cs`,
  `src/Database/Catalog/Configurations/BrandConfiguration.cs`
- Modify: `src/Database/EwaysVNextDbContext.cs` (`DbSet<Brand> Brands`)
- Test: `tests/UnitTests/Catalog/BrandTests.cs`, `tests/IntegrationTests/Catalog/CatalogConstraintTests.cs`

**Interfaces:**
- Produces: `public sealed class Brand : AggregateRoot<long>` with `CanonicalName`, `DisplayName`, `Provenance` (`"admin"` | `"import"` | `"seed"`), `IsActive`, `IReadOnlyCollection<BrandAlias> Aliases`, `CreatedAt`, `UpdatedAt`;
  `static Brand Create(long id, string displayName, string provenance, DateTimeOffset now)`;
  `void Rename(string displayName, DateTimeOffset now)`; `void AddAlias(long aliasId, string alias, DateTimeOffset now)` (no-op if already present or equal to `CanonicalName`);
  `void RemoveAlias(string alias, DateTimeOffset now)`; `void SetActive(bool active, DateTimeOffset now)`.
- Produces: `BrandAlias(long Id, long BrandId, string Alias)` child, table `catalog.brand_aliases`, `UNIQUE(alias)`.
- Produces: `IBrandRepository { Task<Brand?> GetByIdAsync(long id); Task<Brand?> FindByNameOrAliasAsync(string anyName); Task AddAsync(Brand brand); Task<bool> ExistsAsync(long id); }` (all with `ct`).
- Produces DB: `catalog.brands` with `UNIQUE(canonical_name)`, `xmin` token, `ValueGeneratedNever()`.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public void Create_Normalizes_CanonicalName_And_Keeps_DisplayName()
{
    var b = Brand.Create(1, "  Northwind Labs ", "admin", T0);
    b.DisplayName.Should().Be("Northwind Labs");
    b.CanonicalName.Should().Be("northwind-labs");
    b.IsActive.Should().BeTrue();
}

[Fact]
public void AddAlias_Stores_Canonical_Form_Once()
{
    var b = Brand.Create(1, "Northwind", "admin", T0);
    b.AddAlias(2, "North Wind", T1);
    b.AddAlias(3, "north-wind", T1);
    b.AddAlias(4, "NORTHWIND", T1);
    b.Aliases.Select(a => a.Alias).Should().Equal("north-wind");
}
```

Integration: `Brand_Alias_Is_Globally_Unique` (two brands with alias `"acme"` → `23505`) and
`Brand_CanonicalName_Is_Unique`.

- [ ] **Step 2: Run** both classes. Expected: FAIL.
- [ ] **Step 3: Implement** aggregate, repository (`FindByNameOrAliasAsync` canonicalizes the input
  and matches `canonical_name` or `brand_aliases.alias`), configuration, DbSet.
- [ ] **Step 4: Migration** `CatalogBrands`.
- [ ] **Step 5: Run** Step 2. Expected: PASS.
- [ ] **Step 6: Commit** `git commit -m "feat(catalog): brand aggregate with unique aliases" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 5: Product identifiers (GS1 check digits, MPN, source ids)

**Files:**
- Create: `src/Modules/Catalog/Application/Products/Domain/{ProductIdentifier.cs, ProductIdentifierType.cs, Gs1.cs}`
- Test: `tests/UnitTests/Catalog/ProductIdentifierTests.cs`

**Interfaces:**
- Produces: `public enum ProductIdentifierType { Gtin, Upc, Ean, Mpn, SourceId }`.
- Produces: `public sealed class ProductIdentifier : Entity<long>` with `ProductId`, `Type`, `Value`, `SourceDomain`;
  `internal static ProductIdentifier Create(long id, long productId, ProductIdentifierType type, string value, string? sourceDomain)`;
  `public static string Normalize(ProductIdentifierType type, string value, string? sourceDomain)` (validation without an entity, used by matching).
- Produces: `internal static class Gs1 { public static bool HasValidCheckDigit(string digits); }`.

- [ ] **Step 1: Write failing tests**

```csharp
[Theory]
[InlineData(ProductIdentifierType.Gtin, "4006381333931", "4006381333931")]
[InlineData(ProductIdentifierType.Ean, "4006-3813 33931", "4006381333931")]
[InlineData(ProductIdentifierType.Ean, "73513537", "73513537")]
[InlineData(ProductIdentifierType.Upc, "036000291452", "036000291452")]
[InlineData(ProductIdentifierType.Mpn, " ab-12/x ", "AB-12/X")]
public void Normalize_Accepts_Valid(ProductIdentifierType type, string raw, string expected) =>
    ProductIdentifier.Normalize(type, raw, null).Should().Be(expected);

[Theory]
[InlineData(ProductIdentifierType.Gtin, "4006381333932", "IDENTIFIER_CHECK_DIGIT_INVALID")]
[InlineData(ProductIdentifierType.Upc, "4006381333931", "IDENTIFIER_LENGTH_INVALID")]
[InlineData(ProductIdentifierType.Ean, "40063813339A1", "IDENTIFIER_FORMAT_INVALID")]
public void Normalize_Rejects_Invalid(ProductIdentifierType type, string raw, string code) =>
    FluentActions.Invoking(() => ProductIdentifier.Normalize(type, raw, null))
        .Should().Throw<DomainException>().Which.Code.Should().Be(code);

[Fact]
public void SourceId_Requires_Domain() =>
    FluentActions.Invoking(() => ProductIdentifier.Normalize(ProductIdentifierType.SourceId, "123", null))
        .Should().Throw<DomainException>().Which.Code.Should().Be("IDENTIFIER_SOURCE_DOMAIN_REQUIRED");
```

- [ ] **Step 2: Run** `--filter-class "*.ProductIdentifierTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.** Lengths: GTIN 8/12/13/14, UPC 12, EAN 8/13; strip spaces and `-`; digits only;
  `Gs1.HasValidCheckDigit` = standard mod-10 (weights 3,1 from the right, excluding the check digit).
  MPN: trim, upper-invariant, 1–64 chars. SourceId: value 1–128 chars, domain lower-cased host.
- [ ] **Step 4: Run** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(catalog): product identifiers with GS1 validation" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 6: Attribute signature

**Files:**
- Create: `src/Modules/Catalog/Application/Products/Domain/AttributeSignature.cs`
- Test: `tests/UnitTests/Catalog/AttributeSignatureTests.cs`

**Interfaces:**
- Produces: `public sealed record AttributeSignature(string Text, string Hash, IReadOnlyDictionary<string, string> Values) { public static AttributeSignature From(IReadOnlyDictionary<string, string> attributes); public static readonly AttributeSignature Empty; }`
  — `Values` keyed by canonical key, values trimmed display text (used for `attribute_values` jsonb).
- Produces (cross-phase, used by phase 7 Sourcing to compute the identical hash): `public static string Hash(IReadOnlyDictionary<string, string> attributes) => From(attributes).Hash;` on the same public record in `EwaysVNext.Modules.Catalog.Application.Products.Domain`.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public void From_Sorts_Canonical_Keys_And_Lowercases_Values()
{
    var sig = AttributeSignature.From(new Dictionary<string, string> { ["Size"] = " XL ", ["Colour Name"] = "Deep  Red" });
    sig.Text.Should().Be("colour-name=deep red;size=xl");
    sig.Hash.Should().Be(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("colour-name=deep red;size=xl"))));
    sig.Values.Should().BeEquivalentTo(new Dictionary<string, string> { ["colour-name"] = "Deep Red", ["size"] = "XL" });
}

[Fact]
public void Empty_Has_Sha256_Of_Empty_String() =>
    AttributeSignature.From(new Dictionary<string, string>()).Hash
        .Should().Be("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");

[Fact]
public void Separators_In_Values_Are_Escaped() =>
    AttributeSignature.From(new Dictionary<string, string> { ["a"] = "x=y;z%" }).Text.Should().Be("a=x%3dy%3bz%25");

[Fact]
public void Keys_Colliding_After_Canonicalization_Are_Rejected() =>
    FluentActions.Invoking(() => AttributeSignature.From(new Dictionary<string, string> { ["Size"] = "M", ["size"] = "L" }))
        .Should().Throw<DomainException>().Which.Code.Should().Be("VARIANT_ATTRIBUTE_DUPLICATE_KEY");
```

- [ ] **Step 2: Run** `--filter-class "*.AttributeSignatureTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.** Key = `CanonicalKey.From(key)`. Display value = trim + collapse whitespace runs
  to one space + NFC (blank → `VARIANT_ATTRIBUTE_VALUE_EMPTY`). Signature value = display value
  `ToLowerInvariant()`, then escape `%`→`%25`, `=`→`%3d`, `;`→`%3b` (in that order). Join sorted
  (ordinal) `key=value` with `;`. Hash = lower-hex SHA-256 of UTF-8 text.
- [ ] **Step 4: Run** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(catalog): canonical variant attribute signature" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 7: Product aggregate rewrite (variants, media, status, DeactivatedByAdmin)

**Files:**
- Rewrite: `src/Modules/Catalog/Application/Products/Domain/{Product.cs, ProductEvents.cs}`
- Create: `src/Modules/Catalog/Application/Products/Domain/{ProductStatus.cs, Variant.cs, ProductMedia.cs, MediaUsageRights.cs}`
- Delete: `ProductMessages.cs`, `Ports/Inbound/{IProductService.cs, ReservationSnapshot.cs}`, `Services/ProductService.cs`,
  `Validation/{ChangePriceValidator,CreateProductValidator,ReserveStockValidator}.cs`,
  `Adapter/Inbound/Rest/{Dtos,Endpoints,Products}/*`, `QueryApplication/Products/{IProductQueryService,ProductQueryService}.cs`,
  `EwaysVNext.Modules.Catalog.Contracts/Products/*`, `Categories/Ports/Outbound/ICatalogProductLookup.cs` keeps its
  signature (re-implemented in Task 8), and every test that only covered the deleted code
  (`tests/UnitTests/CategoryProductionCompositionTests.cs` product parts, `SnowflakeIdJsonTests` product-DTO cases).
- Test: `tests/UnitTests/Catalog/ProductLifecycleTests.cs`

**Interfaces:**
- Produces: `public enum ProductStatus { Draft, Active, Deactivated }`; `public enum MediaUsageRights { Allowed, RightsUnknown, Denied }`.
- Produces `Product : AggregateRoot<long>`: `BrandId`, `PrimaryCategoryId`, `Title`, `Slug`, `ShortDescription`, `Description`,
  `Status`, `DeactivatedByAdmin`, `Mpn` (denormalized), `MergedIntoProductId`, `IReadOnlyList<long> SourceRefs`,
  `Variants`, `Media`, `Identifiers`, `CreatedAt`, `UpdatedAt`.
  - `static Product CreateDraft(long id, long brandId, long primaryCategoryId, string title, string? slug, string? shortDescription, string? description, DateTimeOffset now)` (slug null → `$"{CanonicalKey.From(title)}-{id}"`; title 1–200; short ≤ 500; description ≤ 20 000).
  - `void UpdateDetails(long brandId, long primaryCategoryId, string title, string? shortDescription, string? description, DateTimeOffset now)`
  - `void AddIdentifier(long identifierId, ProductIdentifierType type, string value, string? sourceDomain, DateTimeOffset now)` (idempotent on equal normalized value; a second, different MPN → `PRODUCT_MPN_ALREADY_SET`; MPN also sets `Mpn`)
  - `void RemoveIdentifier(long identifierId, DateTimeOffset now)`; `void AddSourceRef(long sourceListingId, DateTimeOffset now)`
  - `Variant AddVariant(long variantId, string sku, IReadOnlyDictionary<string, string> attributes, DateTimeOffset now)`
  - `bool SetVariantActive(long variantId, bool active, bool byAdmin, DateTimeOffset now)`
  - `ProductMedia AddMedia(long mediaId, string objectKey, int sortOrder, string? alt, string? sourceUrl, MediaUsageRights rights, DateTimeOffset now)`; `ProductMedia RemoveMedia(long mediaId, DateTimeOffset now)`
  - `bool Activate(bool byAdmin, DateTimeOffset now)`; `void Deactivate(bool byAdmin, DateTimeOffset now)`; `void MarkMergedInto(long survivorId, DateTimeOffset now)`
  - `public IReadOnlyList<string> ActivationProblems()` (empty = activatable).
- Produces `Variant : Entity<long>`: `ProductId`, `Sku` (existing `Sku` value object), `AttributeSignature` (text),
  `AttributeSignatureHash`, `AttributeValues` (`IReadOnlyDictionary<string,string>`), `IsActive`, `DeactivatedByAdmin`, `CreatedAt`, `UpdatedAt`.
- Produces events: `ProductActivatedDomainEvent(long ProductId)`, `ProductDeactivatedDomainEvent(long ProductId, bool ByAdmin)`, `VariantActivationChangedDomainEvent(long ProductId, long VariantId, bool IsActive)`.

- [ ] **Step 1: Write failing tests** (one `[Fact]` per block)

```csharp
[Fact]
public void Activation_Requires_Active_Variant_Brand_Category_Title_And_Usable_Image()
{
    var p = Product.CreateDraft(1, 10, 20, "Demo Kettle", null, null, null, T0);
    p.Slug.Should().Be("demo-kettle-1");
    p.Status.Should().Be(ProductStatus.Draft);
    p.ActivationProblems().Should().BeEquivalentTo("NO_ACTIVE_VARIANT", "NO_USABLE_IMAGE");
    p.AddVariant(2, "SKU-0002", new Dictionary<string, string>(), T0);
    p.AddMedia(3, "products/1/3.png", 0, "kettle", null, MediaUsageRights.RightsUnknown, T0);
    p.Invoking(x => x.Activate(byAdmin: true, T0)).Should().Throw<DomainException>()
        .Which.Code.Should().Be("PRODUCT_ACTIVATION_INVALID");
    p.AddMedia(4, "products/1/4.png", 1, "kettle", null, MediaUsageRights.Allowed, T0);
    p.Activate(byAdmin: true, T0).Should().BeTrue();
    p.Status.Should().Be(ProductStatus.Active);
}

[Fact]
public void Import_Activation_Never_Clears_Admin_Deactivation()
{
    var p = ActiveProduct();               // helper: draft + variant + allowed image + Activate(true)
    p.Deactivate(byAdmin: true, T1);
    p.Activate(byAdmin: false, T2).Should().BeFalse();
    p.Status.Should().Be(ProductStatus.Deactivated);
    p.DeactivatedByAdmin.Should().BeTrue();
    p.Activate(byAdmin: true, T2).Should().BeTrue();
    p.DeactivatedByAdmin.Should().BeFalse();
}

[Fact]
public void Import_Variant_Activation_Never_Clears_Admin_Deactivation()
{
    var p = ActiveProductWithVariants(("size", "M"), ("size", "L"));   // variant ids 2, 3
    p.SetVariantActive(2, false, byAdmin: true, T1).Should().BeTrue();
    p.SetVariantActive(2, true, byAdmin: false, T2).Should().BeFalse();
    p.Variants.Single(v => v.Id == 2).IsActive.Should().BeFalse();
}

[Fact]
public void Deactivating_Last_Active_Variant_Deactivates_The_Product_With_Same_Origin()
{
    var p = ActiveProduct();
    p.SetVariantActive(2, false, byAdmin: false, T1);
    p.Status.Should().Be(ProductStatus.Deactivated);
    p.DeactivatedByAdmin.Should().BeFalse();
}

[Fact]
public void Empty_Signature_Variant_Must_Be_The_Only_Variant()
{
    var p = Product.CreateDraft(1, 10, 20, "Mug", null, null, null, T0);
    p.AddVariant(2, "SKU-A1", new Dictionary<string, string>(), T0);
    p.Invoking(x => x.AddVariant(3, "SKU-B1", new Dictionary<string, string> { ["size"] = "M" }, T0))
        .Should().Throw<DomainException>().Which.Code.Should().Be("VARIANT_SIGNATURE_CONFLICT");
}

[Fact]
public void Duplicate_Signature_Is_Rejected()
{
    var p = Product.CreateDraft(1, 10, 20, "Tee", null, null, null, T0);
    p.AddVariant(2, "SKU-A1", new Dictionary<string, string> { ["Size"] = "M" }, T0);
    p.Invoking(x => x.AddVariant(3, "SKU-A2", new Dictionary<string, string> { ["size"] = " m " }, T0))
        .Should().Throw<DomainException>().Which.Code.Should().Be("VARIANT_SIGNATURE_DUPLICATE");
}

[Fact]
public void Merged_Product_Is_Admin_Deactivated_And_Points_To_Survivor()
{
    var p = ActiveProduct();
    p.MarkMergedInto(99, T1);
    p.Status.Should().Be(ProductStatus.Deactivated);
    p.DeactivatedByAdmin.Should().BeTrue();
    p.MergedIntoProductId.Should().Be(99);
    p.Invoking(x => x.MarkMergedInto(1, T1)).Should().Throw<DomainException>().Which.Code.Should().Be("PRODUCT_MERGE_INVALID");
}
```

- [ ] **Step 2: Run** `--filter-class "*.ProductLifecycleTests"`. Expected: FAIL (compile).
- [ ] **Step 3: Implement** the aggregate. Rules not visible from the tests:
  - `ActivationProblems()` codes, in order: `NO_ACTIVE_VARIANT`, `NO_BRAND`, `NO_PRIMARY_CATEGORY`, `NO_TITLE`, `NO_USABLE_IMAGE`; `Activate` throws `PRODUCT_ACTIVATION_INVALID` with the codes joined by `,` in the message.
  - `Activate(byAdmin:false)` on a `DeactivatedByAdmin` product returns `false` and changes nothing; on an already `Active` product returns `true` and changes nothing.
  - `SetVariantActive(active:true, byAdmin:false)` on a `DeactivatedByAdmin` variant returns `false`; `byAdmin:true` sets/clears the variant's `DeactivatedByAdmin` like the product.
  - `MarkMergedInto(id)`: `id == Id` or `MergedIntoProductId` already set → `PRODUCT_MERGE_INVALID`.
  - Same SKU twice in one product → `VARIANT_SKU_DUPLICATE`; unknown variant/media id → `VARIANT_NOT_FOUND`/`PRODUCT_MEDIA_NOT_FOUND`.
- [ ] **Step 4: Delete** the files listed above and fix compile errors (Catalog `ModuleInstaller` drops `AddProductApplication()` registrations of deleted services; `ProductEndpoints` removed).
- [ ] **Step 5: Run** Step 2 and `dotnet build EwaysVNext.slnx`. Expected: PASS.
- [ ] **Step 6: Commit** `git commit -m "feat(catalog): rewrite product aggregate with variants, media and admin deactivation" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 8: Product persistence and database constraints

**Files:**
- Rewrite: `src/Database/Catalog/Configurations/Products/ProductConfiguration.cs`, `src/Modules/Catalog/Application/Products/Ports/Outbound/IProductRepository.cs`, `src/Modules/Catalog/Adapter/Outbound/Persistence/Products/ProductRepository.cs`, `src/Modules/Catalog/Adapter/Outbound/Persistence/Categories/CatalogProductLookup.cs`
- Create: `src/Database/Catalog/Configurations/Products/{VariantConfiguration.cs, ProductMediaConfiguration.cs, ProductIdentifierConfiguration.cs}`
- Test: `tests/IntegrationTests/Catalog/CatalogConstraintTests.cs`

**Interfaces:**
- Produces: `IProductRepository { Task<Product?> GetByIdAsync(long id); Task<IReadOnlyList<Product>> GetByIdsAsync(IReadOnlyCollection<long> ids); Task<Product?> GetBySlugAsync(string slug); Task<bool> SkuExistsAsync(string sku); Task<Product?> FindByIdentifierAsync(ProductIdentifierType type, string normalizedValue, string? sourceDomain); Task<Product?> FindByBrandAndMpnAsync(long brandId, string mpn); Task<Product?> FindByVariantIdAsync(long variantId); Task AddAsync(Product product); }` (all `ct`; loads include variants, media, identifiers).
- Produces tables and constraints:
  - `catalog.products`: `slug varchar(240) UNIQUE`, `status text`, `source_refs bigint[]`, `UNIQUE(brand_id, mpn) WHERE mpn IS NOT NULL`, FK to brands/categories (`Restrict`), `xmin`.
  - `catalog.product_identifiers`: `UNIQUE(type, value) WHERE type IN ('Gtin','Upc','Ean')`, `UNIQUE(source_domain, value) WHERE type = 'SourceId'`.
  - `catalog.variants`: `sku varchar(32) UNIQUE`, `attribute_signature text`, `attribute_signature_hash char(64)`, `attribute_values jsonb`, `UNIQUE(product_id, attribute_signature_hash)`.
  - `catalog.product_media`: `object_key varchar(512)`, `usage_rights text`, index `(product_id, sort_order)`.
  - `catalog.products` check: `CHECK (status IN ('Draft','Active','Deactivated'))`.

- [ ] **Step 1: Write failing integration tests** in `CatalogConstraintTests`:
  `Duplicate_Gtin_Across_Products_Is_Rejected`, `Duplicate_Brand_Mpn_Is_Rejected`,
  `Duplicate_Source_Domain_And_Id_Is_Rejected`, `Duplicate_Variant_Signature_In_Product_Is_Rejected_By_Database`
  (bypasses the aggregate by inserting a second `variants` row with raw SQL copying the hash),
  `Duplicate_Sku_Across_Products_Is_Rejected`, `Same_Gtin_Value_As_Mpn_Is_Allowed`. Each asserts
  `PostgresException.SqlState == "23505"` (the last asserts success), and
  `Product_Roundtrips_Attribute_Values_As_Jsonb` asserts `attribute_values->>'size' = 'XL'` via raw SQL.
- [ ] **Step 2: Run** `--filter-class "*.CatalogConstraintTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** configurations: `AttributeValues` via `HasConversion` to/from JSON
  (`System.Text.Json`) + `HasColumnType("jsonb")` + a dictionary `ValueComparer`; `SourceRefs` as
  `bigint[]`; owned collections as separate entities with `ValueGeneratedNever()` ids;
  `Navigation(...).UsePropertyAccessMode(PropertyAccessMode.Field)`; DbSet `Products`; name every
  unique index as listed (`ux_products_slug`, `ux_products_brand_mpn`, `ux_product_identifiers_type_value`,
  `ux_product_identifiers_source`, `ux_variants_sku`, `ux_variants_product_signature`). Rewrite the
  repository and `CatalogProductLookup` (`ExistsAndIsActiveAsync` = status `Active`).
- [ ] **Step 4: Migration** `CatalogProductsRewrite` (drops the old product table columns: phase 0 already removed reservations).
- [ ] **Step 5: Run** Step 2. Expected: PASS.
- [ ] **Step 6: Commit** `git commit -m "feat(catalog): product persistence with dedup unique constraints" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 9: Change notification — sellability version and storefront refresh trigger

**Files:**
- Create: `src/Modules/Catalog/Application/Storefront/Ports/Inbound/IStorefrontRefreshTrigger.cs`,
  `src/Modules/Catalog/Application/Products/Ports/Outbound/ICatalogChangeNotifier.cs`,
  `src/Modules/Catalog/Adapter/Outbound/Storefront/{StorefrontRefreshTrigger.cs, CatalogChangeNotifier.cs}`
- Modify: `src/Modules/Catalog/Adapter/Outbound/DependencyInjection.cs`
- Test: `tests/UnitTests/Catalog/StorefrontRefreshTriggerTests.cs`, `tests/IntegrationTests/Catalog/SellableVariantPortTests.cs`

**Interfaces:**
- Produces (master contract): `public interface IStorefrontRefreshTrigger { Task RequestRefreshAsync(CancellationToken ct = default); }`
  → `IJobQueue.EnqueueAsync(JobTypes.RefreshStorefrontViews, new RefreshStorefrontViewsPayload(), runAt: clock.UtcNow.AddSeconds(5), dedupeKey: "storefront")`.
- Produces: `public sealed record RefreshStorefrontViewsPayload();` in `Catalog.Application/Storefront/Ports/Inbound`.
- Produces: `ICatalogChangeNotifier { Task SellabilityChangedAsync(CancellationToken ct = default); Task DisplayChangedAsync(CancellationToken ct = default); Task<long> CurrentVersionAsync(CancellationToken ct = default); }`
  — `SellabilityChangedAsync` = `SELECT nextval('catalog.sellability_version')` then `RequestRefreshAsync`;
  `DisplayChangedAsync` = `RequestRefreshAsync` only; `CurrentVersionAsync` = `SELECT last_value FROM catalog.sellability_version`.
  Called **after** the operation's `SaveChangesAsync` succeeds; a failure is logged at Warning and swallowed
  (the nightly refresh is the backstop; purchasing never reads views).
- Produces DB: `CREATE SEQUENCE catalog.sellability_version` (migration `CatalogSellabilityVersion`).

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public async Task RequestRefresh_Enqueues_Deduplicated_Job_Five_Seconds_Out()
{
    var queue = Substitute.For<IJobQueue>();
    var clock = new FixedClock(T0);
    await new StorefrontRefreshTrigger(queue, clock).RequestRefreshAsync();
    await queue.Received(1).EnqueueAsync(JobTypes.RefreshStorefrontViews, Arg.Any<RefreshStorefrontViewsPayload>(),
        T0.AddSeconds(5), "storefront", Arg.Any<CancellationToken>());
}
```

Integration: `Sellability_Version_Increases_After_Notification` (resolve `ICatalogChangeNotifier`, read
version, call `SellabilityChangedAsync`, assert strictly greater).

- [ ] **Step 2: Run** both. Expected: FAIL.
- [ ] **Step 3: Implement**, register both as scoped, add the migration.
- [ ] **Step 4: Run** both. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(catalog): sellability version sequence and storefront refresh trigger" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 10: Brand admin service, REST and GraphQL

**Files:**
- Create: `src/Modules/Catalog/Application/Brands/{Ports/Inbound/IBrandAdminService.cs, Services/BrandAdminService.cs, DependencyInjection.cs}`,
  `src/Modules/Catalog/Adapter/Inbound/Rest/Admin/BrandAdminEndpoints.cs`, `src/Modules/Catalog/Adapter/Inbound/GraphQL/Brands/BrandAdminResolvers.cs`
- Test: `tests/UnitTests/Catalog/BrandAdminServiceTests.cs`, `tests/IntegrationTests/Catalog/CatalogAdminRestTests.cs`

**Interfaces:**
- Produces:
```csharp
public interface IBrandAdminService {
  Task<Result<long>> CreateAsync(string displayName, IReadOnlyList<string> aliases, AdminActor actor, CancellationToken ct = default);
  Task<Result> RenameAsync(long brandId, string displayName, AdminActor actor, CancellationToken ct = default);
  Task<Result> SetAliasesAsync(long brandId, IReadOnlyList<string> aliases, AdminActor actor, CancellationToken ct = default);
  Task<Result> SetActiveAsync(long brandId, bool active, AdminActor actor, CancellationToken ct = default);
  Task<Result<long>> EnsureAsync(string name, IReadOnlyList<string> aliases, string provenance, CancellationToken ct = default); // publishing/seed: find by name/alias or create; 23505 → reload
}
```
- Produces REST (policy `admin:staff`): `POST /api/v1/admin/catalog/brands`, `PUT /api/v1/admin/catalog/brands/{id}`,
  `PUT /api/v1/admin/catalog/brands/{id}/aliases`, `PUT /api/v1/admin/catalog/brands/{id}/status`, `GET /api/v1/admin/catalog/brands?q=&page=&pageSize=`.
- Produces GraphQL (`[Authorize(Policy = "admin:staff")]`): `createBrand`, `renameBrand`, `setBrandAliases`, `setBrandActive`, query `adminBrands`.
- Errors: `BRAND_NOT_FOUND` (404), `BRAND_NAME_DUPLICATE` (409, name or alias already used by another brand).

- [ ] **Step 1: Write failing tests:** service — `Create_Rejects_Name_Used_As_Another_Brands_Alias` (`BRAND_NAME_DUPLICATE`), `SetActive_Calls_SellabilityChanged_After_Save` (`Received.InOrder(unitOfWork.SaveChangesAsync, notifier.SellabilityChangedAsync)`), `Create_Records_Audit` (`auditTrail.Received(1).Record(Arg.Any<AuditActor>(), "brand.created", "Brand", Arg.Any<string>(), null, Arg.Any<object>())`); REST — `Staff_Creates_Brand_Returns_201`, `Anonymous_Create_Brand_Returns_401`.
- [ ] **Step 2: Run** both classes. Expected: FAIL.
- [ ] **Step 3: Implement** service, endpoints (`EndpointResult`/`ToCreatedHttpResult`), resolvers; register `AddBrandApplication()` in `ModuleInstaller`. Create `src/Modules/Catalog/QueryApplication/Products/{ICatalogAdminQueries.cs, CatalogAdminQueries.cs}` now with only `ListBrandsAsync` (Task 14 adds the product methods) and serve the GET and `adminBrands` from it.
- [ ] **Step 4: Run** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(catalog): brand admin service with REST and GraphQL" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 11: Catalog admin service — product CRUD, variants, identifiers, toggles

**Files:**
- Rewrite: `src/Modules/Catalog/Application/Products/Ports/Inbound/ICatalogAdminService.cs`, `src/Modules/Catalog/Application/Products/Services/CatalogAdminService.cs`, `src/Modules/Catalog/Application/Products/DependencyInjection.cs`
- Create: `src/Modules/Catalog/Application/Products/Ports/Inbound/CatalogAdminCommands.cs`
- Test: `tests/UnitTests/Catalog/CatalogAdminServiceTests.cs`

**Interfaces:**
- Produces:
```csharp
public sealed record CreateProductCommand(long BrandId, long PrimaryCategoryId, string Title, string? Slug, string? ShortDescription, string? Description);
public sealed record UpdateProductCommand(long ProductId, long BrandId, long PrimaryCategoryId, string Title, string? ShortDescription, string? Description);
public sealed record AddVariantCommand(long ProductId, string? Sku, IReadOnlyDictionary<string, string> Attributes);
public sealed record IdentifierInput(ProductIdentifierType Type, string Value, string? SourceDomain);
public interface ICatalogAdminService {
  Task<Result<long>> CreateProductAsync(CreateProductCommand command, AdminActor actor, CancellationToken ct = default);
  Task<Result> UpdateProductAsync(UpdateProductCommand command, AdminActor actor, CancellationToken ct = default);
  Task<Result> SetSecondaryCategoriesAsync(long productId, IReadOnlyList<long> categoryIds, AdminActor actor, CancellationToken ct = default);
  Task<Result<long>> AddVariantAsync(AddVariantCommand command, AdminActor actor, CancellationToken ct = default);
  Task<Result> AddIdentifierAsync(long productId, IdentifierInput identifier, AdminActor actor, CancellationToken ct = default);
  Task<Result> RemoveIdentifierAsync(long productId, long identifierId, AdminActor actor, CancellationToken ct = default);
  Task<Result> SetProductActiveAsync(long productId, bool active, AdminActor actor, CancellationToken ct = default);
  Task<Result> SetVariantActiveAsync(long productId, long variantId, bool active, AdminActor actor, CancellationToken ct = default);
  Task<Result<long>> UploadMediaAsync(UploadMediaCommand command, AdminActor actor, CancellationToken ct = default);   // Task 13
  Task<Result> RemoveMediaAsync(long productId, long mediaId, AdminActor actor, CancellationToken ct = default);         // Task 13
  Task<Result<BulkEditResult>> BulkEditAsync(BulkProductEdit edit, AdminActor actor, CancellationToken ct = default);  // Task 12
  Task<Result> MergeDuplicateAsync(long duplicateProductId, long survivorProductId, AdminActor actor, CancellationToken ct = default); // Task 12
}
```
- Consumes: `IProductRepository`, `IBrandRepository`, `ICategoryRepository`, `ICategoryProductRepository`, `IIdGenerator`, `IClock`, `IUnitOfWork`, `IAuditTrail`, `ICatalogChangeNotifier`.
- Errors: `PRODUCT_NOT_FOUND` 404, `BRAND_NOT_FOUND` 404, `CATEGORY_NOT_FOUND` 404, `VARIANT_SKU_DUPLICATE` 409 (pre-check `SkuExistsAsync`, and `23505` on `ux_variants_sku` mapped to the same), `PRODUCT_SLUG_DUPLICATE` 409, `PRODUCT_IDENTIFIER_DUPLICATE` 409 (unique violation on identifier indexes), domain codes passed through as 400/409 by `Error.SuggestHttpStatus`.
- SKU when `Sku` is null: `$"SKU-{variantId}"`.

- [ ] **Step 1: Write failing tests** (NSubstitute fakes, `FixedClock`):
  - `CreateProduct_Saves_Draft_And_Audits` — status Draft, one `SaveChangesAsync`, audit action `"product.created"`, and `notifier.DidNotReceiveWithAnyArgs().SellabilityChangedAsync()` (drafts are not sellable).
  - `CreateProduct_Rejects_Unknown_Brand_Before_Saving` → `BRAND_NOT_FOUND`, `DidNotReceive().SaveChangesAsync`.
  - `AddVariant_Rejects_Sku_Used_By_Another_Product` → `VARIANT_SKU_DUPLICATE`.
  - `SetProductActive_False_Marks_DeactivatedByAdmin_And_Bumps_Version_After_Save` (`Received.InOrder`).
  - `SetProductActive_True_Returns_Activation_Problems` → error code `PRODUCT_ACTIVATION_INVALID`, message contains `NO_USABLE_IMAGE`.
  - `Unique_Violation_On_Save_Maps_To_Conflict` — `unitOfWork.TrySaveChangesAsync()` returns `SaveOutcome.UniqueViolation` (phase 0) for an identifier add → `PRODUCT_IDENTIFIER_DUPLICATE` (409); for a variant add → `VARIANT_SKU_DUPLICATE`.
- [ ] **Step 2: Run** `--filter-class "*.CatalogAdminServiceTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.** Pattern per method: validate ids → load → mutate in `try/catch (DomainException)` → `auditTrail.Record(...)` (before/after = the changed fields only) → one `SaveChangesAsync` → `notifier.SellabilityChangedAsync()` when status, variant activity, brand or category changed on a non-Draft product; otherwise `DisplayChangedAsync()`.
- [ ] **Step 4: Run** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(catalog): catalog admin service for products and variants" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 12: Bulk edit and duplicate merge

**Files:**
- Modify: `src/Modules/Catalog/Application/Products/Services/CatalogAdminService.cs`, `.../Ports/Inbound/CatalogAdminCommands.cs`
- Test: `tests/UnitTests/Catalog/CatalogBulkAndMergeTests.cs`

**Interfaces:**
- Produces:
```csharp
public enum BulkStatusChange { None, Activate, Deactivate }
public sealed record BulkProductEdit(IReadOnlyList<long> ProductIds, BulkStatusChange Status, long? PrimaryCategoryId, long? BrandId); // 1..500 ids
public sealed record BulkItemOutcome(long ProductId, bool Succeeded, string? ErrorCode);
public sealed record BulkEditResult(IReadOnlyList<BulkItemOutcome> Items);
```
- Merge semantics **[design call, rule 7]:** only the duplicate is written: `duplicate.MarkMergedInto(survivorId)`
  in one save. Identifiers stay on the duplicate (uniqueness intact); `ICatalogPublishingPort` follows
  `MergedIntoProductId` to the survivor (Task 17). The survivor is read only to check it exists, is not
  itself merged, and is not the duplicate.

- [ ] **Step 1: Write failing tests**
  - `BulkEdit_Saves_Successful_Items_Once_And_Reports_Failures` — three products, one fails activation: outcomes `[ok, ok, PRODUCT_ACTIVATION_INVALID]`, exactly one `SaveChangesAsync`, failed product unchanged (`Status == Draft`).
  - `BulkEdit_Rejects_More_Than_500_Ids` → `VALIDATION_FAILED`, no load.
  - `BulkEdit_Rejects_Empty_Edit` (no status, category or brand) → `VALIDATION_FAILED`.
  - `Merge_Deactivates_Duplicate_And_Leaves_Survivor_Untouched` — survivor substitute receives no mutation; duplicate `MergedIntoProductId == survivorId`, `DeactivatedByAdmin == true`; audit `"product.merged"`.
  - `Merge_Into_A_Merged_Survivor_Is_Rejected` → `PRODUCT_MERGE_INVALID`.
- [ ] **Step 2: Run** `--filter-class "*.CatalogBulkAndMergeTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** (bulk is a batch of independent aggregates — allowed by rule 7). Per item, check
  `ActivationProblems()` and ids **before** mutating so a failed item is never half-changed; catch
  `DomainException` per item into its outcome; then one save and one notifier call.
- [ ] **Step 4: Run** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(catalog): bulk product edit and duplicate merge" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 13: Media upload to MinIO

**Files:**
- Create: `src/Modules/Catalog/Application/Products/Ports/Outbound/ICatalogMediaStore.cs`, `src/Modules/Catalog/Adapter/Outbound/Media/ObjectStorageCatalogMediaStore.cs`
- Modify: `CatalogAdminService.cs`, `CatalogAdminCommands.cs`, `src/Modules/Catalog/Application/EwaysVNext.Modules.Catalog.Application.csproj` (reference `EwaysVNext.Core.Application` for `IFileTypeDetector`)
- Test: `tests/UnitTests/Catalog/CatalogMediaUploadTests.cs`

**Interfaces:**
- Produces: `public sealed record UploadMediaCommand(long ProductId, Stream Content, long Length, string? Alt, MediaUsageRights Rights, int? SortOrder);`
- Produces: `ICatalogMediaStore { Task PutAsync(string objectKey, Stream content, string contentType, CancellationToken ct = default); Task CopyFromStagingAsync(string stagingKey, string objectKey, CancellationToken ct = default); Task DeleteAsync(string objectKey, CancellationToken ct = default); }`
  → `IObjectStorage` with bucket `StorageBuckets.CatalogMedia` (copy source `StorageBuckets.ImportStaging`).
- Rules: max `Catalog:Media:MaxBytes` (default `10_485_760`) → `MEDIA_TOO_LARGE` (413); type sniffed with
  `IFileTypeDetector` on the first `HeaderLength` bytes, allowed `Jpeg|Png|Webp` → else `MEDIA_TYPE_UNSUPPORTED` (415).
  Key: `products/{productId}/{mediaId}.{jpg|png|webp}`. Order: validate → put object → mutate + save; if the
  save fails, delete the object (best effort, logged). `RemoveMediaAsync`: save first, then delete object (best effort).
  Sort order default = `max + 1`.

- [ ] **Step 1: Write failing tests:** `Upload_Png_Stores_Object_Then_Saves` (`Received.InOrder(store.PutAsync("products/1/<id>.png", ...), uow.SaveChangesAsync)`),
  `Upload_Rejects_Pdf_Without_Touching_Storage` (`MEDIA_TYPE_UNSUPPORTED`, `store.DidNotReceiveWithAnyArgs().PutAsync`),
  `Upload_Rejects_Over_Limit` (`Length = 10_485_761` → `MEDIA_TOO_LARGE`),
  `Failed_Save_Deletes_Uploaded_Object`. Use the PNG/PDF headers from `tests/UnitTests/FileSamples.cs`.
- [ ] **Step 2: Run** `--filter-class "*.CatalogMediaUploadTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** and register `ObjectStorageCatalogMediaStore` (scoped) in the outbound DI.
- [ ] **Step 4: Run** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(catalog): product media upload to object storage" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 14: Catalog admin queries and admin REST (products, categories, attributes)

**Files:**
- Modify: `src/Modules/Catalog/QueryApplication/Products/{ICatalogAdminQueries.cs, CatalogAdminQueries.cs}` (from Task 10)
- Create: `src/Modules/Catalog/QueryApplication/Products/CatalogAdminDtos.cs`,
  `src/Modules/Catalog/Adapter/Inbound/Rest/Admin/{CatalogAdminEndpoints.cs, CategoryAdminEndpoints.cs, AttributeAdminEndpoints.cs, CatalogAdminDtos.cs}`,
  `src/BuildingBlocks/Presentation.Endpoint/Money/MoneyText.cs`,
  `tests/IntegrationTests/Support/{InMemoryObjectStorage.cs, CatalogTestHost.cs}`
- Modify: `src/Modules/Catalog/QueryApplication/DependencyInjection.cs`
- Test: `tests/UnitTests/MoneyTextTests.cs`, `tests/IntegrationTests/Catalog/CatalogAdminRestTests.cs`

**Interfaces:**
- Produces: `public static class MoneyText { public static bool TryParseMinor(string? text, out long minor); public static string Format(long minor); }` — accepts only `^\d+\.\d{2}$` (no sign, no exponent, no whitespace), checked overflow → false.
- Produces: `ICatalogAdminQueries { Task<PaginatedQuery<ProductAdminRowDto>> ListProductsAsync(ProductAdminFilter filter, int page, int pageSize); Task<Result<ProductAdminDetailDto>> GetProductAsync(long productId); Task<PaginatedQuery<BrandAdminDto>> ListBrandsAsync(string? q, int page, int pageSize); }`
  with `ProductAdminFilter(string? Q, ProductStatus? Status, long? BrandId, long? CategoryId)`; detail includes variants (with `attributeValues`), identifiers, media (`objectKey`, `url` = `Storefront:MediaBaseUrl` + key), `deactivatedByAdmin`, `mergedIntoProductId`.
- Produces REST (all `RequireAuthorization("admin:staff")`, base `/api/v1/admin/catalog`):
  `GET /products`, `GET /products/{id}`, `POST /products`, `PUT /products/{id}`, `PUT /products/{id}/secondary-categories`,
  `POST /products/{id}/variants`, `PUT /products/{id}/variants/{variantId}/status`, `POST /products/{id}/identifiers`,
  `DELETE /products/{id}/identifiers/{identifierId}`, `PUT /products/{id}/status`,
  `POST /products/{id}/media` (`multipart/form-data`: `file`, `alt`, `rights`, `sortOrder`; `.DisableAntiforgery()`),
  `DELETE /products/{id}/media/{mediaId}`, `POST /products/bulk`, `POST /products/{id}/merge-into/{survivorId}`;
  categories over existing `ICategoryService`: `POST /categories`, `PUT /categories/{id}`, `PUT /categories/{id}/status`,
  `PUT /categories/{id}/move`, `PUT /categories/{id}/aliases`, `GET /categories/tree`;
  attributes over `IAttributeBlueprintService`: `POST /attributes`, `PUT /attributes/{id}`, `PUT /attributes/{id}/scope`, `PUT /attributes/{id}/aliases`, `GET /attributes`.
  `AdminActor` built from the authenticated principal (phase 1 helper).

- [ ] **Step 1: Write failing tests**

```csharp
[Theory]
[InlineData("500.00", 50000L)] [InlineData("0.50", 50L)] [InlineData("1000000.00", 100000000L)]
public void TryParseMinor_Accepts_Two_Decimals(string text, long expected)
{ MoneyText.TryParseMinor(text, out var m).Should().BeTrue(); m.Should().Be(expected); }

[Theory]
[InlineData("500")] [InlineData("500.1")] [InlineData("500.123")] [InlineData("1e3")] [InlineData("-5.00")] [InlineData(" 500.00")] [InlineData("")] [InlineData(null)]
public void TryParseMinor_Rejects_Everything_Else(string? text) => MoneyText.TryParseMinor(text, out _).Should().BeFalse();

[Fact] public void Format_Pads_Cents() => MoneyText.Format(50).Should().Be("0.50");
```

REST (`CatalogAdminRestTests`, host from `CatalogTestHost.Create(fixture)` which swaps `IObjectStorage`
for `InMemoryObjectStorage` and sets `Jobs:Mode=Disabled`):
`Staff_Creates_Product_Variant_Uploads_Image_And_Activates` (201s, then `GET /products/{id}` shows
`status = "Active"`, one media whose object exists in `InMemoryObjectStorage`),
`Upload_Text_File_Returns_415_MEDIA_TYPE_UNSUPPORTED`, `Bulk_Returns_Per_Item_Outcomes`,
`Customer_Token_Gets_403_On_Admin_Catalog`.
- [ ] **Step 2: Run** `--filter-class "*.MoneyTextTests"` and `--filter-class "*.CatalogAdminRestTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** queries (no-tracking projections, max page size 100), endpoints, DTOs (money not involved), test support classes.
- [ ] **Step 4: Run** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(catalog): admin catalog REST and admin queries" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 15: Catalog admin GraphQL

**Files:**
- Create: `src/Modules/Catalog/Adapter/Inbound/GraphQL/Products/{ProductAdminMutations.cs, ProductAdminQueries.cs, ProductAdminTypes.cs}`
- Modify: `src/Modules/Catalog/Adapter/Inbound/GraphQL/DependencyInjection.cs`; put `[Authorize(Policy = "admin:staff")]` on every Catalog admin resolver (the existing commented-out `//[Authorize(Policy = "Admin")]` lines in `Categories/` and `AttributeBlueprints/` become this).
- Test: `tests/IntegrationTests/Catalog/CatalogAdminGraphQLTests.cs`

**Interfaces:**
- Produces mutations: `createProduct`, `updateProduct`, `setProductSecondaryCategories`, `addVariant`, `setVariantActive`, `addProductIdentifier`, `removeProductIdentifier`, `setProductActive`, `uploadProductMedia(input: { productId, file: Upload!, alt, rights, sortOrder })` (HotChocolate `IFile`, `AddUploadType()`), `removeProductMedia`, `bulkEditProducts`, `mergeDuplicateProduct`, `setCategoryAliases`, `setAttributeScope`, `setAttributeAliases`.
- Produces queries: `adminProducts(filter, page, pageSize)`, `adminProduct(id)`. Ids are `ID`.
  Each mutation calls the same `ICatalogAdminService` method as REST and returns the reloaded `ProductAdminDetailDto` (pattern of `CategoryAdminMutations.ReloadAsync`).

- [ ] **Step 1: Write failing tests:** `CreateProduct_Then_AdminProduct_Returns_Draft` (via `AdminGraphQLClient.PostForResultAsync`), `Anonymous_AdminProducts_Returns_AUTH_NOT_AUTHORIZED`, `SetProductActive_Without_Image_Returns_PRODUCT_ACTIVATION_INVALID` (error extension code).
- [ ] **Step 2: Run** `--filter-class "*.CatalogAdminGraphQLTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** and register the types.
- [ ] **Step 4: Run** Step 2, and `--filter-class "*.CategoryAttributeAssignmentAdminGraphQLTests"` (now needs an admin token — update its client to use `AdminJwtFactory.CreateToken("Backoffice", "staff")`). Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(catalog): admin catalog GraphQL" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 16: Sellable-variant and variant-lookup ports

**Files:**
- Create: `src/Modules/Catalog/Application/Products/Ports/Inbound/{ISellableVariantPort.cs, IVariantLookupPort.cs}`,
  `src/Modules/Catalog/Application/Products/Ports/Outbound/ISellableVariantReader.cs`,
  `src/Modules/Catalog/Application/Products/Services/SellableVariantService.cs`,
  `src/Modules/Catalog/Adapter/Outbound/Persistence/Products/SellableVariantReader.cs`
- Test: `tests/IntegrationTests/Catalog/SellableVariantPortTests.cs`

**Interfaces:**
- Produces (master contract, exact): `ISellableVariantPort.GetSellableAsync(IReadOnlyCollection<long>? variantIds = null, CancellationToken ct = default)`, `GetVersionAsync(CancellationToken ct = default)` (= `ICatalogChangeNotifier.CurrentVersionAsync`), and `SellableVariant(...)`.
  Sellable = product `Active` **and** variant `IsActive` **and** brand `IsActive`. (Price is Pricing's half.) `CategoryPath` = root→leaf category **names** of the primary category; `Attributes` = `AttributeValues`.
- Produces (addition): `public interface IVariantLookupPort { Task<IReadOnlyDictionary<long, VariantState>> GetStatesAsync(IReadOnlyCollection<long> variantIds, CancellationToken ct = default); }` and `public sealed record VariantState(long VariantId, long ProductId, bool Exists, bool IsSellable);` — used by Pricing before price writes (existence, regardless of status).

- [ ] **Step 1: Write failing tests** (Postgres, data created through `ICatalogAdminService` in a scope):
  `Draft_Product_Variants_Are_Not_Sellable`, `Admin_Deactivated_Variant_Is_Not_Sellable`,
  `Inactive_Brand_Makes_Variants_Unsellable`, `Sellable_Variant_Has_Category_Path_Root_To_Leaf`
  (`["Kitchen","Kettles"]`), `Null_Filter_Returns_All_Sellable`,
  `Lookup_Reports_Existing_Draft_Variant_As_Exists_But_Not_Sellable`,
  `Version_Increases_When_A_Variant_Is_Deactivated`.
- [ ] **Step 2: Run** `--filter-class "*.SellableVariantPortTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** the reader as one no-tracking projection joining variants → products → brands, plus one category query for paths (all categories, small set, assembled in memory); register service for both ports (scoped).
- [ ] **Step 4: Run** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(catalog): sellable variant and variant lookup ports" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 17: Catalog publishing port (idempotent upsert, never reactivates admin-deactivated)

**Files:**
- Create: `src/Modules/Catalog/Application/Products/Ports/Inbound/{ICatalogPublishingPort.cs, PublishProductCommand.cs}`, `src/Modules/Catalog/Application/Products/Services/CatalogPublishingService.cs`
- Test: `tests/UnitTests/Catalog/CatalogPublishingServiceTests.cs`

**Interfaces:**
- Produces (master contract, exact): `ICatalogPublishingPort.UpsertProductAsync(PublishProductCommand command, CancellationToken ct = default)` → `Result<PublishedProduct>`; `PublishedProduct`, `PublishedVariant`.
- Produces the command shape (phase 7 codes against it):
```csharp
public sealed record PublishProductCommand(
  PublishBrand Brand, IReadOnlyList<string> CategoryKeyPath, string Title, string? ShortDescription, string? Description,
  IReadOnlyList<IdentifierInput> Identifiers, IReadOnlyList<PublishMedia> Media, IReadOnlyList<PublishVariant> Variants,
  IReadOnlyList<long> SourceListingIds, string Provenance,     // Provenance: "import" | "seed"
  long? TargetProductId = null);                              // admin "merge into existing": skip matching, upsert this product
public sealed record PublishBrand(string Name, IReadOnlyList<string> Aliases);
public sealed record PublishMedia(string StagingObjectKey, string? Alt, string? SourceUrl, MediaUsageRights Rights, int SortOrder);
public sealed record PublishVariant(IReadOnlyDictionary<string, string> Attributes, string? Sku);
```
- Algorithm (not determined by the signature):
  1. Brand: `IBrandAdminService.EnsureAsync(name, aliases, provenance)` (its own save; idempotent).
  2. Category: `ICategoryRepository.FindByCanonicalPathAsync(CategoryKeyPath)`; none → `PUBLISH_CATEGORY_UNRESOLVED` (422), nothing written by this step.
  3. `TargetProductId` set → load that product (missing → `PRODUCT_NOT_FOUND`), skip identifier matching, then follow `MergedIntoProductId` as below. Otherwise match, first hit wins: any GTIN/UPC/EAN identifier → brand + MPN → `(source domain, SourceId)`; then follow `MergedIntoProductId` until null (max 10 hops, else `PRODUCT_MERGE_CYCLE`).
  4. Media: for each item whose `SourceUrl`/staging key is not already on the product, `ICatalogMediaStore.CopyFromStagingAsync(stagingKey, "products/{productId}/{mediaId}.{ext}")` (extension from the staging key).
  5. New product: `CreateDraft` + identifiers + variants + media + source refs, then `Activate(byAdmin:false)`; activation problems → `PRODUCT_ACTIVATION_INVALID` and **no product save**.
     Existing product: `UpdateDetails` (title/descriptions, brand, category), add missing identifiers/source refs/media, add variants whose signature hash is new, `SetVariantActive(true, byAdmin:false)` for listed existing variants, then `Activate(byAdmin:false)` → `SkippedBecauseDeactivated = !result && product.DeactivatedByAdmin`.
  6. One `SaveChangesAsync`, then `ICatalogChangeNotifier.SellabilityChangedAsync()`.

- [ ] **Step 1: Write failing tests** (fakes for repositories, brand service, media store):

```csharp
[Fact]
public async Task Publish_Updates_But_Never_Reactivates_Admin_Deactivated_Product()
{
    var existing = ActiveProductWithGtin("4006381333931");
    existing.Deactivate(byAdmin: true, T0);
    products.FindByIdentifierAsync(ProductIdentifierType.Gtin, "4006381333931", null, default).Returns(existing);

    var result = await sut.UpsertProductAsync(Command(title: "Renamed Kettle", gtin: "4006381333931"));

    result.Value!.SkippedBecauseDeactivated.Should().BeTrue();
    result.Value.Created.Should().BeFalse();
    existing.Title.Should().Be("Renamed Kettle");
    existing.Status.Should().Be(ProductStatus.Deactivated);
    existing.DeactivatedByAdmin.Should().BeTrue();
    existing.Variants.Where(v => v.DeactivatedByAdmin).Should().OnlyContain(v => !v.IsActive);
    await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
}
```

  Also: `Gtin_Match_Wins_Over_Brand_Mpn`, `Match_Follows_MergedInto_To_Survivor`,
  `TargetProductId_Bypasses_Identifier_Matching` (command with an unmatched GTIN and `TargetProductId = 5` updates product 5, `Created == false`),
  `Republishing_Same_Command_Creates_Nothing_New` (second call: `Created == false`, every `PublishedVariant.Created == false`, `products.DidNotReceive().AddAsync`),
  `Unresolved_Category_Writes_Nothing` (`PUBLISH_CATEGORY_UNRESOLVED`, no save),
  `New_Product_Without_Usable_Image_Is_Not_Saved` (`PRODUCT_ACTIVATION_INVALID`).
- [ ] **Step 2: Run** `--filter-class "*.CatalogPublishingServiceTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** and register (scoped) in `AddProductApplication()`.
- [ ] **Step 4: Run** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(catalog): idempotent catalog publishing port" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 18: Pricing module scaffold and architecture rows

**Files:**
- Create via `./New-EwaysModule.ps1 -ModuleName Pricing` (Application, Adapter/Outbound, Adapter/Inbound/Rest, QueryApplication, Installer); add `src/Modules/Pricing/Adapter/Inbound/GraphQL/EwaysVNext.Modules.Pricing.Adapter.Inbound.GraphQL.csproj` by hand (copy Catalog's GraphQL csproj references) and its `.slnx` entry.
- Create: `src/Modules/Pricing/Installer/ModuleInstaller.cs` (`AddPricingModule(this IServiceCollection, IConfiguration)`), `src/Modules/Pricing/Application/PricingErrors.cs`
- Modify: `src/Database/EwaysVNext.Database.csproj` (reference Pricing.Application), `src/Host/EwaysVNext.Host/{EwaysVNext.Host.csproj, Program.cs}`, `tests/ArchitectureTests/HexagonalBoundaryTests.cs`, `tests/UnitTests/EwaysVNext.UnitTests.csproj`
- Test: `tests/ArchitectureTests/HexagonalBoundaryTests.cs`

**Interfaces:**
- Produces project references: `Pricing.Adapter.Outbound → Catalog.Application` (spec §3.6) and
  `Pricing.Adapter.Outbound → Settings.Application` (**addition**, for `ISettingsPort.GetCommerceLimitsAsync().FxMaxRateAge`; spec §3.6 omits it, see report).
- Produces: `public static class PricingErrors { CurrencyNotEnabled = new("CURRENCY_NOT_ENABLED", ..., 422); FxRateUnavailable = new("FX_RATE_UNAVAILABLE", ..., 503, Retryable: true); PriceMissing = new("PRICE_MISSING", ..., 422); VariantNotFound = new("VARIANT_NOT_FOUND", ..., 404); CurrencyNotFound = new("CURRENCY_NOT_FOUND", ..., 404); FxOverrideConflict = new("FX_OVERRIDE_CONFLICT", ..., 409); }`.

- [ ] **Step 1: Write failing test rows:** add `"Pricing"` to `ModuleNames`; add rows `{ "Pricing", "Catalog" }` and `{ "Pricing", "Settings" }` to phase 0's `SanctionedReferences` (`TheoryData<string,string>`); add a `[Fact] Pricing_Application_References_No_Other_Module`.
- [ ] **Step 2: Run** the architecture command. Expected: FAIL (assemblies missing).
- [ ] **Step 3: Scaffold**, add the project references above, empty `AddPricingModule` that calls each layer's `AddPricing*()` plus `AddEvents(typeof(PricingErrors).Assembly)`, and call it from `Program.cs` after `AddCatalogModule()`.
- [ ] **Step 4: Run** the architecture command and `dotnet build EwaysVNext.slnx`. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(pricing): scaffold pricing module and sanctioned references" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 19: PriceBand

**Files:**
- Create: `src/Modules/Pricing/Application/Bands/PriceBand.cs`
- Test: `tests/UnitTests/Pricing/PriceBandTests.cs`

**Interfaces:**
- Produces (master contract, exact): `public static class PriceBand { public static (long Lo, long Hi) For(long referenceMinor); }`.

- [ ] **Step 1: Write failing tests**

```csharp
[Theory]
[InlineData(100L, 95L, 105L)]
[InlineData(1L, 1L, 1L)]                 // ceil(0.95)=1, floor(1.05)=1
[InlineData(1999L, 1900L, 2098L)]        // ceil(1899.05), floor(2098.95)
[InlineData(50000L, 47500L, 52500L)]
[InlineData(19L, 19L, 19L)]              // ceil(18.05)=19, floor(19.95)=19
[InlineData(100000000L, 95000000L, 105000000L)]
public void For_Uses_Integer_Ceil_And_Floor(long r, long lo, long hi) => PriceBand.For(r).Should().Be((lo, hi));

[Fact] public void For_Rejects_Non_Positive() => FluentActions.Invoking(() => PriceBand.For(0)).Should().Throw<ArgumentOutOfRangeException>();
[Fact] public void For_Overflow_Throws() => FluentActions.Invoking(() => PriceBand.For(long.MaxValue / 100)).Should().Throw<OverflowException>();
```

- [ ] **Step 2: Run** `--filter-class "*.PriceBandTests"`. Expected: FAIL.
- [ ] **Step 3: Implement:** `checked`: `lo = (r * 95 + 99) / 100`, `hi = r * 105 / 100`. No `double`/`float`/`decimal`.
- [ ] **Step 4: Run** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(pricing): integer ±5% price band" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 20: Currency, FxRate, FxRateOverride — domain and persistence

**Files:**
- Create: `src/Modules/Pricing/Application/Currencies/{Domain/Currency.cs, Ports/Outbound/ICurrencyRepository.cs}`,
  `src/Modules/Pricing/Application/Fx/Domain/{FxRate.cs, FxRateOverride.cs}`, `src/Modules/Pricing/Application/Fx/Ports/Outbound/{IFxRateRepository.cs, IFxRateOverrideRepository.cs}`,
  `src/Modules/Pricing/Adapter/Outbound/Persistence/{CurrencyRepository.cs, FxRateRepository.cs, FxRateOverrideRepository.cs}`,
  `src/Database/Pricing/Configurations/{CurrencyConfiguration.cs, FxRateConfiguration.cs, FxRateOverrideConfiguration.cs}`
- Modify: `src/Database/EwaysVNextDbContext.cs`
- Test: `tests/UnitTests/Pricing/CurrencyAndFxDomainTests.cs`, `tests/IntegrationTests/Pricing/PricingPersistenceTests.cs`

**Interfaces:**
- Produces: `Currency : AggregateRoot<long>` (`Code` char(3) unique, `IsEnabled`, `MinorUnits` = 2; `void SetEnabled(bool enabled, DateTimeOffset now)`). **Rule 10 vs spec "Code (PK)":** the PK is `long` and `Code` is `UNIQUE`; seeded with ISO-4217 numeric ids via `HasData`: USD 840, EUR 978, SGD 702, AED 784, GBP 826, all enabled, `MinorUnits = 2`.
- Produces: `FxRate : AggregateRoot<long>` (`Base`, `Quote`, `Rate` `numeric(18,10)`, `Provider`, `RateDate` `date`, `FetchedAt`; `static FxRate Record(long id, string @base, string quote, decimal rate, string provider, DateOnly rateDate, DateTimeOffset fetchedAt)`; no mutators). `UNIQUE(base, quote, provider, rate_date)`, index `(base, quote, fetched_at DESC)`.
- Produces: `FxRateOverride : AggregateRoot<long>` (`Base`, `Quote`, `Rate`, `SetBy` Guid, `SetAt`, `ValidUntil?`, `IsActive`; `static Create(long id, string @base, string quote, decimal rate, Guid setBy, DateTimeOffset now, DateTimeOffset? validUntil)`; `void Replace(decimal rate, Guid setBy, DateTimeOffset now, DateTimeOffset? validUntil)` (sets `IsActive = true`); `void Deactivate(DateTimeOffset now)`; `bool IsEffectiveAt(DateTimeOffset now)` = `IsActive && (ValidUntil is null || now < ValidUntil)`). **One row per pair:** `UNIQUE(base, quote)` (not filtered). Replacing an override mutates that row in place, so the replacement is a single save with no window where the pair has no override. Override history is kept by the audit trail's before/after values (`"fx.override.set"`). **[Plan review call]**
- Produces repositories: `ICurrencyRepository { GetAllAsync(); GetByCodeAsync(string code); }`, `IFxRateRepository { Task<IReadOnlyList<FxRate>> GetLatestPerPairAsync(); Task<IReadOnlySet<string>> GetExistingQuotesAsync(string @base, string provider, DateOnly rateDate); Task AddRangeAsync(IEnumerable<FxRate> rates); Task<FxRate?> GetByIdAsync(long id); }` (`GetLatestPerPairAsync` = `DISTINCT ON (base, quote) ... ORDER BY base, quote, fetched_at DESC` in LINQ: `GroupBy(...).Select(g => g.OrderByDescending(r => r.FetchedAt).First())`), `IFxRateOverrideRepository { GetActiveAsync(); GetForPairAsync(string @base, string quote); AddAsync(FxRateOverride o); }` (`GetForPairAsync` returns the pair's row whether active or not).
- Validation: codes `^[A-Z]{3}$` (`CURRENCY_CODE_INVALID`); `rate > 0` (`FX_RATE_INVALID`); base ≠ quote (`FX_PAIR_INVALID`).

- [ ] **Step 1: Write failing tests:** domain — `Override_Effective_Until_Exclusive_ValidUntil` (`IsEffectiveAt(validUntil)` false, `validUntil - 1 tick` true), `FxRate_Rejects_Non_Positive_Rate`, `FxRate_Rejects_Same_Base_And_Quote`; integration — `Five_Currencies_Are_Seeded_Enabled_With_Two_Minor_Units`, `Second_Active_Override_For_Pair_Is_Rejected` (`23505`), `Same_Rate_Date_From_Same_Provider_Is_Rejected` (`23505`), `Rate_Precision_Survives_Roundtrip` (`1.0812345678m`).
- [ ] **Step 2: Run** both classes. Expected: FAIL.
- [ ] **Step 3: Implement** domain, configurations (`ToTable("currencies"|"fx_rates"|"fx_rate_overrides")`, `ValueGeneratedNever()`, `xmin`), repositories, DbSets.
- [ ] **Step 4: Migration** `PricingInit`.
- [ ] **Step 5: Run** Step 2. Expected: PASS.
- [ ] **Step 6: Commit** `git commit -m "feat(pricing): currencies, append-only FX rates and FX overrides" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 21: FX rate resolution (override > fresh provider > unavailable; EUR cross; AED peg)

**Files:**
- Create: `src/Modules/Pricing/Application/Fx/Domain/{FxRateBook.cs, FxRateResolver.cs, FxResolution.cs, UsdPeg.cs}`
- Test: `tests/UnitTests/Pricing/FxRateResolverTests.cs`

**Interfaces:**
- Produces: `public sealed record FxRateBook(IReadOnlyList<FxRate> LatestProviderRates, IReadOnlyList<FxRateOverride> ActiveOverrides, TimeSpan MaxRateAge);`
- Produces: `public sealed record FxResolution(decimal Rate, PriceSource Source, FxSnapshot? Snapshot, DateTimeOffset? ValidUntil);`
- Produces: `public static class FxRateResolver { public static Result<FxResolution> Resolve(FxRateBook book, string @base, string quote, DateTimeOffset now); }`
- Produces: `public static class UsdPeg { public const decimal UsdToAed = 3.6725m; public const string Provenance = "derived:usd-peg"; }`
- Algorithm (spec §4, made exact):
  1. `base == quote` → `Rate 1`, `Source Base`, `Snapshot null`.
  2. An override for the **exact** pair with `IsEffectiveAt(now)` → `Rate = override.Rate`, `Source Fx`, snapshot `(FxRateId null, FxOverrideId id, base, quote, rate, RateAt = SetAt, "override")`, `ValidUntil = override.ValidUntil`.
  3. USD→AED / AED→USD: `3.6725` / `round10(1 / 3.6725)`, provenance `derived:usd-peg`, `RateAt = now`, `ValidUntil null`, no provider row needed.
  4. Provider legs from EUR: `leg(EUR)=1`; `leg(AED)= leg(USD) × 3.6725` (needs a fresh EUR→USD row); `leg(X)` = latest EUR→X row with `now - FetchedAt <= MaxRateAge` (**inclusive**). Missing/stale leg → `PricingErrors.FxRateUnavailable`.
     `Rate = round10(leg(quote) / leg(base))`, half-even to 10 dp. Snapshot: `FxRateId` = the EUR→quote row (or EUR→base row when quote is EUR, or EUR→USD row for AED legs), `RateAt` = the **oldest** `FetchedAt` among legs used, provenance `"ecb"` for a single direct leg, `"ecb:cross-eur"` for two legs, `"ecb:cross-eur+derived:usd-peg"` when AED is involved. `ValidUntil = RateAt + MaxRateAge`.

- [ ] **Step 1: Write failing tests**

```csharp
static readonly DateTimeOffset Now = new(2026, 10, 30, 16, 0, 0, TimeSpan.Zero);
static FxRate Eur(string quote, decimal rate, DateTimeOffset fetchedAt, long id = 1) =>
    FxRate.Record(id, "EUR", quote, rate, "ecb", DateOnly.FromDateTime(fetchedAt.UtcDateTime), fetchedAt);
static FxRateBook Book(params FxRate[] rates) => new(rates, [], TimeSpan.FromHours(72));

[Fact]
public void Rate_Age_Boundary_Is_Inclusive()
{
    FxRateResolver.Resolve(Book(Eur("USD", 1.08m, Now.AddHours(-72))), "EUR", "USD", Now)
        .IsSuccess.Should().BeTrue();
    var stale = FxRateResolver.Resolve(Book(Eur("USD", 1.08m, Now.AddHours(-72).AddSeconds(-1))), "EUR", "USD", Now);
    stale.Error!.Code.Should().Be("FX_RATE_UNAVAILABLE");
    stale.Error.Retryable.Should().BeTrue();
}

[Fact]
public void Aed_Without_Usd_Rate_Is_Unavailable()
{
    var book = Book(Eur("GBP", 0.85m, Now.AddHours(-1)));
    FxRateResolver.Resolve(book, "EUR", "AED", Now).Error!.Code.Should().Be("FX_RATE_UNAVAILABLE");
    FxRateResolver.Resolve(book, "GBP", "AED", Now).Error!.Code.Should().Be("FX_RATE_UNAVAILABLE");
}

[Fact]
public void Usd_To_Aed_Uses_Peg_Without_Any_Provider_Rate()
{
    var r = FxRateResolver.Resolve(Book(), "USD", "AED", Now).Value!;
    r.Rate.Should().Be(3.6725m);
    r.Snapshot!.Provenance.Should().Be("derived:usd-peg");
}

[Fact]
public void Eur_To_Aed_Is_Usd_Leg_Times_Peg()
{
    var r = FxRateResolver.Resolve(Book(Eur("USD", 1.0812m, Now.AddHours(-1), id: 7)), "EUR", "AED", Now).Value!;
    r.Rate.Should().Be(3.9707070000m);        // 1.0812 × 3.6725
    r.Snapshot!.FxRateId.Should().Be(7);
    r.Snapshot.Provenance.Should().Be("ecb:cross-eur+derived:usd-peg");
}

[Fact]
public void Cross_Rate_Goes_Through_Eur_And_Rounds_To_Ten_Places()
{
    var book = Book(Eur("USD", 1.0812m, Now.AddHours(-2), id: 1), Eur("SGD", 1.4321m, Now.AddHours(-1), id: 2));
    var r = FxRateResolver.Resolve(book, "USD", "SGD", Now).Value!;
    r.Rate.Should().Be(1.3245467999m);        // round10(1.4321 / 1.0812), half-even
    r.Snapshot!.RateAt.Should().Be(Now.AddHours(-2));
    r.ValidUntil.Should().Be(Now.AddHours(70));
}

[Fact]
public void Effective_Override_Beats_Fresh_Provider_And_Expired_Override_Does_Not()
{
    var o = FxRateOverride.Create(9, "EUR", "USD", 1.2m, Guid.NewGuid(), Now.AddDays(-1), validUntil: Now.AddHours(1));
    var book = new FxRateBook([Eur("USD", 1.08m, Now.AddHours(-1))], [o], TimeSpan.FromHours(72));
    FxRateResolver.Resolve(book, "EUR", "USD", Now).Value!.Snapshot!.FxOverrideId.Should().Be(9);
    FxRateResolver.Resolve(book, "EUR", "USD", Now.AddHours(1)).Value!.Rate.Should().Be(1.08m);
}

[Fact]
public void Override_Is_Used_Even_When_Provider_Is_Stale() =>
    FxRateResolver.Resolve(new FxRateBook([], [FxRateOverride.Create(9, "EUR", "GBP", 0.9m, Guid.NewGuid(), Now, null)], TimeSpan.FromHours(72)),
        "EUR", "GBP", Now).Value!.Rate.Should().Be(0.9m);
```

- [ ] **Step 2: Run** `--filter-class "*.FxRateResolverTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** as specified (pure, no IO, `decimal` only).
- [ ] **Step 4: Run** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(pricing): FX rate resolution with inclusive max age, EUR cross and AED peg" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 22: VariantPrice, CurrencyOverride and price resolution

**Files:**
- Create: `src/Modules/Pricing/Application/Prices/Domain/{VariantPrice.cs, CurrencyOverride.cs, PriceProvenance.cs, PriceResolver.cs}`, `src/Modules/Pricing/Application/Prices/Ports/Inbound/PriceQuote.cs` (`PriceSource`, `FxSnapshot`, `PriceQuote` exactly as the master contract)
- Test: `tests/UnitTests/Pricing/PriceResolverTests.cs`

**Interfaces:**
- Produces: `VariantPrice : AggregateRoot<long>` — `VariantId`, `BaseAmountMinor`, `BaseCurrency`, `Provenance` (`PriceProvenance` record from the master contract, persisted as owned columns), `UpdatedAt`, `Overrides`;
  `static VariantPrice Create(long id, long variantId, long baseAmountMinor, string baseCurrency, PriceProvenance provenance, DateTimeOffset now)`;
  `void SetBase(long baseAmountMinor, string baseCurrency, PriceProvenance provenance, DateTimeOffset now)`;
  `void SetOverride(long overrideId, string currency, long amountMinor, DateTimeOffset now, string priceList = "default")`;
  `bool RemoveOverride(string currency, DateTimeOffset now, string priceList = "default")`. Amounts `> 0` (`PRICE_AMOUNT_INVALID`).
- Produces: `public static class PriceResolver { public static PriceQuote? Resolve(VariantPrice price, string currency, Func<string, string, Result<FxResolution>> resolveRate, out Error? error); }` — returns null with `error` = `FX_RATE_UNAVAILABLE` or `PRICE_MISSING` (result rounds to < 1 minor unit).
  `r`: override(currency, "default") → `Source Override`, `Fx null`; else `currency == BaseCurrency` → `Source Base`; else `round_half_even(BaseAmountMinor × rate)` to 0 dp in minor units → `Source Fx` with snapshot. `(Lo, Hi) = PriceBand.For(r)`.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public void Override_Wins_Over_Fx()
{
    var p = VariantPrice.Create(1, 100, 10000, "EUR", Prov, T0);
    p.SetOverride(2, "SGD", 15000, T0);
    var q = PriceResolver.Resolve(p, "SGD", (_, _) => throw new InvalidOperationException("no FX needed"), out _)!;
    q.ReferenceMinor.Should().Be(15000); q.Source.Should().Be(PriceSource.Override); q.Fx.Should().BeNull();
    (q.LoMinor, q.HiMinor).Should().Be((14250L, 15750L));
}

[Fact]
public void Converted_Price_Rounds_Half_Even_In_Minor_Units()
{
    var p = VariantPrice.Create(1, 100, 125, "EUR", Prov, T0);    // 1.25 EUR
    PriceResolver.Resolve(p, "USD", (_, _) => Fx(1.1m), out _)!.ReferenceMinor.Should().Be(138);  // 137.5 → 138
    PriceResolver.Resolve(p, "USD", (_, _) => Fx(1.3m), out _)!.ReferenceMinor.Should().Be(162);  // 162.5 → 162
}

[Fact]
public void Base_Currency_Needs_No_Rate()
{
    var q = PriceResolver.Resolve(VariantPrice.Create(1, 100, 999, "USD", Prov, T0), "USD", (_, _) => throw new InvalidOperationException(), out _)!;
    q.Source.Should().Be(PriceSource.Base); q.ReferenceMinor.Should().Be(999);
}

[Fact]
public void Sub_Minor_Result_Is_Price_Missing()
{
    PriceResolver.Resolve(VariantPrice.Create(1, 100, 1, "USD", Prov, T0), "EUR", (_, _) => Fx(0.4m), out var e).Should().BeNull();
    e!.Code.Should().Be("PRICE_MISSING");
}
```

- [ ] **Step 2: Run** `--filter-class "*.PriceResolverTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** (`decimal.Round(base * rate, 0, MidpointRounding.ToEven)` → `checked((long)...)`).
- [ ] **Step 4: Run** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(pricing): variant prices, currency overrides and price resolution" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 23: Pricing persistence, version sequence, resolved-rate projection

**Files:**
- Create: `src/Database/Pricing/Configurations/{VariantPriceConfiguration.cs, CurrencyOverrideConfiguration.cs}`,
  `src/Modules/Pricing/Application/Prices/Ports/Outbound/{IVariantPriceRepository.cs, IPricingChangeNotifier.cs}`,
  `src/Modules/Pricing/Application/Fx/Ports/Outbound/IResolvedRateProjection.cs`,
  `src/Modules/Pricing/Application/Fx/Services/ResolvedRateProjectionService.cs`,
  `src/Modules/Pricing/Adapter/Outbound/Persistence/{VariantPriceRepository.cs, ResolvedRateProjection.cs}`,
  `src/Modules/Pricing/Adapter/Outbound/Gateways/PricingChangeNotifier.cs`
- Test: `tests/IntegrationTests/Pricing/PricingPersistenceTests.cs`

**Interfaces:**
- Produces DB (migration `PricingPrices`):
  - `pricing.variant_prices` (`variant_id UNIQUE`, `base_amount_minor bigint CHECK (> 0)`, `base_currency char(3)`, provenance columns, `xmin`);
  - `pricing.currency_overrides` (`UNIQUE(variant_price_id, currency, price_list)`, `amount_minor bigint CHECK (> 0)`);
  - `pricing.resolved_fx_rates(base char(3), quote char(3), rate numeric(18,10), provenance text, fx_rate_id bigint null, fx_override_id bigint null, rate_at timestamptz, valid_until timestamptz null, PRIMARY KEY (base, quote))` — a derived read table **[design call]**: the C# resolver is the single implementation of §4 and the storefront SQL only joins its output;
  - `CREATE SEQUENCE pricing.price_version`;
  - `pricing.round_half_even(x numeric) RETURNS numeric` (`IMMUTABLE`): `CASE WHEN x - floor(x) = 0.5 THEN CASE WHEN mod(floor(x), 2) = 0 THEN floor(x) ELSE floor(x) + 1 END ELSE round(x) END` (inputs are positive).
- Produces: `IVariantPriceRepository { GetByVariantIdAsync(long variantId); GetByVariantIdsAsync(IReadOnlyCollection<long> ids); AddAsync(VariantPrice price); }`.
- Produces: `IResolvedRateProjection { Task ReplaceAllAsync(IReadOnlyList<ResolvedRateRow> rows, CancellationToken ct = default); }` with `ResolvedRateRow(string Base, string Quote, decimal Rate, string Provenance, long? FxRateId, long? FxOverrideId, DateTimeOffset RateAt, DateTimeOffset? ValidUntil)`; adapter runs **one** command `DELETE FROM pricing.resolved_fx_rates; INSERT INTO ... VALUES (...), (...)` (a single multi-statement command runs as one implicit transaction — no `BeginTransaction`, rule 8).
- Produces: `ResolvedRateProjectionService.RebuildAsync(DateTimeOffset now, CancellationToken ct)` — for every base in **all** currencies and every quote in **enabled** currencies, `base ≠ quote`, resolve; failures are skipped.
- Produces: `IPricingChangeNotifier { Task PricesChangedAsync(CancellationToken ct = default); Task FxChangedAsync(CancellationToken ct = default); Task<long> CurrentVersionAsync(CancellationToken ct = default); }`
  — `PricesChangedAsync` = `nextval('pricing.price_version')` + `IStorefrontRefreshTrigger.RequestRefreshAsync()`;
  `FxChangedAsync` = `RebuildAsync(now)` + `PricesChangedAsync`. Failures after a committed save are logged and swallowed.

- [ ] **Step 1: Write failing integration tests:** `Variant_Price_With_Overrides_Roundtrips`, `Duplicate_Override_Currency_Is_Rejected` (`23505`),
  `Round_Half_Even_Sql_Matches_Dotnet` — for `x ∈ {0.5, 1.5, 2.5, 137.5, 162.5, 2.4999, 2.5001, 10}` assert `SELECT pricing.round_half_even(@x)` equals `decimal.Round(x, 0, MidpointRounding.ToEven)`,
  `Resolved_Rate_Projection_Replaces_All_Rows` (two rebuilds with different books → table holds only the second set).
- [ ] **Step 2: Run** `--filter-class "*.PricingPersistenceTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** configurations, repositories, projection adapter (parameterized values, no string-concatenated data), service, notifier, migration (`migrationBuilder.Sql` for the sequence and function).
- [ ] **Step 4: Run** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(pricing): price persistence, version sequence and resolved FX projection" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 24: Quote and FX snapshot ports

**Files:**
- Create: `src/Modules/Pricing/Application/Prices/Ports/Inbound/IPriceQuotePort.cs`, `src/Modules/Pricing/Application/Fx/Ports/Inbound/IFxSnapshotPort.cs`,
  `src/Modules/Pricing/Application/Prices/Services/PriceQuoteService.cs`, `src/Modules/Pricing/Application/Fx/Services/FxSnapshotService.cs`,
  `src/Modules/Pricing/Application/Prices/Ports/Outbound/IPricingPolicyGateway.cs`, `src/Modules/Pricing/Adapter/Outbound/Gateways/PricingPolicyGateway.cs`
- Test: `tests/UnitTests/Pricing/PriceQuoteServiceTests.cs`

**Interfaces:**
- Produces (master contract, exact): `IPriceQuotePort.QuoteAsync(...)`, `IPriceQuotePort.GetVersionAsync(...)`, `IFxSnapshotPort.ToUsdAsync(...)`, `UsdConversion`.
- Produces: `IPricingPolicyGateway { Task<TimeSpan> GetFxMaxRateAgeAsync(CancellationToken ct = default); }` → `ISettingsPort.GetCommerceLimitsAsync()`.
- Behaviour: currency must be an **enabled** currency → else `CURRENCY_NOT_ENABLED` (whole call). Variants without a price, without a usable rate, or below 1 minor unit are **omitted**. One book load per call. `ToUsdAsync`: disabled currency → `CURRENCY_NOT_ENABLED`; `USD` → `UsdConversion(amount, null)`; else `round_half_even(amount × rate(currency→USD))`; rate failure → `FX_RATE_UNAVAILABLE`.

- [ ] **Step 1: Write failing tests:** `Quote_Omits_Unpriced_And_Unconvertible_Variants` (3 ids: priced EUR base, unpriced, GBP base with no GBP rate; ask USD → 1 quote), `Quote_Disabled_Currency_Fails_Whole_Call` (`CURRENCY_NOT_ENABLED`), `Quote_Returns_Band_And_Snapshot` (EUR 100.00 base, EUR→USD 1.0812 id 7 → `ReferenceMinor 10812`, `Lo 10272`, `Hi 11352`, `Fx.FxRateId 7`), `ToUsd_Uses_Exact_Pair_Override` (`1250` SGD minor with an active SGD→USD override `0.7550` → `944`, from 943.75), `ToUsd_From_Usd_Has_No_Snapshot`.
- [ ] **Step 2: Run** `--filter-class "*.PriceQuoteServiceTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** and register both ports (scoped) in `AddPricingApplication()`.
- [ ] **Step 4: Run** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(pricing): price quote and USD snapshot ports" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 25: Price publishing port and pricing admin service

**Files:**
- Create: `src/Modules/Pricing/Application/Prices/Ports/Inbound/{IPricePublishingPort.cs, IPricingAdminService.cs}`, `src/Modules/Pricing/Application/Prices/Services/{PricePublishingService.cs, PricingAdminService.cs}`,
  `src/Modules/Pricing/Application/Prices/Ports/Outbound/ICatalogVariantGateway.cs`, `src/Modules/Pricing/Adapter/Outbound/Gateways/CatalogVariantGateway.cs`
- Test: `tests/UnitTests/Pricing/PricingAdminServiceTests.cs`

**Interfaces:**
- Produces (master contract, exact): `IPricePublishingPort.SetReferencePriceAsync(long variantId, long amountMinor, string currency, PriceProvenance provenance, CancellationToken ct = default)`.
- Produces: `ICatalogVariantGateway { Task<bool> VariantExistsAsync(long variantId, CancellationToken ct = default); }` → `IVariantLookupPort`.
- Produces:
```csharp
public interface IPricingAdminService {
  Task<Result> SetVariantPriceAsync(long variantId, long baseAmountMinor, string baseCurrency, AdminActor actor, CancellationToken ct = default);           // staff
  Task<Result> SetCurrencyOverrideAsync(long variantId, string currency, long amountMinor, AdminActor actor, CancellationToken ct = default);            // staff
  Task<Result> RemoveCurrencyOverrideAsync(long variantId, string currency, AdminActor actor, CancellationToken ct = default);                           // staff
  Task<Result<long>> SetFxOverrideAsync(string @base, string quote, decimal rate, DateTimeOffset? validUntil, AdminActor actor, CancellationToken ct = default); // super
  Task<Result> ClearFxOverrideAsync(string @base, string quote, AdminActor actor, CancellationToken ct = default);                                       // super
  Task<Result> SetCurrencyEnabledAsync(string code, bool enabled, AdminActor actor, CancellationToken ct = default);                                     // super
  Task<Result> RequestFxRefreshAsync(AdminActor actor, CancellationToken ct = default);                                                                 // super → IJobQueue FxRefresh, dedupeKey "fx-refresh"
}
```
- Rules: super-only methods return `Error.Forbidden` when `!actor.IsSuperAdmin` before any IO (defence in depth behind the `admin:super` policy). Price currency must exist in `pricing.currencies` (enabled or not) → else `CURRENCY_NOT_FOUND`; override currency must be enabled → `CURRENCY_NOT_ENABLED`. `SetFxOverrideAsync`: `GetForPairAsync`, then either `Replace(...)` the existing row or `Create` and `AddAsync` a new one, then one `TrySaveChangesAsync`. A concurrent first insert for the same pair (`UniqueViolation`) or a stale `Replace` (`ConcurrencyConflict`) maps to `FX_OVERRIDE_CONFLICT` (409). `ClearFxOverrideAsync` = `Deactivate` + one save. After each committed change: prices → `PricesChangedAsync`; FX override / currency toggle → `FxChangedAsync`. Every admin write records audit (`"price.set"`, `"price.override.set"`, `"fx.override.set"`, `"currency.enabled.set"`, ...).

- [ ] **Step 1: Write failing tests:** `Staff_Cannot_Set_Fx_Override` (`IsSuperAdmin:false` → 403 `FORBIDDEN`, `overrides.DidNotReceiveWithAnyArgs().AddAsync`), `Staff_Cannot_Toggle_Currency`, `Staff_Can_Set_Price_And_Override`, `Set_Price_For_Unknown_Variant_Fails` (`VARIANT_NOT_FOUND`), `Override_In_Disabled_Currency_Fails`, `Fx_Override_Replace_Is_One_Save_On_The_Same_Row` (`Received(1).TrySaveChangesAsync`, `overrides.DidNotReceiveWithAnyArgs().AddAsync`, row `Rate == new rate`, `IsActive == true`), `Publishing_Creates_Then_Updates_Same_VariantPrice` (second call updates base and provenance, no second `AddAsync`).
- [ ] **Step 2: Run** `--filter-class "*.PricingAdminServiceTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** and register.
- [ ] **Step 4: Run** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(pricing): price publishing port and pricing admin service" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 26: ECB provider, FX refresh service and recurring job

**Files:**
- Create: `src/Modules/Pricing/Application/Fx/Ports/Outbound/IFxRateProvider.cs`, `src/Modules/Pricing/Application/Fx/Ports/Inbound/{IFxRefreshService.cs, FxProviderBatch.cs, FxRefreshPayload.cs}`, `src/Modules/Pricing/Application/Fx/Services/FxRefreshService.cs`,
  `src/Modules/Pricing/Adapter/Outbound/Fx/Ecb/EcbFxRateProvider.cs`, `src/Modules/Pricing/Adapter/Outbound/Fx/FxRefreshJobHandler.cs`,
  `tests/UnitTests/Pricing/Fixtures/eurofxref-daily-2026-10-27.xml` (copied to output)
- Modify: `src/Modules/Pricing/Adapter/Outbound/DependencyInjection.cs`, `src/Host/EwaysVNext.Host/appsettings.json` (`"Fx": { "RefreshInterval": "06:00:00", "Ecb": { "BaseUrl": "https://www.ecb.europa.eu/stats/eurofxref/" } }`)
- Test: `tests/UnitTests/Pricing/{EcbFxRateProviderTests.cs, FxRefreshServiceTests.cs}`

**Interfaces:**
- Produces: `public sealed record FxProviderBatch(string Provider, DateOnly RateDate, IReadOnlyList<FxQuote> Rates); public sealed record FxQuote(string Quote, decimal Rate);` (base is always EUR).
- Produces: `IFxRateProvider { string Name { get; } Task<Result<FxProviderBatch>> FetchAsync(CancellationToken ct = default); }` — ECB `Name = "ecb"`, `GET eurofxref-daily.xml`, parse `Cube[@time]` / `Cube[@currency][@rate]` with `CultureInfo.InvariantCulture`; HTTP failure or malformed XML → `Result.Failure(new Error("FX_PROVIDER_FAILED", ..., 502, Retryable: true))`.
- Produces: `IFxRefreshService { Task<Result<int>> RefreshAsync(CancellationToken ct = default); Task<Result<int>> IngestAsync(FxProviderBatch batch, CancellationToken ct = default); }` — `Ingest` inserts only quotes not already stored for `(EUR, quote, provider, rateDate)` (append-only), one save, returns inserted count, then **always** calls `IPricingChangeNotifier.FxChangedAsync()` (re-projection drops newly stale rates even when nothing was inserted).
- Produces: `FxRefreshJobHandler : IJobHandler<FxRefreshPayload>` (`Type = JobTypes.FxRefresh`); failure → `JobOutcome.RetryLater(TimeSpan.FromMinutes(5), code)`. Registration: `services.AddJobHandler<FxRefreshJobHandler, FxRefreshPayload>(); services.AddRecurringJob(JobTypes.FxRefresh, config.GetValue("Fx:RefreshInterval", TimeSpan.FromHours(6)));` and `services.AddHttpClient<EcbFxRateProvider>(c => c.BaseAddress = new Uri(config["Fx:Ecb:BaseUrl"]!))`.
- Fixture content (ECB format, values fixed for tests — **not** live data; no test performs live HTTP):

```xml
<?xml version="1.0" encoding="UTF-8"?>
<gesmes:Envelope xmlns:gesmes="http://www.gesmes.org/xml/2002-08-01" xmlns="http://www.ecb.int/vocabulary/2002-08-01/eurofxref">
  <gesmes:subject>Reference rates</gesmes:subject>
  <gesmes:Sender><gesmes:name>European Central Bank</gesmes:name></gesmes:Sender>
  <Cube><Cube time="2026-10-27">
    <Cube currency="USD" rate="1.0812"/><Cube currency="JPY" rate="163.42"/>
    <Cube currency="GBP" rate="0.84215"/><Cube currency="SGD" rate="1.4321"/>
  </Cube></Cube>
</gesmes:Envelope>
```

- [ ] **Step 1: Write failing tests:** ECB — `Parses_Fixture_Rates_And_Date` (stub `HttpMessageHandler` returning the fixture: `RateDate == new DateOnly(2026,10,27)`, `Rates` contains `("USD",1.0812m)`, `("GBP",0.84215m)`, count 4), `Http_500_Is_Retryable_Provider_Failure`, `Malformed_Xml_Is_Provider_Failure`; refresh — `Ingest_Skips_Quotes_Already_Stored_For_Rate_Date` (existing `{USD}` → inserts 3), `Ingest_Always_Reprojects`, `Refresh_Propagates_Provider_Failure_Without_Saving`, `Handler_Maps_Failure_To_RetryLater`.
- [ ] **Step 2: Run** `--filter-class "*.EcbFxRateProviderTests"` and `--filter-class "*.FxRefreshServiceTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** (`<None Update="Pricing\Fixtures\*.xml" CopyToOutputDirectory="PreserveNewest" />` in the unit-test csproj).
- [ ] **Step 4: Run** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(pricing): ECB FX provider and recurring refresh job" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 27: Pricing REST and GraphQL (admin + anonymous currencies), acceptance 8 pricing part

**Files:**
- Create: `src/Modules/Pricing/QueryApplication/{IPricingAdminQueries.cs, PricingAdminQueries.cs, ICurrencyQueries.cs, CurrencyQueries.cs, PricingDtos.cs, DependencyInjection.cs}`,
  `src/Modules/Pricing/Adapter/Inbound/Rest/{PricingAdminEndpoints.cs, ShopCurrencyEndpoints.cs, PricingDtos.cs, DependencyInjection.cs}`,
  `src/Modules/Pricing/Adapter/Inbound/GraphQL/{PricingAdminResolvers.cs, CurrencyQueries.cs, DependencyInjection.cs}`
- Test: `tests/IntegrationTests/Pricing/PricingRestTests.cs`

**Interfaces:**
- Produces: `ICurrencyQueries { Task<IReadOnlyList<CurrencyDto>> GetEnabledAsync(CancellationToken ct = default); Task<bool> IsEnabledAsync(string code, CancellationToken ct = default); }`, `CurrencyDto(string Code, int MinorUnits)`;
  `IPricingAdminQueries { GetVariantPriceAsync(long variantId) → Result<VariantPriceAdminDto>; ListFxRatesAsync(string? quote, int page, int pageSize); ListFxOverridesAsync(bool activeOnly); ListCurrenciesAsync(); }`.
- Produces REST: `GET /api/v1/shop/currencies` (`AllowAnonymous`); under `/api/v1/admin/pricing` — `admin:staff`: `GET /variants/{variantId}`, `PUT /variants/{variantId}/price` body `{ "amount": "100.00", "currency": "EUR" }`, `PUT /variants/{variantId}/overrides/{currency}` body `{ "amount": "150.00" }`, `DELETE /variants/{variantId}/overrides/{currency}`, `GET /fx/rates`, `GET /fx/overrides`, `GET /currencies`; `admin:super`: `POST /fx/overrides` `{ base, quote, rate: "1.2000000000", validUntil? }` (rate as string, `decimal.Parse(InvariantCulture)`, ≤10 dp), `DELETE /fx/overrides/{base}/{quote}`, `PUT /currencies/{code}` `{ enabled }`, `POST /fx/refresh`.
  Amounts are parsed only with `MoneyText.TryParseMinor`; failure → 400 `VALIDATION_FAILED`.
- Produces GraphQL: anonymous `currencies`; admin (`admin:staff`) `adminVariantPrice(variantId)`, `adminFxRates`, `adminFxOverrides`, mutations `setVariantPrice`, `setCurrencyOverride`, `removeCurrencyOverride`; (`admin:super`) `setFxOverride`, `clearFxOverride`, `setCurrencyEnabled`, `requestFxRefresh`.

- [ ] **Step 1: Write failing tests** (Postgres; FX rows inserted with `IFxRefreshService.IngestAsync(new FxProviderBatch("ecb", today, [new("USD", 1.0812m), new("SGD", 1.4321m)]))` resolved from a scope; variant from `ICatalogAdminService`):

```csharp
[Fact]
public async Task Acceptance08_Base_Conversion_And_Override_Give_Exact_Minor_Units_And_Stable_Snapshot()
{
    // staff: PUT price {"amount":"100.00","currency":"EUR"}; PUT override SGD {"amount":"150.00"}
    var usd = (await quotes.QuoteAsync([variantId], "USD", now)).Value!.Single();
    usd.ReferenceMinor.Should().Be(10812); usd.Source.Should().Be(PriceSource.Fx);
    var sgd = (await quotes.QuoteAsync([variantId], "SGD", now)).Value!.Single();
    sgd.ReferenceMinor.Should().Be(15000); sgd.Source.Should().Be(PriceSource.Override);
    // a newer rate arrives (next rate date) …
    await fx.IngestAsync(new FxProviderBatch("ecb", tomorrow, [new("USD", 1.0900m)]));
    var later = (await quotes.QuoteAsync([variantId], "USD", now.AddDays(1))).Value!.Single();
    later.ReferenceMinor.Should().Be(10900);
    // … the earlier snapshot still points at an unchanged, append-only row
    (await rates.GetByIdAsync(usd.Fx!.FxRateId!.Value))!.Rate.Should().Be(1.0812m);
}
```

  Plus `Acceptance13_Staff_Gets_403_On_Fx_Override_And_Currency_Enablement` (staff token → 403 on `POST /fx/overrides` and `PUT /currencies/SGD`; super token → 200/201),
  `Shop_Currencies_Is_Anonymous_And_Lists_Five`, `Disabled_Currency_Disappears_From_Shop_Currencies`,
  `Price_Amount_Must_Be_Two_Decimal_String` (`"100"` → 400 `VALIDATION_FAILED`).
- [ ] **Step 2: Run** `--filter-class "*.PricingRestTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** queries, endpoints, resolvers; wire them in `AddPricingModule`.
- [ ] **Step 4: Run** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(pricing): pricing admin and currency APIs (REST + GraphQL)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 28: Demo seed catalog (`--seed`, refused in Production)

**Files:**
- Create: `src/Host/EwaysVNext.Host/Seeding/{DemoSeedGuard.cs, PlaceholderPng.cs, DemoCatalogData.cs, DemoCatalogSeeder.cs}`
- Modify: `src/Host/EwaysVNext.Host/Program.cs` (after the `--migrate` block), `src/Host/EwaysVNext.Host/EwaysVNext.Host.csproj` (`System.IO.Hashing` for CRC-32)
- Test: `tests/UnitTests/Seeding/{DemoSeedGuardTests.cs, PlaceholderPngTests.cs, DemoCatalogDataTests.cs}`, `tests/IntegrationTests/Seeding/DemoSeedTests.cs`

**Interfaces:**
- Produces: `public static class DemoSeedGuard { public static Result EnsureAllowed(IHostEnvironment env); }` → `Production` → `Error("SEED_REFUSED_IN_PRODUCTION", ..., 400)`; Program prints it and exits with code `2`.
- Produces: `public static class PlaceholderPng { public static byte[] Create(int width, int height, byte r, byte g, byte b); }` — solid-colour RGB PNG: signature, `IHDR` (bit depth 8, colour type 2), one `IDAT` (`ZLibStream` over rows each prefixed by filter byte 0), `IEND`; chunk CRC via `System.IO.Hashing.Crc32`.
- Produces: `public static class DemoCatalogData { public static IReadOnlyList<DemoCategory> Categories { get; } public static IReadOnlyList<DemoProduct> Products { get; } }` — deterministic (`new Random(20261028)`):
  - 12 root categories (`Kitchen`, `Office`, `Garden`, `Audio`, `Lighting`, `Tools`, `Sports`, `Toys`, `Bags`, `Bath`, `Pet`, `Stationery`), each with one child category used as primary category.
  - 20 brands named `"Demo <Word>"`; titles start with `"[DEMO] "`; short description `"Fictitious demo product — not for sale."`.
  - Per category: 10 products × 3 variants (`size` S/M/L **or** `colour` Red/Blue/Green) + 3 single-variant products (empty signature), plus one more single-variant product in each of the first 4 categories — totals: **400 variants**, 160 products.
  - Base prices log-uniform over `[50, 5_000_000]` minor units, with the first variant of `Kitchen` pinned to `50` ($0.50) and the first variant of `Audio` pinned to `5_000_000` ($50,000); base currency `USD`, except every 5th product `EUR`, every 7th `SGD`, every 11th `GBP` (precedence in that order).
  - Every product has a GTIN-13 built as `"20" + 10-digit sequence + GS1 check digit` (`20…` = GS1 in-store/restricted range, so never a real product code).
- Produces: `DemoCatalogSeeder.RunAsync(IServiceProvider sp, CancellationToken ct)` → `Result<SeedSummary>`; `SeedSummary(int Categories, int Products, int Variants, int Prices)`. Steps: ensure categories via `ICategoryService` (create if `FindByCanonicalPathAsync` misses, then set Active); put `PlaceholderPng.Create(600, 600, colour)` to `import-staging` at `seed/{gtin}.png`; `ICatalogPublishingPort.UpsertProductAsync` with `Provenance = "seed"`, rights `Allowed`; `IPricePublishingPort.SetReferencePriceAsync` per variant with provenance `SourceUrl = "seed:demo"`; FX: `IFxRefreshService.RefreshAsync()`, and when it fails (offline), `IngestAsync` a `"seed-demo"` batch with the fixture's four rates for today's date. Idempotent: a second run creates nothing new (publishing matches by GTIN).

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public void Seed_Is_Refused_In_Production()
{
    var env = Substitute.For<IHostEnvironment>(); env.EnvironmentName.Returns(Environments.Production);
    DemoSeedGuard.EnsureAllowed(env).Error!.Code.Should().Be("SEED_REFUSED_IN_PRODUCTION");
}

[Fact]
public void Png_Has_Signature_And_Dimensions()
{
    var png = PlaceholderPng.Create(600, 400, 10, 20, 30);
    png[..8].Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
    BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)).Should().Be(600);
    BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)).Should().Be(400);
    new FileTypeDetector().Detect(png).FileType.Should().Be(FileType.Png);
}

[Fact]
public void Demo_Data_Has_400_Variants_In_12_Categories_Spanning_50_To_5000000_Minor()
{
    DemoCatalogData.Categories.Should().HaveCount(12);
    DemoCatalogData.Products.SelectMany(p => p.Variants).Should().HaveCount(400);
    var prices = DemoCatalogData.Products.SelectMany(p => p.Variants).Select(v => v.BaseAmountMinor).ToList();
    prices.Min().Should().Be(50); prices.Max().Should().Be(5_000_000);
    DemoCatalogData.Products.Should().OnlyContain(p => p.Title.StartsWith("[DEMO] "));
    DemoCatalogData.Products.Select(p => p.Gtin).Should().OnlyHaveUniqueItems().And.OnlyContain(g => g.StartsWith("20"));
}
```

  Integration `DemoSeedTests.Seed_Creates_400_Sellable_Variants_And_Is_Idempotent` (host via `CatalogTestHost`; run twice; `ISellableVariantPort.GetSellableAsync()` count `== 400` after both; `IPriceQuotePort.QuoteAsync(all, "USD")` returns 400 quotes).
- [ ] **Step 2: Run** the three unit classes and `--filter-class "*.DemoSeedTests"`. Expected: FAIL.
- [ ] **Step 3: Implement**; in `Program.cs`: `if (args.Contains("--seed")) { guard → scope → RunAsync → print summary → return; }`.
- [ ] **Step 4: Run** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(seed): fictitious demo catalog with --seed, refused in production" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 29: Phase 2 gate

**Files:**
- Create: `docs/reports/phase-2.md`
- Modify: `AGENTS.md` (module list gains Pricing, Catalog Brand; the Pricing→Settings row), `docs/TESTING-GUARANTEES.md` (new suites)

- [ ] **Step 1: Build** `dotnet build EwaysVNext.slnx`. Expected: 0 errors, 0 warnings-as-errors.
- [ ] **Step 2: Test** `dotnet test EwaysVNext.slnx` (Docker running). Expected: all green, including `Rate_Age_Boundary_Is_Inclusive` and `Aed_Without_Usd_Rate_Is_Unavailable`.
- [ ] **Step 3: Mutation-check two guards** (AGENTS.md "mutation-test every guard"): change `<=` to `<` in the resolver's age check → `Rate_Age_Boundary_Is_Inclusive` fails; remove the `DeactivatedByAdmin` check from `Product.Activate` → `Publish_Updates_But_Never_Reactivates_Admin_Deactivated_Product` fails; restore both.
- [ ] **Step 4: Write** `docs/reports/phase-2.md`: what works (per task), incomplete items, risks (ECB availability and the 72h window; merge redirect chains), reproduction commands (`--migrate`, `--seed`, the test commands above).
- [ ] **Step 5: Commit** `git commit -m "docs: phase 2 report" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

# Phase 3 — Storefront materialized views and shop browse API

**Read-path invariant (ADR-0020):** the views only decide what is *shown*. Cart, checkout and
composition read live tables through `ISellableVariantPort` and `IPriceQuotePort`. Nothing in this
phase may be consumed by a purchasing path.

**Popularity / best sellers [design call]:** `sales` tables do not exist until phase 4, and a view
cannot reference a missing table. V1 views ship `popularity_30d = 0`, and `mv_home` emits
`best_sellers` rows only `WHERE popularity_30d > 0`, so the section is **empty** (not fake) until
sales exist. **Phase 4–5 obligation:** the task in the phase 4–5 plan that creates the
`sales.orders` / `sales.order_lines` migration must, in the same task, add
`StorefrontViewSql.ProductV2` and `StorefrontViewSql.HomeV2` and a migration
`StorefrontPopularityFromSales` that runs `DROP MATERIALIZED VIEW catalog.mv_home; DROP MATERIALIZED
VIEW catalog.mv_storefront_product;`, recreates both with their V1 indexes, and replaces
`0::bigint AS popularity_30d` with
`COALESCE((SELECT sum(ol.quantity) FROM sales.order_lines ol JOIN sales.orders o ON o.id = ol.order_id
WHERE ol.product_id = v.product_id AND o.created_at >= now() - interval '30 days'
AND o.order_status <> 'Cancelled'), 0)::bigint AS popularity_30d` (adapting only column names to the
phase 4 schema), plus an integration test that a completed order moves its product into `best_sellers`.

### Task 30: Storefront view SQL (V1) and migration

**Files:**
- Create: `src/Database/Catalog/Views/StorefrontViewSql.cs`, migration `StorefrontViewsV1`
- Test: `tests/IntegrationTests/Storefront/StorefrontViewTests.cs`

**Interfaces:**
- Produces: `public static class StorefrontViewSql { public const string VariantV1, ProductV1, FacetsV1, HomeV1, IndexesV1; public static readonly IReadOnlyList<string> RefreshOrder = ["catalog.mv_storefront_variant", "catalog.mv_storefront_product", "catalog.mv_category_facets", "catalog.mv_home"]; }`.
  Migration `Up` = `Sql(VariantV1); Sql(ProductV1); Sql(FacetsV1); Sql(HomeV1); Sql(IndexesV1);` (views are created `WITH DATA`, so `CONCURRENTLY` works from the first refresh); `Down` drops in reverse order. The migration is generated after `PricingPrices` and `CatalogProductsRewrite` (source tables first).

`VariantV1` (grain: variant × enabled currency; eligible = Active product, active variant, active
brand, primary category inside the active category tree, a price, and an override, a base-currency
match or a non-expired resolved rate):

```sql
CREATE MATERIALIZED VIEW catalog.mv_storefront_variant AS
WITH RECURSIVE cat_tree AS (
    SELECT c.id, ARRAY[c.id]::bigint[] AS path_ids
    FROM catalog.categories c WHERE c.parent_id IS NULL AND c.status = 1
  UNION ALL
    SELECT c.id, t.path_ids || c.id
    FROM catalog.categories c JOIN cat_tree t ON c.parent_id = t.id WHERE c.status = 1
), eligible AS (
    SELECT v.id AS variant_id, p.id AS product_id, p.slug, v.sku, p.title,
           lower(unaccent(p.title)) AS title_norm, b.id AS brand_id, b.display_name AS brand_name,
           p.primary_category_id AS category_id, t.path_ids AS ancestor_category_ids,
           v.attribute_values, p.created_at,
           (SELECT m.object_key FROM catalog.product_media m
             WHERE m.product_id = p.id AND m.usage_rights = 'Allowed'
             ORDER BY m.sort_order, m.id LIMIT 1) AS primary_image_key,
           vp.id AS variant_price_id, vp.base_amount_minor, vp.base_currency
    FROM catalog.variants v
    JOIN catalog.products p ON p.id = v.product_id AND p.status = 'Active'
    JOIN catalog.brands b ON b.id = p.brand_id AND b.is_active
    JOIN cat_tree t ON t.id = p.primary_category_id
    JOIN pricing.variant_prices vp ON vp.variant_id = v.id
    WHERE v.is_active
), priced AS (
    SELECT e.*, cur.code AS currency,
           CASE WHEN co.amount_minor IS NOT NULL THEN co.amount_minor
                WHEN cur.code = e.base_currency THEN e.base_amount_minor
                ELSE pricing.round_half_even(e.base_amount_minor * r.rate)::bigint END AS price_minor,
           CASE WHEN co.amount_minor IS NOT NULL THEN 'Override'
                WHEN cur.code = e.base_currency THEN 'Base' ELSE 'Fx' END AS price_source,
           CASE WHEN co.amount_minor IS NULL AND cur.code <> e.base_currency
                THEN r.valid_until END AS fx_valid_until
    FROM eligible e
    CROSS JOIN pricing.currencies cur
    LEFT JOIN pricing.currency_overrides co
           ON co.variant_price_id = e.variant_price_id AND co.currency = cur.code
          AND co.price_list = 'default'
    LEFT JOIN pricing.resolved_fx_rates r
           ON r.base = e.base_currency AND r.quote = cur.code
          AND (r.valid_until IS NULL OR r.valid_until >= now())
    WHERE cur.is_enabled
)
SELECT variant_id, product_id, slug, sku, title, title_norm, brand_id, brand_name, category_id,
       ancestor_category_ids, attribute_values, primary_image_key, created_at, currency,
       price_minor, price_source, fx_valid_until,
       setweight(to_tsvector('simple', unaccent(title)), 'A')
    || setweight(to_tsvector('simple', unaccent(brand_name)), 'B')
    || setweight(to_tsvector('simple', unaccent(coalesce(
           (SELECT string_agg(x.value, ' ') FROM jsonb_each_text(attribute_values) x), ''))), 'C')
       AS search
FROM priced
WHERE price_minor >= 1;
```

`ProductV1` (grain: product × currency):

```sql
CREATE MATERIALIZED VIEW catalog.mv_storefront_product AS
SELECT v.product_id, v.currency, min(v.slug) AS slug, min(v.title) AS title,
       min(v.title_norm) AS title_norm, min(v.brand_id) AS brand_id, min(v.brand_name) AS brand_name,
       min(v.category_id) AS category_id, min(v.ancestor_category_ids) AS ancestor_category_ids,
       min(v.price_minor) AS min_price_minor, max(v.price_minor) AS max_price_minor,
       count(*) AS variant_count, min(v.primary_image_key) AS primary_image_key,
       (SELECT coalesce(array_agg(m.object_key ORDER BY m.sort_order, m.id), '{}')
          FROM catalog.product_media m
         WHERE m.product_id = v.product_id AND m.usage_rights = 'Allowed') AS image_keys,
       (SELECT coalesce(jsonb_object_agg(f.key, f.vals), '{}'::jsonb)
          FROM (SELECT kv.key, jsonb_agg(DISTINCT kv.value ORDER BY kv.value) AS vals
                  FROM catalog.mv_storefront_variant v2, jsonb_each_text(v2.attribute_values) kv
                 WHERE v2.product_id = v.product_id AND v2.currency = v.currency
                 GROUP BY kv.key) f) AS facet_values,
       0::bigint AS popularity_30d,
       min(v.created_at) AS created_at
FROM catalog.mv_storefront_variant v
GROUP BY v.product_id, v.currency;
```

`FacetsV1` (grain: category × currency × kind × key × value; category `0` = whole catalog;
attribute facets only for keys with a non-removed `is_filterable` assignment on the variant's
category path; exclusions are not applied — documented limitation):

```sql
CREATE MATERIALIZED VIEW catalog.mv_category_facets AS
WITH scoped AS (
    SELECT s.category_id, v.product_id, v.currency, v.price_minor, v.brand_id, v.brand_name,
           v.attribute_values, v.ancestor_category_ids
    FROM catalog.mv_storefront_variant v
    CROSS JOIN LATERAL unnest(v.ancestor_category_ids || 0::bigint) AS s(category_id)
)
SELECT sc.category_id, sc.currency, 'attribute'::text AS facet_kind, kv.key AS facet_key,
       kv.value AS facet_value, count(DISTINCT sc.product_id) AS product_count
FROM scoped sc CROSS JOIN LATERAL jsonb_each_text(sc.attribute_values) kv
WHERE EXISTS (SELECT 1 FROM catalog.category_attribute_assignments a
              JOIN catalog.attribute_blueprints ab ON ab.id = a.attribute_blueprint_id
              WHERE ab.canonical_key = kv.key AND a.is_filterable AND a.removed_at IS NULL
                AND a.category_id = ANY (sc.ancestor_category_ids))
GROUP BY sc.category_id, sc.currency, kv.key, kv.value
UNION ALL
SELECT category_id, currency, 'brand', brand_id::text, min(brand_name), count(DISTINCT product_id)
FROM scoped GROUP BY category_id, currency, brand_id
UNION ALL
SELECT category_id, currency, 'price', 'bucket', b.lower_minor::text, count(DISTINCT product_id)
FROM scoped CROSS JOIN LATERAL (SELECT CASE
        WHEN price_minor < 1000 THEN 0 WHEN price_minor < 5000 THEN 1000
        WHEN price_minor < 10000 THEN 5000 WHEN price_minor < 50000 THEN 10000
        WHEN price_minor < 100000 THEN 50000 WHEN price_minor < 500000 THEN 100000
        WHEN price_minor < 1000000 THEN 500000 ELSE 1000000 END AS lower_minor) b
GROUP BY category_id, currency, b.lower_minor;
```

`HomeV1` (grain: section × currency × rank):

```sql
CREATE MATERIALIZED VIEW catalog.mv_home AS
SELECT 'new_arrivals'::text AS section, x.currency, x.rank, x.product_id, NULL::bigint AS category_id
FROM (SELECT currency, product_id,
             row_number() OVER (PARTITION BY currency ORDER BY created_at DESC, product_id DESC) AS rank
      FROM catalog.mv_storefront_product) x WHERE x.rank <= 12
UNION ALL
SELECT 'best_sellers', x.currency, x.rank, x.product_id, NULL
FROM (SELECT currency, product_id,
             row_number() OVER (PARTITION BY currency ORDER BY popularity_30d DESC, product_id DESC) AS rank
      FROM catalog.mv_storefront_product WHERE popularity_30d > 0) x WHERE x.rank <= 12
UNION ALL
SELECT 'featured_categories', x.currency, x.rank, NULL, x.category_id
FROM (SELECT p.currency, p.ancestor_category_ids[1] AS category_id,
             row_number() OVER (PARTITION BY p.currency
                                ORDER BY count(*) DESC, p.ancestor_category_ids[1]) AS rank
      FROM catalog.mv_storefront_product p
      GROUP BY p.currency, p.ancestor_category_ids[1]) x WHERE x.rank <= 8;
```

`IndexesV1`:

```sql
CREATE UNIQUE INDEX ux_mv_sv_grain ON catalog.mv_storefront_variant (variant_id, currency);
CREATE INDEX ix_mv_sv_search ON catalog.mv_storefront_variant USING gin (search);
CREATE INDEX ix_mv_sv_ancestors ON catalog.mv_storefront_variant USING gin (ancestor_category_ids);
CREATE INDEX ix_mv_sv_title_trgm ON catalog.mv_storefront_variant USING gin (title_norm gin_trgm_ops);
CREATE INDEX ix_mv_sv_currency_price ON catalog.mv_storefront_variant (currency, price_minor);
CREATE INDEX ix_mv_sv_currency_created ON catalog.mv_storefront_variant (currency, created_at);
CREATE INDEX ix_mv_sv_product ON catalog.mv_storefront_variant (product_id, currency);
CREATE UNIQUE INDEX ux_mv_sp_grain ON catalog.mv_storefront_product (product_id, currency);
CREATE UNIQUE INDEX ux_mv_sp_slug ON catalog.mv_storefront_product (slug, currency);
CREATE INDEX ix_mv_sp_ancestors ON catalog.mv_storefront_product USING gin (ancestor_category_ids);
CREATE INDEX ix_mv_sp_title_trgm ON catalog.mv_storefront_product USING gin (title_norm gin_trgm_ops);
CREATE INDEX ix_mv_sp_currency_price ON catalog.mv_storefront_product (currency, min_price_minor);
CREATE INDEX ix_mv_sp_currency_created ON catalog.mv_storefront_product (currency, created_at);
CREATE UNIQUE INDEX ux_mv_cf_grain ON catalog.mv_category_facets
    (category_id, currency, facet_kind, facet_key, facet_value);
CREATE UNIQUE INDEX ux_mv_home_grain ON catalog.mv_home (section, currency, rank);
```

- [ ] **Step 1: Write failing tests** (`[Collection(PostgresSuite.Name)]`): `Every_View_Has_A_Unique_Grain_Index` (the four `ux_*` names exist in `pg_indexes` and `indexdef` contains `UNIQUE`), `Every_View_Refreshes_Concurrently_On_Real_Postgres` (`REFRESH MATERIALIZED VIEW CONCURRENTLY <v>` for each of `StorefrontViewSql.RefreshOrder` against the migrated container — no exception).
- [ ] **Step 2: Run** `--filter-class "*.StorefrontViewTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** the constants and the migration.
- [ ] **Step 4: Run** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(storefront): materialized views v1 with grain and search indexes" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 31: Refresher, refresh job, nightly backstop; acceptance 11

**Files:**
- Create: `src/Modules/Catalog/Adapter/Outbound/Storefront/{StorefrontViewRefresher.cs, RefreshStorefrontViewsJobHandler.cs}`, `tests/IntegrationTests/Support/StorefrontScenario.cs`
- Modify: `src/Modules/Catalog/Adapter/Outbound/DependencyInjection.cs`
- Test: `tests/UnitTests/Catalog/RefreshStorefrontViewsJobHandlerTests.cs`, `tests/IntegrationTests/Storefront/StorefrontViewTests.cs`

**Interfaces:**
- Produces: `public interface IStorefrontViewRefresher { Task RefreshAllAsync(CancellationToken ct = default); }` (adapter-level, public so tests resolve it). For each name in `StorefrontViewSql.RefreshOrder`, one separate command `REFRESH MATERIALIZED VIEW CONCURRENTLY <name>` (names are constants, never input); `PostgresException` with `SqlState == "55000"` (view not yet populated) → refresh that view without `CONCURRENTLY`; command timeout 300 s; logs per-view duration.
- Produces: `RefreshStorefrontViewsJobHandler : IJobHandler<RefreshStorefrontViewsPayload>` (`Type = JobTypes.RefreshStorefrontViews`) → `JobOutcome.Completed`; `NpgsqlException { IsTransient: true }` → `RetryLater(TimeSpan.FromSeconds(30), ...)`.
  DI: `AddJobHandler<RefreshStorefrontViewsJobHandler, RefreshStorefrontViewsPayload>()` and `AddRecurringJob(JobTypes.RefreshStorefrontViews, TimeSpan.FromHours(24))` (the "nightly" backstop is a 24 h interval; the contract has no wall-clock schedule). Refresh jobs enqueued during phase 2 had no handler; that is harmless (display only).
- Produces test support `StorefrontScenario.CreateAsync(IServiceProvider sp)`: under a unique root category `sf-{guid}` with child `kettles-{guid}` and a filterable Option attribute `colour` assigned to the root — brands A and B; P1 "Steel Kettle" (A; variants colour Red USD 20.00, Blue USD 25.00); P2 "Glass Kettle" (B; one variant EUR 30.00); P3 "Draft Kettle" (Draft, priced); P4 "Retired Kettle" (activated, then admin-deactivated); FX via `IFxRefreshService.IngestAsync(new FxProviderBatch("ecb", today, [USD 1.0812, SGD 1.4321, GBP 0.84215]))`; then `RefreshAllAsync()`. Returns the ids, variant ids, slugs and staff/super `AdminActor`s.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public async Task Acceptance11_Deactivated_Variant_Leaves_Plp_After_Refresh()
{
    var s = await StorefrontScenario.CreateAsync(sp);
    await admin.SetVariantActiveAsync(s.P1, s.P1Blue, active: false, s.Staff);
    (await sellable.GetSellableAsync([s.P1Blue])).Should().BeEmpty();     // never purchasable, even before refresh
    (await VariantRowsAsync(s.P1Blue)).Should().NotBeEmpty();               // still displayed until refresh (allowed)
    await refresher.RefreshAllAsync();
    (await VariantRowsAsync(s.P1Blue)).Should().BeEmpty();
    (await ProductRowsAsync(s.P1)).Should().OnlyContain(r => r.VariantCount == 1);
}

[Fact]
public async Task Plp_Views_Never_Contain_Draft_Or_Deactivated_Products()
{
    var s = await StorefrontScenario.CreateAsync(sp);
    (await ProductRowsAsync(s.P3)).Should().BeEmpty();
    (await ProductRowsAsync(s.P4)).Should().BeEmpty();
    (await ProductRowsAsync(s.P1)).Select(r => r.Currency)
        .Should().BeEquivalentTo("USD", "EUR", "SGD", "AED", "GBP");
}
```

  `VariantRowsAsync` / `ProductRowsAsync` are raw `SqlQueryRaw` helpers in the test class. Unit: `Handler_Refreshes_Then_Completes`, `Transient_Npgsql_Error_Retries_Later`.
- [ ] **Step 2: Run** `--filter-class "*.StorefrontViewTests"` and `--filter-class "*.RefreshStorefrontViewsJobHandlerTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** and register.
- [ ] **Step 4: Run** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(storefront): concurrent view refresh job with nightly backstop" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 32: View price parity with `IPriceQuotePort`; stale and disabled currencies

**Files:**
- Test: `tests/IntegrationTests/Storefront/StorefrontPriceParityTests.cs`

**Interfaces:** consumes `IPriceQuotePort`, `IFxRefreshService`, `IPricingAdminService`, `IPricingChangeNotifier`, `IStorefrontViewRefresher`.

- [ ] **Step 1: Write the tests**
  - `View_Prices_Equal_Live_Quotes_For_Every_Currency` — after `StorefrontScenario`, for each enabled currency every `(variant_id, price_minor, price_source)` view row for the scenario's variants equals `QuoteAsync(ids, currency, now)` (`ReferenceMinor`, `Source.ToString()`); add an SGD override on P1 Red, refresh, re-check.
  - `Half_Even_Rounding_Agrees_Between_View_And_Quote` — a variant priced 1.25 EUR (`125`) with an exact-pair FX override EUR→USD `1.1` → view and quote both give `138` (137.5 → 138); with override `1.3` both give `162` (162.5 → 162).
  - `Stale_Rate_Drops_Converted_Prices_After_Reprojection` — ingest the scenario rates with `FetchedAt` 73 h ago (a `FixedClock` swapped in through `ConfigureTestServices`), `FxChangedAsync`, refresh → P2 (EUR base) keeps only its `EUR` row; P1 (USD base) keeps `USD` and `AED` (peg) rows.
  - `Disabled_Currency_Leaves_The_Views` — `SetCurrencyEnabledAsync("GBP", false, super)`, refresh → no `GBP` rows.
- [ ] **Step 2: Run** `--filter-class "*.StorefrontPriceParityTests"`. Expected: PASS when Tasks 23 and 30 are correct. A failure is a bug in the view SQL or the projection: fix that, never the test.
- [ ] **Step 3: Mutation check:** temporarily replace `pricing.round_half_even(...)` with `round(...)` in `VariantV1` (fresh container) → `Half_Even_Rounding_Agrees_Between_View_And_Quote` fails on 162.5; restore.
- [ ] **Step 4: Commit** `git commit -m "test(storefront): view prices match live quotes; stale and disabled currencies leave views" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 33: Storefront queries — home, categories, product detail, currency guard

**Files:**
- Create: `src/Modules/Catalog/QueryApplication/Storefront/{IStorefrontQueries.cs, StorefrontQueries.cs, StorefrontSql.cs, StorefrontDtos.cs, StorefrontOptions.cs}`
- Modify: `src/Modules/Catalog/QueryApplication/DependencyInjection.cs`
- Test: `tests/IntegrationTests/Storefront/StorefrontQueryTests.cs`

**Interfaces:**
- Produces DTOs:
```csharp
public sealed record MoneyDto(string Amount, string Currency);   // Amount = minor / 100 formatted "0.00", invariant
public sealed record ProductCardDto(long ProductId, string Slug, string Title, long BrandId, string BrandName, long CategoryId,
  MoneyDto MinPrice, MoneyDto MaxPrice, int VariantCount, string? ImageUrl);
public sealed record CategoryTileDto(long CategoryId, string Name, string Slug, long ProductCount);
public sealed record HomeDto(string Currency, IReadOnlyList<ProductCardDto> NewArrivals, IReadOnlyList<ProductCardDto> BestSellers,
  IReadOnlyList<CategoryTileDto> FeaturedCategories);
public sealed record CategoryCrumbDto(long CategoryId, string Name, string Slug);
public sealed record VariantOfferDto(long VariantId, string Sku, IReadOnlyDictionary<string, string> Attributes, MoneyDto Price);
public sealed record ProductDetailDto(long ProductId, string Slug, string Title, string? ShortDescription, string? Description,
  long BrandId, string BrandName, IReadOnlyList<CategoryCrumbDto> Breadcrumb, IReadOnlyList<string> ImageUrls,
  IReadOnlyList<VariantOfferDto> Variants, IReadOnlyDictionary<string, IReadOnlyList<string>> AttributeOptions);
public sealed class StorefrontOptions { public string MediaBaseUrl { get; set; } = "/media/"; }   // config section "Storefront"
```
- Produces on `IStorefrontQueries`: `Task<Result<HomeDto>> GetHomeAsync(string currency, CancellationToken ct = default)`, `Task<IReadOnlyList<CategoryTreeNodeDto>> GetCategoryTreeAsync(CancellationToken ct = default)` (delegates to `ICategoryQueries.GetTreeAsync`), `Task<Result<ProductDetailDto>> GetProductBySlugAsync(string slug, string currency, CancellationToken ct = default)` (Task 34 adds the rest).
- Rules: the currency must satisfy `SELECT EXISTS (SELECT 1 FROM pricing.currencies WHERE code = @c AND is_enabled)` (cross-schema read, ADR-0020) → else `CURRENCY_NOT_ENABLED` (422). Every variant-view read adds `(fx_valid_until IS NULL OR fx_valid_until >= now())`, so a rate that expired after the last refresh is never shown. PDP descriptions and breadcrumb come from live `catalog.products` / `catalog.categories`, but only for a product present in `mv_storefront_product` for `(slug, currency)` → else `NOT_FOUND` (404). Raw SQL runs through `db.Database.SqlQueryRaw<TRow>(sql, NpgsqlParameter[])`; every selected column is aliased to the row property in double quotes (`p.product_id AS "ProductId"`), so mapping never depends on naming-convention behaviour for ad-hoc types; row types are `sealed class` with `init` properties.

- [ ] **Step 1: Write failing tests** (on `StorefrontScenario`): `Home_Has_New_Arrivals_Newest_First_And_No_Best_Sellers_Before_Sales` (`BestSellers` empty, `NewArrivals` ordered by creation desc), `Home_Featured_Categories_Are_At_Most_Eight_With_Products`, `Pdp_Returns_Variants_Prices_Media_And_Breadcrumb` (P1 in USD: offers `"20.00"` and `"25.00"`, `AttributeOptions["colour"]` equals `["Blue","Red"]`, breadcrumb root→child, image URL starts with `/media/products/`), `Pdp_Of_Draft_Is_Not_Found`, `Unknown_Or_Disabled_Currency_Is_CURRENCY_NOT_ENABLED`.
- [ ] **Step 2: Run** `--filter-class "*.StorefrontQueryTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(storefront): home, category tree and product detail queries" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 34: Storefront queries — PLP filters, sort, cursor, search with trigram fallback, facets

**Files:**
- Create: `src/Modules/Catalog/QueryApplication/Storefront/StorefrontCursor.cs`
- Modify: `IStorefrontQueries.cs`, `StorefrontQueries.cs`, `StorefrontSql.cs`, `StorefrontDtos.cs`
- Test: `tests/UnitTests/Catalog/StorefrontCursorTests.cs`, `tests/IntegrationTests/Storefront/StorefrontQueryTests.cs`

**Interfaces:**
- Produces:
```csharp
public enum StorefrontSort { Relevance, Newest, PriceAsc, PriceDesc, Popular }
public sealed record ProductListQuery(string Currency, long? CategoryId, IReadOnlyList<long>? BrandIds,
  IReadOnlyDictionary<string, IReadOnlyList<string>>? Attributes, long? PriceMinMinor, long? PriceMaxMinor,
  string? Q, StorefrontSort? Sort, string? Cursor, int? Limit);
public sealed record ProductPageDto(IReadOnlyList<ProductCardDto> Items, string? NextCursor, bool UsedTypoFallback);
public sealed record FacetValueDto(string Value, string Label, long Count);
public sealed record FacetGroupDto(string Key, IReadOnlyList<FacetValueDto> Values);
public sealed record PriceBucketDto(MoneyDto From, MoneyDto? To, long Count);
public sealed record FacetSetDto(IReadOnlyList<FacetGroupDto> Attributes, IReadOnlyList<FacetValueDto> Brands, IReadOnlyList<PriceBucketDto> PriceBuckets);
// IStorefrontQueries additions
Task<Result<ProductPageDto>> GetProductsAsync(ProductListQuery query, CancellationToken ct = default);
Task<Result<ProductPageDto>> SearchAsync(string q, string currency, string? cursor, int? limit, CancellationToken ct = default); // = GetProductsAsync with Q only
Task<Result<FacetSetDto>> GetFacetsAsync(long? categoryId, string currency, CancellationToken ct = default);          // null → category 0
public sealed record StorefrontCursor(string Mode, StorefrontSort Sort, string Key, long ProductId) {
  public string Encode(); public static bool TryDecode(string? text, out StorefrontCursor? cursor); }       // base64url(JSON)
```
- Validation → `VALIDATION_FAILED` (400): `Limit` 1–48 (default 24); `Q` ≤ 200 chars; `PriceMin > PriceMax`; `Relevance` without `Q`; more than 20 attribute keys or 50 brand ids; an undecodable cursor, or one whose `Sort`/`Mode` differs from the request. Default sort: `Relevance` with `Q`, otherwise `Newest`.
- PLP SQL shape (`StorefrontSql.ProductPage`; bracketed clauses are appended only when that filter is present; every value is a parameter):
```sql
WITH q AS (SELECT websearch_to_tsquery('simple', unaccent(@q)) AS query),   -- only with Q
matches AS (
  SELECT v.product_id, min(v.price_minor) AS price_minor,
         max(ts_rank(v.search, q.query)) AS rank          -- Mode "trgm": max(similarity(v.title_norm, lower(unaccent(@q))))
  FROM catalog.mv_storefront_variant v [CROSS JOIN q]
  WHERE v.currency = @currency AND (v.fx_valid_until IS NULL OR v.fx_valid_until >= now())
    [AND v.ancestor_category_ids @> ARRAY[@category_id]::bigint[]]
    [AND v.brand_id = ANY(@brand_ids)]
    [AND (v.attribute_values ->> @k0) = ANY(@v0)] [AND (v.attribute_values ->> @k1) = ANY(@v1)] ...
    [AND v.price_minor >= @price_min] [AND v.price_minor <= @price_max]
    [AND v.search @@ q.query]                              -- Mode "fts"
    [AND v.title_norm % lower(unaccent(@q))]               -- Mode "trgm"
  GROUP BY v.product_id)
SELECT ... FROM matches m
JOIN catalog.mv_storefront_product p ON p.product_id = m.product_id AND p.currency = @currency
WHERE <keyset>
ORDER BY <sort key>, p.product_id
LIMIT @limit_plus_one;
```
  Sort keys: `PriceAsc` `m.price_minor ASC`; `PriceDesc` `m.price_minor DESC`; `Newest` `p.created_at DESC`; `Popular` `p.popularity_30d DESC`; `Relevance` `m.rank DESC`; tie-break always `p.product_id ASC`. Keyset for a DESC key: `(key < @ck OR (key = @ck AND p.product_id > @cid))`; for an ASC key: `(key, p.product_id) > (@ck, @cid)`. Attribute filters apply per variant (all keys on the same variant). The card's `MinPrice` is the **matched** variants' minimum, `MaxPrice` the product's maximum. Fetch `limit + 1` rows; the extra row means `NextCursor` is built from row `limit`. Trigram fallback: only when `Q` is set, `Cursor` is null and the FTS page is empty → rerun in Mode `"trgm"` (default `pg_trgm.similarity_threshold` 0.3), `UsedTypoFallback = true`; its cursors carry `Mode = "trgm"`.
- Facets: read `mv_category_facets` for `(categoryId ?? 0, currency)`; attribute groups ordered by key, values by count desc then value; price buckets `From = lower`, `To` = the next bucket's lower bound (null for the last).

- [ ] **Step 1: Write failing tests:** unit `Cursor_Roundtrips`, `Garbage_Cursor_Does_Not_Decode`; integration on `StorefrontScenario`:
  `Plp_Category_Includes_Descendants_And_Excludes_Draft_And_Deactivated` (root id → `{P1, P2}`),
  `Plp_Brand_Filter`,
  `Plp_Attribute_Filter_Matches_Same_Variant` (`colour=[Blue]` → P1 with `MinPrice.Amount == "25.00"`),
  `Plp_Price_Range_Is_In_Requested_Currency` (USD `PriceMinMinor = 2600` → only P2, `MinPrice.Amount == "32.44"` = round_half_even(3000 × 1.0812)),
  `Plp_Cursor_Pages_Without_Gaps_Or_Duplicates` (`Limit 1`, `PriceAsc`: page 1 P1, page 2 P2, then `NextCursor == null`),
  `Search_Supports_Websearch_Syntax` (`"kettle -glass"` → `{P1}`),
  `Search_Typo_Falls_Back_To_Trigram` (`"stell kettel"` → `UsedTypoFallback == true`, contains P1),
  `Facets_Count_Distinct_Products` (root: colour Red 1 and Blue 1; brands A 1 and B 1; bucket counts sum ≥ 2),
  `Relevance_Without_Q_Is_Validation_Failed`.
- [ ] **Step 2: Run** `--filter-class "*.StorefrontCursorTests"` and `--filter-class "*.StorefrontQueryTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(storefront): PLP filters, keyset paging, search with trigram fallback, facets" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 35: Shop REST endpoints (anonymous)

**Files:**
- Create: `src/Modules/Catalog/Adapter/Inbound/Rest/Shop/{ShopEndpoints.cs, ShopDtos.cs}`
- Test: `tests/IntegrationTests/Storefront/ShopRestTests.cs`

**Interfaces:**
- Produces (all `.AllowAnonymous()`, every response through `EndpointResult`):
  `GET /api/v1/shop/home?currency=USD`; `GET /api/v1/shop/categories`;
  `GET /api/v1/shop/products?currency=&categoryId=&brandIds=1&brandIds=2&attr.colour=Red&attr.colour=Blue&priceMin=10.00&priceMax=99.99&q=&sort=priceAsc&cursor=&limit=`;
  `GET /api/v1/shop/products/{slug}?currency=`; `GET /api/v1/shop/facets?currency=&categoryId=`;
  `GET /api/v1/shop/search?q=&currency=&cursor=&limit=`. (`GET /api/v1/shop/currencies` is Pricing's, Task 27.)
  `attr.<key>` query keys become `Attributes`; `priceMin`/`priceMax` parse only through `MoneyText.TryParseMinor` (else 400 `VALIDATION_FAILED`); missing `currency` → 400 `VALIDATION_FAILED`.

- [ ] **Step 1: Write failing tests:** `Plp_Never_Shows_Draft_Or_Deactivated` (HTTP, scenario root category, each of the five currencies: returned slugs ⊆ {P1, P2}), `Plp_Attribute_And_Price_Query_Params_Are_Applied`, `Bad_Price_Format_Is_400` (`priceMin=10`), `Pdp_By_Slug_Is_Anonymous_200`, `Unknown_Slug_Is_404_NOT_FOUND`, `Search_Endpoint_Returns_Next_Cursor`.
- [ ] **Step 2: Run** `--filter-class "*.ShopRestTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(shop): anonymous storefront REST endpoints" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 36: Shop GraphQL (anonymous)

**Files:**
- Create: `src/Modules/Catalog/Adapter/Inbound/GraphQL/Shop/{ShopQueries.cs, ShopTypes.cs}`
- Modify: `src/Modules/Catalog/Adapter/Inbound/GraphQL/DependencyInjection.cs`
- Test: `tests/IntegrationTests/Storefront/ShopGraphQLTests.cs`

**Interfaces:**
- Produces on the root `Query` (`[AllowAnonymous]`; prefixed `shop*` because the admin `categories` field already exists on the same `/graphql` root): `shopHome(currency: String!)`, `shopCategories`, `shopProducts(currency: String!, filter: ShopProductFilterInput, sort: StorefrontSort, after: String, first: Int)`, `shopFacets(currency: String!, categoryId: ID)`, `shopProduct(slug: String!, currency: String!)`, `shopSearch(q: String!, currency: String!, after: String, first: Int)`.
  `ShopProductFilterInput { categoryId: ID, brandIds: [ID!], attributes: [AttributeFilterInput!], priceMin: String, priceMax: String, q: String }`; `AttributeFilterInput { key: String!, values: [String!]! }`. Failures surface as GraphQL errors whose `extensions.code` is the `Error.Code` (`ResultGraphQLExtensions.ValueOrThrow`). `currencies` comes from Task 27.

- [ ] **Step 1: Write failing tests:** `ShopProducts_Matches_Rest_For_Same_Filter` (same scenario filter through REST and GraphQL → same product id sequence), `ShopProduct_Draft_Slug_Is_NOT_FOUND`, `Anonymous_ShopHome_Succeeds`.
- [ ] **Step 2: Run** `--filter-class "*.ShopGraphQLTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** and register the types.
- [ ] **Step 4: Run** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat(shop): anonymous storefront GraphQL" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 37: Phase 3 gate

**Files:**
- Create: `docs/reports/phase-3.md`
- Modify: `docs/adr/0020-*.md` (record `pricing.resolved_fx_rates`, the read-time `fx_valid_until` filter, V1→V2 view versioning and the phase 4 popularity obligation), `AGENTS.md` (API table: `/api/v1/shop/*`, `/api/v1/admin/catalog/*`, `/api/v1/admin/pricing/*`)

- [ ] **Step 1: Build** `dotnet build EwaysVNext.slnx`. Expected: 0 errors.
- [ ] **Step 2: Test** `dotnet test EwaysVNext.slnx` (Docker running). Expected: all green.
- [ ] **Step 3: Mutation-check:** temporarily remove `AND p.status = 'Active'` from `VariantV1` (fresh container) → `Plp_Never_Shows_Draft_Or_Deactivated` and `Plp_Views_Never_Contain_Draft_Or_Deactivated_Products` fail; restore.
- [ ] **Step 4: Write** `docs/reports/phase-3.md`: what works; incomplete (best sellers empty until phase 4; facets ignore attribute exclusions and the current filter selection); risks (refresh duration on large catalogs; the 24 h backstop is an interval, not a wall-clock time; the views depend on `pricing` column names); reproduction (`--migrate`, `--seed`, `curl "http://localhost:5000/api/v1/shop/products?currency=USD&q=demo"`).
- [ ] **Step 5: Commit** `git commit -m "docs: phase 3 report" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

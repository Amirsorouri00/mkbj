# API-First B2B E-Commerce MVP — Implementation Plan (master)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the whole PRD (`docs/product-scope/ecommerce_mvp_prd.md`) end to end, on the
existing EwaysVNext modular monolith, using Postgres, MinIO, Keycloak, REST and GraphQL, with
production Docker builds under `build/`.

**Architecture:**
- Hexagonal modules on one `EwaysVNextDbContext` (now Npgsql). The modules are Partners, Customer,
  Catalog, Pricing, Sourcing, Sales, Settings and Admin. The Iran-specific modules (Payment,
  Location, Notification) are deleted.
- A partner order is atomic because it is **one `Order` aggregate** saved by one
  `SaveChangesAsync`, not because of a transaction.
- Storefront browse reads come from Postgres materialized views, refreshed by an in-database job
  queue (`FOR UPDATE SKIP LOCKED`).

**Tech Stack:**
- .NET 10 SDK 10.0.100, EF Core 10 + `Npgsql.EntityFrameworkCore.PostgreSQL`,
  `EFCore.NamingConventions`
- HotChocolate 16, Keycloak (self-hosted)
- MinIO via `AWSSDK.S3`, QuestPDF, AngleSharp, CsvHelper, ClosedXML
- Brave Search API, Anthropic API (`claude-sonnet-5-5`)
- xUnit v3, AwesomeAssertions, NSubstitute, FsCheck, NetArchTest
- Testcontainers (PostgreSql, Minio), k6

**Spec:** `docs/superpowers/specs/2026-10-28-ecommerce-mvp-design.md`. Executors read the spec
and this file before starting any phase plan.

## Phase plans (execute in order; one continuous run [spec §14])

| # | Plan file | Depends on |
|---|---|---|
| 0 | `docs/superpowers/plans/2026-10-28-ecommerce-mvp/phase-0-platform.md` | — |
| 1 | `docs/superpowers/plans/2026-10-28-ecommerce-mvp/phase-1-identity-tenancy.md` | 0 |
| 2–3 | `docs/superpowers/plans/2026-10-28-ecommerce-mvp/phase-2-3-catalog-pricing-storefront.md` | 0, 1 (Settings for currencies is in Pricing, not 1) |
| 4–5 | `docs/superpowers/plans/2026-10-28-ecommerce-mvp/phase-4-5-sales.md` | 0, 1, 2–3 |
| 6, 8 | `docs/superpowers/plans/2026-10-28-ecommerce-mvp/phase-6-8-admin-ops.md` | 4–5 (and 7 for import counters in the dashboard) |
| 7 | `docs/superpowers/plans/2026-10-28-ecommerce-mvp/phase-7-sourcing.md` | 0, 2–3 (may start in parallel with 4–5) |

**Ending each phase:**
- `dotnet build EwaysVNext.slnx` passes with zero warnings-as-errors.
- `dotnet test EwaysVNext.slnx` passes (Docker must be running for integration tests).
- Write a short report at `docs/reports/phase-N.md` covering: what works, what is incomplete,
  risks, and reproduction commands.
- Commit.

## Global Constraints

Every task in every phase must meet all of these. Values are copied from the spec.

**Architecture rules**
- AGENTS.md rules 1–10 hold. **Never** call `BeginTransaction`/`UseTransaction` in `src/`.
- Aggregate and entity primary keys are `long` from `IIdGenerator`. Map every root with
  `ValueGeneratedNever()`. Mark every id that crosses the wire with `[Snowflake]`.
- Aggregates do not read the clock. Services read `IClock` once per operation and pass `now` in.
- Application projects reference no EF Core types and no adapter. QueryApplication never writes.
- Cross-module references exist only as the `Adapter.Outbound → Provider.Application` rows in
  spec §3.6, and each one must be added to the architecture-test exception theory in the same task
  that introduces it.

**Money and pricing**
- Money is stored as `amount_minor bigint` + `currency char(3)`.
- Composition and band math use `long` minor units only. Never `double` or `float`.
- The band is `lo = ceil(0.95·r)` and `hi = floor(1.05·r)`, computed with integer arithmetic.
- Enabled currencies at seed: `USD, EUR, SGD, AED, GBP`, all with 2 minor units.
- AED is derived from USD at peg `3.6725`, with provenance `derived:usd-peg`.
- `Fx:MaxRateAge` defaults to `72h`. FX refresh runs every `6h` by default.
- Order bounds default to `MinUsd = 10.00` and `MaxUsd = 1,000,000.00`.
- Composition defaults: `CompositionDeadlineMs = 8000`, `CompositionMaxLines = 200`.
- Bulk defaults: `BulkDefaultMax = 100`, bulk parallelism `4`, bulk deadline `120s`.

**Invoices and fulfillment wording**
- No GST is ever charged. Documents never say "Tax Invoice". The seller is "Not GST registered".
- Every partner response and every document carries this fulfillment text, verbatim:
  "Fulfillment is an operational assertion, not verified proof of delivery".
- Document number prefixes are `PF-`, `INV-` and `CN-`, each from its own Postgres sequence. The
  order number prefix is `ORD-`. Gaps are allowed.

**Errors and responses**
- Error body: `{ code, message, retryable, correlationId, details?, items? }`.
- Every response carries an `X-Correlation-Id` header.
- Error codes are exactly the ones listed in spec §6.3.
- Cross-partner access returns **404**.

**Partner wire format**
- Partner API JSON is **snake_case** for requests and responses, exactly as in the PRD §9 sample
  (`external_order_id`, `external_customer_id`, `customer.full_name`, `payment_status`, ...).
- Status values on the partner API are upper snake: `PAID`, `PARTNER_REPORTED`, `FULFILLED`,
  `SYSTEM_AUTOMATIC`.
- Shop, admin and GraphQL stay camelCase.

**Routes and auth**
- Partner REST paths are the bare `/api/v1/orders`, `/api/v1/customers/{id}/orders` and
  `/api/v1/invoices/...`.
- Website routes go under `/api/v1/shop/...` and admin routes under `/api/v1/admin/...`.
- GraphQL has one endpoint, `/graphql`.
- API key format is `pk_<8-char prefix>_<32-byte base62 secret>`, stored as
  HMAC-SHA256(secret, `ApiKeys:Pepper`).
- Partner scopes are `customers:read`, `customers:write`, `orders:create`, `orders:read` and
  `invoices:read`.
- Keycloak roles: `Shop` realm for customers; `Backoffice` realm roles `super_admin` and `staff`.

**Storage and environments**
- MinIO buckets: `catalog-media`, `import-staging`, `documents`. Presigned URL TTL is 5 minutes.
- `--seed` refuses to run when `ASPNETCORE_ENVIRONMENT=Production`.
- Secrets come only from environment variables or `.env`. Nothing secret is committed.

## Review Focus

These input classes are the ones most likely to hurt a real user and are not covered by the
phase tests the spec obviously implies. Each has a pinned test in the owning task (named in
brackets).

1. **Amounts in the wrong format.** Inputs like `"500"`, `"500.1"`, `"500.123"`, `"1e3"`,
   `"-5.00"`, `" 500.00"` or a JSON number must be rejected with 400 `VALIDATION_FAILED`, never
   rounded or coerced. Only `^\d+\.\d{2}$` is accepted. [phase 4–5, Task "Partner order REST
   endpoint": `Amount_Must_Be_A_Two_Decimal_String`]
2. **Concurrent identical partner requests.** Ten parallel `POST /orders` with the same
   `(partner, external_order_id)` must produce exactly one order: one 201 and nine replays. Ten
   parallel requests with *different* amounts must produce one order and 409s. [phase 4–5,
   integration `Concurrent_Same_External_Id_Creates_Exactly_One_Order`]
3. **A variant becomes unsellable mid-flow.** A variant deactivated between add-to-cart and
   checkout, or between the cached candidate snapshot and the order save, is never sold. Checkout
   returns `PRICE_CHANGED` with that line removed; the partner path re-quotes and recomposes or
   fails. [phase 4–5, `Deactivated_Variant_After_Cache_Is_Never_Ordered`,
   `Checkout_With_Deactivated_Line_Returns_Price_Changed`]
4. **FX at the edges.** A rate exactly `72h` old is usable but `72h + 1s` is not. AED with no
   USD rate fails. A rate change after an order leaves the order's totals and snapshot unchanged.
   [phase 2–3, `Rate_Age_Boundary_Is_Inclusive`, `Aed_Without_Usd_Rate_Is_Unavailable`]
5. **Re-importing an admin-deactivated product.** The fields update but the product stays
   deactivated, along with its variants. It does not reappear on the PLP or in composition.
   [phase 7, `Reimport_Never_Reactivates_Admin_Deactivated_Product`]

---

## Cross-phase contracts

Later phases code against these names. A phase that **produces** one must use exactly this
signature. Every method takes a final `CancellationToken ct = default`, omitted below for brevity.
Records are `sealed record`.

### Building blocks (phase 0)
```csharp
// src/BuildingBlocks/Storage  → EwaysVNext.Storage (abstractions + MinIO/S3 adapter)
public interface IObjectStorage {
  Task PutAsync(string bucket, string key, Stream content, string contentType);
  Task<Stream?> GetAsync(string bucket, string key);
  Task<Uri> GetPresignedUrlAsync(string bucket, string key, TimeSpan ttl);
  Task CopyAsync(string srcBucket, string srcKey, string dstBucket, string dstKey);
  Task DeleteAsync(string bucket, string key);
  Task<bool> ExistsAsync(string bucket, string key);
}
public static class StorageBuckets { CatalogMedia = "catalog-media"; ImportStaging = "import-staging"; Documents = "documents"; }

// src/BuildingBlocks/Jobs  → EwaysVNext.Jobs
public interface IJobQueue {
  Task EnqueueAsync<TPayload>(string type, TPayload payload, DateTimeOffset? runAt = null, string? dedupeKey = null);
}
public interface IJobHandler<TPayload> { string Type { get; } Task<JobOutcome> HandleAsync(TPayload payload, JobContext context); }
public sealed record JobContext(long JobId, int Attempt, Func<CancellationToken, Task> HeartbeatAsync);
public abstract record JobOutcome { Completed; RetryLater(TimeSpan delay, string reason); Failed(string reason); } // static factories
// DI: services.AddJobs(IConfiguration); services.AddJobHandler<THandler, TPayload>(); services.AddRecurringJob(string type, TimeSpan interval)
// Config: Jobs:Mode = InProcess | WorkerOnly | Disabled
public static class JobTypes { ImportJobStep, FxRefresh, RefreshStorefrontViews, AuditSeal, UsagePrune, StagingImageCleanup } // string constants

// src/BuildingBlocks/Audit  → EwaysVNext.Audit
public sealed record AuditActor(Guid UserId, string Role);           // Role: "super_admin" | "staff" | "system"
public interface IAuditTrail { void Record(AuditActor actor, string action, string targetType, string targetId, object? before, object? after); }
// Record() adds an AuditEvent to the current EwaysVNextDbContext; it is persisted by the caller's own SaveChangesAsync.

// src/BuildingBlocks/Core/Application (extend)
public sealed record AdminActor(Guid UserId, bool IsSuperAdmin) { public AuditActor ToAudit(); }
public sealed record PartnerPrincipal(long PartnerId, long ApiKeyId, IReadOnlySet<string> Scopes) {
  public static bool TryFrom(ClaimsPrincipal user, out PartnerPrincipal principal); // claims: partner_id, api_key_id, scope (multi)
  public bool Has(string scope);
}
public static class PartnerScopes { CustomersRead="customers:read"; CustomersWrite="customers:write"; OrdersCreate="orders:create"; OrdersRead="orders:read"; InvoicesRead="invoices:read"; }

// src/BuildingBlocks/Outcomes (extend Error)
public sealed record Error(string Code, string Message, int HttpStatus, bool Retryable = false);
// EndpointResult error body: { code, message, retryable, correlationId, details?, items? }

// src/BuildingBlocks/Persistence
public static class MinorUnits { long ToMinor(decimal amount); decimal FromMinor(long minor); } // 2dp, checked, throws on >2dp
```

### Partners (phase 1)
```csharp
public interface IApiKeyAuthenticator { Task<ApiKeyAuthResult?> AuthenticateAsync(string presentedKey); }
public sealed record ApiKeyAuthResult(long PartnerId, long ApiKeyId, IReadOnlySet<string> Scopes, int RateLimitPerMinute, int BulkMaxItems);
// Authentication scheme name: "PartnerApiKey"; policy names: "partner:{scope}" e.g. "partner:orders:create"
```

### Phase 0 additions (authoritative; exact signatures in phase-0 plan's "Additions" block)
- `IUnitOfWork.TrySaveChangesAsync(CancellationToken ct = default)` → `enum SaveOutcome { Saved, UniqueViolation, ConcurrencyConflict }`
  is **produced in phase 0** (23505 → UniqueViolation with ChangeTracker cleared; xmin mismatch → ConcurrencyConflict). Every later
  phase consumes it and must not redefine it.
- `MinorUnitsConverter`, `TransientFailure.Is`, `DatabaseMigrator.MigrateAsync`, `ICorrelationContext`, `IDataSeeder`,
  `PartnerScopes.All`, `Error.RateLimited`, `Error.ServiceUnavailable` (`SERVICE_UNAVAILABLE`, retryable), `ErrorResponse`,
  `CorrelationHeaders`, `EndpointResult.Fail`, `EmptyPayload`, `JobLeaseLostException`, `IAuditVerifier`/`AuditVerificationResult`, host `--probe <url>`.
- Storage, Jobs and Audit are each split into `.Application` (abstractions) + `.Implementation`; type names/namespaces as above.
- `ITransactionExecutor` is deleted. `dedupe_key` is unique among *queued* jobs only. DB grants live in repeatable scripts run by `--migrate`.

### Phase 1 additions (authoritative; added after drafting)
```csharp
public static class EwaysClaimTypes { PartnerId="partner_id"; ApiKeyId="api_key_id"; Scope="scope"; RateLimitPerMinute="rate_limit_per_minute"; BulkMaxItems="bulk_max_items"; }
public static class AuthSchemes { PartnerApiKey = "PartnerApiKey"; }
public static class AuthPolicies { AdminStaff="admin:staff"; AdminSuper="admin:super"; ShopCustomer="shop:customer"; static string Partner(string scope) => $"partner:{scope}"; }
public static class AdminPermissions { /* e.g. "partners.manage", "refunds.manage" — full list in phase-1 Task 1 */ }
// SaveOutcome / TrySaveChangesAsync: owned by phase 0 (see above).
// SellerSnapshot.RequiredBuyerFields entries are '|'-joined alternatives; default ["email|phone"].
// Fx:MaxRateAge is read by Pricing from configuration; CommerceLimitsSnapshot.FxMaxRateAge mirrors it read-only.
```

### Phase 2–3 additions (authoritative; exact signatures in that plan)
- `IVariantLookupPort.GetStatesAsync(IReadOnlyCollection<long>)` → `IReadOnlyDictionary<long, VariantState>`; `VariantState(long VariantId, long ProductId, bool Exists, bool IsSellable)`.
- `public static string AttributeSignature.Hash(IReadOnlyDictionary<string,string>)` in Catalog.Application, shared by Sourcing.
- `PublishProductCommand(..., string Provenance, long? TargetProductId = null)` + `PublishBrand`, `PublishMedia`, `PublishVariant`.
- `ICatalogAdminService.SetProductActiveAsync(long productId, bool active, AdminActor actor, CancellationToken ct = default)`.
- `MoneyText.TryParseMinor(string?, out long)` / `MoneyText.Format(long)`: Sales reuses these for strict amount parsing.
- `RefreshStorefrontViewsPayload()`, `IStorefrontViewRefresher`, `IFxRefreshService.RefreshAsync()`/`IngestAsync(FxProviderBatch)`, `IPricingAdminService`, `IBrandAdminService`.
- New sanctioned row: `Pricing.Adapter.Outbound → Settings.Application` (FX max rate age).
- `Currency` PK is the ISO-4217 numeric `long`; `Code` is unique. FX overrides: one row per pair, replaced in place in one save.
- Storefront GraphQL fields are prefixed `shop*`. Test host: `PostgresFixture.CreateFactory()` + `[Collection(PostgresSuite.Name)]`.
- Phase 4–5 Task 18 (popularity migration) must run after the Sales orders migration (Task 9). It drops and recreates `mv_home` and `mv_storefront_product` with 30-day sales popularity.

### Phase 4–5 additions (authoritative; exact signatures in that plan)
- `ISalesAdminQueries` (`EwaysVNext.Modules.Sales.QueryApplication.Admin`):
  - `ListOrdersAsync(AdminOrderFilter)` → `Result<CursorPage<AdminOrderListItemDto>>`
  - `GetOrderAsync(long)` → `Result<AdminOrderDetailDto>`
  - `GetDocumentAsync(long)` → `Result<DocumentDto>`
  - `ListCustomerOrdersAsync(long customerId, int limit)` → `IReadOnlyList<AdminOrderListItemDto>`
  - `GetDashboardAsync(DateTimeOffset now)` → `SalesDashboardDto`
- `RefundResult(long RefundId, long CreditNoteDocumentId, string CreditNoteNumber, string PaymentStatus, long RefundedMinor)`.
- Append-only `sales.composition_attempts` table. Extra codes: `ORDER_STATE_CONFLICT`, `REFUND_EXCEEDS_REFUNDABLE`, `EMAIL_NOT_VERIFIED`, `CUSTOMER_SUSPENDED`.

### Phase 6/8 additions (exact signatures in that plan)
- `RateLimitPolicies`, `RequireAdmin(...)` (every admin route group uses it), `GraphQLLimits`, `IStandaloneAuditWriter`,
  `IAdminActorAccessor`, `AdminPermissions.AuditVerify`, `ICatalogAdminQueries.ListDuplicateCandidatesAsync`,
  config `RateLimiting:AdminPermitPerMinute`, `OpenApi:Expose`, and `AdminReadReferences`, which replaces phase 0's
  catch-all Admin rule.
- Phase 6 Task 15 creates the GraphQL SDL snapshot test that phase 7 Task 23 updates.

### Phase 7 additions
- `ISourcingAdminQueries.GetDashboardCountsAsync(DateTimeOffset since)` → `SourcingDashboardCounts(...)`. New row: `Admin.Adapter.Outbound → Sourcing.QueryApplication`.
- `ICatalogImportLookupPort` and `ICatalogMatchPort` are added to Catalog.Application by phase 7. `MeasurementUnit` gains `Dimension` and `ToBaseFactor`.

### Settings (phase 1)
```csharp
public interface ISettingsPort {
  Task<SellerSnapshot> GetSellerAsync();
  Task<PaymentInstructionSnapshot> GetActivePaymentInstructionAsync();
  Task<CommerceLimitsSnapshot> GetCommerceLimitsAsync();
}
public sealed record SellerSnapshot(string LegalName, string Uen, string Address, string Email, bool GstRegistered, string InvoiceFooter, IReadOnlyList<string> RequiredBuyerFields);
public sealed record PaymentInstructionSnapshot(long Id, int Version, string Text);
public sealed record CommerceLimitsSnapshot(long MinUsdMinor, long MaxUsdMinor, int BulkDefaultMax, int CompositionDeadlineMs, int CompositionMaxLines, TimeSpan FxMaxRateAge);
```

### Customer (phase 1)
```csharp
public interface ICustomerResolutionPort {
  Task<PartnerCustomerRef?> FindPartnerCustomerAsync(long partnerId, string externalCustomerId);
  Task<Result<PartnerCustomerRef>> CreatePartnerCustomerAsync(long partnerId, string externalCustomerId, ContactInput contact, string correlationId);
  Task<Result> SyncPartnerContactAsync(long customerId, long partnerId, ContactInput contact, string correlationId);
  Task<Result<InvoiceParty>> GetInvoicePartyAsync(long customerId);
}
public sealed record ContactInput(string? Email, string? Phone, string? FullName);
public sealed record PartnerCustomerRef(long CustomerId, string? Email, string? Phone, string? FullName);
public sealed record InvoiceParty(long CustomerId, string? FullName, string? CompanyName, string? Email, string? Phone, AddressSnapshot? BillingAddress, string? RegistrationNumber);
public sealed record AddressSnapshot(string Line1, string? Line2, string City, string PostalCode, string CountryIso2);
public interface IWebsiteCustomerPort { Task<Result<WebsiteCustomerRef>> EnsureFromIdentityAsync(Guid identityUserId, string email, bool emailVerified); }
public sealed record WebsiteCustomerRef(long CustomerId, bool EmailVerified, bool IsActive);
```

### Catalog (phase 2–3)
```csharp
public interface ISellableVariantPort {
  Task<IReadOnlyList<SellableVariant>> GetSellableAsync(IReadOnlyCollection<long>? variantIds = null); // null = all sellable
  Task<long> GetVersionAsync(); // bumps on any catalog write affecting sellability
}
public sealed record SellableVariant(long VariantId, long ProductId, string Sku, string ProductTitle, long BrandId, string BrandName,
  long PrimaryCategoryId, IReadOnlyList<string> CategoryPath, IReadOnlyDictionary<string, string> Attributes);
public interface ICatalogPublishingPort { Task<Result<PublishedProduct>> UpsertProductAsync(PublishProductCommand command); }
// PublishProductCommand: brand (name+aliases), category canonical keys path, title, descriptions, identifiers, media (staging keys),
//   variants (attribute values, sku?), source refs. Matching: identifiers > brand+MPN > (domain, sourceId); never clears DeactivatedByAdmin.
public sealed record PublishedProduct(long ProductId, bool Created, bool SkippedBecauseDeactivated, IReadOnlyList<PublishedVariant> Variants);
public sealed record PublishedVariant(long VariantId, string SignatureHash, bool Created);
public interface IStorefrontRefreshTrigger { Task RequestRefreshAsync(); } // enqueues JobTypes.RefreshStorefrontViews with dedupeKey "storefront", runAt now+5s
```

### Pricing (phase 2–3)
```csharp
public enum PriceSource { Override, Fx, Base } // Base = requested currency equals base currency
public sealed record FxSnapshot(long? FxRateId, long? FxOverrideId, string Base, string Quote, decimal Rate, DateTimeOffset RateAt, string Provenance);
public sealed record PriceQuote(long VariantId, string Currency, long ReferenceMinor, long LoMinor, long HiMinor, PriceSource Source,
  FxSnapshot? Fx, long BaseAmountMinor, string BaseCurrency);
public interface IPriceQuotePort {
  Task<Result<IReadOnlyList<PriceQuote>>> QuoteAsync(IReadOnlyCollection<long> variantIds, string currency, DateTimeOffset now);
  // variants lacking a price or usable rate are omitted, never errored individually; whole-call errors: CURRENCY_NOT_ENABLED
  Task<long> GetVersionAsync();
}
public interface IFxSnapshotPort { Task<Result<UsdConversion>> ToUsdAsync(long amountMinor, string currency, DateTimeOffset now); }
public sealed record UsdConversion(long UsdMinor, FxSnapshot? Fx);
public interface IPricePublishingPort { Task<Result> SetReferencePriceAsync(long variantId, long amountMinor, string currency, PriceProvenance provenance); }
public sealed record PriceProvenance(long? PriceObservationId, string? SourceUrl, DateTimeOffset? CollectedAt, long? OriginalAmountMinor, string? OriginalCurrency, bool? TaxIncluded);
public static class PriceBand { public static (long Lo, long Hi) For(long referenceMinor); } // in Pricing.Application, pure
```

### Sales (phase 4–5): consumed by Admin (phase 6)
```csharp
public interface IOrderAdminService {
  Task<Result> ConfirmExternalPaymentAsync(long orderId, string method, string? reference, AdminActor actor);
  Task<Result> CancelAsync(long orderId, AdminActor actor);
  Task<Result<RefundResult>> RecordRefundAsync(long orderId, long amountMinor, string reason, string? externalReference, AdminActor actor); // super admin only
}
// Sales.QueryApplication: ISalesAdminQueries (list/detail/dashboard aggregates) — names fixed in the phase 4–5 plan.
```

### Error codes (exact strings, spec §6.3; plus those named in the spec body)
`VALIDATION_FAILED, CUSTOMER_CONTACT_REQUIRED, AMOUNT_OUT_OF_RANGE, CURRENCY_NOT_ENABLED,
EXACT_COMPOSITION_UNAVAILABLE, COMPOSITION_TIMEOUT, ORDER_IDEMPOTENCY_CONFLICT,
INVOICE_BUYER_FIELDS_MISSING, FX_RATE_UNAVAILABLE, PRICE_CHANGED, NOT_FOUND, INSUFFICIENT_SCOPE,
RATE_LIMITED, BATCH_DEADLINE_EXCEEDED, CONTACT_SYNC_FAILED (warning), PRICE_MISSING`.

`retryable = true` only for: `COMPOSITION_TIMEOUT`, `FX_RATE_UNAVAILABLE`, `RATE_LIMITED`,
`BATCH_DEADLINE_EXCEEDED`, and transient DB errors.

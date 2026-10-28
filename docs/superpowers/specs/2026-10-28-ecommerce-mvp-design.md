# API-First B2B E-Commerce MVP — Design

**Date:** 2026-10-28 · **Status:** Draft for approval
**Inputs:** `docs/product-scope/ecommerce_mvp_prd.md` (the PRD is authoritative on business rules),
`docs/product-scope/ecommerce_agent_master_prompt.md`, `AGENTS.md`, `docs/DDD-GUIDELINES.md`,
`docs/ARCHITECTURAL-DEFENSE.md`, `docs/adr/*`.

Markers used below:
- **[Decided]**: the product owner chose it in the design conversation.
- **[Design call]**: chosen by the designer. The builder may revisit it if they write down why.
- **[Builder]**: left to the implementer on purpose.

---

## 1. Intent

The platform is a Singapore seller with two sales channels on one catalog:

1. **Partner API orders.** An authenticated B2B partner sends an amount, a currency and a
   customer reference. The system builds an order from real, active SKUs that totals that amount
   exactly. Each unit price may move at most ±5% from its reference price. The order is recorded
   as partner-reported PAID and system-automatic FULFILLED, and a commercial invoice is issued at
   once. The whole thing is all-or-nothing.
2. **Website orders.** A registered customer browses the homepage, PLP and PDP, buys at the
   displayed fixed prices, and gets a pro forma invoice plus one payment instruction. An admin
   confirms the external payment by hand. Only then does the order become PAID and FULFILLED with
   a commercial invoice.

The reason to design before building is that the repository is a template built for a different
market. Its modules are Iranian: NationalCode and Shahkar verification, IRR bank gateways,
provinces, SMS. Its docs (AGENTS.md) describe modules that don't exist: Identity, Orders,
Supplier, Brand, Admin. Its database is SQL Server. The architecture is what we keep. The domain
content gets replaced.

## 2. Goals, non-goals, anti-goals

**Goals.** Everything in PRD §2–§12, delivered end to end **[Decided: whole PRD in one run, phased
per §14]**:
- All APIs the storefront and admin need, in both REST and GraphQL **[Decided]**.
- The partner API in REST only, published as OpenAPI **[Decided]**.
- PostgreSQL **[Decided]**.
- MinIO for object storage **[Decided]**.
- Postgres materialized views behind the PLP, PDP and homepage **[Decided]**.
- A `build/` directory holding the production Dockerfiles and compose files **[Decided]**.

**Non-goals.** Everything in PRD §2.3 is out: payment gateway, proof upload, shipping, stock
reservations, partner webhooks, partner portal, an RBAC builder, ZIP export and scheduled
re-crawls. Also out for now: the Next.js frontend **[Decided: not necessary now]**. The admin
panel ships as APIs only, with no UI.

**Anti-goals.** The build is a failure even if it "works" if it does any of these:
- Invents an invoice line, or inflates a price outside ±5% to hit an amount.
- Leaves a partial API order behind: an order without its invoice, or a payment record without
  its order.
- Lets one partner read or infer another partner's records.
- Lets a stale storefront view make a deactivated variant **purchasable**. Being displayed for a
  few seconds is acceptable.
- Calls an invoice a "GST tax invoice", or charges GST.
- Breaks the architecture rules without an ADR that names the invariant involved
  (DDD-GUIDELINES §12).

## 3. Architecture changes to the existing repository

### 3.1 What is kept
- The hexagonal module layout: `Application`, `Adapter/Inbound/{Rest,GraphQL}`,
  `Adapter/Outbound`, `QueryApplication`, `Installer`.
- One `EwaysVNextDbContext` (`src/Database`), with schema-per-module.
- `Result`/`Error`, `EndpointResult`, Snowflake `long` ids with `[Snowflake]` on the wire,
  `IClock` passed as `now`, and in-process domain events through `EfCoreUnitOfWork`.
- Keycloak multi-realm authentication (`Shop`, `Backoffice`).
- The `src/Authorization` RBAC subsystem.
- The single Host `/graphql` endpoint composed from module GraphQL adapters.
- Serilog, OpenTelemetry, the rate limiter, Scalar and OpenAPI.
- Rules 1–10 in AGENTS.md. The exceptions are only the ADR amendments in §3.4.

### 3.2 What is removed **[Decided: replace with the PRD model]**
| Removed | Why |
|---|---|
| `Payment` module: BankTransaction, IRR `Money`, gateways, ADR-0015 | The PRD has no gateway. Payment status moves into `Sales` (§5.6). |
| `Location` module: provinces, cities, districts | No PRD requirement. Addresses become a value object in Customer. |
| `Notification` module (SMS) | Email only for auth, and Keycloak sends it (§6.1). |
| Customer's NationalCode, CompanyNationalId, Shahkar verification, LegalInformation, TeamMember, JobType | Iran-specific. Replaced by the §5.2 model. |
| Catalog's `ProductReservation`, `ReservationExpirationService` (the reaper), stock fields | PRD §2.1: no stock and no reservations. Every active variant is always available. |
| `tests/PaymentTests` and the tests for removed code | They follow the code they cover. |
| The old `20260930075311_init` migration, and the SQL-Server-specific `IdentityColumnMigrationExtensions` | Replaced by a fresh Postgres `init` (§3.3). There is no production data to keep. **[Design call]** |

Catalog's `AttributeBlueprint`, `MeasurementUnit`, `Category`, `CategoryAttributeAssignment` and
`CategoryAttributeExclusion` are **kept and extended** (§5.3), not rewritten.

### 3.3 Postgres and MinIO **[Decided]**
- Use `Npgsql.EntityFrameworkCore.PostgreSQL` with `EnableRetryOnFailure`, so rule 8 still holds.
  Use snake_case naming (`EFCore.NamingConventions`).
- Use Postgres `xmin` as the concurrency token on aggregate roots, replacing `rowversion`. Keep
  `Entity.RowVersion` only if mapping it costs nothing. Otherwise remove it and update
  DDD-GUIDELINES "Known inconsistencies". **[Builder]**
- Enable the `pg_trgm` and `unaccent` extensions through migrations.
- Check constraints, filtered unique indexes (`WHERE ...`) and `jsonb` columns are all first-class.
- `src/Authorization` has its own `AuthorizationDbContext`. It moves to Postgres in the same pass.
- **Money storage:** `amount_minor bigint` plus `currency char(3)` everywhere. The `Money` domain
  type stays decimal 2dp. Conversion happens at the persistence edge. All five PRD currencies are
  2dp, so the existing `MONEY_UNSUPPORTED_CURRENCY_DECIMALS` deny-list stays as it is.
  **[Design call]**
- **MinIO** sits behind an `IObjectStorage` port (put, get-stream, presigned GET with TTL,
  delete) in a new building block, `BuildingBlocks/Storage`, with an AWS S3 SDK / MinIO adapter.
  Buckets:
  - `catalog-media`: published images. Public-read through the reverse proxy, or presigned.
  - `import-staging`: raw files and downloaded images. Private.
  - `documents`: invoice PDFs. Private, presigned URLs with a 5-minute TTL.

### 3.4 New and amended ADRs (written as part of phase 0)
| ADR | Content |
|---|---|
| 0017 Postgres + MinIO | Replaces SQL Server. Covers the `xmin` concurrency token, minor-unit money storage and the object-storage port. Testing moves from LocalDB to Testcontainers Postgres (§9). |
| 0018 Sales aggregate owns order, payment, fulfillment, documents, refunds | Why API-order atomicity is achieved by **aggregate design, not a transaction**, so rules 7 and 8 stay intact. Supersedes ADR-0015. |
| 0019 Postgres-backed job queue | Replaces the in-process reaper pattern. Amends ADR-0010: a `SKIP LOCKED` claim makes jobs safe with several replicas. |
| 0020 Storefront materialized views | Amends ADR-0008's "reads are never stale" for **storefront browse reads only**. Records the cross-schema SQL dependency (catalog + pricing) and that purchasing re-validates against live tables. |
| 0021 Partner API-key authentication | Key format, hashing, scopes, per-partner rate limits, usage logging. |
| 0022 Remove Iran-specific modules | Records §3.2. |
| Accept ADR-0016 | QueryApplication is the read-side implementation (it injects `EwaysVNextDbContext` and projects). Current Catalog code already works this way. New modules follow it. **[Design call]** |

`AGENTS.md`, `docs/ONBOARDING.md`, `docs/TESTING-GUARANTEES.md` and the architecture-test
`ModuleNames` and exception theory rows are updated to the real module list. Today AGENTS.md
describes modules that do not exist, and that is a defect in its own right.

### 3.5 Module map
| Module | Owns | Aggregates |
|---|---|---|
| **Partners** | B2B partners, API keys, scopes, rate limits, key usage | `Partner` (children: `ApiKey`), plus append-only `ApiKeyUsage` (log rows, not an aggregate) |
| **Customer** | Website customers and partner-linked customers, contact audit | `Customer` (children: `ContactChange`) |
| **Catalog** | Categories, attributes, brands, products, variants, media; storefront read models | `Category`, `AttributeBlueprint`, `Brand`, `Product` (children: `Variant`, `ProductMedia`, `ProductIdentifier`) |
| **Pricing** | Reference prices, currency overrides, FX rates and overrides, enabled currencies, price resolution, ±5% band | `VariantPrice` (one per variant; children: `CurrencyOverride`), `Currency`, `FxRate` (provider observation), `FxRateOverride` |
| **Sourcing** | Import jobs, staging, source listings, price observations, duplicate review, source policies | `ImportJob`, `StagedRecord`, `SourceListing` (children: `PriceObservation`), `SourcePolicy` |
| **Sales** | Carts, orders (both channels), composition engine, invoices and pro formas, payment confirmation, refunds, credit notes | `Cart`, `Order` (children: `OrderLine`, `PaymentRecord`, `Document`, `Refund`, `CompositionTrace`) |
| **Settings** | Seller identity, website payment instruction (versioned), order amount bounds, operational limits | `SellerProfile`, `PaymentInstruction`, `CommerceLimits` |
| **Admin** | Cross-module composition: dashboard, customer 360, admin GraphQL composition | none (ADR-0006/0007) |

Plus two cross-cutting pieces:
- `BuildingBlocks/Jobs`: the job queue (§7).
- `BuildingBlocks/Audit`: the admin audit trail (§6.4).

### 3.6 Sanctioned cross-module references (new arch-test exception rows)
All are `Consumer.Adapter.Outbound → Provider.Application` (write-side or inbound-port ACL), except
Admin's read side.

| Consumer → Provider | Why |
|---|---|
| Sales → Catalog.Application | Sellable-variant snapshot for composition, cart and checkout validation |
| Sales → Pricing.Application | Price quotes, the ±5% band and FX snapshots in the requested currency |
| Sales → Customer.Application | Resolve or create a partner customer, contact sync, website customer standing |
| Sales → Settings.Application | Seller snapshot, payment instruction, amount bounds |
| Pricing → Catalog.Application | Variant existence and active state before price writes |
| Sourcing → Catalog.Application | Upsert canonical entities when publishing |
| Sourcing → Pricing.Application | Set reference prices with provenance when publishing |
| Admin → each QueryApplication / admin port | Composition (existing ADR-0006/0007 pattern) |

Partners is referenced by **no one**. The partner id reaches Sales as an authenticated principal
(§6.2), not as a module call. Catalog, Customer, Settings and Partners reference no other module.

---

## 4. Money, currency and FX rules (shared by Pricing and Sales)

- **Enabled currencies:** USD, EUR, SGD, AED, GBP, all 2dp. The Super Admin can enable or
  disable them (`Currency` aggregate). Disabling a currency removes it from the storefront and
  rejects partner orders in it.
- **Base price:** each `VariantPrice` has one reference price in a **base currency**, which can be
  any enabled currency. It keeps provenance: source listing id or price observation id,
  `collected_at`, the original captured price and currency, and `tax_included` when known.
- **Price priority for currency C:**
  1. `CurrencyOverride(C)` when present, otherwise
  2. `round_half_even(base × rate(base→C))` to 2dp.
  The result is the **applicable reference price** `r` in C.
- **Rate resolution:**
  1. An active `FxRateOverride(pair)` wins.
  2. Otherwise use the latest provider `FxRate(pair)` no older than `Fx:MaxRateAge` (default 72h,
     so the ECB feed survives a weekend). **[Design call]**
  3. Otherwise fail with `FX_RATE_UNAVAILABLE`.
  Cross rates are computed through EUR, the ECB base. **AED is derived from USD using the official
  peg (3.6725)**, with provenance `derived:usd-peg`, because ECB does not publish AED.
- **FX provider:** `IFxRateProvider` port, with ECB as the default adapter **[Decided]**. Refresh
  interval is configurable (default every 6h; ECB publishes daily). Every fetched rate is stored
  as a new row (`provider`, `fetched_at`, `rate_date`), never updated in place.
- **±5% band, per unit, in minor units:** `lo = ceil(0.95·r)` and `hi = floor(1.05·r)`. This is
  integer arithmetic on minor units (multiply by 95 and by 105, then divide by 100 with explicit
  ceil/floor). No floating point anywhere in pricing or composition.
- **Order bounds:** `CommerceLimits.MinUsd` = 10.00 and `MaxUsd` = 1,000,000.00, both
  configurable. The amount is converted to USD with the same FX snapshot the order will lock.
- **Snapshots:** an order locks, per line: `r`, its source (override or FX), the rate used, the
  rate's id and timestamp, and the base price. A later rate change cannot alter the order.
- **Tax:** the seller is not GST-registered. A `ITaxPolicy` port returns zero tax and the label
  "Not GST registered" from `SellerProfile`. A partner's `amount` is final and inclusive. Because
  the policy is a port that every document goes through, future GST support can be added without
  a redesign.

---

## 5. Modules in detail

Each module lists its aggregates, invariants, DB constraints and ports. Unique constraints are the
duplicate-control backstop required by PRD §4.4.

### 5.1 Partners
- `Partner`:
  - Fields: `Id`, `Name`, `Status{Active, Suspended}`, `Scopes` (a set from `customers:read`,
    `customers:write`, `orders:create`, `orders:read`, `invoices:read`),
    `RateLimitPerMinute`, `BulkMaxItems` (default 100).
  - Invariant: a suspended partner authenticates no key.
- `ApiKey` (child):
  - Fields: `Id`, `Prefix` (8 chars, unique, shown to admins), `SecretHash`, `Scopes` (a subset of
    the partner's), `CreatedAt`, `ExpiresAt?`, `RevokedAt?`, `LastUsedAt`.
  - Rotation creates a new key and gives the old one a configurable overlap expiry.
- **Key format:** `pk_<prefix>_<32-byte base62 secret>`. The plaintext is shown once at creation.
  **[Design call]**
- **Hash:** HMAC-SHA256(secret, server pepper), compared in constant time.
  Rationale: keys are high-entropy random values, so a slow password hash adds latency on every
  request without adding security. Passwords are the low-entropy case, and Keycloak hashes those.
  ADR-0021 records this as the reading of the PRD's "modern hashing" for keys. **[Design call]**
- **Usage log:**
  - Columns: `api_key_usage(partner_id, key_id, at, method, path, status, correlation_id,
    duration_ms)`.
  - Written through a bounded in-memory channel and batch-inserted, so it never blocks a request.
    Logs never contain the key.
  - Retention is 90 days, configurable, enforced by a job.
- **Ports:**
  - `IPartnerAdminService`: create, suspend, reactivate, set scopes or limits, issue, rotate and
    revoke keys. Super Admin only.
  - `IApiKeyAuthenticator`: used by the auth handler (§6.2).

### 5.2 Customer
`Customer`:
- **Fields:**
  - `Id`
  - `Kind{Website, PartnerLinked}`
  - For Website: `IdentityUserId` (Guid, the Keycloak `sub`)
  - For PartnerLinked: `PartnerId` and `ExternalCustomerId`
  - `Email?`, `Phone?` (E.164 value object), `EmailVerified` (for Website, mirrors Keycloak)
  - `FullName?`, `CompanyName?`
  - `BillingAddress?` (value object: lines, city, postal code, country ISO-2)
  - `RegistrationNumber?`
  - `Status{Active, Suspended}`
- **`ContactChange` (child):** `Field{Email, Phone, FullName}`, `OldValue`, `NewValue`,
  `ChangedAt`, `SourcePartnerId?`, `CorrelationId`.
- **Invariants:**
  - A PartnerLinked customer is **created** only with both email and phone
    (`CUSTOMER_CONTACT_REQUIRED`).
  - Applying a sync with an omitted field keeps the stored value. A changed field appends a
    `ContactChange`.
  - New contact values are never marked verified.
- **Constraints:**
  - `UNIQUE(partner_id, external_customer_id) WHERE kind='PartnerLinked'`
  - `UNIQUE(identity_user_id) WHERE kind='Website'`
  - **No** uniqueness on email or phone across customers. There is no merging (PRD §3).
- **Ports:**
  - `ICustomerResolutionPort.ResolvePartnerCustomer(partnerId, externalId, contact?)`. Returns the
    customer id. It creates the customer or syncs contacts in **its own** save.
  - `IWebsiteCustomerPort.EnsureFromIdentity(sub, email, emailVerified)`. Lazily creates the
    website customer on first authenticated call.
  - Profile update, `ICustomerAdminService`, and `GetInvoiceParty(customerId)` (used by Sales for
    the buyer block).

### 5.3 Catalog
Kept and extended: `Category`, `AttributeBlueprint` (the PRD's "attribute definition"),
`MeasurementUnit`, and the category attribute assignments and exclusions.

**`Category`**
- Add `CanonicalKey` (normalized) and `Aliases`.
- `UNIQUE(parent_id, canonical_key)` with NULLS NOT DISTINCT, so root-level names are unique.
- Already has an active status.

**`AttributeBlueprint`**
- Add `CanonicalKey`, `Scope` (global or category), `Aliases`, a datatype
  `{Text, Number, Option, Boolean, ...}`, a unit, and validation (existing).
- `UNIQUE(scope, canonical_key)`.

**`Brand`** (new, inside Catalog; not the separate module from ADR-0009, which does not exist)
- `CanonicalName` (normalized), `DisplayName`, `Aliases`, `Provenance`, `IsActive`.
- `UNIQUE(canonical_name)`, plus a `brand_aliases(alias UNIQUE)` table.

**`Product`** (rewritten)
- **Fields:** `BrandId`, `PrimaryCategoryId`, `SecondaryCategoryIds` (existing CategoryProduct),
  `Title`, `Slug` (unique), `ShortDescription`, `Description`, `Status{Draft, Active,
  Deactivated}`, `DeactivatedByAdmin` (bool), `SourceRefs` (source listing ids).
- **`ProductIdentifier` (child):**
  - `Type{GTIN, UPC, EAN, MPN, SourceId}` and `Value`.
  - `UNIQUE(type, value) WHERE type IN (GTIN, UPC, EAN)`
  - `UNIQUE(brand_id, mpn)`, done as a denormalized column on product
  - `UNIQUE(source_domain, source_id)`
- **`ProductMedia` (child):** `ObjectKey` (MinIO), `SortOrder`, `Alt`, `SourceUrl`, `UsageRights`.
- **`Variant` (child):**
  - Fields: `Sku` (unique), `AttributeSignature` (canonical sorted `key=value` string, hashed),
    `AttributeValues` (jsonb), `IsActive`, `DeactivatedByAdmin`.
  - `UNIQUE(product_id, attribute_signature_hash)`.
  - A product with no varying attributes has exactly one variant with an empty signature.
- **Invariants:**
  - A product can be `Active` only with at least one active variant, a brand, a primary category,
    a title and at least one image whose rights allow use.
  - Variant sellability = product Active **and** variant active **and** a price exists. The price
    part is checked by Pricing at quote time.
  - `Reactivate` from an import must not clear `DeactivatedByAdmin`. Only an admin action can.
- **Ports:**
  - `ICatalogAdminService` (CRUD, toggles, bulk edit, merge duplicates)
  - `ICatalogPublishingPort` (used by Sourcing: idempotent upserts keyed by identifiers and
    signature)
  - `ISellableVariantPort.GetSellable(filter?)`. Returns `(variantId, productId, categoryId,
    brandId, titles, attributes)` for Sales and Pricing.
- **Storefront read side:** the materialized views in §8 live in `Catalog.QueryApplication`.

### 5.4 Pricing
- **`VariantPrice`** (aggregate, one per variant)
  - `VariantId` (unique), `BaseAmountMinor`, `BaseCurrency`, `Provenance` (observation id, URL,
    collected_at, original price and currency, tax_included?), `UpdatedAt`.
  - **`CurrencyOverride` (child):** `Currency`, `AmountMinor`, `PriceList` (default `"default"`).
    `UNIQUE(variant_price_id, currency, price_list)`.
- **`Currency`:** `Code` (PK), `IsEnabled`, `MinorUnits` = 2.
- **`FxRate`:**
  - Fields: `Base`, `Quote`, `Rate numeric(18,10)`, `Provider`, `RateDate`, `FetchedAt`.
  - `UNIQUE(base, quote, provider, rate_date)`. Append-only.
- **`FxRateOverride`:** `Base`, `Quote`, `Rate`, `SetBy`, `SetAt`, `ValidUntil?`, `IsActive`.
  At most one active override per pair (filtered unique).
- **Ports:**
  - `IPriceQuotePort.Quote(variantIds, currency, now)`. Returns per variant: `r`, `lo`, `hi`,
    source, and the FX snapshot. Returns `FX_RATE_UNAVAILABLE` or `PRICE_MISSING` per item.
  - `IFxSnapshotPort.ToUsd(amount, currency)`
  - `IPricingAdminService` (Super Admin for FX overrides and currency enablement; Staff can edit
    prices and overrides)
  - `IPricePublishingPort` (used by Sourcing)
- **Jobs:** `FxRefreshJob` (recurring).

### 5.5 Sourcing (catalog import and discovery)
**Aggregates**
- **`SourcePolicy`:** `Domain`, `CrawlAllowed`, `ContentUseAllowed`, `ImageUseAllowed`, `Notes`.
  A domain with no policy is **crawl-allowed only if robots.txt allows it**. Its images and text
  are staged but marked `RightsUnknown`, which sends the record to review and never auto-publishes
  it. **[Design call: the PRD requires "legal ability to use content"; this is the safe default.]**
- **`ImportJob`:**
  - **Fields:**
    - `Mode{Crawl, WebSearch, Combined, File}`, `Sources` (URLs or domains), `Queries`,
      `TargetCategoryId?`
    - `IncludeDomains` and `ExcludeDomains`
    - Limits: `MaxPages`, `MaxDepth`, `MaxRecords`
    - `PublishPolicy{AutoPublishValidated (default), ManualReview}`
    - `Status{Queued, Running, Paused, Completed, Failed, Cancelled}`
    - Counters: discovered, extracted, valid, invalid, duplicate, published, review
    - `Checkpoint` (jsonb, for resumability)
  - **Invariants:** state transitions only. A cancelled job publishes nothing further.
- **`StagedRecord`:**
  - **Fields:**
    - `JobId`
    - `Fingerprint`: SHA-256 of the normalized source identity. `UNIQUE(fingerprint)`, so a
      record is never staged twice across jobs. A re-import updates the existing staged record.
    - `Raw` (jsonb), `Normalized` (jsonb)
    - `MatchResult{New, ExactMatch(productId/variantId), FuzzyCandidates[...]}`
    - `ValidationErrors[]`
    - `Status{Staged, Valid, Invalid, NeedsReview, Published, Rejected}`
    - `ReviewedBy?`
- **`SourceListing`:** `Url`, `Domain`, `SourceId?`, `CollectedAt`, `Title`, `ImageUrls`,
  `Rights`. `UNIQUE(domain, source_id)` and `UNIQUE(url)`.
  - **`PriceObservation` (child):** `Amount`, `Currency`, `TaxIncluded?`, `CollectedAt`.

**Adapters**: `ISourceAdapter.Discover(job, checkpoint) → IAsyncEnumerable<RawListing>`.
- `WebsiteCrawlerAdapter`:
  - HttpClient + AngleSharp.
  - Respects robots.txt, applies per-domain politeness delays, enforces depth, page limits and
    domain filters.
  - Extracts from schema.org `Product` JSON-LD, microdata and OpenGraph. Uses no heuristic price
    scraping when no structured data exists; those records become `Invalid: NO_STRUCTURED_DATA`.
    **[Design call]**
- `WebSearchAdapter`:
  - `ISearchProvider` port with a Brave Search adapter **[Decided]**.
  - Feeds result URLs, after the include/exclude filters, into the crawler extraction.
  - Disabled with a clear job error when no key is configured.
- `FileImportAdapter`:
  - CSV (CsvHelper), XLSX (ClosedXML), and JSON (a documented JSON Schema with the same fields).
  - Downloadable templates via `GET /api/v1/admin/imports/templates/{csv|xlsx|json-schema}`.
  - Column-mapping and preview step before the job runs.
- **Future scraper providers** implement `ISourceAdapter`. None ship in the MVP.

**Pipeline** (each stage idempotent, checkpointed per batch of 50 records):
1. Discover.
2. Extract.
3. Download images to `import-staging`.
4. **Normalize**:
   - Deterministic first: whitespace and case, unit conversion through `MeasurementUnit`, brand
     and category alias tables, attribute alias tables.
   - Then optional enrichment through `IEnrichmentProvider` (Claude adapter, model
     `claude-sonnet-5-5`, structured JSON output) **[Decided]**.
   - The enrichment prompt and output schema allow only re-mapping facts **present in the raw
     record**. Every enriched field carries the raw field path it came from.
   - Output that names a value with no source path is discarded.
   - Disabled when no key is configured.
5. **Match**:
   - GTIN/UPC/EAN, then brand+MPN, then (domain, source_id) give an exact match.
   - Title similarity (`pg_trgm` ≥ 0.6, within the same brand) gives fuzzy candidates, which go
     to `NeedsReview`. Fuzzy candidates are never merged automatically.
   - For a variant: (product, attribute signature).
6. **Validate**: required attributes for the category, a positive price in a supported currency,
   at least one usable image, rights allowed, a brand and category resolved, and source
   availability where the structured data states it (`offers.availability`).
7. **Publish**:
   - Under the job's policy, `Valid` records publish via `ICatalogPublishingPort` and
     `IPricePublishingPort`.
   - Images are copied to `catalog-media`.
   - Upserts reuse canonical entities. An entity with `DeactivatedByAdmin` is updated but **not
     reactivated**.
8. **Refresh**: enqueue a storefront MV refresh (§8).

**Review queue (admin):** list, filter, diff against the matched entity, then approve, merge into
an existing entity, create new, or reject. Bulk approve/publish is supported.

### 5.6 Sales
**`Cart`** (website only):
- `CustomerId` (unique), `Currency`, and `Lines[variantId, quantity, displayedUnitPriceMinor,
  priceQuotedAt]`.
- Quantity is 1–999 per line. **[Design call]**

**`Order`** (the aggregate that makes partner orders atomic) **[Decided]**:
- **Header:**
  - `Id`, `Number` (`ORD-000123`), `Channel{Website, PartnerApi}`
  - `PartnerId?`, `ExternalOrderId?`, `RequestFingerprint?`
  - `CustomerId`, `Currency`
  - `TotalMinor`, `SubtotalMinor`, `TaxMinor` (= 0), `UsdEquivalentMinor`
  - `CreatedAt`, `CorrelationId`
- **Status dimensions, each independent (PRD §8.3):**
  - `OrderStatus{PendingPayment, Completed, Cancelled}`
  - `PaymentStatus{Unpaid, Paid, PartiallyRefunded, Refunded}` + `PaymentSource{PartnerReported,
    AdminConfirmed}`
  - `FulfillmentStatus{Unfulfilled, Fulfilled}` + `FulfillmentSource{SystemAutomatic}`. The text
    on every document and API response: "Fulfillment is an operational assertion, not verified
    proof of delivery".
  - `DocumentStatus` is derived from the documents.
- **`OrderLine`** (immutable snapshot):
  - variant and product ids, SKU, product title, brand, category path, attribute values
  - quantity, `UnitPriceMinor`, `LineTotalMinor`
  - `ReferencePriceMinor`, `PriceSource{Override, Fx}`, `FxRate`, `FxRateId`, `FxRateAt`,
    `BaseAmountMinor`, `BaseCurrency`
  - `DeviationBps` (basis points from reference)
- **`PaymentRecord`:** `Source`, `Method?`, `Reference?`, `VerifiedBy?` (admin user id),
  `RecordedAt`, `AmountMinor`.
- **`Document`:**
  - `Type{ProForma, Commercial, CreditNote}`, `Number` (separate Postgres sequences per type:
    `PF-`, `INV-`, `CN-` with zero-padded numbers; gaps allowed **[Decided]**)
  - `IssuedAt`, `Content` (jsonb: the complete immutable snapshot, including the seller block from
    `SellerProfile`, the buyer block, lines, totals, the tax statement, and for website pro formas
    the payment instruction version and text)
  - `RelatedDocumentId?` (credit note → commercial invoice), `PdfObjectKey?`
  - Documents are never edited.
- **`Refund`:** `AmountMinor`, `Reason`, `RecordedBy`, `RecordedAt`, `ExternalReference?`,
  `CreditNoteDocumentId`.
- **`CompositionTrace`:** `Seed`, `StrategyVersion`, `Iterations`, `DurationMs`,
  `CandidatePoolSize`, `Attempts` (jsonb summary).

**Invariants (in the aggregate):**
- **PartnerApi order:** `Order.CreatePartnerOrder(...)` builds the whole thing in one call:
  - lines whose `Σ LineTotal == TotalMinor == requested amount`
  - every line `lo ≤ unit ≤ hi`, with quantity ≥ 1
  - a PartnerReported PaymentRecord, Fulfilled/SystemAutomatic, and a Commercial document
  It is then saved once. **That single `SaveChangesAsync` is the atomicity guarantee:** order,
  lines, payment, fulfillment and invoice are one aggregate, so rules 7 and 8 hold.
- **Website order:** `Order.CheckoutWebsite(...)` creates PendingPayment and a ProForma.
  - `ConfirmExternalPayment(verifier, method, ref, now)` is allowed only from PendingPayment. It
    sets Paid/AdminConfirmed and Fulfilled/SystemAutomatic, then issues the Commercial document,
    in one save.
  - `Cancel()` is allowed only while Unpaid.
- **Refunds:**
  - `RecordRefund(amount)` requires Paid or PartiallyRefunded and `Σ refunds + amount ≤ total`.
  - It issues a CreditNote linked to the commercial invoice and moves PaymentStatus to
    PartiallyRefunded or Refunded.
  - Super Admin only.
- **Invoice issuance** requires the buyer fields listed in
  `SellerProfile.RequiredBuyerFields`. The default is `[email OR phone]`. **[Design call]** It is
  configurable so legal review can tighten it. A missing field fails with
  `INVOICE_BUYER_FIELDS_MISSING`, and for partner orders that means no order is written.

**Constraints:**
- `UNIQUE(partner_id, external_order_id) WHERE channel='PartnerApi'`
- `UNIQUE(type, number)` on documents
- `CHECK(total_minor > 0)`, `CHECK(quantity > 0)`
- `CHECK(unit_price_minor BETWEEN ceil(reference*0.95) AND floor(reference*1.05))` for partner
  lines. This is a belt-and-braces DB check, mutation-tested.

**Idempotency:**
- The natural key is `(partner_id, external_order_id)`.
- `RequestFingerprint = SHA-256(external_customer_id | amount_minor | currency)`.
- **Replay:**
  - On an existing key with a matching fingerprint, return the existing order. HTTP 200, with the
    header `Idempotent-Replay: true`.
  - A mismatch returns 409 `ORDER_IDEMPOTENCY_CONFLICT`.
- **Concurrent same-key requests:**
  - The loser's insert hits the unique index.
  - It catches the unique violation (`23505`), reloads, and applies the replay rule.
  - No locks are taken.
- Contact fields are **excluded** from the fingerprint (PRD §8.1: contact sync is separate).
- An `Idempotency-Key` header is accepted and logged but not required. The natural key is
  authoritative. **[Design call]**

**Documents and PDF:**
- The JSON `Content` is the legal record and is written in the same save.
- The PDF is rendered **lazily** from `Content` with QuestPDF **[Decided]** on first request.
  It is stored at `documents/{orderId}/{docNumber}.pdf` and served via a presigned URL.
- This keeps MinIO off the order-write path: a MinIO outage cannot fail an order, and the
  latency budget is untouched.
- Rendering is deterministic, so a re-render is identical. **[Design call]**

**Composition engine** (§10) is a pure domain service in `Sales.Application`, with no IO.

**Ports:**
- `IPartnerOrderService`: create, bulk, get, list, customer history.
- `IInvoiceService`: get, pdf, search.
- `ICartService`, `ICheckoutService`.
- `IOrderAdminService`: confirm payment, cancel, refund. Refund requires Super Admin.
- Outbound gateways per §3.6.

### 5.7 Settings
- `SellerProfile`: legal name, UEN, address, email, `GstRegistered` (false), invoice footer,
  `RequiredBuyerFields`.
- `PaymentInstruction`: versioned and immutable per version, with one active version. Each website
  pro forma snapshots its text.
- `CommerceLimits`:
  - `MinUsd`, `MaxUsd`
  - `BulkDefaultMax` = 100
  - `CompositionDeadlineMs` = 8000
  - `CompositionMaxLines` = 200 (an engineering bound, not a business cap)
  - `Fx:MaxRateAge`
- All Settings writes are Super Admin only.

### 5.8 Admin
- Composition only, with no tables:
  - Dashboard: order counts and amounts by currency and channel, pending website payments, import
    job and review-queue counts, the composition success rate and p95 for the last 24h/7d, and
    recent failure codes.
  - Customer 360: profile, contact history, orders.
  - Order detail with documents.
- Single-module admin operations stay in their owning module (ADR-0007 amendment).

---

## 6. Identity, authorization, tenancy

### 6.1 Humans: Keycloak **[Decided]**
- **`Shop` realm (website customers):**
  - Keycloak's hosted pages handle registration, login, email verification and password reset,
    and Keycloak's SMTP sends the emails. That covers the PRD's only email use.
  - The storefront uses OIDC Authorization Code + PKCE.
  - The API creates the `Customer(Website)` lazily on the first authenticated call, from `sub`,
    `email` and `email_verified`.
  - Checkout requires `email_verified = true`.
  - **[Design call: no custom register/login REST endpoints. The Next.js app redirects to
    Keycloak. If you want a custom registration form, that is a change.]**
- **`Backoffice` realm (admins):**
  - Realm roles `super_admin` and `staff`, mapped to `src/Authorization` permissions.
  - The permission matrix is seeded (PRD §3, acceptance 13):

    | Permission area | Staff | Super Admin |
    |---|---|---|
    | catalog, imports, customers, orders, payment confirmation | ✓ | ✓ |
    | partners and keys, FX overrides, currency enablement, settings, refunds and credit notes, admin accounts | ✗ | ✓ |

  - Admin account management happens in Keycloak itself. The API exposes no user-creation
    endpoint. **[Design call]**
- **Self-hosted in production compose [Decided]:**
  - Keycloak and its own Postgres database run from `build/compose`.
  - The realm exports for both realms (`build/keycloak/*.json`) are the **production
    configuration**: clients, roles, redirect URIs per environment, token lifetimes, and the
    email-verification and reset settings.
  - Secrets are never committed. Client secrets, SMTP host, user and password, the admin
    bootstrap password and hostname are `${ENV}` placeholders, filled from `.env` at
    `--import-realm`.
  - Keycloak SMTP is the only outbound email path. Startup fails fast if SMTP variables are
    missing in Production.
  - The AGENTS.md references to a pre-existing Keycloak instance are replaced.

### 6.2 Partners: API keys **[Decided]**
- A `PartnerApiKey` authentication scheme reads `Authorization: Bearer pk_...`. Keycloak JWTs are
  distinguished by the `pk_` prefix.
- The resulting claims principal carries `partner_id`, `key_id` and `scope` claims.
- Policies check scopes per endpoint.
- A `PartnerPrincipal` record (like `Actor`), built only from claims, is the **only** way
  partner-facing ports receive the partner id. No request DTO has a `partnerId` property. This is
  arch-tested, following the existing "Orders request DTOs must not accept money" test.
- **Tenancy:** every partner-facing query takes `PartnerPrincipal`. Its repository and query
  methods filter on `partner_id` in SQL. A record belonging to another partner returns **404, not
  403**, so it cannot be inferred.
- **Rate limiting:** the ASP.NET rate limiter, partitioned by partner id, at
  `Partner.RateLimitPerMinute`. It is in-process, so ADR-0010's multi-replica caveat applies and
  is noted in ADR-0021.

### 6.3 Errors and correlation
- Every response carries `X-Correlation-Id`: the incoming one if valid, otherwise generated.
- The error body extends `EndpointResult` as `{ code, message, retryable, correlationId, details?,
  items? }`.
- `retryable` is true only for timeouts, `FX_RATE_UNAVAILABLE`, DB transient errors and
  `RATE_LIMITED`.
- Key codes: `VALIDATION_FAILED`, `CUSTOMER_CONTACT_REQUIRED`, `AMOUNT_OUT_OF_RANGE`,
  `CURRENCY_NOT_ENABLED`, `EXACT_COMPOSITION_UNAVAILABLE`, `COMPOSITION_TIMEOUT`,
  `ORDER_IDEMPOTENCY_CONFLICT`, `INVOICE_BUYER_FIELDS_MISSING`, `FX_RATE_UNAVAILABLE`,
  `PRICE_CHANGED`, `NOT_FOUND`, `INSUFFICIENT_SCOPE`.

### 6.4 Admin audit trail
- `BuildingBlocks/Audit` provides `IAuditTrail.Record(actor, action, target, before?, after?)`.
  Every admin service calls it, and it is written **in the same `SaveChangesAsync`** as the change
  as an added entity. An append-only row is not an aggregate with invariants, so this does not
  breach rule 7.
- The table `audit.audit_events` is append-only:
  - The app's DB role has no UPDATE or DELETE on it, granted in the migration.
  - A `AuditSealJob` hash-chains rows in id order every minute (`hash = sha256(prev_hash || row)`).
    The hashes go in a separate append-only `audit.audit_seals(event_id, hash)` table, so the app
    role never needs UPDATE on `audit_events`. **[Design call]**
  - Verification is exposed in admin.
- Tamper evidence is "detectable after the fact", not prevented. **[Design call]**
- PII in `before` and `after` is limited to the changed fields.

---

## 7. Background jobs **[Decided: Postgres-backed queue]**
- **Tables:**
  - `jobs.job(id, type, payload jsonb, status, run_at, attempts, max_attempts, locked_by,
    locked_until, last_error, dedupe_key UNIQUE NULLS DISTINCT)`
  - `jobs.recurring(type, interval)`
- **Claim:**
  `UPDATE ... WHERE id IN (SELECT id ... WHERE status='Queued' AND run_at<=now() ORDER BY run_at
  FOR UPDATE SKIP LOCKED LIMIT n) RETURNING *`
  This runs in an execution-strategy-wrapped command per rule 8.
- **Leases:** a lease expiry (`locked_until`) plus a heartbeat. An expired lease is re-claimed,
  which makes jobs at-least-once, so **every job is idempotent**.
- **Retries:** exponential backoff.
- **Dedupe:** `dedupe_key` coalesces duplicates. For example, only one pending
  `RefreshStorefrontViews` exists at a time.
- **Runners:**
  - The API process runs the worker unless `Jobs:Mode=Disabled`.
  - The same image with `--worker` runs only workers.
  - Production compose runs one `api` with jobs disabled plus one `worker`.
- **Job types:** `ImportJobStep` (one batch; re-enqueues itself until done), `FxRefresh`,
  `RefreshStorefrontViews`, `AuditSeal`, `UsagePrune`, `StagingImageCleanup`.
- Partner orders are **never** jobs. The master prompt forbids making them async.

---

## 8. Storefront read models: materialized views **[Decided]**
Owned by `Catalog.QueryApplication`. Created by migrations with `migrationBuilder.Sql`.

| View | Grain | Contents |
|---|---|---|
| `catalog.mv_storefront_variant` | variant × enabled currency | variant/product ids, slug, SKU, title, brand, primary category id plus ancestor id array, attribute values jsonb, primary image key, resolved price minor (override, else FX), price source, `search` tsvector (title, brand, attributes; `unaccent`) |
| `catalog.mv_storefront_product` | product × currency | min/max price, variant count, facet attribute values, image keys, popularity placeholder (order line counts from `sales`), created_at |
| `catalog.mv_category_facets` | category × attribute × value × currency | counts, price histogram buckets |
| `catalog.mv_home` | section × rank × currency | new arrivals, best sellers (from sales lines, 30d), featured categories |

- **Eligibility:** only Active products, active variants, a priced variant and a usable FX rate.
  So a deactivated variant leaves the views on the next refresh (acceptance 11).
- **Indexes:**
  - A unique index on each view's grain (required for `REFRESH ... CONCURRENTLY`).
  - GIN on `search` and on `ancestor_category_ids`.
  - A trigram index on title.
  - B-tree indexes on `(currency, price_minor)` and `(currency, created_at)`.
- **Refresh:**
  - Through the `RefreshStorefrontViews` job, deduplicated and debounced 5s.
  - Enqueued after any catalog or price admin write, publishing, FX refresh, or override change.
  - Refreshes run `CONCURRENTLY` in dependency order.
  - A nightly full refresh is the backstop.
  - Expected staleness is seconds.
- **Safety net:** the cart, checkout and composition **never read the MVs**. They validate against
  live tables through `ISellableVariantPort` and `IPriceQuotePort`. Staleness can therefore only
  affect what is shown, never what is sold. That is the invariant ADR-0020 names.
- **Cross-schema SQL:** the views read `catalog`, `pricing` and `sales` tables in SQL. This is a
  read-only, DB-level composition, documented in ADR-0020. Migration ordering keeps view
  definitions after their source tables. A schema change in Pricing or Sales must update the view
  definitions. A persistence-contract test fails if any view fails to refresh.
- **Search:** `websearch_to_tsquery` over `search`, ranked by `ts_rank`, with a trigram fallback
  for typos. No external search engine.

---

## 9. APIs

### 9.1 Partner REST (`/api/v1`, API key, OpenAPI with examples) **[Decided: REST only]**
These are the paths from PRD §9:

| Method & path | Scope | Purpose |
|---|---|---|
| `POST /orders` | `orders:create` (+ `customers:write` when creating or syncing a customer) | Create one amount-driven order |
| `POST /orders/bulk` | same | Bulk create (§10.4) |
| `GET /orders` | `orders:read` | Partner-scoped list; filters on external ids, dates, currency, customer; cursor paging |
| `GET /orders/{id}` | `orders:read` | `{id}` is our id **or** `ext:{externalOrderId}` |
| `GET /customers/{id}/orders` | `customers:read` | `{id}` accepts `ext:` as above |
| `GET /invoices/{id}` | `invoices:read` | Invoice JSON |
| `GET /invoices/{id}/pdf` | `invoices:read` | 302 redirect to a presigned URL |
| `POST /invoices/search` | `invoices:read` | Body: external order ids (≤100); per-item outcomes |

- **Route conflict:** website and admin routes also need orders. The **partner** API keeps the
  PRD paths at `/api/v1/orders` etc. The website uses `/api/v1/shop/...` and admin uses
  `/api/v1/admin/...`. Routes are disambiguated by prefix, not by the auth scheme. **[Design call]**
- **Response body** (create, get): order id, external refs, customer id, line snapshots, money as
  `{amount: "500.00", currency}` strings, the status dimensions with their sources, the
  commercial invoice `{id, number, json, pdfUrl}`, and `correlationId`.

### 9.2 Storefront: REST `/api/v1/shop/*` and GraphQL (same `/graphql`) **[Decided: both]**
Anonymous:
- `home(currency)`
- `categories` (tree)
- `products(filter: {categoryId, brandIds, attributes, priceMin/Max, q}, sort, cursor, currency)`
  with facets. This is the PLP.
- `product(slug, currency)`: the PDP with variants, attributes, media and prices.
- `search(q)`
- `currencies`

Authenticated (Shop):
- `me`, `updateProfile`
- `cart`, `addToCart`, `updateCartLine`, `removeCartLine`, `setCartCurrency`
- `checkout(expectedTotal)`: returns `PRICE_CHANGED` with fresh prices if the live total differs
  from what the customer saw. This guarantees "no post-checkout repricing" and "price shown =
  price charged".
- `myOrders` (website channel only)
- `order(id)`
- `documentPdfUrl(documentId)`

### 9.3 Admin: REST `/api/v1/admin/*` and GraphQL **[Decided: both]**
Covers every PRD §10 view:
- Catalog CRUD, toggles, bulk edit, duplicate merge, media upload (multipart → MinIO)
- Pricing and overrides
- Currencies and FX (Super Admin)
- Imports: create job, upload file, mapping preview, progress, staged records, review actions,
  bulk publish, templates
- Orders: list and filter both channels, detail, confirm payment, cancel, refund (Super Admin)
- Customers and contact history
- Partners and keys with usage (Super Admin)
- Settings (Super Admin)
- Dashboard
- Audit verification

GraphQL limits: max depth 10, complexity limits, and the existing `admin` rate-limit policy.
REST and GraphQL are both thin adapters over the same inbound ports, so behaviour cannot diverge.

### 9.4 Delivered artifacts
- The OpenAPI document with partner examples.
- A Bruno collection under `docs/api/`. **[Design call: Bruno over Postman because it is plain
  text files in the repo.]**
- A GraphQL schema SDL snapshot test.

---

## 10. Amount-driven composition engine
This is a pure function in `Sales.Application/Composition`:
`Compose(target, candidates[], options, seed, deadline) → Result<Composition>`.
All values are `long` minor units.

### 10.1 Inputs
- `target T`
- candidates `{variantId, categoryId, r, lo, hi}` from `IPriceQuotePort`, restricted to sellable
  variants with a quote in the requested currency
- options: max lines, deadline, strategy version
- `seed`: random, persisted

**Candidate pool:** every partner draws from the **whole sellable catalog**. There is no
per-partner assortment. The PRD's "partner must have authorization for these purchases" is a
contractual and legal release gate (§16), not a data filter. **[Decided]**

**Candidate loading:**
- The gateway returns the full sellable set for the currency in one query.
- It is cached in-process for 30s per currency and invalidated whenever a price or catalog write
  bumps a version number. **[Design call]**
- Before the order is built, the chosen lines are **re-quoted live** so the snapshot uses
  current values.

### 10.2 Algorithm (strategy v1) **[Design call; the builder may improve it if the property tests
and the coverage report stay green]**
1. **Pick the line count:** `k` lines, drawn from a range scaled to the amount. For example
   `k ≈ clamp(round(log10(T_usd)) + rand(0..2), 1, maxLines)`. That gives small orders 1–3 lines
   and large orders more.
2. **Choose candidates:**
   - Sample `k` candidates, weighted toward category variety (no repeated category until all are
     used) and toward `r` near `T/k`.
   - Quantities: `q_i = max(1, round(T_i / r_i))` for the per-line share `T_i`, capped by a
     "realistic quantity" bound. Default 500, or larger when `r` is small relative to `T`.
3. **Check feasibility:**
   - Reachable totals form the sumset of `{q_i·p : p ∈ [lo_i, hi_i]}`.
   - Compute `L = Σ q_i·lo_i` and `H = Σ q_i·hi_i`. If `T ∉ [L, H]`, adjust quantities or swap a
     candidate, then repeat.
4. **Hit the exact amount:**
   - Start every `p_i` at `r_i`. Let `Δ = T − Σ q_i·r_i`.
   - Spread `Δ` across lines in proportion to `q_i·(hi_i−lo_i)`, moving `p_i` by whole steps of
     size `q_i`.
   - The leftover `ρ` (less than the largest `q`) is absorbed by a line with `q=1` (any integer is
     reachable), or by solving `Σ q_i·d_i = ρ` over the remaining slack with a small bounded
     search (gcd-aware).
   - The engine always tries to include at least one `q=1` line, because that guarantees
     minor-unit exactness.
5. **Pick the best:** prefer minimal Σ|deviation|, realistic quantities, 2–8 lines for typical
   amounts, and category variety. Generate several valid solutions within budget and choose by a
   scoring function, using the seed for tie-breaking.
6. **Broaden on failure:** a wider candidate pool, a different k, relaxed variety. All attempts
   share one deadline (default 8s, inside the 10s p95).
7. **Fail cleanly:** `EXACT_COMPOSITION_UNAVAILABLE` when the search space is exhausted,
   `COMPOSITION_TIMEOUT` when the deadline passes. **No writes.**
8. **Verify independently:** a separate checker re-validates bounds, `Σ == T` and `q ≥ 1`. The
   `Order` aggregate validates them again, and the DB check constraint is the last line.

### 10.3 Partner order flow (`POST /orders`)
1. Authenticate, derive the `PartnerPrincipal`, check scopes.
2. Validate the body. Look up `(partner, external_order_id)`; if found, apply the replay or 409
   rule and return.
3. Check the currency is enabled, then check the amount's USD equivalent against the bounds
   (`IFxSnapshotPort`).
4. Run composition. This is pure: no writes happen before it succeeds.
5. Resolve the customer via `ICustomerResolutionPort`.
   - **Existing customer:** read only. Contacts are not synced yet.
   - **New customer:** created here in its own save. Missing email or phone fails with no writes
     at all. That check runs **before** composition, as a validation step.
   - Creation comes **after** composition on purpose, so a failed composition creates no customer.
   - **Documented non-atomic edge:** if the order save in step 6 fails, the new customer
     persists. That is harmless and idempotent by `(partner, external_customer_id)`.
6. Build `Order.CreatePartnerOrder(...)` with the live re-quote, the seller and buyer snapshots,
   and the document number from a sequence. Make **one save**. On `23505` for the external key,
   go to the replay path.
7. **Contact sync, only after a new order is successfully created [Decided]:** for an existing
   customer, apply the supplied email, phone and name changes with `ContactChange` audit entries
   (its own save).
   - Replays, 409 conflicts, validation failures and failed compositions **never** sync contacts.
   - A sync failure here does not undo the committed order. It is logged and metered
     (`customer_contact_sync_failures`), the response still returns 201, and the response's
     `warnings` carries `CONTACT_SYNC_FAILED`.
   - The invoice buyer block uses the **supplied** contact values, so the invoice matches the
     request even if the sync fails.
8. Return 201 with the order and invoice.

### 10.4 Bulk (`POST /orders/bulk`)
- Up to `Partner.BulkMaxItems` items (default 100). Over the limit returns a 400 for the whole
  batch.
- Items run **independently**: each uses the §10.3 flow with its own DI scope and DbContext.
  Execution is bounded-parallel, degree 4, configurable. **[Design call]**
- The response is 200 with per-item `{index, externalOrderId, status: created|replayed|failed,
  order?, error?}`. One failure never rolls back another item.
- An overall request deadline is configurable (default 120s). Items not started before the
  deadline report `BATCH_DEADLINE_EXCEEDED, retryable: true`.

### 10.5 Coverage simulator and load test
- **`tools/CompositionSimulator`** (console app):
  - Runs over the seeded catalog or a supplied one, for all enabled currencies.
  - Amount distribution: log-uniform $10–$1M, plus the mandated points (10.00, 17.43, 500.00,
    12,845.99, 1,000,000.00) and random cent values.
  - Reports success rate, failure codes, p50/p95/p99 duration, mean lines and deviation, and
    uncovered ranges. Output is markdown + JSON written to `docs/reports/`.
  - This file **defines** the "representative distribution" the PRD asks to document. The 99%
    target is measured against it and not claimed beyond it.
- **k6 script** (`tests/load/partner-orders.js`): single-order p95 under declared load (default
  10 VUs, 5 min) against the compose stack. The results are recorded in the same report.
- **Seed catalog:** about 400 variants across 12 categories, with prices from $0.50 to $50,000,
  so that $10 and $1M are both reachable. This is demo data marked as fictitious. It is not
  crawled.
  - **The seed is dev/test only.** `--seed` refuses to run when `ASPNETCORE_ENVIRONMENT=Production`.
    Production starts with an empty catalog, so partner orders return
    `EXACT_COMPOSITION_UNAVAILABLE` until an import publishes real products. The runbook says so.
    This blocks anti-goal #1, invoicing fictitious SKUs. **[Design call]**

---

## 11. Website order flow
1. Cart operations quote live prices, and the cart stores the displayed unit prices.
2. `checkout(expectedTotal)`:
   - Re-quote live and re-check sellability.
   - If any unit price or the total differs from what was displayed, return `PRICE_CHANGED` with
     the new cart and write nothing.
   - Otherwise, using the buyer fields and the active `PaymentInstruction`, run
     `Order.CheckoutWebsite`: PendingPayment plus a ProForma snapshot including the instruction.
   - Clear the cart. That is a second save of a different, independent aggregate. If it fails,
     the cart is stale but harmless.
3. The customer sees the pro forma and the instruction, and pays outside the system.
4. Staff or a Super Admin calls `confirmPayment(orderId, method, reference)`. In one save it sets
   Paid/AdminConfirmed and Fulfilled, and issues the Commercial invoice.
5. Super Admin can call `refund(orderId, amount, reason)` to create a Refund and a CreditNote.
   `cancel` is available on any unpaid order.

---

## 12. Production build directory **[Decided]**
```
build/
├── docker/
│   ├── api.Dockerfile          # multi-stage: sdk restore/build/publish → aspnet:10 chiseled, non-root;
│   │                           # same image runs api | --worker | --migrate | --seed
│   └── .dockerignore
├── compose/
│   ├── docker-compose.yml          # production: caddy, api, worker, migrations(init), postgres,
│   │                               #   minio, minio-init(mc: buckets+policies), keycloak, keycloak-db
│   ├── docker-compose.dev.yml      # dev: postgres, minio, keycloak only (API runs from IDE)
│   ├── .env.example                # every variable, no values for secrets
│   └── caddy/Caddyfile             # TLS termination, /media → minio catalog-media
├── keycloak/
│   ├── shop-realm.json
│   └── backoffice-realm.json
└── postgres/
    └── init/01-roles.sql           # app role (no UPDATE/DELETE on audit), migrator role
```
- The current root `Dockerfile`, `Dockerfile.ci` and `docker-compose.yml` move into `build/`, and
  the root copies are removed. **[Design call]**
- Health checks run on every service. `api` and `worker` depend on `migrations` completing
  successfully.
- Secrets come only from `.env` or the environment:
  - `ConnectionStrings__Default`, `JWT`/Keycloak settings
  - `Minio__AccessKey/SecretKey`
  - `ApiKeys__Pepper`
  - `Brave__ApiKey`, `Anthropic__ApiKey`
  - `KC_DB_PASSWORD`, `POSTGRES_PASSWORD`, `MINIO_ROOT_*`
  - Keycloak SMTP
- `docs/RUNBOOK.md` covers deploy, migrate, seed, key rotation, backup/restore (pg_dump + mc
  mirror), and incident checks.
- `docs/SECURITY-CHECKLIST.md` is part of the ops package.

---

## 13. Testing
The existing discipline continues: mutation-test every guard.

- **ArchitectureTests:**
  - Update `ModuleNames`, the exception rows in §3.6, and "QueryApplication isolation".
  - Add: no `partnerId` on partner request DTOs; no money on website request DTOs except
    `expectedTotal`; no `BeginTransaction` (existing); Sales.Application has no IO dependencies in
    `Composition/`.
- **UnitTests:**
  - Money and band math, FX resolution including the AED peg and stale rates.
  - Composition: FsCheck property tests (`Σ == T`, bounds, q ≥ 1, determinism per seed) and the
    mandated amounts × 5 currencies.
  - Order aggregate state transitions, refund limits, customer contact sync, and the
    import-normalizer "no invented facts" rule.
- **PersistenceContractTests:**
  - Npgsql against a refused port (translation) plus InMemory (materialization), as today.
  - Plus refresh of every MV against a real database (that one is in the integration suite).
- **IntegrationTests:**
  - **Testcontainers PostgreSQL** (and MinIO) replace LocalDB. Docker is required.
    **[Design call]**
  - Covers the 16 PRD acceptance scenarios, each as a named test.
  - Plus: concurrent same-key orders, cross-partner 404s, the DB check constraint on the band,
    the audit append-only grant, and MV staleness never allowing a sale.
- **Load and coverage:** §10.5. Not part of `dotnet test`.

---

## 14. Delivery phases (one run, reviewed per phase)
After each phase: build, run the tests, then write a short report (what works, what is
incomplete, risks, reproduction commands), as the master prompt requires.

**Execution mode [Decided: whole PRD end to end]:**
- The run is continuous. Each phase report is written and work carries on, with no stop for
  sign-off.
- Every phase must still end with a green build and passing tests.
- The Postgres `init` migration may be regenerated until the run finishes. After that, migrations
  are additive only.
- Phases follow dependency order. Phase 7 (Sourcing) may start once phase 2 is done.

| # | Phase | Main contents |
|---|---|---|
| 0 | Platform | ADRs 0017–0022; Postgres switch + fresh migration; remove legacy modules; `Storage`, `Jobs`, `Audit` building blocks; `build/` + compose; Testcontainers; doc fixes |
| 1 | Identity and tenancy | Keycloak realms + permission seeding; Partners module + API-key scheme + rate limits + usage log; Settings; Customer |
| 2 | Catalog and Pricing | Catalog extensions (Brand, Product/Variant/Media/Identifiers); Pricing, FX (ECB), overrides; seed catalog |
| 3 | Storefront reads | MVs + refresh job; shop REST + GraphQL browse/search |
| 4 | Sales: partner | Composition engine + simulator; Order aggregate; partner REST create/bulk/get/list/invoices; QuestPDF |
| 5 | Sales: website | Cart, checkout, pro forma, payment confirmation, cancel, refunds, credit notes |
| 6 | Admin | Admin REST + GraphQL for all PRD §10 views; dashboard; audit verification |
| 7 | Sourcing | File import + templates; crawler; Brave search; Claude enrichment; matching, review queue, publishing |
| 8 | Ops package | OpenAPI examples, Bruno collection, k6 + coverage report, runbook, security checklist, seed/demo guide |

---

## 15. Left to the builder
- Exact class and file names inside each module, following the existing Catalog conventions.
- The Mapperly vs hand-mapping inconsistency: follow DDD-GUIDELINES §9 (map by hand).
- Composition scoring weights and the specific k/quantity heuristics, as long as §10.5 shows ≥99%
  on the documented distribution or the report states the gap honestly.
- Page sizes, cursor encoding, GraphQL type naming.
- QuestPDF layout. It must show the seller block, "Invoice" (never "Tax Invoice"), the not
  GST-registered statement, and the fulfillment-assertion statement.
- Whether `RowVersion` stays on `Entity<TId>` once `xmin` is in use.

## 16. Release gates (not blockers for the build; from PRD §13)
The build does not resolve these, and the runbook lists them as go-live blockers:
- Evidence of real supply and delivery for partner-selected goods.
- End-customer authorization.
- Legal invoice recipient fields per jurisdiction (hence the configurable
  `RequiredBuyerFields`).
- Content and image rights policy per crawled domain (hence `SourcePolicy`).
- Accounting review for non-GST invoicing and credit notes.
- Agreement on the load profile behind the p95 target.

## 17. Calls the designer made (for review)
1. Fresh Postgres migration; no data migration from SQL Server.
2. Money stored as `bigint` minor units.
3. HMAC-SHA256 with a pepper for API keys, not Argon2.
4. Hosted Keycloak pages for registration, login and reset, so there are no custom auth
   endpoints.
5. Lazy PDF rendering; the JSON snapshot is the legal record.
6. New-customer creation happens after composition but before the order save. If the order save
   then fails, the customer stays. Contact sync for an existing customer runs after the order
   save, and if it fails the order still succeeds, with a warning.
7. Domains with no source policy: crawl only if robots.txt allows; content goes to review, never
   auto-publish.
8. The crawler extracts structured data only (JSON-LD, microdata, OG).
9. Route prefixes `/api/v1/shop` and `/api/v1/admin`; the partner API keeps the PRD's bare paths.
10. Default invoice buyer fields: email or phone (configurable).
11. In-process 30s candidate cache for composition, with a live re-quote of chosen lines.
12. Testcontainers in place of LocalDB.
13. ADR-0016 accepted as the read-side convention.
14. Audit hash-chaining by a sealing job (detect, not prevent).
15. Root Docker files move into `build/`.
16. `--seed` refuses to run in Production; production starts with an empty catalog.
17. Audit hashes go in a separate append-only seal table.

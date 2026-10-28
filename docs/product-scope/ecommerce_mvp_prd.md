# API-First B2B E-Commerce — MVP Product Requirements Document

**Status:** Discovery baseline agreed through Round 40 | **Version:** 1.0 | **Date:** 2026-10-28

## 1. Executive summary

Build a small, API-first Singapore-based e-commerce system with a minimal customer storefront and an internal admin panel. The platform is the seller. It supports two sales paths:

1. **Partner API orders:** An authenticated B2B partner submits its external customer ID, contact details when needed, its external order ID, amount and currency. The platform resolves/creates the partner-specific customer, automatically chooses genuine, active catalog items and quantities, adjusts each item's selling price by at most ±5% of its applicable reference price, and creates an order whose **final total matches the requested amount exactly**. Upon successful creation, mark payment as partner-reported PAID, fulfillment as SYSTEM_AUTOMATIC/FULFILLED, and issue a commercial invoice. Failed composition creates no order/invoice/payment.
2. **Website orders:** A registered consumer browses the homepage/PLP/PDP, selects cart items at displayed fixed prices, and checks out without an embedded payment gateway. At checkout generate a pro forma invoice and show one centrally configured payment instruction. An admin independently confirms external receipt of funds; only then issue the commercial invoice and mark the order PAID and FULFILLED.

**Business integrity requirement:** Automatically selected products/services must actually be sold and supplied under a valid purchasing authorization. A partner-submitted amount is not, by itself, evidence of customer consent or delivery. The 'FULFILLED' status is a workflow assertion, not independent proof of delivery. Never invent commercial invoice line items that do not represent a genuine sale.

## 2. Confirmed product scope

### 2.1 Modules
- Catalog: categories, hierarchical attributes, brands, products, variants, media, catalog import/discovery and staging.
- Inventory: no decrementing stock or warehousing; active/published availability controls. All active variants are assumed always available.
- Pricing: base-currency reference prices, market source provenance, currency-specific overrides, exchange rates, ±5% partner pricing adjustment and exact-total composition.
- Orders: API and website flows, immutable order item snapshots, order statuses, idempotency and audit logs.
- Customers: website accounts and partner-specific customer identities, profile, contact synchronization and order isolation.
- Payments: offline website payment confirmation, partner-reported API payments, refund/credit note records.

### 2.2 Storefront MVP
Homepage, product listing page, product detail page, search/category/filter navigation, cart, checkout, customer registration/login, email verification, password reset, profile, website-only order history, pro forma/commercial invoice viewing. All active products and active variants appear in the website and can be selected by the partner engine; inactive/draft/unvalidated items appear in neither.

### 2.3 Out of scope
Payment gateway, payment proof upload, carrier shipping integration, shipment tracking, stock reservations or deductions, supplier provisioning integration, customer order emails, partner webhooks, partner portal, configurable multi-role RBAC builder, bulk invoice ZIP export, scheduled catalog/price re-crawling.

## 3. Actors and authorization

- **Super Admin:** Full system access; manages other admin accounts, company settings, partners/keys, currency enablement, rate overrides, payment instruction, refunds and credit notes.
- **Staff:** Catalog/import job management, customer and order administration, external website payment confirmation; no API key/partner, FX or system settings access. Refund/credit-note permission reserved for Super Admin.
- **B2B partner:** Authenticated via individual API key(s), with scopes, throttling, credential rotation and revocation. No partner UI. `partner_id` is ALWAYS derived from key authentication.
- **Website customer:** Email/password account; verification/reset emails only. Website history includes only directly placed website orders.

Partner records are tenant-scoped: order external key = `(partner_id, external_order_id)`; partner customer key = `(partner_id, external_customer_id)`. Equal emails/phones across partners or website accounts do not automatically merge customers.

## 4. Catalog and import requirements

### 4.1 Catalog entities
- **Category:** unique canonical key within parent; parent-child hierarchy; attributes assigned by category; active flag.
- **Attribute definition:** canonical key, datatype (text, number, option, boolean, etc.), units, validation and scope; canonical aliases; deduplicated across import jobs.
- **Brand:** normalized canonical name, aliases, provenance, active flag.
- **Product:** canonical product with brand, primary category, title, descriptions, images, manufacturer identifiers and source metadata; active/published flag.
- **Variant/SKU:** belongs to a product; uniquely defined by its normalized variant attributes (and reliable external identifiers where present); its own reference price and overrides; active flag; unlimited logical availability.
- **Source listing / price observation:** provenance URL/feed, collected timestamp, captured currency and price, tax inclusion where known, images and any rights/usage metadata.

### 4.2 Import/discovery modes (MVP)
- Crawl supplied website URLs/domains, respecting permitted access and content rights.
- Search the web by query/keyword/category; permit included/excluded domains and combined web+specific-source mode.
- Bulk CSV, Excel and JSON import; provide downloadable CSV/XLSX templates within the admin panel and mapping/preview validation. JSON should use a documented equivalent schema.
- Design a source adapter interface for **future** third-party scraper providers, but do not implement provider-specific integrations in MVP.

### 4.3 Import pipeline
1. Admin starts an import job with source(s), queries, category targeting, volume/depth restrictions, allow/deny domain filters and publishing policy.
2. Discover/extract product data and images, source prices and original currency; collect at scale and show progress/stats.
3. Normalize names, categories, brands, units, attributes, images and variant structure; enrich with AI or deterministic matching as needed; do not hallucinate unavailable facts.
4. Run reliable ID matches and duplicate checks; fuzzy matches enter review, never unverified automatic merge.
5. Validate sellability and actual availability/deliverability, key attributes, price, reference, images and legal ability to use content.
6. By default **auto-publish validated** records and route invalid/ambiguous records to a review queue. Every job may override to preview/manual publishing.
7. On repeated import, upsert/reuse canonical entities, never proliferate duplicates, and do not automatically reactivate explicitly deactivated products/variants.

**One-time import only:** No scheduled catalog refresh or automatic market price updates. Admin may start another job to refresh later. Exchange-rate updates are separate.

### 4.4 Duplicate controls
Use database constraints alongside matching rules. Categories: normalized name+parent; attributes: canonical scope+key; brands: canonical name plus aliases; products: GTIN/UPC/EAN or brand+MPN or source ID with ambiguity review; variants: product+normalized attribute signature; currency price: variant+currency+price list. Idempotent import-stage fingerprinting and external source IDs prevent duplicates within and across jobs.

## 5. Currency and price requirements

- Enabled currencies configurable by Super Admin. Initially `USD`, `EUR`, `SGD`, `AED`, `GBP`; all use two decimal minor units.
- Each variant has a reference price in its base currency. A requested currency-specific override, if present, takes precedence; otherwise convert via selected FX rate.
- FX: external provider, configurable refresh interval, per-pair administrator override, rate provenance, timestamp, bounded stale-cache allowance; fail safely when a usable rate is unavailable.
- A partner API order may adjust **final per-unit selling price within ±5%** of that SKU's applicable currency reference price. Website always uses the **displayed fixed selling price**.
- The fixed selling price presented to a website customer must be preserved at confirmed checkout; no post-checkout repricing.
- Lock ordered SKU identity, names, attributes, quantities, unit prices, price reference, FX rate/source and invoice totals into immutable order/invoice snapshots. Global catalog updates cannot change historical transactions.
- Order allowed range: `USD 10` to `USD 1,000,000` equivalent, both configurable; validate with an established FX snapshot.
- B2B requested amount is **final payable inclusive** of applicable taxes/fees. The commercial invoice `total_due` must exactly equal the submitted amount in the requested currency.

## 6. Amount-driven order composition engine

**Inputs:** partner identity from API key, partner external customer/order refs, amount in currency minor units, supported currency, catalog snapshot, permitted price band, price/tax rules.

**Non-negotiable:** Randomness is used to select among valid/competitive compositions, never to compromise transaction validity. Every selected item is a genuine sold/deliverable item and the partner must have authorization for these purchases.

Algorithmic plan:
1. Validate amount, currency, partner, client idempotency and customer identity; resolve/create customer in transactional flow.
2. Retrieve eligible active/published SKUs in requested currency, with conversion/override and permitted min/max selling unit prices.
3. Generate amount-proportional randomized candidate sets across categories; weight towards sensible reference prices and quantities. Do not set a fixed **business** line-item cap; impose **engineering** execution/resource bounds.
4. Optimize quantities and replace/add/remove SKUs, considering per-line permitted interval and exact money units; choose a solution via integer/decimal-safe constrained optimization, not floating point.
5. Allocate final unit prices (within 95%–105% of reference, after currency rounding), verify each line amount and exact final invoice total. Prefer minimal deviation, realistic quantities, reasonable number of line items, and category variety.
6. If initial search fails, broaden candidate universe, quantities and search strategy within the same valid business constraints. Use bounded retries and deadline.
7. If impossible or time budget exhausted, return `EXACT_COMPOSITION_UNAVAILABLE` (or `COMPOSITION_TIMEOUT`) without order/payment/invoice writes. Do not silently inflate prices or fabricate items.
8. On success, atomically persist order, items, partner-reported payment record, automatic fulfillment assertion and commercial invoice. Persist composition strategy and seed/trace for audit and reproducible debugging.

**Target:** at least 99% exact composition success for valid amount requests **measured on a documented representative test distribution**, not assumed for every mathematically possible amount/catalog. A price coverage tool must run sample requests over min/max amounts, price decimals and all configured currencies, and report gaps. Success rate, candidate iterations, duration and failure codes must be monitored.

**Latency:** target p95 under 10 seconds for a single synchronous order request, including invoice issuance, under the agreed test workload; enforce server-side timeouts. Synchronous bulk batches are independently processed and may take longer overall, with a configurable initial batch size of 100.

## 7. Customers

- API partner must supply `external_customer_id`. For a **new** customer, both `email` and `phone` are required. For **existing** partner-linked customers, these fields may be omitted.
- Synchronize supplied changed email/phone on an existing partner-linked customer automatically; omit field means preserve stored value. Store old/new values, timestamp, source partner and request correlation ID. New email/phone is NOT automatically treated as verified.
- Website account email/password + verification and reset. Keep it separate from partner identities; do not auto-link on contact equality.
- `full_name`, `company_name`, billing address/country/registration details remain optional in the customer model. Any fields legally required for a specific commercial invoice must be validated before issuance; fail cleanly or require additional data rather than issuing a misleading invoice.

## 8. Orders, invoices and payments

### 8.1 API orders
- Accepted create: `PAID` (source `PARTNER_REPORTED`), `FULFILLED` (source `SYSTEM_AUTOMATIC`), commercial invoice issued immediately.
- 'Automatic fulfilled' is a configured assertion; do not claim independently verified delivery.
- Duplicate `(partner_id, external_order_id)` with identical material request returns existing order and invoice; conflicting amounts/currency or materially different order payload return 409. Contact-info synchronization should be handled separately from immutable order idempotency logic.
- No pending successful API order state; if exact match, billing validation, conversion or invoice creation fails, create no order/payment/invoice.

### 8.2 Website orders
- Customer picks cart items and quantities at visible fixed prices.
- Checkout creates `PENDING_PAYMENT` order and pro forma invoice, displays the same centrally configured instructions for every currency; preserve instructions shown for that order.
- Customer pays externally. No gateway and no receipt-upload flow. Admin verifies receipt independently and accepts order, recording verifier, time and payment method; system then records `PAID`, `FULFILLED` and issues commercial invoice.
- Website customer sees website orders only, plus PDFs and pro forma/commercial documents.

### 8.3 Invoices and refunds
- Separate identifiers/sequences for pro formas, commercial invoices, and credit notes; preserve issued originals (never silently rewrite).
- PDF + JSON per invoice; B2B bulk JSON retrieval returns individual invoice records and secure PDF URLs; no ZIP requirement.
- Admin can cancel unpaid orders, record full/partial external refunds and issue proper linked credit notes/adjustments for paid orders; originals retained. Restrict refunds and credit notes to Super Admin in MVP.
- Model payment, order, fulfillment, refund and document status as independent dimensions. Preserve audit trail.
- Seller is a **Singapore company currently not GST-registered**: do not charge Singapore GST or label a document a GST tax invoice. Seller legal name/UEN/address are configurable. Future GST registration/tax rules must be supportable without redesign; seek jurisdiction-specific accounting review before go-live.

## 9. B2B API draft (paths may be refined in OpenAPI)

Authentication: `Authorization: Bearer <partner-api-key>` over TLS, backed by a secure hashed key and optional `Idempotency-Key`. Permissions: `customers:read`, `customers:write`, `orders:create`, `orders:read`, `invoices:read`. Partner always derived from key.

| Method | Path | Purpose |
|---|---|---|
| POST | `/api/v1/orders` | Create one amount-driven order |
| POST | `/api/v1/orders/bulk` | Synchronous array of independent orders; item-level outcomes |
| GET | `/api/v1/orders` | Partner-scoped paginated orders, filtering |
| GET | `/api/v1/orders/{id}` | Partner-scoped single order |
| GET | `/api/v1/customers/{id}/orders` | Partner-scoped customer history |
| GET | `/api/v1/invoices/{id}` | Invoice JSON |
| GET | `/api/v1/invoices/{id}/pdf` | Authorized invoice PDF |
| POST | `/api/v1/invoices/search` | Batch lookup via external order IDs, per-item outcomes |

Sample create request:
```json
{
  "external_order_id": "173",
  "external_customer_id": "456",
  "customer": {"email": "martin@example.com", "phone": "+6591234567", "full_name": "Martin"},
  "amount": "500.00",
  "currency": "USD"
}
```

Response includes order ID, partner external refs, customer ID, item snapshots, money totals, `payment_status=PAID`, `payment_source=PARTNER_REPORTED`, `fulfillment_status=FULFILLED`, `fulfillment_source=SYSTEM_AUTOMATIC`, commercial invoice ID/number/JSON and authorized PDF URL. Every response includes correlation/request ID. Use precise error classes and explicit retry eligibility.

Bulk: recommended max 100 items per synchronous batch initially, configurable. One failing item does not roll back other successful items; idempotent per item, including concurrent calls. No partner can access or infer another partner's records.

## 10. Admin panel views

- Dashboard: order counts, amounts by currency, pending website payments, import jobs/validation queues, failure indicators and basic composition success metric.
- Catalog: category tree, brands, attributes, products/variants, images, status toggles, prices/overrides; bulk editing and duplicate resolution.
- Imports: source selection (website, search, files), download template, mapping, staging preview, discovered products with photos and source prices, errors, duplicate actions, job progress, auto-publish toggle (ON by default) or manual review, bulk approve/publish.
- Orders: website/API orders, statuses, customer details, PDFs; admin website payment acceptance and cancellation.
- Customers: website and partner-linked customer records, contact-change audit history.
- Payments/refunds: offline confirmations, manual full/partial refunds and credit notes with access checks.
- Partners (Super Admin): create/suspend, scopes, rate limits, issue/rotate/revoke keys, key usage logs.
- Settings (Super Admin): seller identity, one website payment instruction, enabled currencies, FX provider/refresh/overrides, order bounds and other config.

## 11. Data integrity, security and reliability

- PostgreSQL or equivalent relational DB with immutable foreign-key links, dedup unique indexes, money stored in minor units/precise decimal and transactional idempotency.
- Scope every partner-bound lookup, list query, invoice document and key by authenticated partner. Never trust `partner_id` sent in an API body.
- Encrypt private data at rest where appropriate; hash API keys and customer passwords with modern password hashing; TLS, rate limiting, authorization, tamper-evident admin audit logs, log redaction and PII retention controls.
- Use an internal background job worker for **catalog discovery**, even though order creation must be synchronous. Catalog imports should expose progress and resumability.
- Invoice file downloads require authorization or short-lived scoped signed URLs.
- Email is used ONLY for customer account verification and password resets in MVP.
- Version public API (`/api/v1`); publish OpenAPI contract and examples. Observability includes request IDs, latency histograms, import outcomes, conversion failures and exact-match rate.

## 12. Acceptance scenarios (minimum)

1. API partner A creates a new customer (email+phone supplied) and $500 order: a valid SKU composition totaling exactly $500, paid/fulfilled partner-reported order, commercial invoice, PDF link; completes within defined latency target.
2. Same external order submitted again: returns identical order and invoice; no double charges/records.
3. Same external order with different amount: HTTP 409 with conflict code; original remains intact.
4. Partner A and B use same external customer/order IDs: independent records; cross-partner invoice access denied.
5. Existing customer contact details change: updates with field-level audit entries; name/contact omission does not erase prior values.
6. Customer creation without required phone/email: validation error; no order/invoice.
7. Request fails to find exact composition by deadline: explicit error, no partial order/payment/invoice. No SKU violates ±5%.
8. Multi-currency base conversion and override produce exact minor-unit total; applied FX/override snapshot remains stable if rates update later.
9. Website checkout creates pro forma only, pending payment; admin acceptance creates commercial invoice, partner API orders not shown in website history.
10. Repeated catalog import does not duplicate category, attribute, brand, product or variant; invalid/ambiguous records remain staged; default auto-publish validated only.
11. Deactivated variant disappears from both PLP and partner selection; existing invoice snapshots remain unchanged.
12. Full/partial external refunds create audit entries and linked credit notes while preserving original commercial invoice.
13. Staff cannot manage partner credentials, currency overrides, system settings or refunds.
14. Bulk 100-item order request returns per-item outcomes, with successful items committed and failed items isolated.
15. Import crawler/search preserves source provenance and rejects invalid content; source restrictions enforced.
16. Load test records p50/p95/p99 composition latency and exact-match coverage across all enabled currencies and the configured amount range.

## 13. Important design decisions still to validate before production

These are *implementation/release gates*, not reasons to stall the initial build:
- Choose the implementation stack, deployment environment and email service.
- Establish reliable **real delivery, customer authorization, transaction evidence and legal invoice content** for partner-selected goods. Agreement with partner alone may not establish end-customer consent or prove fulfillment; seller must substantiate actual supply.
- Confirm any legally required invoice recipient fields for the seller's product mix and relevant jurisdictions; minimum email+phone may not always suffice.
- Configure the source/terms/rights policy for web-crawled product data and product photos.
- Define the representative sampling distribution and load used to measure 99% composition success and p95 under 10 seconds.
- Decide technical order search cutoffs, maximum batch request time, FX stale-rate ceiling and supported refund edge cases.

## 14. Proposed build sequence

1. Domain model, role/tenant isolation, auth, partner keys, customer identities, migrations.
2. Canonical catalog, categories/brands/attributes/products/variants, currency/FX/rate overrides.
3. Amount composition service with coverage simulator, rigorous integer-unit tests and transaction/idempotency guarantees.
4. Partner APIs, synchronous bulk behavior, invoice JSON/PDF, order histories and document security.
5. Website auth/storefront/cart/checkout, pro forma, manual payment acceptance.
6. Admin panel, offline refund/credit notes, import staging/validation/publishing.
7. Web discovery adapters/keyword search/import templates, observability, end-to-end/load/security tests.

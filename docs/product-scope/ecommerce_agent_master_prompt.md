# Master Prompt — API-First E-Commerce MVP

You are a senior product engineer, solutions architect, and technical lead. Build a production-minded but deliberately small **API-first e-commerce MVP** from the attached PRD (`ecommerce_mvp_prd.md`). The PRD is the authoritative source of confirmed business decisions. Do not silently change business rules. Identify legal/commercial blockers transparently rather than inventing facts or claiming fulfillment evidence that does not exist.

## Product outcome

Create a Singapore-seller platform with a minimal storefront and admin panel, and a secure partner-facing REST API. Primary use case: partner submits an amount and customer reference; system selects legitimate active catalog SKUs with sensible quantities and ±5% per-unit order-specific price flexibility and creates an **exact-amount** order with a commercial invoice. Partner orders are partner-reported PAID and system-automatically FULFILLED. Website orders use fixed displayed catalog prices and external payment: pro forma at checkout, admin payment confirmation, commercial invoice afterward.

## Delivery requirements

1. Begin with a short architecture decision record: framework/stack choice, modular-monolith boundaries, schema and data safety assumptions, jobs/queues, PDF generator, email auth delivery, observability and deployment. Prefer a minimal maintainable architecture. If stack is already specified in the repo, respect it.
2. Convert the PRD into a prioritized issue/task breakdown with dependencies and acceptance criteria, then implement vertical slices end to end. Produce readable code, migrations, seeded demo data and setup commands.
3. Define domain entities and database constraints for partner, API key/scope, partner customer identity, website account, category, attribute, brand, product, variant, source listing, price observation, reference price, override price, FX rate, import job/staging record, order, line item, payment, fulfillment assertion, invoice, credit note and audit event.
4. Publish an OpenAPI v1 specification with examples for all agreed endpoints; implement server-side partner scope on every data access; include structured error schema, request correlation IDs and idempotency behavior.
5. Implement the order composition service independently, with tests for minor-unit money math, FX, ±5% unit bounds, permitted quantity changes, multi-SKU price allocation, exact totals, randomized variety, realistic line-item count, duplicate requests and failures. Do not fabricate products or bypass pricing constraints to hit the amount.
6. Implement synchronous batch creation with item-level success/failure and configurable batch limits (initially 100); isolate transactions per item and enforce partner + external order id uniqueness under concurrency.
7. Implement website registration/login/email verification/password reset, storefront PLP/PDP/cart/checkout, pro forma PDF, pending payment and staff-admin payment acceptance; keep website history separate from B2B orders.
8. Implement admin screens for categories, attribute definitions, brands, products/variants, import jobs, staging previews and images, duplicate resolution, automatic publishing of verified records by default, and per-job manual-review switch.
9. Implement website crawling, keyword discovery and CSV/Excel/JSON ingestion through a shared adapter interface, with future third-party scraper adapters clearly extensible. Provide downloadable and validated CSV/XLSX import templates; maintain source attribution and use-of-content restrictions.
10. Implement Super Admin/Staff rights as specified; partner onboarding and key controls are admin-only. Implement manual refunds and associated linked credit notes while preserving issued invoice originals.
11. Ship an operations and QA package: environment config template, migrations/seeds, API collection, test plan, load/coverage simulator, failure modes, security checklist and deployment/runbook.

## Critical quality requirements

- API order final payable total **equals requested amount exactly**, including applicable tax/fees, in currency minor units. USD/EUR/SGD/AED/GBP initially; configurable.
- Target at least 99% success on a *defined representative* valid request distribution between configurable USD-equivalent $10 and $1,000,000. Prove via simulation; report inaccessible ranges. Do not claim guarantee without data.
- Single-order API p95 target <10 seconds under declared load conditions, with bounded solver computation and meaningful timeout errors; batch is synchronous with independently reported results.
- Fixed website prices vs adjustable API prices (±5% per SKU, per order only), quantity and selection sensibility, catalog price unaffected by API adjustments.
- Price priority = currency override, else FX conversion from base. Rate provider + admin override + configurable refresh; lock effective rate and price on order.
- Products active permanently until admin deactivates them; no scheduled catalog recrawl; separate FX refresh permitted. All active variants show publicly and are API eligible.
- New partner customer requires phone+email, identified by `(partner_id, external_customer_id)`; existing customer auto-contact sync with audit. Never merge across partners automatically.
- Invoice document types: website checkout pro forma, commercial post website admin acceptance, immediate commercial for successful API order; separate non-fiscal/internal records and credit notes.
- Singapore seller **not GST registered** initially; do not charge GST or describe a GST tax invoice; keep future tax configurability and legal validation gates.
- No embedded payment gateway, checkout proof upload, shipping/warehouse system, partner portal, order notification emails, webhook system or invoice ZIP in MVP. Authentication emails required.
- Ensure API key hashing/rotation/revocation/scopes/rate limiting; all partner lookups isolated. No sensitive key in logs.
- Show 'FULFILLED — SYSTEM_AUTOMATIC' as a configured operational claim, not verified proof of delivery. Require actual sales and delivery evidence policy before go-live.

## Order creation pseudoflow

1. Authenticate API key, derive partner and scopes.
2. Validate body / currency / USD-equivalent range / legal invoice fields and idempotency.
3. Resolve existing partner customer; for new customer require email and phone. Audit supplied contact updates.
4. Load eligible active SKU references, currency overrides and/or current valid FX snapshot.
5. Search randomized, amount-scaled candidates; optimize counts and price adjustments in integer minor units; broaden search when needed within ±5%; choose realistic solution and capture selection evidence.
6. Verify every item is a genuine supply and total equals input exactly; if unsuccessful, return structured failure and no order/invoice/payment.
7. Atomically create order + item price snapshots + partner-reported paid record + system-automatic fulfillment record + invoice/issuance metadata; provide JSON and authorized PDF URL.
8. Return existing immutable order on exact idempotent replay; reject conflicting replay with 409.

## Mandatory scenarios to demonstrate

- $10.00, $17.43, $500.00, $12,845.99 and $1,000,000.00 equivalents where catalog coverage permits, all currencies.
- Duplicate partner ID/external ID retry and conflicting replay; concurrent same-ID requests.
- Changed partner contact details + audit; missing new-customer contact details.
- Cross-partner access blocked for order history and invoice PDFs.
- Bulk mixed success/failure; no batch-wide rollback.
- Currency override vs conversion, rate change after sale, stale FX failure.
- Website checkout with no automatic paid status; admin marks PAID and issues commercial invoice.
- Valid automated import / duplicate entities / ambiguous entries stay staged / deactivation / repeated import without duplication.
- Partial refund, full refund and credit note linked to unchanged original invoice.

## Agent execution protocol

Keep project work reviewable. Before each major implementation phase, list the files and schema/contracts it will touch. After each phase, run relevant tests and report: what works, what is incomplete, security/risk notes, and precise reproduction commands. When requirements leave a technical default unspecified, use a reasonable simple default and record it in an ADR; ask only if the decision affects legal accuracy or changes the agreed business semantics. Do not implement enterprise systems beyond the MVP or turn partner API operations into asynchronous jobs.

**Expected final handoff:** working codebase, setup/run/deploy instructions, OpenAPI spec, sample partner API calls, admin/demo credentials guidance (never checked-in secrets), downloadable import templates, tests and benchmark/coverage report against actual fixtures.

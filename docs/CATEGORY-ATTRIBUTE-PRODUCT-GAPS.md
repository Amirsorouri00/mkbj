# Category Attributes and Product Entry: Remaining Work

## Purpose

This document records the gap between the category-attribute capability already present in the
Catalog module and the Backoffice workflow needed for staff to create products and variants from
those category rules.

## Implementation status (2026-11-16)

This is a living gap register. The following work is now implemented:

- Categories expose a labelled **Attributes** action, and category creation offers **Assign
  attributes** as the next step.
- The product resource editor queries `effectiveCategoryAttributes(categoryId)` and the matching
  blueprint metadata. Variant entry renders category-defined option/typed fields, identifies
  required and inherited rules, pre-populates canonical keys, and removes empty optional values.
- Catalog's write service validates new variants and revalidates all existing variants before
  product activation. It refuses missing required values, unknown/non-variant keys, invalid typed
  values, and values outside the blueprint options with `PRODUCT_ATTRIBUTE_INVALID` field context.
- The Backoffice now has Imports, Customers, Orders & invoices, and Pricing navigation and routes.
  Customers include list/360/status actions; Orders include list/360/payment/cancellation/refund
  and authenticated PDFs; Pricing includes variant prices, currency overrides, currencies, FX
  rates/overrides, and refresh.
- Imports now list/create/control jobs and list staged records. File jobs have a connected
  multipart upload endpoint and UI for CSV/XLSX/JSON sample preview and explicit column mapping.
  Job detail and record review screens expose approve, merge, create-new, and reject actions.
- The deterministic demo seeder now creates `Colour` and `Size` option blueprints and assigns
  them as optional, filterable, searchable, variant-generating rules on every demo root category;
  child categories exercise the existing inheritance path.

The items below remain authoritative where they are not marked complete. Do not infer that an
implemented screen closes backend contracts that are still absent; the explicit remaining API
gaps in section 7 must be completed from the linked plans.

## What exists today

The relationship is owned by Catalog and persists in PostgreSQL; it is not a front-end-only
concept.

```text
Attribute blueprint
    -> category attribute assignment
    -> effective category attributes (including inheritance and exclusions)
    -> product and variant attribute values
```

- Attribute blueprints are created in **Catalog > Attributes**.
- A blueprint can be assigned to a category through the Backoffice route
  `/catalog/categories/{categoryId}/attributes`.
- Assignments are stored in `catalog.category_attribute_assignments`; inherited removals are
  stored in `catalog.category_attribute_exclusions`.
- The API exposes the effective set through `effectiveCategoryAttributes(categoryId)` and writes
  it through category-attribute assignment mutations.
- An assignment already supports the rules needed by the catalogue: required, filterable,
  searchable, in-title and title order, primary, and variant-generating.
- Children inherit eligible attributes from their ancestors and may exclude inherited ones.

## Observed local-data state

At the time this document was written, the local development database contained 24 categories,
zero active attribute blueprints, and zero active category-attribute assignments. Therefore no
category can currently supply product-entry attributes.

## Gaps to implement

### 1. Make category attribute management discoverable

**Current state:** The Category list has a per-row icon linking to the attribute-management route.
It is easy to miss, and the create/edit category dialog does not mention attributes.

**Required work:**

1. ~~Add a clearly labelled **Attributes** action beside each category's normal actions.~~
2. ~~After creating a category, offer a deliberate next action: **Assign attributes**.~~
3. Show a compact attribute summary in the Categories table (for example, direct/effective count).
4. Explain inheritance and exclusions in the category screen before an administrator changes a
   child category.

### 2. Connect a selected product category to its effective attributes

**Current state:** Product creation only sends `primaryCategoryId`. The product editor does not
query `effectiveCategoryAttributes`, so category rules do not appear in the form.

**Required work:**

1. When a primary category is selected, query that category's effective attributes.
2. Show a dedicated **Product attributes** section with clear loading, empty, and error states.
3. Render fields from each blueprint's data type and allowed options.
4. Mark required fields and prevent submission until the category's required values are supplied.
5. Warn before changing a category when existing values would no longer be applicable; do not
   silently discard them.
6. Preserve a draft per category while staff compare categories or correct a selection.

### 3. Make category rules drive variant creation

**Current state:** Variant attribute values exist in the Catalog domain and are returned by the
API, but the Backoffice does not generate variant combinations or input fields from
`isVariantGenerating` assignments.

**Required work:**

1. ~~Read the category's variant-generating attributes after the product is created or while its
   draft is being completed.~~
2. Provide a combination builder for option-based attributes and an explicit entry flow for
   free-form attributes where combinations cannot be generated safely.
3. Prevent duplicate signatures before submission and retain the backend's duplicate protection.
4. Make SKU entry, activation state, and per-variant pricing follow the generated combinations.
5. ~~Support explicit creation of a default variant when its category has no variant-generating
   attributes.~~ Automatic creation remains intentionally unimplemented.

### 4. Enforce category attribute rules in the write-side API

**Current state:** Assignment rules are modelled and can be read, but product/variant write paths
must be checked and completed so they validate against the effective category attributes rather
than accepting an arbitrary attribute dictionary.

**Required work:**

1. ~~Connect Product application code to the existing category-attribute and blueprint contracts
   without crossing module boundaries.~~
2. On create/update/activation, validate that required effective attributes have values.
3. ~~Validate value shape and option membership against the blueprint data type and options.~~
4. ~~Reject variant attributes that are not effective/variant-generating for the product's
   category.~~ Product-level attribute persistence still requires the planned decision below.
5. ~~Require required variant-generating attributes consistently when adding and activating
   variants.~~ Category changes still need an explicit revalidation/warning flow in the editor.
6. ~~Return refusal errors with field-level context that the Backoffice can render.~~
7. ~~Do not read storefront materialized views in these write paths; use live Catalog contracts.~~

### 5. Complete read-side and storefront consequences

**Required work:**

1. Ensure product detail responses expose values with blueprint metadata needed for readable
   labels, units, and option names.
2. Include only effective, filterable attributes in storefront facet generation.
3. Refresh the versioned storefront views after mapping or value changes using the established
   job mechanism.
4. Confirm search and title rules use the effective category assignment rather than raw keys.
5. Design thumbnail/media selection separately: category attributes should not be used as a proxy
   for primary product imagery.

### 6. Seed, migration, and operational work

**Required work:**

1. ~~Add a deterministic demo seed set of blueprints and category assignments so local product
   entry can be exercised meaningfully.~~
2. Keep assignments as data rows; no schema migration is needed merely to create mappings.
3. Add migration(s) only if validation requires new persisted product/variant value structures or
   indexes.
4. Document the recommended setup order: create blueprints, assign them to categories, then
   create products and variants.

## 7. Backoffice bulk product import and remaining parity gaps

**Current state:** The Backoffice navigation has no import workspace, even though the approved
Sourcing plan defines four import modes and a complete staging/review pipeline. This is a frontend
coverage gap, not a reason to create a second import model in the client.

**Current implementation:** The import workspace, four job modes, polling/progress, lifecycle
actions, file upload/sample mapping, record queue, record detail, and row-level review decisions
are present. The implemented backend uses the normalized resource paths
`/api/v1/admin/imports/jobs` and `/api/v1/admin/imports/jobs/{id}`; clients must consume the routes
exposed by `SourcingAdminEndpoints`, not duplicate the older route shape in a second controller.

**Still missing from the approved plan:** mapping validation preview (beyond raw sample rows),
downloadable CSV/XLSX/JSON-schema templates, `publish-valid`, bulk approve, record diff, staged
image URLs, source-policy management UI, configurable limits in the create form, upload size and
content-sniff enforcement, and GraphQL parity. These are backend-plus-frontend tasks; their exact
contracts originate in Phase 7 Tasks 20–23 and the Phase 6–8 PRD coverage matrix linked below.

**Initial plan source (must be followed):**

- [Master ecommerce MVP plan](docs/superpowers/plans/2026-10-28-ecommerce-mvp.md)
- [Phase 7 — Sourcing](docs/superpowers/plans/2026-10-28-ecommerce-mvp/phase-7-sourcing.md), especially Tasks 3, 10, 11, 19–23, and 25–27
- [Phase 6–8 — Admin and operations](docs/superpowers/plans/2026-10-28-ecommerce-mvp/phase-6-8-admin-ops.md), especially the admin coverage matrix and import routes

**Required work from those plans:**

1. Add an Imports area that creates and lists jobs using the exact planned modes: `Crawl`,
   `WebSearch`, `Combined`, and `File`.
2. For crawl/search jobs, expose the planned source URLs, search queries, include/exclude domains,
   target category, limits, publish policy, and source-rights policy controls. Do not add client-only
   crawling options that are not represented by the backend contract.
3. For file jobs, implement the planned CSV, XLSX, and JSON flow: create draft job, upload to the
   import-staging flow, download the CSV/XLSX/JSON-schema templates, map canonical and `attr:*`
   columns, preview validation, then start the job.
4. Show the planned job state machine and progress counters (`Draft`, `Queued`, `Running`,
   `Paused`, `Completed`, `Failed`, `Cancelled`; discovered/extracted/valid/invalid/duplicate/
   published/review). Add start, pause, resume, cancel, and publish-valid actions only where the
   API allows them.
5. Add the staged-record review queue with filtering, row-level issues, source provenance and
   image previews, plus approve, merge, create-new, reject, bulk-approve, diff, and publish-valid
   actions from the planned endpoints.
6. Preserve resumability and idempotency in the UI: a refresh must reload the server job and
   checkpoint state, never replay rows or invent a local progress state.

**Backend contracts the frontend must consume:**

- `POST/GET /api/v1/admin/imports`, `GET /api/v1/admin/imports/{id}`
- `POST /api/v1/admin/imports/{id}/file`, `POST /api/v1/admin/imports/{id}/mapping/preview`
- `POST /api/v1/admin/imports/{id}/{start|pause|resume|cancel|publish-valid}`
- `GET /api/v1/admin/imports/records`, `GET /api/v1/admin/imports/records/{id}`,
  `GET /api/v1/admin/imports/records/{id}/diff`
- `POST /api/v1/admin/imports/records/{id}/{approve|merge|create-new|reject}` and
  `POST /api/v1/admin/imports/records/bulk-approve`
- `GET /api/v1/admin/imports/templates/{csv|xlsx|json-schema}`
- source-policy routes under `/api/v1/admin/imports/source-policies`

The plan also calls out the staged-record image URL route as an admin-ops coverage item. Keep that
preview path separate from product-media URLs and use the server-provided URL rather than deriving
object-storage keys in React.

## 8. Customers Backoffice surface

**Current state:** Implemented. The Backoffice has customer list/detail routes, URL-addressable
filters, Customer 360, contact history, and suspend/reactivate actions. The planned
customer admin API already distinguishes website customers from partner-linked customers and keeps
contact-change history; the frontend must expose those read and action contracts rather than
flattening the two customer kinds into one local shape.

**Initial plan source (must be followed):**

- [Phase 1 — Identity and tenancy](docs/superpowers/plans/2026-10-28-ecommerce-mvp/phase-1-identity-tenancy.md), Task 14 (`ICustomerAdminService`, `ICustomerAdminQueries`, admin REST/GraphQL)
- [Phase 6–8 — Admin and operations](docs/superpowers/plans/2026-10-28-ecommerce-mvp/phase-6-8-admin-ops.md), Tasks 2, 4, and 6
- [Master ecommerce MVP plan](docs/superpowers/plans/2026-10-28-ecommerce-mvp.md)

**Required work from those plans:**

1. Add a Customers navigation item and route backed by `GET /api/v1/admin/customers` with the
   planned filters: kind, status, partner, search, page, and page size.
2. Add customer detail using `GET /api/v1/admin/customers/{id}` and the planned Customer 360
   route `GET /api/v1/admin/customers/{id}/360?orderLimit=20`.
3. Render contact history, identity kind, partner/external customer reference, verification state,
   billing data and recent orders according to the planned DTOs. Do not infer partner ownership
   from a browser-supplied id.
4. Add the planned suspend/reactivate actions with the `customers.manage` permission and show
   refusal/error responses from the shared API error shape.

## 9. Orders and invoice/document Backoffice surface

**Current state:** Implemented for the existing REST contract. The Backoffice has Orders list and
360 detail, confirm-payment, cancellation, super-admin refund, and authenticated document PDF
access. Remaining work is automated interaction/accessibility coverage and any backend contract
gaps recorded by the Phase 6–8 coverage matrix. The approved plan defines documents as part of
sales/order operations; it does not define an unrelated client-side invoice data model.

**Initial plan source (must be followed):**

- [Phase 4–5 — Sales](docs/superpowers/plans/2026-10-28-ecommerce-mvp/phase-4-5-sales.md), Tasks 25–30
- [Phase 6–8 — Admin and operations](docs/superpowers/plans/2026-10-28-ecommerce-mvp/phase-6-8-admin-ops.md), Tasks 2, 4, 6, 10, and the PRD coverage matrix
- [Master ecommerce MVP plan](docs/superpowers/plans/2026-10-28-ecommerce-mvp.md)

**Required work from those plans:**

1. Add an Orders route consuming `ISalesAdminQueries`/the planned admin order list, detail,
   document and dashboard contracts. Support the server-defined filters and statuses; do not
   invent browser-side order totals or payment state.
2. Add order 360 using `GET /api/v1/admin/orders/{id}/360`, including the composed customer view,
   payments, refunds, documents in issue order and composition trace specified by the plan.
3. Implement staff-only confirm-payment and cancel actions and super-admin-only refunds. Show the
   planned audit-sensitive outcomes and credit-note relationship; never label a document a GST/tax
   invoice contrary to the sales wording rules.
4. Expose the planned PDF/document access route `GET /api/v1/admin/documents/{id}/pdf` and the
   document links returned by the order DTO. The plan specifies document/PDF access, not a separate
   invoice-list endpoint, so a standalone Invoices tab must only be added if it composes these
   existing contracts rather than inventing another API.
5. Add loading, empty, forbidden, not-found and stale-action states for all order mutations, and
   reload the authoritative order detail after each successful mutation.

## 10. Pricing Backoffice surface

**Current state:** Implemented for the existing REST contract: variant prices, currency overrides,
enabled currencies, FX rates/overrides, and refresh are available in a separate Pricing workspace.
Pricing remains separate from Catalog and is driven by its planned contracts.

**Initial plan source (must be followed):**

- [Phase 2–3 — Catalog, pricing and storefront](docs/superpowers/plans/2026-10-28-ecommerce-mvp/phase-2-3-catalog-pricing-storefront.md), Tasks 25–29
- [Phase 6–8 — Admin and operations](docs/superpowers/plans/2026-10-28-ecommerce-mvp/phase-6-8-admin-ops.md), the pricing/settings coverage matrix and authorization tasks
- [Master ecommerce MVP plan](docs/superpowers/plans/2026-10-28-ecommerce-mvp.md)

**Required work from those plans:**

1. Add a Pricing area that can locate a variant and read its current price, then use the planned
   variant price and per-currency override operations.
2. Add FX rates and FX overrides views, including the super-admin-only set/clear override actions
   and the planned FX refresh request.
3. Add enabled-currency management using the planned `PUT /api/v1/admin/pricing/currencies/{code}`
   operation and keep staff/super-admin visibility aligned with the authorization matrix.
4. Use the exact money inputs from the plan (`amount` plus `currency` for prices; decimal-string FX
   rate and optional validity for overrides). Do not allow a client to set quote totals or basket
   prices through an alternate field.
5. After each write, reload the server projection and show audit/refusal errors. Keep pricing UI
   separate from category availability and sellability: a published variant without a usable price
   is omitted from quotes, as specified by the architecture.

**Planned REST surface:**

- `GET /api/v1/admin/pricing/variants/{variantId}`
- `PUT /api/v1/admin/pricing/variants/{variantId}/price`
- `PUT/DELETE /api/v1/admin/pricing/variants/{variantId}/overrides/{currency}`
- `GET /api/v1/admin/pricing/fx/rates`, `GET /api/v1/admin/pricing/fx/overrides`
- `POST/DELETE /api/v1/admin/pricing/fx/overrides/{base}/{quote}`
- `GET /api/v1/admin/pricing/currencies`, `PUT /api/v1/admin/pricing/currencies/{code}`
- `POST /api/v1/admin/pricing/fx/refresh`

## 11. Cross-surface implementation constraints

These remaining frontend surfaces must remain thin clients of the approved API composition:

- Keep customer, order, import, and pricing response types in reusable feature components; do not
  duplicate domain rules in page components.
- Keep pagination/filter state URL-addressable so sorting and additional filters can be added without
  replacing the data model.
- Treat thumbnails and staged images as server-provided media URLs with an explicit loading/failure
  state; never assume a product image exists or construct storage URLs in the browser.
- Keep permission checks and mutation refusals server-authoritative. A hidden button is not an
  authorization boundary.

## Recommended delivery order

1. Seed representative blueprints and category mappings.
2. Improve the Category screen's Attributes entry point and summary.
3. Add the product editor's effective-attribute query and dynamic product field section.
4. Add write-side validation and its unit/integration coverage.
5. Add the variant combination editor and validation.
6. Complete storefront facets, search/title projection, and refresh-job coverage.
7. Add the plan-backed Imports workspace and staged-record review flow.
8. Add Customers list/detail/360 and suspend/reactivate actions.
9. Add Orders/order-360, document/PDF access, payment confirmation, cancellation and refunds.
10. Add Pricing for variant prices, currency overrides, FX operations and enabled currencies.

## Verification checklist

- A staff member can create an attribute blueprint and assign it to a root category.
- A child category displays the inherited attribute and can explicitly exclude it.
- Selecting the child category in New Product shows the resulting effective fields.
- A required field blocks save when empty and accepts a valid typed value.
- A variant-generating attribute produces valid, unique variants.
- An invalid option, unknown attribute, duplicate variant signature, or missing required attribute
  is refused by the backend even if a client bypasses the Backoffice.
- Storefront facets and product titles update after the established view-refresh job runs.
- Imports expose all four planned modes and the CSV/XLSX/JSON template and review workflow.
- Customers expose the planned filters, contact history, 360 view and permissioned status actions.
- Orders reload authoritative detail after payment, cancellation or refund actions and expose the
  planned documents/PDFs.
- Pricing uses the planned money and FX contracts with staff/super-admin boundaries enforced by the
  API.

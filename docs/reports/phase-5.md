# Phase 5 — Sales: the website channel

A customer fills a cart at displayed prices, checks out, receives a pro forma telling them what
to pay and where, pays externally, and an administrator confirms receipt before the commercial
invoice is issued. Both channels can then be refunded. That closes the PRD's two sales paths.

Verified at `d9dad9b` on `feature/ecommerce-mvp`:

- `dotnet build EwaysVNext.slnx -c Release` — 0 errors, 0 warnings.
- `dotnet test EwaysVNext.slnx` — **1976 tests, 0 failures**, Docker up (1 skipped: an
  Identity assertion parked since phase 1).

## What works

**The cart holds the price the customer was shown, never an agreed one.** Every operation
re-quotes against live tables — Catalog for what is still sellable, Pricing for what it costs
now — and never reads a storefront view, which is stale by construction (ADR-0020) and this is a
purchasing path. Reading the cart is therefore a write: the refreshed prices are persisted, so
checkout compares against what was actually displayed rather than a figure nobody saw. A price
that moved or a product withdrawn comes back as a reported change, not a silent adjustment.

**Nothing is written until the price is agreed.** Every checkout refusal — unverified email,
empty cart, an unquotable line, a stale total — happens before any number is allocated. A
checkout that failed after drawing an invoice number would leave a gap an auditor reads as a
missing invoice. The comparison is per line *and* in total: two lines that moved in opposite
directions leave the sum unchanged while both prices are wrong.

**A changed price is not an error.** `PRICE_CHANGED` is 409 carrying the fresh cart, because the
next thing the customer will do is look at what it now costs.

**Website lines are priced at the catalogue exactly.** The ±5% band is a composition rule; a
line 1% off is well inside it and still refused with `ORDER_LINE_PRICE_NOT_REFERENCE`, because a
website customer pays what they were shown.

**Refunds credit, they do not edit.** A refund adds a credit note pointing at the invoice rather
than reducing it — accounting runs on documents that do not change once issued, and the linkage
is the only way a partial refund can be told from an edited invoice. Both channels refund
identically. Each act is audited in the same save as the change, carrying the status dimensions
and the refunded total and no personal data.

**Refunds are gated twice.** Super-admin realm policy plus `refunds.manage`, *and* an
`IsSuperAdmin` check in the service before the order is even loaded. The route is one mapping
mistake away from being wrong, and this is the operation that moves money out of the business.

### Endpoints

| Route | Gate |
|---|---|
| `GET/POST/PUT/DELETE /api/v1/shop/cart…` | `shop:customer` |
| `POST /api/v1/shop/checkout` | `shop:customer` |
| `GET /api/v1/shop/orders`, `/orders/{id}` | `shop:customer`, own Website orders only |
| `GET /api/v1/shop/documents/{id}/pdf-url` | `shop:customer` |
| `GET /api/v1/admin/orders`, `/orders/{id}` | `admin:staff` + `orders.manage` |
| `POST /api/v1/admin/orders/{id}/confirm-payment`, `/cancel` | `admin:staff` + `orders.manage` |
| `POST /api/v1/admin/orders/{id}/refunds` | `admin:super` + `refunds.manage` |
| `GET /api/v1/admin/documents/{id}/pdf` | `admin:staff` + `orders.manage`, 302 |

GraphQL mirrors all of it: `cart`, `myOrders`, `order`, `documentPdfUrl`, `adminOrders`,
`adminOrder`, and the mutations `addToCart`, `updateCartLine`, `removeCartLine`,
`setCartCurrency`, `checkout`, `confirmOrderPayment`, `cancelOrder`, `refundOrder`.

### PRD acceptance

| # | Test | Result |
|---|---|---|
| 1–7, 14 | `PartnerOrderAcceptanceTests`, `PartnerBulkAcceptanceTests` | pass (phase 4) |
| 8 | `PricingRestTests.Acceptance08_Base_Conversion_And_Override_Give_Exact_Minor_Units_And_Stable_Snapshot` | pass |
| 9 | `WebsiteOrderAcceptanceTests.Acceptance09_Website_Checkout_Is_Pro_Forma_Until_Admin_Confirms` | pass |
| 12 | `WebsiteOrderAcceptanceTests.Acceptance12_Refunds_Credit_Notes_Are_Linked_Audited_And_Super_Only` | pass |

## What is incomplete

- **The admin dashboard has no endpoint.** `ISalesAdminQueries.GetDashboardAsync` exists and is
  tested; phase 6 composes it into the backoffice surface. The names in `QueryApplication/Admin`
  are fixed for that reason.
- **The k6 load run is phase 8**, against a deployed stack rather than a test host.
- **No order-level cancellation by the customer.** Cancelling is an administrator's act; a
  customer who changes their mind contacts the seller. The PRD does not ask for more.

## Risks

- **`GET /shop/cart` writes.** It persists the refreshed displayed prices, which is what makes
  checkout's comparison meaningful, but it does mean a read-shaped request takes a write lock on
  one row. A cart is per customer, so there is no contention between customers.
- **A failed cart clear leaves a stale cart.** The order is committed by then; failing the
  request would tell a customer their order did not happen when it did, and they would place it
  again. A stale cart is visibly wrong and harmless; a duplicate order is neither. It is logged
  as a warning.
- **Popularity counts both channels.** A partner's composed basket influences the storefront's
  best sellers. That is a design call, flagged in the phase 4 plan, and flipping it to website
  orders only is a one-line change to the view.
- **The composition trace is returned to any staff admin.** It includes the seed and the
  candidate pool size — nothing about another customer, but it does describe how the engine
  picks. Staff are trusted with it.
- **Cross-module writes remain non-atomic.** A website customer record persists if the order
  save then fails (spec §10.3). The orphan is harmless and the next checkout reuses it.

## Reproduction

```bash
docker compose -f build/compose/docker-compose.dev.yml up -d
dotnet build EwaysVNext.slnx -c Release
dotnet run --project src/Host/EwaysVNext.Host -- --migrate
dotnet test EwaysVNext.slnx
dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "EwaysVNext.IntegrationTests.Sales.WebsiteOrderAcceptanceTests"
```

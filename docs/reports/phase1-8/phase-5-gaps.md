# Phase 5 — Website Channel & Checkout: Gaps & Risks

**Gate:** Passed. 2,010 tests, 0 failures, 1 skipped. Release build clean.

## Gaps / Skipped

| Gap | Status | Why |
|---|---|---|
| **No order cancellation by customer** | By design | Cancelling is an administrator's act; a customer contacts the seller. The PRD does not ask for customer-initiated cancellation. |
| **Admin dashboard endpoint not exposed** | Resolved in P6 | `ISalesAdminQueries.GetDashboardAsync` exists and is tested; P6 composes it into the backoffice surface. |
| **k6 load run** | Resolved in P8 | Against a deployed stack rather than a test host. |

## Carried Risks

| Risk | Why it remains | Impact |
|---|---|---|
| **`GET /shop/cart` takes a write lock** | Persists refreshed displayed prices for checkout comparison. | Lock is per-customer — no contention between customers. Only a concern if one customer hammers the endpoint. |
| **Failed cart clear leaves a stale cart** | The order is committed by then. | Failing the request would tell a customer their order didn't happen when it did. A stale cart is visibly wrong and harmless; a duplicate order is neither. Logged as a warning. |
| **Popularity counts both channels** | A partner's composed basket influences storefront best sellers. | Design call flagged in the P4 plan. Flipping to website-only is a one-line view change. |

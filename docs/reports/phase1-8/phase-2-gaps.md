# Phase 2 — Catalog & Pricing: Gaps & Risks

**Gate:** Passed 2026-11-02. 1,556 tests, 0 failures, 1 skipped. Release build clean.

## Gaps / Skipped

None. Brands, categories, attribute blueprints, products with variants, the publishing port,
currencies, FX rates (ECB provider), reference prices, per-currency overrides, the ±5% price
band, and the demo seed (160 products / 400 variants) were all delivered.

## Carried Risks

| Risk | Why it remains | Impact |
|---|---|---|
| **ECB is a single point of failure for FX rates** | No second provider. Rates refresh on a recurring job, usable for `Fx:MaxRateAge` (default 72h). | A prolonged ECB outage returns `FX_RATE_UNAVAILABLE` (503, retryable) for every cross-currency quote. |
| **"Published" never implies "priced"** | Cross-module writes are not atomic by design. | A failure between publishing and pricing leaves a sellable variant with no price — omitted from quotes, never an error. |
| **Demo seed runs through real ports** | Refused in Production and tested on its own database. | Nothing stops an operator pointing a Development-configured host at a production database. |

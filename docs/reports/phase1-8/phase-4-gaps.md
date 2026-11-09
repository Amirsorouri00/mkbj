# Phase 4 — Partner Orders & Composition: Gaps & Risks

**Gate:** Passed. 1,820 tests, 0 failures, 1 skipped. Release build clean.

## Gaps / Skipped

| Gap | Status | Why |
|---|---|---|
| **Composition coverage simulator** | Resolved in P8 | Produces a report, not a product feature. Delivered in P8: 100% over 10,025 compositions across 5 currencies. |
| **k6 load script** | Resolved in P8 | Load testing belongs against a deployed stack, not a test host. Delivered in P8: 27,001 orders at 90/s, p95 23.79 ms. |
| **No GraphQL for the partner channel** | By design | Partners use REST; GraphQL is the admin and storefront surface. The PRD does not ask for partner GraphQL. |

## Carried Risks

| Risk | Why it remains | Impact |
|---|---|---|
| **QuestPDF Community licence revenue cap** | A commercial licence is required past a revenue threshold. | Business decision, not technical. Must be reviewed before revenue grows. |
| **Candidate pool cache is in-process** | ADR-0010. With replicas > 1 each instance keeps its own cache. | Correctness unaffected (pool is re-quoted live before any write). Cache warmth is per instance. |
| **New customer persists if order save fails** | Cross-module writes are not atomic by design (spec §10.3). | Orphan is harmless; next order with the same external customer id reuses it. |

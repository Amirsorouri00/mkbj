# Phase 3 — Storefront Views & Search: Gaps & Risks

**Gate:** Passed 2026-11-02. 1,622 tests, 0 failures, 1 skipped. Release build clean.

## Gaps / Skipped

| Gap | Status | Why |
|---|---|---|
| **Facets ignore per-category attribute exclusions** | Deferred | Facet counts are for the whole category, not narrowed to the current filter selection. Documented limitation in `StorefrontViewSql`. Post-MVP refinement. |
| **No incremental refresh for materialized views** | Deferred | Every refresh rebuilds all four views in full. Cheap at current catalogue size. ADR-0020 records that incremental refresh is the answer when cost grows. |

## Carried Risks

| Risk | Why it remains | Impact |
|---|---|---|
| **Search quality is untuned** | `simple` text search config — no stemming, no stop words. Trigram threshold is Postgres default 0.3. | Neither has been measured against real queries. First real catalogue will expose the gaps. |
| **View rows can outlive their FX rate** | Every read path repeats the `fx_valid_until` guard. | A new read path added later that forgets the guard would show a price the quote path refuses. |
| **Refresh cost grows with catalogue** | `CONCURRENTLY` roughly doubles cost. Command timeout is 300s. | At a size where full refresh stops being cheap the answer is incremental, not more frequent. |

# ADR-0020: Storefront Browse Reads Come From Materialized Views

## Status
Accepted — 2026-10-28.

Amends [ADR-0008](0008-read-optimized-cqs-terminology.md) for **storefront browse reads only**. Its
"reads are never stale" guarantee continues to hold everywhere else, including every read that
decides what may be sold.

## Context

The homepage, product listing and product detail pages are the highest-traffic reads in the system
and the most expensive to compute. A PLP row needs the product, its active variants, the brand, the
category path, facet attributes, the primary image and — per currency — a price resolved through a
currency override or an FX conversion. Computing that per request, per currency, with filtering,
faceting and full-text search over it, is a join of six tables plus a price calculation that cannot
use an index.

ADR-0008 established that reads project directly from live tables and are never stale. For
browsing, that guarantee is expensive and, on inspection, is not the one that matters.

## Decision

**Four materialized views serve storefront browsing. Nothing that decides a sale reads them.**

`catalog.mv_storefront_variant`, `mv_storefront_product`, `mv_category_facets` and `mv_home`, each
grained per enabled currency with the price already resolved, a `tsvector` for search, and the
indexes faceting needs. They are refreshed `CONCURRENTLY` by the `RefreshStorefrontViews` job,
enqueued with a dedupe key after any catalog publish, price change or FX update, and nightly as a
backstop (ADR-0019 D4 coalesces a burst into one run).

### The invariant this rests on

**Staleness may affect what is *shown*; it may never affect what is *sold*.**

The cart, checkout and the composition engine read `ISellableVariantPort` and `IPriceQuotePort`
against **live tables**, and checkout re-quotes before writing. A variant deactivated a second ago
can still appear on a PLP until the next refresh; adding it to a cart or composing an order with it
fails immediately. The customer sees a listing that is seconds out of date — the ordinary
experience of any catalogue — rather than being sold something that is gone.

That is why the amendment to ADR-0008 is narrow. Had the purchase path been allowed to read a
view, the guarantee ADR-0008 protects would be gone rather than scoped.

### The cross-schema dependency, stated because it is a real cost

The view definitions read `catalog`, `pricing` and `sales` tables in SQL. That is a database-level
join across module schemas — exactly what the module boundaries forbid in code.

It is accepted here because it is **read-only, one-directional and confined to view definitions
that `Catalog.QueryApplication` owns**. No module gains a reference to another; no write crosses a
boundary. But the coupling is real and has two consequences nobody should discover by surprise:

1. **A schema change in Pricing or Sales can break a Catalog view.** Migration ordering must put
   view definitions after their source tables, and a persistence-contract test fails the build if
   any view cannot be refreshed.
2. **`mv_home` and `mv_storefront_product` depend on `sales.order_lines` for popularity.** Phase 3
   creates them with popularity fixed at zero, because the sales tables do not exist yet; the phase
   4–5 task that creates those tables must redefine both views in the same migration.

### Ownership, and the role that refreshes

`REFRESH MATERIALIZED VIEW` requires **ownership** of the view. The migrator role owns everything
the migrations create, and the application role `eways_app` owns nothing (ADR-0017 D6) — so the
refresh job cannot run as the application role.

Phase 3 must resolve this one of two ways: run the refresh under a role that owns the views, or
wrap it in a `SECURITY DEFINER` function owned by `eways_migrator` with `EXECUTE` granted to
`eways_app`. The second keeps the application's grant narrow and is the expected choice, but it is
a decision phase 3 makes explicitly, not a detail to be discovered when the first refresh fails
with "must be owner of materialized view".

## Consequences

- **Browse reads become index scans** over a table whose price is already computed, instead of a
  multi-table join with per-row currency logic.
- **Search needs no external engine.** `websearch_to_tsquery` over the view's `tsvector`, with a
  `pg_trgm` fallback for typos, covers the PRD's requirement without a second datastore to keep in
  sync — and without the reconciliation questions one would bring.
- **Expected staleness is seconds**, bounded by the job's debounce plus refresh time. It is visible
  only in listings.
- **Each view needs a unique index on its grain**, because `REFRESH ... CONCURRENTLY` requires one.
- **Refresh cost grows with the catalogue**, and `CONCURRENTLY` roughly doubles the work. At a size
  where a full refresh stops being cheap, the answer is incremental refresh of changed rows, not
  more frequent full refreshes.

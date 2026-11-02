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

Phase 3 chose the second of the two options it was given: `catalog.refresh_storefront_view(
view_name text, use_concurrently boolean)`, `SECURITY DEFINER`, owned by `eways_migrator`, with
`EXECUTE` revoked from `PUBLIC` and granted to `eways_app` in a repeatable script. The name is
checked against the four known views inside the function rather than interpolated — a
`SECURITY DEFINER` function that trusts an identifier from its caller is a privilege escalation,
since the caller owns nothing and the function owns the schema. `search_path` is pinned for the
same reason.

The parameter is `use_concurrently` because `concurrently` is a reserved word, and plpgsql
validates the body at `CREATE` time: the refusal arrives as a syntax error positioned inside the
body, which reads like a truncated statement rather than a reserved name.

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

## Amendments from phase 3

### `pricing.resolved_fx_rates` is the views' rate source

The views do not resolve a rate themselves. Pricing projects the currently applicable rate for
each `(base, quote)` pair into `pricing.resolved_fx_rates` — override, peg or provider, already
decided — and the views join that one row. Resolution logic exists once, in `FxRateResolver`;
the view reads its conclusion. `StorefrontPriceParityTests` holds the two together to the minor
unit.

### `fx_valid_until` is filtered again at read time

A rate can expire between two refreshes, and the row stays in the view until the next one. Every
read of `mv_storefront_variant` therefore repeats `(fx_valid_until IS NULL OR fx_valid_until >=
now())`. Without it the storefront would quote a price the live quote path now refuses — which is
precisely the drift this ADR's invariant forbids, arriving through the back door of a view that
was correct when it was built.

### Views are versioned V1 → V2, never edited in place

The SQL lives in `StorefrontViewSql` as `VariantV1`, `ProductV1`, `FacetsV1`, `HomeV1`. A change
to a view's shape adds a V2 constant and a migration that drops and recreates the view with its
indexes; it does not amend the V1 text, because a migration that has already run cannot be
changed and the constant it read must keep saying what it applied.

### The phase 4 popularity obligation, restated

`popularity_30d` is `0::bigint` in V1, and `mv_home` emits `best_sellers` rows only
`WHERE popularity_30d > 0` — so the section is empty rather than ranking everything equally. The
phase 4–5 task that creates `sales.orders` and `sales.order_lines` must, in the same change, add
`ProductV2` and `HomeV2`, a migration that drops and recreates both views with their V1 indexes,
and replace the constant with a 30-day sum over non-cancelled order lines. An integration test
that a completed order moves its product into `best_sellers` is part of that task, not a
follow-up.

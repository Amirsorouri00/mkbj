# Phase 3 — Storefront materialized views and shop browse API

**Gate passed 2026-11-02.** 1622 tests, 0 failures, 1 skipped. Release build clean: 0 warnings,
0 errors.

Phase 3 is the shop a visitor actually sees. Four materialized views hold the catalogue with its
price already resolved per currency, and one query layer serves them over both REST and GraphQL,
anonymously. Nothing here may price a basket: these answers are stale by construction, and
ADR-0020's invariant is that staleness may affect what is *shown* but never what is *sold*.

## What works

| Area | What was built |
|---|---|
| `mv_storefront_variant` | Variant × enabled currency, eligible only when the product is Active, the variant active, the brand active, the category inside the active tree, and a price exists in that currency |
| `mv_storefront_product` | Product × currency, with the price range, variant count, image keys and facet values a card needs |
| `mv_category_facets` | Category × currency × kind × key × value, where category `0` is the whole catalogue |
| `mv_home` | New arrivals, best sellers and featured categories, ranked per currency |
| Refresh | `catalog.refresh_storefront_view`, `SECURITY DEFINER`, name-checked; a refresher that runs the four in order, and a job on a 5-second debounce plus a 24-hour backstop |
| Price parity | The view computes prices the way `PriceResolver` does — override, base match, half-even conversion — and a test holds the two to the minor unit |
| Home / PDP | `IStorefrontQueries` with home, category tree and product detail; absence from the view is the only definition of absence from the shop |
| PLP | Category containment, brand, same-variant attribute and price-range filters, five sort orders |
| Paging | A keyset cursor carrying its sort and mode, refused if replayed under another ordering |
| Search | `websearch_to_tsquery` over the view's `tsvector`, with a `pg_trgm` fallback that reports when it fired |
| Facets | Attribute groups, brands with id and label, and price buckets whose ranges meet |
| Shop REST | Six anonymous routes under `/api/v1/shop` |
| Shop GraphQL | Six anonymous `shop*` fields on the shared `Query`, over the same query layer |

## Counts

| Suite | Tests | Needs |
|---|---|---|
| ArchitectureTests | 62 | nothing |
| UnitTests | 1253 | nothing |
| PersistenceContractTests | 18 | nothing |
| IntegrationTests | 289 | Docker |
| **Total** | **1622** (1 skipped) | |

## Acceptance criteria met

- **Acceptance 11** — a variant an admin deactivates leaves `ISellableVariantPort` immediately and
  the listing at the next refresh. Both halves are asserted, including the lag: that gap is
  ADR-0020's design, and a test that demanded the listing update instantly would be testing
  against the decision.

## What this phase found

1. **`concurrently` is a reserved word in plpgsql**, and the body is validated at `CREATE` time —
   so the refusal arrives as a syntax error positioned *inside* the function body, which reads
   exactly like a statement truncated at a semicolon. I wrote a comment blaming EF for splitting
   migration SQL, moved the function to a repeatable script on that theory, and only then tested
   it. The migration path was fine all along. A plausible cause that was never tested is a guess
   with a code comment attached.
2. **A mutation check that proves nothing is worse than none**, because it reads as evidence.
   Deleting the refresh function's whole name whitelist left its test green: the probe name was
   `pg_catalog.pg_class`, which the migrator does not own either, so the refusal came back as the
   same `42501` the guard raises. A negative test needs a probe that would otherwise *succeed*.
3. **`.AllowAnonymous()` guarded nothing observable.** Stripping it from all six shop routes left
   the suite green — the application has no fallback authorization policy, so an ungated endpoint
   is already open. It now has a test that configures exactly that policy and asserts the shop
   still answers, so the attribute is a guard rather than decoration.
4. **Two opposite naming rules for raw SQL, in one file.** The snake_case convention applies to
   ad-hoc `SqlQueryRaw<TRow>` types, so `AS "BrandId"` is looked up as `brand_id` and reported
   missing — but a *scalar* `SqlQueryRaw<T>` wants a column named exactly `Value`, which the
   convention does not touch.
5. **`Popularity30d` maps to `popularity30d`, not `popularity_30d`.** The convention splits on
   case, not on digits.
6. **A `CASE WHEN … THEN …::bigint` does not stop Postgres evaluating the cast** for every row,
   and an attribute value like "Red" is not a bigint. The join it was reaching for was
   unnecessary anyway: the facet view already carries the brand id as `facet_key` and the name as
   `facet_value`.
7. **A suffixed attribute blueprint silently emptied every attribute facet.** The facet view
   matches a blueprint's canonical key against the key stored on the variant, and
   `colour-4084bc9a` never equals `colour`. It was caught by an assertion about a count, not by
   one about a shape.
8. **One assertion was about other tests' data, not about the code.** "This scenario's root is
   among the featured categories" is a claim about every other test in a shared database, since
   the section keeps the eight largest. Removed, with the reason recorded.

## Incomplete, by decision

- **Best sellers is empty.** `popularity_30d` is `0` until `sales.order_lines` exists in phase 4,
  and `mv_home` emits no `best_sellers` rows at all rather than ranking everything equally. The
  phase 4–5 obligation is recorded in ADR-0020.
- **Facets ignore per-category attribute exclusions**, and do not narrow to the current filter
  selection — every count is for the category as a whole. Both are documented limitations in
  `StorefrontViewSql`, not oversights.
- **No incremental refresh.** Every refresh rebuilds all four views in full.

## Risks carried forward

- **Refresh cost grows with the catalogue, and `CONCURRENTLY` roughly doubles it.** The command
  timeout is 300 s. At a size where a full refresh stops being cheap the answer is incremental
  refresh of changed rows, not more frequent full refreshes — ADR-0020 says so, and nothing has
  measured where that size is.
- **The 24-hour backstop is an interval, not a wall-clock time.** It fires 24 hours after the last
  run, so it drifts; there is no "nightly at 02:00".
- **The views read `pricing` column names in SQL.** A rename in Pricing breaks a Catalog view, and
  the only thing that catches it is a migration or a test run — not the compiler.
- **A view row can outlive its FX rate.** Every read repeats the `fx_valid_until` guard for that
  window. A read path added later that forgets it would show a price the quote path refuses.
- **Search quality is untuned.** `simple` is the text search configuration — no stemming, no stop
  words — and the trigram threshold is Postgres' default 0.3. Neither has been measured against
  real queries.

## Reproducing the gate

```bash
docker compose -f build/compose/docker-compose.dev.yml up -d

dotnet build EwaysVNext.slnx -c Release
dotnet test EwaysVNext.slnx

dotnet run --project src/Host/EwaysVNext.Host -- --migrate
dotnet run --project src/Host/EwaysVNext.Host -- --seed
dotnet run --project src/Host/EwaysVNext.Host
```

Then, with no token:

```bash
curl "http://localhost:5000/api/v1/shop/products?currency=USD&q=demo"
```

The guard named in the gate was mutation-checked on 2026-11-02: removing `AND p.status =
'Active'` from `VariantV1` fails both `Plp_Never_Shows_Draft_Or_Deactivated` and
`Plp_Views_Never_Contain_Draft_Or_Deactivated_Products`.

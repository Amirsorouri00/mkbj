# Phase 2 — Catalog & Pricing

**Gate passed 2026-11-02.** 1556 tests, 0 failures, 1 skipped. Release build clean: 0 warnings,
0 errors.

Phase 2 answers the two questions a shop has to answer before it can sell anything: *what is
buyable*, and *what does it cost in the currency you are asking in?* The catalogue owns the first
and says nothing about price; Pricing owns the second and says nothing about availability. Keeping
them apart is what lets a storefront show an unpriced product as unavailable rather than crash,
and lets the composition engine in phase 4 ask each question separately.

## What works

| Area | What was built |
|---|---|
| Canonical keys | One normalizer — NFKD, marks stripped, folded to a 120-character key — shared by categories, attributes and brands, so "Kaffee­maschine" and "Kaffeemaschine" are the same thing on import |
| Categories | Canonical keys, up to 20 aliases, a tree whose activation gates publishing into it |
| Attributes | Blueprints with a canonical key, aliases, a global-or-category scope, a unit, and an `Option` data type |
| Brands | Aliases unique across all brands, provenance (`admin` / `import` / `seed`); a rename drops an alias that has become the name |
| Identifiers | GTIN-8/12/13/14, ISBN and MPN, with real GS1 check-digit validation and one normalizer shared by storage and matching |
| Products & variants | Product with variants, media, identifiers, merge redirects and an admin-deactivation flag an importer cannot undo |
| Variant signature | A canonical attribute signature — sorted, escaped, hashed — so the same combination is one variant whatever order it arrives in |
| Catalogue admin | Create, edit, bulk-edit, merge, activate, media upload to object storage, over REST and GraphQL |
| Publishing port | Idempotent upsert matched GS1 → brand+MPN → source id, following merge chains up to 10 hops |
| Sellability | `ISellableVariantPort` — Active product, active variant, **active brand**, with root-to-leaf category path — plus a version sequence a cache can compare against |
| Currencies | ISO-4217 rows keyed by numeric code, enabled/disabled by a super admin |
| FX rates | Append-only ECB rates, EUR-crossed, with a USD peg for AED and admin overrides that win |
| Rate resolution | Override → peg → provider, with an **inclusive** max-age bound; a cross's age is its oldest leg |
| Prices | Reference price per variant in its own currency, per-currency admin overrides, half-even rounding to minor units |
| Quotes | `IPriceQuotePort` — omits what cannot be quoted, fails the whole call for a currency the shop does not sell in, and carries the rate snapshot it used |
| Pricing admin | Prices and overrides for staff; rates, overrides and currency enablement for super admins — REST and GraphQL |
| Demo seed | 160 products / 400 variants, deterministic, published through the real ports, refused in Production |

Everything above is reachable over **both REST and GraphQL**, gated identically.

## Counts

| Suite | Tests | Needs |
|---|---|---|
| ArchitectureTests | 62 | nothing |
| UnitTests | 1238 | nothing |
| PersistenceContractTests | 18 | nothing |
| IntegrationTests | 238 | Docker |
| **Total** | **1556** (1 skipped) | |

## Acceptance criteria met

- **Acceptance 8 (pricing half)** — a €100.00 base price quotes as exactly `10812` USD minor units
  at a rate of 1.0812; a SGD override of 150.00 stays `15000` when the rate moves; and the rate a
  quote recorded is still readable unchanged after a newer one arrives, because the rate table is
  append-only.
- **Acceptance 13 (pricing half)** — staff get 403 on FX overrides and currency enablement, super
  admins get 201/200, and a customer token cannot reach the pricing admin surface at all.

## What this phase found

Four of these were errors in the plan itself, and one was a live security hole.

1. **Fourteen catalogue admin GraphQL resolvers had no authorization of any kind.** The
   `[Authorize]` attributes were commented out and no `[RequirePermission]` had ever been added —
   `git show HEAD:… | grep -c RequirePermission` returned 0. Any authenticated caller could create,
   edit, merge or deactivate catalogue data over GraphQL while the identical REST routes were
   gated twice. All six catalogue GraphQL files were regated per resolver.
2. **The resolved-rate projection as specified could not run.** `DELETE …; INSERT …` in one command
   fails twice over: Npgsql's extended protocol refuses parameters on a multi-statement command
   (`08P01`), and every sub-statement sees the same snapshot, so re-inserting an existing pair
   collides. Rewritten as a single CTE — `VALUES` → `INSERT … ON CONFLICT DO UPDATE` → trailing
   `DELETE … WHERE NOT EXISTS`. Separately, **EF binds `DbParameter`s by name**, so `$1`
   placeholders bind nothing; the statement uses `@p0`.
3. **An architecture test can pass because the compiler deleted the evidence.** Removing
   `{ "Pricing", "Catalog" }` from `SanctionedReferences` left every boundary test green: the
   reference is elided from the compiled assembly when no code in it is used. The rules now also
   read the `.csproj` files, where the reference is declared rather than inferred.
4. **`FileTypeMetadata.GetExtension` already returns the dot.** `$"{mediaId}.{Extension}"` produced
   `777..png`, which would have made every stored object key wrong.
5. **A non-seekable stream has no content length**, so the concatenated upload stream failed at S3.
   Fixed by rewinding when the content is seekable — an ASP.NET form file is buffered — and keeping
   the replay path for forward-only streams, with a `ForwardOnlyStream` test so the fallback stays
   covered.
6. **A module that does not register a dependency fails the whole host.** The catalogue used
   `IFileTypeDetector` without `AddFileTypeDetection()`, and service-descriptor validation took the
   entire integration suite down with it.
7. **Restoring a mutation silently dropped the guard, twice.** Once the `if` went back in the wrong
   place — comment present, check absent; once it was replaced by a blank line. The tests caught
   both, which is the whole argument for running the suite after every restore rather than reading
   the diff and believing it.

## Risks carried forward

- **The ECB is a single point of failure for non-USD pricing.** Rates refresh on a recurring job
  and are usable for `Fx:MaxRateAge` (default 72 h). A longer outage makes every cross-currency
  quote return `FX_RATE_UNAVAILABLE` — 503, retryable — rather than quoting a stale number. No
  second provider exists.
- **Merge redirects are followed for at most 10 hops.** A chain longer than that refuses rather
  than looping. Nothing prevents a chain being built one merge at a time.
- **Cross-module writes are not atomic.** Publishing a product and setting its price are two
  commits; a failure between them leaves a sellable variant with no price, which quotes as omitted
  rather than as an error. That is the intended behaviour, not a gap, but it means "published"
  never implies "priced".
- **The demo seed publishes 160 products through the real ports.** It is refused in Production and
  tested on its own database, but nothing stops an operator pointing a Development-configured host
  at a production database.
- **Popularity does not exist yet.** Phase 3's views ship `popularity_30d = 0` and an empty best
  sellers section until the sales tables exist in phase 4.

## Reproducing the gate

```bash
docker compose -f build/compose/docker-compose.dev.yml up -d

dotnet build EwaysVNext.slnx -c Release
dotnet test EwaysVNext.slnx

dotnet run --project src/Host/EwaysVNext.Host -- --migrate
dotnet run --project src/Host/EwaysVNext.Host -- --seed
```

`--seed` exits 2 without touching anything when `ASPNETCORE_ENVIRONMENT=Production`.

The two guards named in the gate were mutation-checked on 2026-11-02: changing `<=` to `<` in the
FX resolver's age check fails `Rate_Age_Boundary_Is_Inclusive`, and removing the
`DeactivatedByAdmin` check from `Product.Activate` fails
`Publish_Updates_But_Never_Reactivates_Admin_Deactivated_Product`.

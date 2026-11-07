# Testing Guarantees — what a green run does and does not prove

**IntegrationTests requires Docker.** The other three suites need nothing.

This document exists because this repository has already shipped a green suite over a broken
system: it once had 95 passing tests while the application **could not log a user in**, because
every test ran on EF InMemory and the failure only appeared against a real database. Read this
before trusting a green run.

## The four suites

| Suite | Needs | Runs against | Catches |
|---|---|---|---|
| `ArchitectureTests` | nothing | compiled assemblies + source text | boundary violations, forbidden constructs, build-layout and documentation drift |
| `UnitTests` | nothing | pure code | domain rules, value objects, helpers |
| `PersistenceContractTests` | nothing | the EF model on **two** providers | mapping, naming, translation and materialization faults |
| `IntegrationTests` | **Docker** | the real host on real Postgres + S3 | HTTP behaviour, grants, concurrency, containers |

```bash
dotnet test EwaysVNext.slnx
dotnet test --project tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj
dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "*.MinorUnitsTests"
```

Filters are `--filter-class` / `--filter-method` — this is Microsoft.Testing.Platform, not VSTest,
so `--filter` does not exist.

## EF InMemory is not a database

It is used in exactly two places: the Development fallback when no connection string is configured,
and one half of `PersistenceContractTests`. It must never stand in for a persistence test.

| Behaviour | PostgreSQL | EF InMemory |
|---|---|---|
| Untranslatable LINQ | throws | silently evaluates client-side |
| Check constraints | enforced | ignored |
| Filtered unique indexes | enforced | ignored |
| `xmin` concurrency token | real | absent (explicitly disabled in the model) |
| Execution strategy / retries | yes | none |
| Raw SQL (`SKIP LOCKED`, `jsonb`) | works | unsupported |

That first row is the one that cost this repository 95 false passes.

## Why PersistenceContractTests runs two providers

Neither sees the whole contract:

- **Npgsql pointed at a refused port.** Translation happens *before* connection, so an
  untranslatable query shape throws a translation error while a valid one throws a connection
  error. This catches shapes that would fail in production, in milliseconds, with no database.
- **EF InMemory, which actually executes.** This catches materialization failures — a value
  converter that cannot parse a stored value — which a connection error would otherwise mask.

It also asserts the model-wide contract: snake_case for every table, column, index, key, foreign
key and check constraint; `xmin` as the concurrency token; never-generated aggregate keys; the
`pg_trgm` and `unaccent` extensions; a named schema for every table; and money stored as
`amount_minor bigint` + `char(3)`.

## What IntegrationTests actually exercises

The fixture starts a Postgres container built by **the same `build/postgres/init/01-roles.sql`
production uses**, applies the real migrations as `eways_migrator`, and hands tests a connection as
`eways_app`. That matters: the application role owns nothing and cannot change the schema, so
anything the production grants forbid fails in the suite too, instead of passing as a superuser and
failing on deploy.

Verified there today:

- **Migrations apply**, extensions exist, and the app role owns no table.
- **Lost-race reporting**: a stale write returns `ConcurrencyConflict`, a duplicate key returns
  `UniqueViolation`.
- **Object storage**: put/get/exists/copy/delete, and a presigned URL that downloads and then
  genuinely stops working after it expires.
- **The job queue's concurrency contract**: 50 jobs drained by 5 concurrent workers with no
  double-claim, lease expiry and reclaim, and a worker that lost its lease being unable to complete
  a job someone else now owns.
- **The worker's outcome handling**: completion, retry with backoff, failure, unknown job type,
  the coalesced retry, and the correlation id a handler sees.
- **The audit trail is append-only**: the application role is refused `UPDATE`, `DELETE` and
  `TRUNCATE` on both audit tables with SQLSTATE 42501.
- **The hash chain detects tampering**: a superuser `UPDATE` to a sealed event is reported at the
  right event id, and a removed seal is counted as a gap.
- **Host commands**: `--seed` refuses in Production without calling a single seeder, a `WorkerOnly`
  host serves health and 404s `/graphql`, and migrating twice is a no-op the second time.
- **The error contract end to end**: a correlation header on every response, a caller-supplied id
  echoed, and a rate-limit rejection carrying the standard body.

## What no suite proves

- **Performance.** There is no load test. Nothing here measures throughput or latency, and no
  number in any document is measured. The composition simulator and k6 script arrive in phase 8.
- **MinIO specifically.** The storage tests run against LocalStack's S3. MinIO no longer publishes
  anonymously pullable images — `docker.io/minio/minio` and `quay.io/minio/minio` both refuse an
  unauthenticated pull — so the dev stack runs an archived mirror of the same AGPL server. The
  adapter speaks plain S3 and MinIO implements S3, so the adapter is exercised; a MinIO-specific
  quirk would not be caught until deploy.
- **The production image and compose stack beyond a first run.** The image now builds and its
  `--probe` mode works, and the dev stack starts, migrates, seeds and serves `/health` 200. That
  is one run on one machine, not a deployment: nothing exercises the production compose end to
  end, restarts, upgrades, volume persistence or the Caddy TLS path.
- **Keycloak beyond discovery.** Both realms import and serve `.well-known/openid-configuration`,
  and their content is asserted — password policy, session lifetimes, client flows, TOTP as a
  default action, every secret a `${VAR}` placeholder. But that is a structural check on a JSON
  file. **No automated test obtains a token from Keycloak**: the integration suite signs its own
  JWTs with a test key and substitutes its own scheme router, because test tokens come from an
  issuer no realm claims. The production router's realm-to-tenant branch is therefore exercised
  by nothing.
- **The partner probe endpoints prove the gates, not the product.** `/test/partner/{scope}` exists
  only in the integration suite and returns the partner id. It proves that a key authenticates,
  that a scope is required, that suspension and revocation take effect, and that the rate limit
  partitions per partner. It proves nothing about any real partner endpoint, since none exist
  yet — those arrive with Sales.
- **The ECB.** `EcbFxRateProvider` is unit-tested against recorded XML, and the demo seed falls
  back to fixed rates when the fetch fails. Nothing proves the live endpoint's shape has not
  changed, and nothing exercises what happens after `Fx:MaxRateAge` has elapsed in production.
- **Search quality.** The storefront's full-text configuration is `simple` — no stemming, no stop
  words — and the trigram fallback uses Postgres' default 0.3 threshold. Tests prove the
  mechanisms fire; nothing measures whether the results are good.
- **Refresh cost.** Every storefront test refreshes a shop of a handful of products. Nothing
  measures a full refresh of a real catalogue, and `CONCURRENTLY` roughly doubles the work.
- **Anything in a later phase.** The composition engine, orders, invoices, imports and the admin
  surface do not exist yet, so nothing covers them.

## Two places the HTTP suite cannot reach, and what covers them instead

Both were found by mutation testing, and both would otherwise have read as covered.

- **Whether key expiry is inclusive or exclusive.** `Rotated_Old_Key_Works_Until_Overlap_Expiry`
  proves the rotation overlap ends, but it cannot pin the boundary: Postgres stores `timestamptz`
  to the microsecond while `DateTimeOffset` keeps 100ns ticks, so the `ExpiresAt` read back is not
  the value the rotation computed and the clock cannot land exactly on it. Mutating the
  authenticator's `>=` to `>` leaves that test green. `ApiKeyAuthenticatorTests
  .Key_At_Exact_Expiry_Returns_Null` holds the boundary, with no database in the way.
- **The production authentication scheme router.** The integration fixture substitutes its own,
  because test JWTs are signed by an issuer no realm claims and the production router would reject
  them. Removing the partner-key branch from the production router therefore left all eight
  partner tests green. Both routers now call one `AuthSchemes.IsPartnerKey`, and `AuthSchemesTests`
  covers it; the realm-to-tenant branch is still exercised by nothing.

## What phase 1's suites cover

- **The permission split is tested over HTTP, not against the matrix.** The matrix being right is
  not the same as every route consulting it, so `Acceptance13StaffPermissionTests` sends real
  requests as staff and as a super admin. Each refusal has a positive control: without one, a
  route broken for everyone would pass the refusal test and look like correct security.
- **Both gates are deliberate, and the acceptance test proves the outcome rather than which one
  fired.** Weakening a group from `admin:super` to `admin:staff` leaves the suite green, because
  the permission still refuses. Only opening both fails it. That is the layered design working,
  but it does mean a single weakened gate is caught by review and the unit matrix, not by this
  suite.
- **Concurrency is tested, not reasoned about.** Ten parallel creates for one partner reference,
  and ten for one identity subject, each settle on a single row through the filtered unique index
  and a reload on violation.
- **Mutation testing earned its place this phase.** It found a test that could not pin an expiry
  boundary at all (Postgres stores microseconds, `DateTimeOffset` keeps ticks), a production
  scheme router the suite substitutes and therefore never exercised, and an architecture rule
  whose name match let the planted probe through.

## What phase 2's suites cover

- **The catalogue and pricing halves of acceptance 8 and 13 are tested over HTTP**, with positive
  controls, the same way phase 1's were: staff refused and a super admin accepted on the same
  route, so a route broken for everyone cannot pass as correct security.
- **The storage adapter is exercised on a forward-only stream.** The fast path rewinds a seekable
  stream, which is what an ASP.NET form file is — so without `ForwardOnlyStream` in the unit
  tests, the replay path that production's concatenated stream actually takes would be covered by
  nothing.
- **The resolved-rate projection is tested against Postgres, not the model.** Its single-CTE
  shape exists because the obvious `DELETE; INSERT` fails only on a real connection: Npgsql
  refuses parameters on a multi-statement command and every sub-statement shares one snapshot.
  EF InMemory would have run the wrong thing happily.
- **The demo seed runs on its own database, through the seeder list.** The assertion that
  `IDataSeeder` resolves a `DemoCatalogDataSeeder` is the one that matters: calling the seeder
  directly would pass with the registration missing, leaving a correct seed that `--seed` cannot
  reach.
- **A `.csproj` reference is evidence; a compiled assembly reference is not.** The compiler elides
  a project reference nothing uses, so `SanctionedReferences` rows could be deleted with every
  boundary test still green. `Module_Project_References_Are_Sanctioned` reads the project files.

## What phase 3's suites cover

- **The storefront's staleness window is asserted, not just tolerated.** Acceptance 11 checks both
  halves: a deactivated variant leaves the live sellable port at once, and stays on the listing
  until the next refresh. A test that demanded the listing update instantly would be testing
  against ADR-0020 rather than against the code.
- **The view's price and the quote's price are held together to the minor unit.** They are two
  implementations of one rule — one in SQL, one in C# — so nothing but a test keeps them in step,
  and a cent of drift is a listing that advertises a price the cart then changes.
- **The two API surfaces are held to each other.** `ShopProducts_Matches_Rest_For_Same_Filter`
  sends one filter through REST and GraphQL and compares the product id sequence, because "both
  surfaces exist" is not the same as "both surfaces answer the same".
- **`.AllowAnonymous()` is a guard that can be observed failing.** There is no fallback
  authorization policy today, so removing it from every shop route changes nothing; one test
  configures that policy in the test host, and without the attribute the storefront starts
  demanding a token.
- **The read-time FX guard is tested with real seconds.** `now()` is the database's clock and no
  test clock moves it, so the test sets a rate valid for two seconds, refreshes, waits it out, and
  requires the offer to disappear while the view row is still there.

## What phase 4's suites cover

- **The composition engine is tested as a pure function, because it is one.** Property tests over
  the PRD's 25 mandated amounts, with the seed persisted and replayed. If it ever needed a port or
  a clock, `SalesArchitectureTests` would fail the build before a test could.
- **Idempotency is tested concurrently, and it found a real defect.** Ten simultaneous retries of
  one request: exactly one order, one 201, nine 200s carrying `Idempotent-Replay`. The first
  implementation returned 201 to all ten and no sequential test could have seen it.
- **Lazy invoice rendering is proved by breaking the object store.** Every call throws and
  `POST /orders` still returns 201. That is the only way to show MinIO is not on the order path.
- **The PDF is read back with PdfPig, not asserted against the layout API.** A test that checked
  the calls would pass while the seller's UEN was missing from the page. The "no tax invoice" rule
  is checked on rendered text, because a source scan cannot see a string built at render time.
- **InMemory cannot see two things Sales depends on.** The ±5% band `CHECK` is ignored there, and
  the `23505` unique-violation path that idempotency rides on never fires. Both are covered only
  by `IntegrationTests` against real Postgres.
- **Popularity's two clauses were mutation-checked separately.** Dropping the cancelled filter
  fails one test; dropping the 30-day window fails another. The first attempt restored nothing,
  so the "second" mutation ran on a file still carrying the first and the identical failure count
  looked like proof. It wasn't.

## The shared database is a shared fixture, and nothing enforces cleanup

`IntegrationTests` runs against one database. Most rows are per-test, but the
**settings singletons** — the seller profile, the payment instruction, the commerce limits — are
not, and neither are the enabled currencies or the FX overrides. A test that changes one and does
not put it back changes the world every later test runs in.

This is not hypothetical. `SettingsAdminApiTests.Seller_Update_Round_Trips` left
`requiredBuyerFields` as `["email|phone", "companyName"]`, and two phases later every partner
order in the full run failed with `INVOICE_BUYER_FIELDS_MISSING` while passing when its class ran
alone. Nothing had been wrong with the Sales code.

So: **a test that writes a shared singleton restores it in a `finally`.** The pricing, currency
and settings tests all do. Nothing checks that they do — the ordering needed to detect it would
itself be the bug — which is why it is written here instead.

The gap between writing and noticing is the dangerous part: nothing read `requiredBuyerFields`
until Sales existed, so the damage sat invisible for two phases. The further apart the writer and
the reader, the longer that lasts.

## Two habits this repository insists on

**Mutation-test every guard.** Reintroduce the bug, watch the test fail, restore. A guard nobody
has seen fail is not known to work. And check the mutation actually changed the file — a
replacement that silently matched nothing produces a "passing" mutation run that proves exactly
nothing. That happened here, on the audit-grant check, and was caught only by querying the real
grants.

**Assert against the world, not against your own string.** The first version of the realm-name
check passed immediately: it compared a file name spelled in the test against itself, and Windows
matched the real file case-insensitively. A test that reads the directory failed at once. Prefer
enumerating what exists over asserting a name you also wrote.

**Run Release before claiming a build is green.** `TreatWarningsAsErrors` is on in Release only.
The Release build was broken for a long time while Debug CI stayed green, which meant the
production image could not have been built by anyone. CI now gates on it.

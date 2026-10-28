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
  but their secrets are still placeholders and phase 1 fills them. No test authenticates a human.
- **Anything in a later phase.** Pricing, the composition engine, orders, invoices, imports and
  the admin surface do not exist yet, so nothing covers them.

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

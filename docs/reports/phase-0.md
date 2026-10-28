# Phase 0 — Platform

**Gate passed 2026-10-28.** 766 tests, 0 failures. Release build clean: 0 warnings, 0 errors.

Phase 0 built no product. It replaced the platform the Iran-market codebase stood on, so that
every later phase has a database that enforces its own rules, a worker that cannot lose a job, an
audit log nobody can rewrite, and a deployment artefact that has actually been built.

## What exists now

| Area | What was built | Where |
|---|---|---|
| Database | PostgreSQL via Npgsql: snake_case throughout, `xmin` as the concurrency token, schema per module from the CLR namespace, `pg_trgm`/`unaccent`, money as `amount_minor bigint` + `char(3)` | `src/Database`, `src/BuildingBlocks/Persistence` |
| Role split | `eways_migrator` owns the schema; `eways_app` writes rows and owns nothing — the same script in dev, CI and production | `build/postgres/init/01-roles.sql` |
| Outcomes | One `Result`/`Error` type across the whole system, carrying `Retryable`; `TrySaveChangesAsync` reports a lost race as `UniqueViolation`/`ConcurrencyConflict` instead of throwing | `src/BuildingBlocks/Outcomes`, `Persistence` |
| Object storage | `IObjectStorage` over the S3 API, presigned URLs, three buckets | `src/BuildingBlocks/Storage` |
| Jobs | Postgres queue claimed with `FOR UPDATE SKIP LOCKED`: leases, heartbeats, dedupe-while-queued, exponential backoff, three modes | `src/BuildingBlocks/Jobs` |
| Audit | Append-only by grant, not convention, plus a minute-by-minute SHA-256 hash chain and a verifier that locates tampering | `src/BuildingBlocks/Audit`, `src/Database/Scripts/010-audit-append-only.sql` |
| Host | One image, four roles: API, `--worker`, `--migrate`, `--seed`, `--probe` | `src/Host` |
| Deployment | Chiselled non-root image, dev and production compose, Caddy, Keycloak realms | `build/` |

## Verified on real infrastructure, not just in tests

The dev stack was brought up and driven end to end on 2026-10-28:

- Migrations apply as `eways_migrator`; 19 tables across `catalog`, `audit`, `jobs`,
  `authorization` and `public`.
- `eways_app` selects rows and is **refused** `CREATE TABLE`.
- Audit grants for `eways_app` are `INSERT, SELECT` and nothing else, on both audit tables.
- `--seed` runs; `--probe` returns non-zero against a dead endpoint.
- Both Keycloak realms import and serve `.well-known/openid-configuration`.
- `/health` 200, `/health/live` 200, `/health/ready` 200, GraphQL answers `{__typename}`.
- The production image builds (96 MB, uid 1654, no shell) and its `--probe` mode runs.

## Defects this phase found in its own work

Five, each now covered by a test:

1. **The Release build had never succeeded.** `TreatWarningsAsErrors` is Release-only and CI gated
   on Debug, so the production image could not have been built by anyone. Fixed and gated.
2. **The image build enforced different analyzer rules than the local build**, because
   `.editorconfig` was never copied into the Docker context. The image failed on code that passed
   CI. It is copied now, with a comment saying why.
3. **`/health` returned 500, not 503, when object storage was unconfigured.** The health check's
   constructor threw while the middleware was resolving it, so the exception escaped the
   middleware entirely — a probe reporting "internal server error" for a blank config key. Options
   are now validated at startup and name the exact key (`Minio:Endpoint is required.`).
4. **Keycloak crash-looped on a file-name/realm-name mismatch.** The exports are named after their
   realms now, and a test enumerates the directory to check it.
5. **An EF Core version split**: the Npgsql provider pulled Relational 10.0.4 under Core 10.0.11 in
   one project. Pinned so they move together.

Two of these — 1 and 2 — meant the deployment artefact was unbuildable while every suite was
green. That is the failure mode this repository already has a scar from, and the reason
`docs/TESTING-GUARANTEES.md` leads with what the tests cannot see.

## Mutation checks

Every guard added this phase was mutation-tested: the bug reintroduced, the failure observed, the
fix restored. One mutation **silently matched nothing** and produced a meaningless pass — caught
only by querying the real grants. One test **passed vacuously** because it compared a file name
against itself on a case-insensitive filesystem. Both lessons are recorded in
`docs/TESTING-GUARANTEES.md`.

## Known gaps carried into phase 1

- **MinIO's images are no longer anonymously pullable.** Both `docker.io/minio/minio` and
  `quay.io/minio/minio` refuse an unauthenticated pull. The dev stack defaults to an archived
  mirror of the same AGPL server behind `MINIO_IMAGE`/`MINIO_MC_IMAGE`. **This needs a decision**:
  credentials for MinIO's registry, staying on the archive, or another S3-compatible server.
  Nothing in the code is MinIO-specific either way.
- **Keycloak realm secrets are placeholders.** Phase 1 fills them and adds the first
  authentication test.
- **Nothing is measured.** There is no load test and no number in any document is measured; the
  simulator and k6 script arrive in phase 8.
- **The production compose has not been deployed**, only validated and, for the image, built.
- `SanctionedReferences` is empty — there is no cross-module bridge yet, so its theory skips.

## Counts

| Suite | Tests | Needs |
|---|---|---|
| ArchitectureTests | 37 | nothing |
| UnitTests | 638 | nothing |
| PersistenceContractTests | 12 | nothing |
| IntegrationTests | 79 | Docker |
| **Total** | **766** (2 skipped) | |

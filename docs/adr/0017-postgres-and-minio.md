# ADR-0017: PostgreSQL and MinIO Replace SQL Server and the Local Filesystem

## Status
Accepted — 2026-10-28.

Amends [ADR-0014](0014-per-module-configuration-files.md) (connection-string shape) and
[ADR-0010](0010-kubernetes-deployment-strategy.md) (the deployment assumes Postgres and an object
store). Supersedes the SQL Server assumptions throughout `AGENTS.md` and
`docs/TESTING-GUARANTEES.md`.

## Context

The MVP (`docs/superpowers/specs/2026-10-28-ecommerce-mvp-design.md`) needs three things the
SQL Server setup did not provide:

1. **Materialized views** for the storefront's homepage, PLP and PDP, refreshed concurrently.
2. **Object storage** for product images, import files and invoice PDFs.
3. **A job queue** that several replicas can share, claimed with `FOR UPDATE SKIP LOCKED`.

Postgres does all three natively, and the product owner chose it. The template's persistence was
SQL Server with `rowversion` concurrency tokens, `dbo` schemas, PascalCase names and a committed SA
password in `appsettings.json`.

## Decision

### D1 — Npgsql, snake_case, one naming convention
Both contexts (`EwaysVNextDbContext` and `AuthorizationDbContext`) use
`Npgsql.EntityFrameworkCore.PostgreSQL` with `UseSnakeCaseNamingConvention()` and
`EnableRetryOnFailure(3, 2s)`.

Every explicit `ToTable`/`HasColumnName`/`HasDatabaseName` in the entity configurations was
**removed** so the convention is the single source of names. A PascalCase name written by hand
survives the convention and produces a quoted, case-sensitive identifier that every hand-written
query then has to quote too. `ModelContractTests.Every_Name_Is_Snake_Case` fails the build on any
table, column, index, key, foreign key or check constraint that is not `^[a-z][a-z0-9_]*$`.

Index **filters** are raw SQL and are *not* translated by the provider: `"[Status] <> 2"` would have
reached Postgres verbatim. They are rewritten against the convention's column names
(`"status <> 2"`, `"removed_at IS NULL AND is_primary"`, `"deleted_at IS NULL"`).

### D2 — `xmin` is the concurrency token; `RowVersion` becomes `uint`
`Entity<TId>.RowVersion` changes from `byte[]` to `uint`, mapped by a model-wide convention in
`OnModelCreating` to Postgres's system column `xmin` (`HasColumnName("xmin")`,
`HasColumnType("xid")`, `ValueGeneratedOnAddOrUpdate`, `IsConcurrencyToken`). No migration creates
a column for it — `xmin` already exists on every row.

Spec §3.3 said to keep `RowVersion` only if mapping it costs nothing. It costs nothing, so it stays.
The per-configuration `IsRowVersion()` calls are gone: one convention is harder to forget than
seven call sites.

**This does not change the DDD granularity.** `xmin` is a real token only where a save actually
checks it, which is the aggregate root; a child row is still only reachable through its root. The
"Known inconsistencies" note in `docs/DDD-GUIDELINES.md` about `RowVersion` on children stands,
with `varbinary NOT NULL` replaced by "an inherited `uint` mapped to the row's own `xmin`".

Under EF InMemory (dev fallback and some unit tests) `RowVersion` is explicitly made a non-token
with `ValueGenerated.Never`: InMemory has no `xmin`, and a token it cannot supply fails every save.

### D3 — Money is stored in minor units
Monetary values are persisted as `amount_minor bigint` plus `currency char(3)`. The domain's
`Money` stays a decimal value object; conversion happens at the persistence edge through
`MinorUnits` (Task 6). Binary floating point never touches money, and the composition engine's
exact-total arithmetic (spec §10) is integer arithmetic over the same unit the database stores.

### D4 — A named schema per module, with no fallback
`OnModelCreating` derives the schema from each aggregate's CLR namespace:
`EwaysVNext.Modules.{X}.*` → `x`, `EwaysVNext.Jobs*` → `jobs`, `EwaysVNext.Audit*` → `audit`.
Anything else **throws** and names the offending CLR type.

The previous code fell back to `dbo` for unrecognised namespaces. A silent default is how two
modules end up sharing a schema nobody chose; `Every_Table_Lives_In_A_Named_Schema` and this throw
make that impossible to do by accident.

### D5 — `pg_trgm` and `unaccent` are declared in the model
Both are declared with `HasPostgresExtension`, so `init` creates them. The storefront's search
(spec §8) needs trigram similarity for typo tolerance and `unaccent` for the search vector; the
import pipeline's fuzzy matching (spec §5.5) needs `pg_trgm` at a 0.6 threshold.

### D6 — Two database roles: `eways_migrator` owns, `eways_app` writes rows
`build/postgres/init/01-roles.sql` runs once, as the superuser, when the data directory is
initialised. `eways_migrator` owns the database and everything the migrations create; `eways_app`
connects and does DML and owns nothing, so the application cannot change its own schema.
Passwords are read from the environment with psql's `\getenv`, so none is ever written into the
file or baked into an image.

The integration suite connects as `eways_app` and migrates as `eways_migrator`, which is what makes
`App_Role_Can_Write_Catalog_Tables_But_Owns_Nothing` meaningful: a suite running as superuser
proves nothing about the grants production will actually have.

**Consequence for phase 3:** `REFRESH MATERIALIZED VIEW` requires ownership of the view. The
storefront refresh therefore cannot run as `eways_app`. Phase 3 must either run the refresh job
under a role that owns the views, or wrap it in a `SECURITY DEFINER` function owned by
`eways_migrator` and grant `EXECUTE` to `eways_app`. This is a real decision phase 3 has to make,
not a detail.

### D7 — Repeatable SQL scripts, not hand-edited migrations
`DatabaseMigrator.MigrateAsync` applies the EF migrations, then runs every
`EwaysVNext.Database.Scripts.*.sql` embedded resource in ordinal name order, each through
`CreateExecutionStrategy().ExecuteAsync` (retries are on, so a bare `ExecuteSqlRawAsync` would not
be replayed — AGENTS.md rule 8).

`init` is regenerated repeatedly while the MVP is being built, so hand-written SQL inside a
migration would be lost on every regeneration. The scripts are therefore **idempotent** and run on
every migrate. Grants live there (Task 12). Anything the model can express — extensions, indexes,
check constraints — stays in the model and so lands in `init`.

### D8 — `ITransactionExecutor` is deleted (implemented in Task 5)
`EfCoreTransactionExecutor` called `BeginTransactionAsync(IsolationLevel.Serializable)`. With
`EnableRetryOnFailure` on, an execution strategy throws on a user-initiated transaction it did not
begin, and AGENTS.md rule 8 forbids `BeginTransaction` anywhere in `src/`. `CategoryService`, its
only user, becomes load → mutate → one save; the races the serializable transaction covered are
closed by optimistic concurrency (a write whose validity depends on another category also *touches*
that category, so `xmin` rejects the loser) and by unique indexes, with `23505` → `Error.Conflict`.

### D9 — Testcontainers replaces LocalDB
`PostgresFixture` starts `postgres:17-alpine` with the production roles script mapped into
`/docker-entrypoint-initdb.d/`, applies the real migrations, and hands tests an `eways_app`
connection. The suite now requires Docker and no longer requires SQL Server LocalDB. Tests run
against the migrations rather than `EnsureCreated`, so a broken migration fails the suite — which
is the point, since `EnsureCreated` would have hidden exactly that.

### D10 — Fresh `init`; no data migration
Both migration histories are deleted and regenerated as a single `init`. There is no production
database behind this template and the schema is being rewritten module by module. The dead
`UnifyCategoryStatus` migration test went with them: it asserted the ordering of a backfill in a
migration this repository does not contain, and had been failing since before this work started.

### D11 — MinIO behind an `IObjectStorage` port
Object storage is reached through `IObjectStorage` (Task 9) with an AWS S3 SDK adapter pointed at
MinIO, over three buckets: `catalog-media` (published images), `import-staging` (raw downloads,
private) and `documents` (invoice PDFs, private, served by 5-minute presigned URLs).

**MinIO's images are no longer anonymously pullable, and we run an archived mirror.** Both
`docker.io/minio/minio` and `quay.io/minio/minio` refuse an unauthenticated pull; this is a
distribution change on MinIO's side, not a transient registry fault — other namespaced images on
both registries pull normally. Resolved 2026-10-28, with the product owner choosing the mirror
over obtaining registry credentials:

- The dev stack reads `MINIO_IMAGE` and `MINIO_MC_IMAGE`, defaulting to `bitnamilegacy/minio`
  pinned by digest — the same AGPLv3 MinIO server (DEVELOPMENT.2025-05-24), redistributed. Point
  the variables back at `minio/minio` once credentials exist; nothing else changes.
- The mirror runs as uid 1001 and cannot write a freshly created named volume, so the dev service
  sets `user: "0"`, matching the official image rather than diverging from it.
- Tests use LocalStack's S3, not MinIO, for the same reason.

**What this costs.** The mirror is frozen: it receives no security updates, so it is a development
and test convenience, not a production supply chain. Production compose points at the same
variables, and a deployment should set them to an image someone is maintaining. Nothing in the
adapter is MinIO-specific — it speaks plain S3 — so a MinIO-specific quirk would not be caught
before deploy either way, and switching to another S3-compatible server is a configuration change
rather than a code one.

### D12 — InMemory stays the dev fallback, and nothing else
With no connection string configured and `ASPNETCORE_ENVIRONMENT=Development`, the host still falls
back to EF InMemory so a developer can start the API without a database. Outside Development a
missing connection string throws. InMemory is **not a database** — it ignores check constraints and
filtered unique indexes and evaluates untranslatable LINQ client-side — so it never stands in for a
test that asserts persistence behaviour; `PersistenceContractTests` runs the model against Npgsql
on a refused port for exactly that reason.

## Consequences

- **Docker is now required** to run `tests/IntegrationTests`. SQL Server LocalDB is not.
- **The committed SA password is gone.** `appsettings.json` ships `"Default": ""`; the dev value
  lives in `appsettings.Development.json` and matches the dev compose defaults.
- **`dotnet ef` works without a database.** Both design-time factories fall back to a placeholder
  connection string, because `migrations add` builds the model and never connects.
- **A new module must name its schema.** Adding entities under a namespace the resolver does not
  recognise fails fast with the CLR type in the message.
- **Index filters are provider-specific SQL.** A new filtered index must be written in Postgres
  syntax against snake_case columns; there is no translation layer to catch a mistake.

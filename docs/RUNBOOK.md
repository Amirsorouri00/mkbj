# Runbook

What to do when this system is running and something needs doing to it. Written for whoever is
on call, which may be somebody who has never read the code.

Every command runs from the repository root unless it says otherwise.

---

## The shape of it

One image, four roles. The same container runs as an API, a background worker, a migrator or a
health probe depending on its arguments — so there is one thing to build, one thing to scan and
one thing to roll back.

| Role | Command | Notes |
|---|---|---|
| API | *(default)* | Serves HTTP. `Jobs__Mode=Disabled` in production: it runs no background work. |
| Worker | `--worker` | Claims jobs with `FOR UPDATE SKIP LOCKED`. **Exactly one** in production. |
| Migrator | `--migrate` | Applies EF migrations, then every repeatable `Scripts/*.sql`. Run it before the new API starts. |
| Probe | `--probe <url>` | Exit 0 healthy, 1 not, 64 misused. The image has no curl, so this is the health check. |

Two Postgres roles. `eways_app` reads and writes rows and **owns nothing**; `eways_migrator`
owns the schema. The API never connects as the migrator.

---

## Releasing

**The host never migrates on startup.** That is deliberate: with replicas, two starting at once
would race, and a migration that needs reverting is easier to reason about when it is its own
step.

```bash
# 1. Build and push the image.
docker build -f build/docker/api.Dockerfile -t eways-api:$TAG .

# 2. Migrate, on its own, and watch it finish.
docker run --rm -e ConnectionStrings__Default="$MIGRATOR_CONNECTION" eways-api:$TAG --migrate

# 3. Roll the API and the worker onto the new image.
```

Migrations are expand-only in practice: the new schema must work with the *old* code, because
for a few seconds both are running. A column that is dropped in the same release that stops
using it will take the old replicas down with it.

**Rolling back.** Roll the image back first; the schema almost always tolerates it. If a
migration genuinely has to be undone, do it deliberately and by hand — there is no automatic
down path in production, and `ef migrations remove` talks to a *development* database.

---

## When something is wrong

### The API will not start

Read the first fifty lines of its log. The usual causes, in order of how often they happen:

1. **`ConnectionStrings__Default` missing or wrong.** Outside Development this throws rather
   than falling back to an in-memory database — a server that starts with no data is harder to
   diagnose than one that refuses to start.
2. **A secret is missing.** Compose uses `${VAR:?}` for every one, so the stack refuses rather
   than defaulting. A default password that reaches production is worse than an outage.
3. **Migrations have not been applied.** The API does not apply them. Run step 2 above.

### Orders are failing with `COMPOSITION_TIMEOUT`

This is the one partner-facing error marked retryable, and it means the composition engine ran
out of its budget — not that the amount was impossible.

- Check the admin dashboard's composition statistics: success rate and p95 duration, over 24
  hours and 7 days.
- A falling success rate with a *stable* p95 usually means the catalogue thinned out: too few
  active, priced variants for the amounts partners are asking for.
- A rising p95 usually means the candidate pool is being rebuilt too often. The pool is cached
  in process and invalidated by catalogue and pricing version changes, so a job republishing
  prices in a loop will do it.

### A partner says an order is missing

Ask for the correlation id — every response carries `X-Correlation-Id`, and the partner's own
logs will have it. Then:

```sql
SELECT id, number, order_status, payment_status, created_at
FROM sales.orders WHERE correlation_id = '<id>';
```

If there is no row, the order was never created; the partner's retry will have the same
`externalOrderId`, and creating it again is safe — that is what the idempotency key is for.

### The storefront is showing stale prices

The storefront reads materialized views, which are stale by construction (ADR-0020). They are
refreshed by a background job. If the worker is down, the shop keeps serving the last refresh
and nothing looks broken.

```sql
-- Is the worker claiming anything at all?
SELECT type, status, count(*) FROM jobs.jobs GROUP BY type, status ORDER BY 3 DESC;
```

The cart, checkout and the composition engine never read those views, so a stale storefront
cannot sell at the wrong price — a customer sees an old figure, and checkout refuses with
`PRICE_CHANGED`.

### An import is stuck

```sql
SELECT id, mode, order_status AS status, checkpoint_phase, step_sequence, count_discovered,
       count_extracted, count_published
FROM sourcing.import_jobs WHERE id = <jobId>;
```

A job that is `Running` with an unmoving `step_sequence` has lost its scheduled step — pause and
resume it. Resuming issues a fresh sequence, which strands any step still in flight so the work
is not done twice.

### The audit log

```
GET /api/v1/admin/audit/verify     (super admin, audit.verify)
```

`intact: false` means a hash in the chain does not recompute, or a row below the newest seal has
no seal of its own. **That is evidence that the record of who did what has been edited**, and it
is not a staff-level conversation. Preserve the database before doing anything else.

---

## Things that are true and surprising

- **One worker.** The job store uses `SKIP LOCKED` so several would be safe, but the candidate
  pool cache and the rate limiter are per process (ADR-0010): a second worker doubles the
  effective limits.
- **Cross-module writes are not atomic.** An order's customer may exist when the order does not.
  The orphan is harmless and the next attempt reuses it.
- **`GET /api/v1/shop/cart` writes.** It re-quotes and persists what the customer was shown, so
  checkout can compare against it. A read-shaped request taking a row lock is expected.
- **Invoice PDFs are rendered on first download**, not at checkout. MinIO being down does not
  stop orders being placed; it stops invoices being fetched.
- **`--seed` refuses to run in Production.** Use `Staging` if you genuinely need a demo
  catalogue in a deployed environment.

---

## Release gates

Before tagging a release:

```bash
dotnet build EwaysVNext.slnx -c Release     # 0 warnings; TreatWarningsAsErrors is Release-only
dotnet test EwaysVNext.slnx                 # needs Docker
tools/smoke/smoke.sh                        # the real image, the real compose file
```

Read [`docs/TESTING-GUARANTEES.md`](TESTING-GUARANTEES.md) before trusting a green run. It is
specific about what the suites do **not** prove, which is the part that matters at three in the
morning.

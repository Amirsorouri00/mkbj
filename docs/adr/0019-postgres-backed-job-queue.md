# ADR-0019: A Postgres-Backed Job Queue, Not a Broker

## Status
Accepted — 2026-10-28. Amends [ADR-0010](0010-kubernetes-deployment-strategy.md), whose
in-process `BackgroundService` reaper and its "replicas > 1 needs a CronJob" problem this replaces.

## Context

The MVP needs background work: catalog imports that run for minutes and must survive a restart,
an FX refresh every few hours, a storefront view refresh after every publish, audit sealing, log
pruning. ADR-0011 removed the message broker deliberately, so there is nothing to put this on.

The template's answer was `BackgroundService` registered in a module. ADR-0010 recorded the
consequence honestly: **every replica runs every hosted service**, so at `replicas: 3` the reaper
ran three times, and the fix proposed there was a Kubernetes CronJob — which moves the schedule out
of the repository and only works for idempotent batch work.

A queue in the database we already run solves the same problem without new infrastructure, and
`FOR UPDATE SKIP LOCKED` is exactly the primitive for it.

## Decision

**Jobs live in Postgres. Workers claim them with `SKIP LOCKED`. There is no broker and no
scheduler outside the application.**

### D1 — Two tables
`jobs.job` holds the work: type, `jsonb` payload, status (`Queued`/`Running`/`Completed`/`Failed`,
with a check constraint so no other value can be written, including by hand), `run_at`, attempt
counters, `locked_by`/`locked_until`, `last_error`, `dedupe_key`.

`jobs.recurring` holds the schedule: type, interval, `next_run_at`.

Both name their tables explicitly rather than by convention, because the store is raw SQL — a
convention-derived rename would break it at runtime with no compiler warning.

### D2 — The claim is one statement
```sql
UPDATE jobs.job SET status='Running', locked_by=@worker, locked_until=@now+@lease, attempts=attempts+1
 WHERE id IN (SELECT id FROM jobs.job
               WHERE (status='Queued' AND run_at <= @now)
                  OR (status='Running' AND locked_until < @now)
               ORDER BY run_at FOR UPDATE SKIP LOCKED LIMIT @n)
RETURNING id, type, payload::text, attempts, max_attempts;
```
`SKIP LOCKED` hands each row to exactly one claimer with no coordination between workers; the
second predicate reclaims a job whose lease expired, so a worker that died mid-run does not strand
its work. *Proven, not asserted*: `JobStoreTests` drains 50 jobs with five concurrent workers and
requires 50 unique ids.

Every ownership-sensitive write (`Complete`, `Retry`, `Fail`, `Heartbeat`) carries `locked_by` in
its `WHERE`, so a worker that lost its lease silently does nothing instead of finishing a job
another worker is now running.

Every statement runs inside `CreateExecutionStrategy().ExecuteAsync(...)`: retries are enabled, and
a bare `ExecuteSqlRawAsync` would not be replayed after a transient fault (AGENTS.md rule 8).

### D3 — Leases are explicit; delivery is at-least-once
A claim holds the job for `Jobs:LeaseDuration` (5 minutes). A handler that works longer must call
`JobContext.HeartbeatAsync`, which throws `JobLeaseLostException` when the job is no longer its.

**There is no automatic heartbeat.** A hung handler therefore loses its lease and the job is run
again elsewhere — which is the desired outcome, because the alternative is a stuck process holding
work forever. The cost is explicit: **every handler must be idempotent.** This is the same
requirement ADR-0010 identified for the CronJob approach, and it does not go away here.

### D4 — `dedupe_key` is unique among queued rows only
`UNIQUE (dedupe_key) WHERE status = 'Queued'`.

Table-wide uniqueness would be wrong twice over: a completed job would block every future job with
that key, and work requested while a run is in flight would be dropped even though the running job
may already have read the state the new request is about. Dropping only against a *queued*
duplicate coalesces a burst of "refresh the storefront" into one run without ever losing the last
one.

A retry that collides with a newer queued duplicate is marked `Completed` with
`last_error = 'coalesced into queued duplicate'` rather than retried — the newer job will do the
work, and retrying would leave the old job unable to requeue and claimed forever.

### D5 — Enqueue after the save, never inside it
`IJobQueue.EnqueueAsync` is its own statement and is not part of the caller's `SaveChangesAsync`.
Callers enqueue **after** their business write commits. A job enqueued in the same transaction
would either be lost on rollback or — worse — be claimed by a worker before the row it refers to
exists.

This is the trade the broker-free design makes: there is no outbox, so a crash between the save and
the enqueue loses the job. The work that uses this queue is all re-triggerable (an admin can start
another import; the next FX tick refreshes rates; a view refresh is requested again on the next
publish), so a lost enqueue degrades freshness rather than correctness. **Work that cannot tolerate
that must not use this queue.**

### D6 — Retries back off exponentially, capped
`JobBackoff.For(attempt, base, max)` = `min(base · 2^(attempt-1), max)`, defaulting to 5 seconds up
to 1 hour over 10 attempts. A failing dependency gets progressively more room instead of being
hammered every poll interval, and the cap stops a long-broken job drifting to never.

An exception past the attempt budget fails the job; a handler can also return `Failed` to give up
immediately without spending the remaining attempts, or `RetryLater` with its own delay.

Exceptions thrown through the registry's reflection call are unwrapped from
`TargetInvocationException` before being recorded. Left wrapped, every `last_error` would read
"Exception has been thrown by the target of an invocation" — a log that tells an operator nothing.

### D7 — The recurring tick is a guarded UPDATE
```sql
UPDATE jobs.recurring SET next_run_at = @now + interval WHERE next_run_at <= @now RETURNING type;
```
Read and advance in one statement, so several workers ticking together still produce one run. The
enqueue that follows uses `dedupeKey = "recurring:{type}"`, so a tick arriving while the previous
run is still queued is dropped instead of building a backlog.

Registration updates the interval on restart but leaves `next_run_at` alone: resetting it on every
deploy would make a frequently-restarted service run its recurring jobs far more often than
configured.

### D8 — `Jobs:Mode` decides who runs the loop
`InProcess` (default) runs the worker inside the API; `WorkerOnly` is a dedicated worker process;
`Disabled` runs nothing while still allowing enqueue.

The mode is checked **both** at registration and at the top of `ExecuteAsync`. `AddJobs` reads
configuration before any source layered on later exists, so the registration check alone is not a
reliable off switch — a test host, or a reloaded configuration file, would not be honoured.

Production compose runs `api` with `Jobs:Mode=Disabled` and one `worker` with `WorkerOnly`, which
is how the multi-replica problem ADR-0010 raised is avoided: the API scales freely, and the number
of workers is an explicit decision. Several workers are safe — that is what `SKIP LOCKED` is for —
but running one keeps ordering simple while the catalogue is small.

### D9 — Partner orders are never jobs
PRD §6 requires order creation to be synchronous: the partner receives the order and its invoice in
the response. Composition, the order write and invoice issuance all happen in the request. Nothing
in the order path touches this queue.

## Consequences

- **No new infrastructure.** The queue is in the database that already has to be up for the
  application to serve a request.
- **Handlers must be idempotent**, and this is load-bearing rather than a nicety (D3).
- **A lost enqueue is possible** between a business save and the enqueue that follows it (D5).
  Acceptable for every job type the MVP has; it must be re-checked before adding one that sends
  money, email or anything else a customer can see.
- **Jobs compete with request traffic for connections.** The worker claims in batches of 4 by
  default and holds a connection per in-flight job; a dedicated worker process keeps that pool
  separate from the API's.
- **ADR-0010's CronJob plan is withdrawn.** The schedule stays in the repository, where it is
  reviewable, instead of in cluster configuration.

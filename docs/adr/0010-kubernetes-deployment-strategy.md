# ADR-0010: Kubernetes-Native Deployment Strategy

## Status

Accepted — 2026-08-28.

Supersedes nothing. Amends the operational assumptions of ADR-0005 (saga crash semantics) and
records the deployment context that ADR-0004's outbox amendment depends on.

**Amended (2026-09-01)**: `CrossModuleSaga` (D5) and the outbox migration path ADR-0004
describes are both gone — see
[ADR-0011](0011-simplify-back-to-a-broker-free-bus-free-architecture.md). D5's compensation-token
fix and its compliance-checklist entries are moot; there is no saga left to orphan on SIGTERM.
D1–D4, D6, D7 (single-instance phase 1, the migration Job, the reaper CronJob, edge rate
limiting, per-replica caching, observability) are all deployment/infrastructure concerns
independent of the saga/outbox layer and are unaffected.

## Context

### Current state

The system is a single-process modular monolith. Three pieces of infrastructure are
**in-process**, which is correct for one instance and wrong for several:

| Concern | Implementation | Where |
|---|---|---|
| Reservation TTL reaper | `BackgroundService`, 60-second `PeriodicTimer` | `Catalog/Adapter/Outbound/ReservationExpirationService.cs` |
| Rate limiting | `PartitionedRateLimiter` — global 100/min by IP, `"admin"` policy 10/min by user name | `Host/Program.cs` |
| Analytics caching | `IMemoryCache`, 30-second TTL | `Admin/Application/Service/AdminServices.cs`, `FailureReportService.cs` |

### The problem

A production-readiness review identified that **every replica runs every
`BackgroundService`**, and that both the rate limiter and the cache are per-process. At
`replicas: 3`:

- Three reapers scan and write every 60 seconds. They do not corrupt data — the operations are
  independent, idempotent and commutative, and `rowversion` makes concurrent losers fail — but
  it is 3× the load and produces a steady trickle of `DbUpdateConcurrencyException` in the logs
  that reads like a fault and is not.
- The admin rate limit becomes **10 × N requests/minute**. It is not a limit.
- Cache hit rate drops to ~1/N, and two admins can see different totals inside the TTL window.

### The constraint and the insight

The team will deploy on Kubernetes. Kubernetes already owns scheduling, singleton execution and
edge concerns; solving them again in C# would mean maintaining a leader-election implementation,
a distributed-lock story and a Redis dependency for problems the platform is built to answer.

**The decision below therefore moves these concerns OUT of the application rather than solving
them inside it.** That is a genuine architectural stance, not a deferral: the application stays
a stateless request handler plus a set of idempotent batch operations, and the platform decides
how many of each to run.

## Decision

Three phases. **Phase 1 is the current code with no changes.** Each later phase names the code
change it requires, so the cost is visible before it is incurred.

### D1 — Phase 1: single instance (now → launch)

```yaml
spec:
  replicas: 1
  strategy:
    type: Recreate        # NOT RollingUpdate — see the note below
```

- Reaper stays a `BackgroundService`.
- Rate limiting stays in-process. With one replica the configured numbers are the real numbers.
- `IMemoryCache` stays. One replica, so one cache.
- **No code changes.**

**`Recreate`, not `RollingUpdate`, while `replicas: 1`.** A rolling update briefly runs two pods,
which silently puts you in Phase 2 conditions — two reapers, two limiters — during every deploy.
`Recreate` accepts a few seconds of downtime in exchange for never violating the single-instance
assumption. Revisit only when Phase 2 lands.

**Probes must use the split endpoints** added for exactly this purpose:

```yaml
livenessProbe:
  httpGet: { path: /health/live, port: 8080 }    # runs NO checks — process only
readinessProbe:
  httpGet: { path: /health/ready, port: 8080 }   # DbContext checks, tagged "ready"
```

Do **not** point liveness at `/health` or `/health/ready`. Both include the database checks, so a
five-second database blip would not merely remove the pod from rotation — it would **restart
it**, turning a transient outage into a rolling restart. That inversion is the entire reason the
endpoints are split.

### D2 — Phase 1: migrations run as a Job, before the rollout

The host never auto-migrates (`Program.cs` — `--migrate` applies and then `return`s without
serving). In Kubernetes that becomes a `Job` gated ahead of the `Deployment`, reusing the same
image:

```yaml
apiVersion: batch/v1
kind: Job
metadata:
  name: ewaysvnext-migrate
spec:
  backoffLimit: 2
  template:
    spec:
      restartPolicy: Never
      containers:
        - name: migrate
          image: ewaysvnext/host:{{ .Values.image.tag }}
          args: ["--migrate"]
          envFrom: [{ secretRef: { name: ewaysvnext-connectionstrings } }]
```

**A Job, not an init container on the Deployment.** An init container runs once per pod, so
`replicas: 3` means three concurrent migrators. EF Core does acquire an exclusive advisory lock
for migration application, so they serialise rather than corrupt — but two of the three then sit
idle holding a pod slot, and a failure surfaces three times. One Job is one answer.

### D3 — Phase 2: the reaper becomes a CronJob (when `replicas > 1`)

**Code change required: one flag, following the existing `--migrate` precedent exactly.**

```csharp
// Program.cs — same shape as the --migrate branch directly above it.
if (args.Contains("--reap-once"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<IReservationReaper>().ReapOnceAsync(default);
    return;   // exit without serving, exactly like --migrate
}
```

This requires extracting `ReapOnceAsync` from `ReservationExpirationService` behind a small port
so both the timer host and the CLI path can call it. The `BackgroundService` registration then
becomes conditional (`Reaper:Mode = InProcess | External`), defaulting to `InProcess` so Phase 1
and local development are unaffected.

```yaml
apiVersion: batch/v1
kind: CronJob
metadata:
  name: reservation-reaper
spec:
  schedule: "*/1 * * * *"
  concurrencyPolicy: Forbid
  successfulJobsHistoryLimit: 3
  failedJobsHistoryLimit: 5
  startingDeadlineSeconds: 120
  jobTemplate:
    spec:
      backoffLimit: 1
      template:
        spec:
          restartPolicy: Never
          containers:
            - name: reaper
              image: ewaysvnext/host:{{ .Values.image.tag }}   # SAME image as the API
              args: ["--reap-once"]
              envFrom: [{ secretRef: { name: ewaysvnext-connectionstrings } }]
```

**Deliberate deviations from the original proposal, and why:**

1. **Same image, different args — not a separate `ewaysvnext/reaper` image with an
   `/app/reaper` binary.** A second image is a second thing to build, version, scan, sign and
   keep in lockstep with the first; a reaper running different code from the API is a class of
   bug that cannot happen if there is one artifact. `docker-compose.yml` already uses this
   pattern for migrations (`build: { context: ., dockerfile: Dockerfile }`, then
   `command: ["dotnet", "EwaysVNext.Host.dll", "--migrate"]`).
2. **`startingDeadlineSeconds` is set.** Without it, a controller outage longer than the
   schedule interval can cause the CronJob controller to consider many missed starts and, past
   100, stop scheduling entirely. A one-minute schedule makes that reachable.

#### What Kubernetes does and does not guarantee here

`concurrencyPolicy: Forbid` prevents a **new** Job while the previous one is still running. It
is **not** exactly-once execution, and this ADR must not be read as claiming otherwise:

- CronJob is **at-least-once**: a controller restart at the wrong moment can create two Jobs for
  one schedule.
- It can also **miss** a schedule entirely under controller pressure or clock skew.

**This is acceptable for the reaper, and the reason is specific to the reaper:** releasing an
expired hold and pruning a retention-elapsed row are idempotent and commutative, and a missed
minute only delays reclamation by one tick. `One_Reaper_Cycle_Leaves_Nothing_For_The_Next_One`
already pins that a cycle terminates.

**It is NOT a general licence.** ADR-0004's outbox amendment specifies an atomic row claim or
leader election precisely because a drainer that sends email must not run twice. "Kubernetes
handles singleton execution" is true for *idempotent batch work* and false for *side effects*.
Do not generalise D3 to the drainer.

### D4 — Phase 2: rate limiting moves to the edge

Enforce at an API gateway (Kong, Envoy/Gateway API, or the cloud load balancer) rather than
Redis-backed middleware. The limit belongs where the request count is already known, and the
gateway sheds load *before* it reaches a pod.

**Two consequences that must be handled, not discovered:**

1. **The in-process limiter must be removed or neutralised in the same change**, not left
   registered. Two limiters with independent windows produce 429s that neither operator can
   explain. Gate it on the same `Host:RateLimiting:Mode` switch used for the reaper.
2. **The partition semantics change.** The current `"admin"` policy partitions by
   `context.User.Identity?.Name` — per *user*. A gateway typically partitions by IP or API key —
   per *client*. Several admins behind one office NAT would share a bucket. If per-user limiting
   is a requirement, the gateway needs to read the JWT subject, which not all gateways do
   cheaply. Decide this explicitly; do not let it change by accident.

The integration test suite depends on the current in-process behaviour: `LocalDbFixture` builds a
host per test *because* the admin limiter allows 10/min. If the limiter is disabled in-process,
that fixture can be simplified — but only then, and its comment says so.

### D5 — Phase 2 prerequisite: SIGTERM must not orphan a saga — **CODE FIX RESOLVED 2026-08-28**

**This is not in the original brief and is the most important item in this ADR.** Kubernetes
makes SIGTERM routine — every deploy, scale-down, node drain and HPA event — where a long-lived
VM almost never sees one. That changes the frequency of an existing weakness by orders of
magnitude.

Verified in `CrossModuleSaga.cs`: on cancellation the saga compensates and rethrows —

```csharp
catch (OperationCanceledException) when (ct.IsCancellationRequested)
{
    _logger.LogWarning("Saga {Saga}: cancelled during step {Step}", _name, step.Name);
    await CompensateAsync(completedWithCompensation, ct).ConfigureAwait(false);
    throw;
}
```

— but `CompensateAsync` receives **the already-cancelled token**. Any compensation that touches
the database throws `OperationCanceledException` immediately, lands in the `catch (Exception)`
arm, logs `COMPENSATION FAILED — manual reconciliation required` and increments
`sagas.compensation_failures`. So on shutdown, compensation is *attempted and reliably fails*.

Today that is rare. Under Kubernetes it would happen on every deploy that interrupts an
in-flight order.

**Required before `replicas > 1`:**

1. ~~Compensate with an independent, short-lived token.~~ **DONE.** `CompensateAsync` now runs on
   its own `CancellationTokenSource` with a 30-second budget shared across the whole pass, and the
   token *parameter was removed* rather than shadowed so the caller's token cannot be passed back
   in by accident. A compensation that exhausts the budget is logged as `COMPENSATION TIMED OUT`
   and metered — distinguished from a generic failure because a timeout may mean the work
   PARTIALLY applied, which is a different remedy. Three tests in `CrossModuleSagaTests` pin it;
   restoring the old signature fails them.
2. **`terminationGracePeriodSeconds: 45`** with a `preStop` sleep of ~5s, so the pod leaves the
   endpoints list before it stops accepting work, and the framework's 30-second shutdown timeout
   fits inside the grace period rather than being cut short by SIGKILL.
3. **Alert on `sagas.compensation_failures`.** The OTLP exporter now ships it (ADR-0004
   amendment); nothing consumes it yet.

### D6 — Phase 2: caching stays per-replica, deliberately

Do **not** move `IMemoryCache` to Redis in Phase 2.

The cached values are admin analytics with a 30-second TTL. Per-replica caching costs hit rate,
not correctness — with one exception worth stating: **two admins on different replicas can see
different totals within the same 30-second window.** For a dashboard that is acceptable; for a
figure someone reconciles against, it is not. If that becomes a complaint, the cheaper fix is
lowering the TTL or adding the replica identity to the response, not adding a Redis dependency.

### D7 — Phase 3: observability and elasticity (high traffic)

Only once Phases 1–2 are stable:

- **Tracing**: the OTLP exporter already exists and is endpoint-driven
  (`Otlp:Endpoint` / `OTEL_EXPORTER_OTLP_ENDPOINT`). Point it at a collector; no code change.
- **Logging**: Serilog already writes structured console output — ship it with the platform's
  agent. Add a correlation id to error responses first, or a support ticket still cannot be
  joined to a trace.
- **Metrics**: Prometheus scraping via the collector. First dashboard should be
  `sagas.*`, not request rate.
- **HPA / PDB / topology spread**: standard, with one architectural caveat — **HPA on a monolith
  scales every module together.** If Catalog reads dominate, you scale Identity and Payment too.
  That is the price of the deployment unit, and the module boundaries are what make extracting a
  hot module possible later (ADR-0001).

## Consequences

### Positive

- **Phase 1 ships with zero code changes** and the current tests remain valid.
- Singleton scheduling, load shedding and restart policy become platform configuration rather
  than application code. No leader-election implementation to own, no Redis in the critical path.
- One image for API, migrations and reaper — a single artifact to build, scan and version.
- The Phase 2 code delta is small and enumerated: one CLI flag, one extracted port, two config
  switches, and the D5 compensation-token fix.

### Negative

- **`Recreate` in Phase 1 means deploys have brief downtime.** Accepted; the alternative is
  running Phase 2 conditions without Phase 2 code.
- **The reaper's schedule moves out of the repository** into cluster config, so its cadence is no
  longer visible to a developer reading the code. The `Reaper:Mode` switch and this ADR are the
  mitigation.
- **CronJob is at-least-once**, so the reaper's idempotence moves from "nice property" to
  "load-bearing requirement". `One_Reaper_Cycle_Leaves_Nothing_For_The_Next_One` is now
  protecting a production invariant, not just a performance one.
- **Rate-limit partitioning changes** from per-user to per-client at the edge (D4).
- **Local development and the integration suite keep the in-process path**, so the Phase 2
  wiring is exercised only in the cluster. That gap is real; a smoke test against the CronJob
  path in staging is the cheapest cover.

### Neutral / deliberately unchanged

- Consistency model is untouched. ADR-0002 and ADR-0005 still hold: cross-module calls are
  synchronous, not transactional, compensated by saga. Kubernetes changes nothing about that.
- No message bus. ADR-0004 still governs the async path; D3 explicitly does not authorise
  using a CronJob as one.
- One database per module. Nothing here consolidates them.

## Compliance checklist — before setting `replicas > 1`

- [x] D5.1 — compensation runs on an independent token — **DONE 2026-08-28**, mutation-proven
- [ ] D5.2 — `terminationGracePeriodSeconds` + `preStop` configured
- [ ] D5.3 — alert on `sagas.compensation_failures`
- [ ] D3 — `--reap-once` flag, `Reaper:Mode`, CronJob deployed, in-process reaper disabled
- [ ] D4 — gateway limits live, in-process limiter disabled, partition semantics agreed
- [ ] D6 — per-replica cache accepted in writing, or TTL lowered
- [ ] Load test establishing the actual breaking point (still absent — see the readiness
      assessment; every scaling number in this ADR is reasoning, not measurement)

## References

- ADR-0001: Hexagonal-First architecture (why extracting a hot module stays possible)
- ADR-0002: Direct inter-module calls (the "same transaction" claim is withdrawn)
- ADR-0004: Domain events — NoOp today; the 2026-08-28 amendment specifies the outbox path and
  why D3's reasoning does **not** extend to it
- ADR-0005: `CrossModuleSaga` — crash semantics amended by D5
- `docs/TESTING-GUARANTEES.md` — what the suite does and does not prove
- `Program.cs` — `--migrate` (the precedent D2/D3 follow), health-probe split, OTLP export

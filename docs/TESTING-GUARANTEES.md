# What the tests prove — and what they do not

**265 tests · 4 suites · ~25 seconds.**
**The integration suite requires SQL Server LocalDB.** The other three suites need no database.

This document exists because a green suite is a claim, and every claim in it was once wrong
in a way nobody noticed. The original review of this codebase found that **the system could
not log a user in against SQL Server while all 95 tests passed**, because the integration
tests ran on EF InMemory, which evaluates untranslatable LINQ client-side. The tests were not
weak — they were *confidently testing the wrong runtime*.

So: know what each suite can see. A test that cannot fail is documentation with a build step.

| Suite | Count | Speed | What it runs against |
|---|---|---|---|
| `UnitTests` | 151 | ~0.5 s | Pure domain + saga toolkit, no IO |
| `ArchitectureTests` | 23 | ~2 s | Compiled assemblies (NetArchTest) + source scans |
| `PersistenceContractTests` | 31 | ~5 s | **Both** SQL Server (no connection) and EF InMemory |
| `IntegrationTests` | 60 | ~20 s | Full HTTP host via `WebApplicationFactory`, **SQL Server LocalDB** |

### Prerequisite: LocalDB

The integration suite creates seven databases (one per module) named
`EwaysVNext_Test_{runId}_{Module}`, **applies the migrations**, and drops them afterwards. It
needs a running LocalDB instance:

```bash
sqllocaldb start MSSQLLocalDB
```

LocalDB ships with Visual Studio and with the SQL Server Express installer. On a machine
without it the integration suite fails to start; the other three suites are unaffected.

---

## The InMemory problem, stated plainly

`IntegrationTests` used to run against the **EF InMemory provider**, and that is why this
document exists. It is not a database, and it differs from SQL Server in ways that matter:

| Behaviour | SQL Server | EF InMemory |
|---|---|---|
| Untranslatable LINQ (`u.Email.Value == s`) | **throws** | silently evaluated in memory |
| Check constraints | enforced | **ignored** |
| Filtered unique indexes | enforced | **ignored** |
| `rowversion` concurrency tokens | generated | **not generated** (suppressed by `ModuleDbContextBase`, or every update would throw) |
| Execution strategy / retries | configurable | **none** |
| `OFFSET -20 ROWS` | error | tolerated |
| Server-side vs client-side `Take` | distinguishable | **indistinguishable** |

Every row of that table is a place where an integration test could pass on a system that would
fail in production. Two things now close that gap: `PersistenceContractTests` covers the
translation and materialization contract on both providers, and **the integration suite runs
on LocalDB**, so the remaining rows are exercised for real.

> **Why the swap was more than a provider change.** Each of the five integration classes
> carried its own copy of a `Replace<TContext>(…UseInMemoryDatabase…)` block that *overrode*
> `DbContextOptions` — which meant they bypassed the production registration entirely,
> including `EnableRetryOnFailure` and the migrations-history-table setting. `LocalDbFixture`
> instead supplies **connection strings via configuration**, so `AddModuleDbContext` takes its
> real SQL Server path. The system under test is now the one that ships.
>
> One consequence to know before "optimising" the fixture: each test builds its own host,
> because the admin GraphQL endpoint is rate limited to **10 requests per minute** and the
> suite makes far more. Sharing one host trips the limiter and later tests fail with an
> empty-bodied HTTP 429. The InMemory suite depended on the same per-test isolation without
> anyone noticing — xUnit re-runs a class's constructor for every test method.

## Why PersistenceContractTests runs on two providers

It runs **the same real production code paths** — the services are `internal sealed`, so the
tests reach them through DI rather than reimplementing their queries — against two providers,
because neither alone sees the whole contract:

- **SQL Server, pointed at a refused port.** Translation happens before connection, so an
  untranslatable query throws a *translation* error while a translatable one throws a
  *connection* error. The classifier is an **allowlist**: a `DbException` anywhere in the
  exception chain means "reached the database", i.e. the shape translated. An earlier version
  used a message *denylist* and passed a genuinely broken fix.
- **EF InMemory, which actually executes.** This catches materialization failures — a value
  converter that cannot parse a stored value — that the connection error would mask. One
  missing enum case (`"Refunded"`) had made a refunded payment row *permanently unreadable*.

## What each guarantee actually is

### Verified end-to-end
- **Authorization**: every endpoint carries `IAuthorizeData`, checked by walking the live
  `EndpointDataSource` against a 6-entry allowlist (`/health`, `/health/live`, `/health/ready`,
  register, login, refresh). A new unprotected endpoint fails the build.
- **Ownership**: a user cannot read or mutate another user's order, customer profile or
  payment — asserted over HTTP with real tokens.
- **Idempotency**: order and payment replay, both interleavings — sequential replay and the
  concurrent race on the filtered unique index.
- **Saga compensation**: ordering, index-based targeting, and that a compensation failure is
  logged and metered rather than swallowed.
- **Read-side projection**: admin product rows do not hydrate the `Product` aggregate,
  asserted via an **empty change tracker** after the query.

### Verified against a real database
`RealDatabaseGuaranteesTests` covers the three things that were previously impossible to
assert. Each is mutation-proven.

- **Optimistic concurrency.** Two contexts load the same product, both mutate, both save; the
  second must fail with `DbUpdateConcurrencyException` and the first writer's value must
  survive. This was structurally untestable before: InMemory generates no `rowversion`, so
  `ModuleDbContextBase` disables the token there or every update would throw.
- **The check constraint, as behaviour.** A raw `UPDATE … SET Stock = -1` that bypasses the
  aggregate is rejected by SQL Server, naming `CK_Products_Stock_NonNegative`. Previously this
  could only be asserted as DDL.
- **The migration backfill.** On its own database, migrate to the version *before*
  `CommittedAt` existed, insert a legacy committed reservation, then migrate forward and assert
  the row was backfilled. Removing the `migrationBuilder.Sql(...)` fails this test. That is the
  real upgrade path, not a simulation of it.
- **Deadlock survival.** A genuine SQL Server deadlock is staged — EF updates parent-then-child
  inside one `SaveChanges` while a rival session takes the same locks in the opposite order
  with `DEADLOCK_PRIORITY HIGH`, forcing EF to be the victim — and `SaveChangesAsync` must
  still **succeed**, because `EnableRetryOnFailure` absorbs it. Disabling retry fails this
  test.

> **A subtlety worth carrying into production thinking.** Retry replays the *same*
> `SaveChanges` with the *same* tracked state, including the `rowversion` read before the first
> attempt. If the rival transaction had also *modified* the contended row, the retry would fail
> with `DbUpdateConcurrencyException` instead of succeeding — correctly, because retry must not
> paper over a genuine concurrent modification. Retry absorbs *lock-ordering* faults; it does
> not absorb *lost-update* conflicts, and nothing should be designed as though it does. The
> deadlock test therefore uses `SELECT … WITH (XLOCK)` for the rival's second lock, to isolate
> the fault under test.

### Verified: graceful shutdown does not orphan a saga

When a request is cancelled mid-saga — which under Kubernetes means **every deploy, scale-down,
node drain and HPA event** — the completed steps must still be undone.

`CompensateAsync` runs on its **own bounded budget** (30s), not on the caller's token, and the
token parameter is gone rather than shadowed so it cannot be passed back in by accident. Three
tests pin it:

- `A_Cancelled_Saga_Still_Compensates_On_An_Independent_Token` — cancellation still propagates to
  the caller, the completed step is still compensated, and the token the compensation receives is
  **not** the cancelled one.
- `Compensation_Runs_On_An_Independent_Token_On_The_Failure_Path_Too` — the same on the ordinary
  failure path, and the budget is a real cancellable token rather than `CancellationToken.None`
  (an unbounded compensation would hang the shutdown it exists to survive).
- `A_Compensation_That_Fails_After_Cancellation_Is_Still_Metered` — shielding compensation from
  cancellation must not shield its *failures* from the operator.

**Mutation-proven:** restoring the old signature (compensation receives the caller's token) fails
the first test. Before the fix, compensation on the cancellation path was *attempted and reliably
failed* — it logged `COMPENSATION FAILED — manual reconciliation required` and incremented
`sagas.compensation_failures` without undoing anything.

**What this does NOT prove.** The budget must fit inside the host's shutdown timeout, which must
fit inside `terminationGracePeriodSeconds`. Those are deployment configuration (ADR-0010 D5) and
no test here asserts them — a grace period shorter than the budget still ends in SIGKILL
mid-compensation.

### Verified as *schema*, not as *behaviour*
These assert the DDL EF Core would emit (`Database.GenerateCreateScript()`), which is the
schema the migration applies.

- Read-path indexes and their **column order** (`IX_Orders_UserId_PlacedAt`, …). This proves
  the index is **created** — and the integration suite now confirms the migrations that create
  them apply cleanly — but it says nothing about the query optimiser *choosing* an index.
  Asserting a query plan is a different discipline and is not attempted here.

### Verified as *configuration*, not as *survival*
- The "no user-initiated transactions" precondition is a **source text scan** for
  `BeginTransaction` / `UseTransaction` under `src/`. It would miss a transaction started
  through an alias, a helper in another assembly, or reflection.

### Limitations that are ENFORCED rather than tested away
Some things this template does not support are refused at runtime instead of being left as a
silent trap. Tests pin the refusal, not the missing capability.

- **Non-two-decimal currencies.** `Money` rounds every amount to 2dp, so `Money.Create` throws
  `MONEY_UNSUPPORTED_CURRENCY_DECIMALS` for JPY/KRW (no minor unit) and KWD/BHD/OMR/TND (three).
  `MoneyTests` asserts both halves: the refusal, and that ordinary 2dp currencies still work. The
  check is a denylist, so an exotic non-2dp code missing from it would still be mis-rounded.
- **Mixed-currency baskets and reports.** `Order.Place` refuses
  (`ORDER_MIXED_CURRENCIES_NOT_SUPPORTED`); the failure report and analytics refuse with a 409.
  None of these is a capability gap that a test could paper over.

### Still not verified
- **Scan bounds as scans.** `ScanBoundTests` proves `GetFailedPaymentsAsync` honours its `take`
  and keeps the *newest* rows. It cannot prove the `Take` reached SQL rather than being applied
  after materialization — that is a query-plan question. `QueryTranslationTests` covers that the
  shape translates; the two together are the argument.
- **Real load.** Nothing here is a performance test. "Seeds 60 noise orders" checks
  *correctness past a page boundary*, not throughput.
- **Anything beyond LocalDB.** LocalDB is a real SQL Server engine, so translation, constraints,
  concurrency, indexes and deadlocks all behave faithfully. It is not a substitute for testing
  against your actual edition and configuration — collation, isolation level defaults, Azure SQL
  throttling and permission models are all environment-specific.

## The two lessons worth copying

**1. The dataset shape is part of the test.** Customer 360 used to fetch the globally newest
50 orders and filter by customer in memory, so any customer outside that window rendered with
an empty history — wrong data, silently. A 6-order test passed against the broken code. Only a
test seeding *past* the page size fails. If a bug hides below a threshold, the test must cross
it.

**2. Mutation-test every guard.** Every guard in this repository was verified by reintroducing
the bug and confirming a test fails. Two traps found doing it:
- A mutation that fails to **apply** looks identical to a test gap. Verify the edit landed
  before concluding "not caught".
- Testing the executor is not testing the call sites. Reverting one saga's metric call passed
  all 141 tests until an assertion was added to that saga's own test.

## Running them

```bash
dotnet test EwaysVNext.slnx                                                  # all 265
dotnet test tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj      # boundaries, fast
dotnet test tests/PersistenceContractTests/EwaysVNext.PersistenceContractTests.csproj
```

### The runner: Microsoft.Testing.Platform, not VSTest

xUnit.net v3 (v4.x) runs on **Microsoft.Testing.Platform**. Three consequences worth knowing
before something looks broken:

- **`global.json` carries the opt-in.** Its `"test": { "runner": "Microsoft.Testing.Platform" }`
  entry is load-bearing — delete it and every `dotnet test` fails with *"Testing with VSTest
  target is no longer supported"*. It is configuration, not preference.
- **Each suite is an executable.** `<OutputType>Exe</OutputType>` is mandatory (the package
  errors out otherwise), so a suite can be run with no runner package at all:
  `tests/UnitTests/bin/Debug/net10.0/EwaysVNext.UnitTests.exe`. Useful when `dotnet test` is
  the thing under suspicion.
- **The four suites now run concurrently**, so the wall clock is the slowest suite plus
  contention rather than the sum. The integration suite's own reported duration roughly doubled
  because it shares a machine — and one LocalDB instance — with the other three. That is
  scheduling, not a regression.

**Coverage** is collected by `Microsoft.Testing.Extensions.CodeCoverage`, not coverlet:
`dotnet test --coverage` (coverlet's collector is a VSTest data collector and does not run
under MTP).

**`xUnit1051` is suppressed for test projects** in `Directory.Build.targets`, with the
reasoning at the suppression. Short version: it is an advisory about cancelling hung tests,
it fires 432 times at call sites that merely omit an optional parameter, and injecting an
ambient token into the saga tests would blur exactly what they exist to assert.

**Run Release too.** `TreatWarningsAsErrors` is Release-only and has caught five regressions
during this codebase's hardening that Debug did not:

```bash
dotnet build EwaysVNext.slnx -c Release && dotnet test EwaysVNext.slnx -c Release
```

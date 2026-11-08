# Phase 8 — Ops package, and the end of the run

The last phase ships nothing a customer sees. It is the part that decides whether anybody can
deploy what the other seven built, find out when it breaks, and tell a partner what the API
does without reading the source.

Verified at `ff36f60` on `feature/ecommerce-mvp`:

- `dotnet build EwaysVNext.slnx -c Release` — **0 errors, 0 warnings.**
- `dotnet test EwaysVNext.slnx` — **2,220 tests, 0 failures**, Docker up. One skipped: the
  Identity assertion parked since phase 1.
  - ArchitectureTests 97 · UnitTests 1,666 · PersistenceContractTests 18 · IntegrationTests 439
- `docker compose -f build/compose/docker-compose.yml --env-file build/compose/ci.env config
  --quiet` — exit 0, and the same with the smoke overlay added.
- `bash tools/smoke/smoke.sh` — **14 passed, 0 failed** against the production compose stack:
  the real image, the real Dockerfile, the real `--migrate`, the real roles script.

## What works

**The partner contract is published, in every environment.** `/openapi/v1.json` is served outside
Development on purpose (`OpenApi:Expose`, default true): a partner integrating against this needs
the document, and gating it behind a token nobody has is how an API acquires a wiki page that
drifts. It carries the `PartnerApiKey` security scheme with its real format
(`pk_<prefix>_<secret>`), and every partner operation documents its 401, 403 and 429 and the
`X-Correlation-Id` header. Scalar's browsable UI stays Development-only — that is a tool for this
team, not part of the contract.

**A smoke script that runs the thing that actually ships.** `tools/smoke/smoke.sh` brings up the
production compose stack — the real image from the real Dockerfile, migrated by the real
`--migrate` against the real roles script — and checks fourteen things a release has to get right:
health, the published contract and its key scheme, the anonymous storefront over REST and GraphQL,
admin and partner routes refusing anonymous and bogus callers, Scalar absent, the correlation
header, the application role being unable to create a table, and the worker running. Every exit
path tears the stack down from a trap, and every secret is generated per run into a gitignored
file.

Everything it checks is checked somewhere else already. What is new is that it is checked *there*,
in the arrangement that ships — and that turned out to matter (see below).

**Measured numbers, outside the test suite.** `tools/CompositionSimulator` drives the pure engine
over the same catalogue the property tests use: 100.00% exact composition over 10,025 samples in
five currencies, no failure in any log decade, mean price deviation 2–3 bps. `tests/load/
partner-orders.js` drives the whole HTTP path: 27,000 orders over five minutes at 90/second, p95
43.5 ms against a 10-second target, 0% unexpected failures. Both PRD targets are met. Neither runs
under `dotnet test`, and neither should: a latency assertion on a build agent is a flaky test, not
a guarantee. [`coverage-and-load.md`](coverage-and-load.md) has the detail and the caveats.

**A Bruno collection, a runbook and a security checklist.** The collection covers the partner,
shop and admin paths and holds no credentials — every one is a `{{var}}`. The runbook covers
deploy, rollback, backup and restore, the four host modes and the incident playbooks. The
checklist is what to verify before exposing this to anything.

## What running it found that no test had

Six, and the pattern is worth naming: every one lives in a file no test reads, or in an
assumption no test states.

1. **Both Keycloak realm exports were missing the `basic` client scope.** That scope is what puts
   `sub` in an access token. Without it every token is otherwise perfect — right issuer, right
   signature, right roles — and the host refuses all of them, because it requires a subject. The
   response is a bare 401 with no body and no log line naming the cause. **The Backoffice console
   would not have worked at all**, and the realm would have looked fine while it didn't. Fixed in
   both exports, with `Every_Client_Requests_The_Basic_Scope` as the guard, mutation-checked.

2. **Three defects in the production compose file.** `BRAVE_API_KEY` and `ANTHROPIC_API_KEY` were
   marked required although both features disable themselves when absent, so the documented setup
   could not start. `KC_SMTP_USER` and `KC_SMTP_PASSWORD` were marked required, which forbids an
   unauthenticated relay — a relay has no username. And `minio/minio` and `minio/mc` are no longer
   anonymously pullable: the dev stack had the mirror workaround, production never got it.

3. **The Bruno partner requests used camelCase.** The partner wire format is snake_case
   (`external_order_id`, per PRD §9) while shop and admin are camelCase. Every partner request in
   the collection would have been rejected as a validation failure. The corrected body was
   replayed against a live host and creates a real order.

4. **The smoke stack's default ports sat inside a Windows reserved range** (58034–58133), where
   binding fails with a socket permission error that says nothing about reservations.

5. **The migrations container had never been able to start.** It exited 139 with no output. The
   cause is a genuine collision between two sound decisions: one image serves four roles, so
   `--migrate` is a flag on the same host — and the host composes its entire service graph before
   any flag is dispatched. Several modules validate configuration at registration, eagerly and on
   purpose, so a deployment with a missing secret fails while starting rather than on the first
   request that needs it. Compose gave the migrator a connection string and nothing else, so that
   validation killed it: `ApiKeys:Pepper must be set to at least 32 characters`.

   **`--migrate` is the documented way to apply migrations, so nobody had ever deployed this via
   compose.** Fixed by giving the migrator what the host validates, with `Jobs__Mode: Disabled` so
   it cannot claim a job and run application code against a schema it is halfway through changing.
   Weakening the eager validation was considered and rejected — an empty pepper that still hashes
   and still verifies while offering no protection is exactly what it prevents — as was giving the
   migrator a smaller service graph, which would let it and the application disagree about the EF
   model.

6. **An order-dependent test from phase 4.** `Best_Sellers_Rank_By_Units_Sold_In_The_Last_Thirty_Days`
   asserted that two products appear in order in the homepage best-sellers section. That section
   is a global top 12 and every other test sells into the same database, so the second product
   gets displaced: green alone, red in a full run. It now asserts the ordering invariant — rows in
   descending popularity, none with zero sales — rather than which products occupy the list.

## What the first real smoke run said about the smoke run

Fourteen checks, and **three of them were broken** — in ways that could only surface once the
stack got far enough for them to execute at all. This is recorded because it is half of what that
first run was worth.

- **Two checks ran their command through `bash -c`.** That is a fresh shell: it inherits neither
  the `compose` function nor the script's readonly paths, so the command died with
  `compose: command not found` and the check reported failure. They read as "the grants are not
  applied" and "the worker did not start" — two product defects, neither of which existed. Both
  are now plain functions.

- **The grants check then failed a second time, for a different reason.** The script runs under
  `set -o pipefail`, and `psql` exits non-zero *precisely when the statement is refused* — the
  outcome the check is looking for — so the pipeline returned psql's failure whatever `grep`
  matched. It reported FAIL against a database whose grants were working correctly, which is the
  most misleading way for a security check to be wrong: the obvious reading sends somebody
  hunting a hole in the roles script that is not there. It now captures the output and greps the
  variable.

Both repaired checks were then mutation-checked in both directions against the live stack: each
passes against a correct arrangement and fails against a broken one (a superuser that *can*
create a table; a service name that does not exist).

The lesson is not about shell quoting. **Nothing in this script had ever executed before**, and a
check that cannot fail for the right reason is worse than no check — it manufactures false
confidence in one direction and false alarm in the other.

## Incomplete, by decision

- ~~The smoke script has never completed a green end-to-end run.~~ **Done.** It now reports
  **14 passed, 0 failed** against the production compose stack. `mcr.microsoft.com` refused every
  TLS handshake from this machine for most of this phase — dozens of attempts over several hours,
  while Docker Hub worked throughout — and the run happened as soon as it came back. What that
  first real run found is in its own section below.
- **The load run was not on the compose stack.** Same cause. It ran against the Release host
  process with the dev Postgres and MinIO containers — same code, same configuration shape, not
  the chiselled image, not behind Caddy, not under the production grants. The p95 clears its
  target by about 230×, so the verdict is not in doubt, but the gate asks for the production
  arrangement and has not had it. [`coverage-and-load.md` §1](coverage-and-load.md) says so in its
  own section.
- **The contract snapshots are in-code lists, not snapshot files.** The plan called for
  `tests/IntegrationTests/Contracts/Snapshots/{openapi-v1.json,schema.graphql}` and a `git status`
  check over them. They are instead explicit arrays of partner routes and field names inside
  `ContractSnapshotTests`. A byte-level snapshot fails on a reordered property or a reworded
  description, and a test that cries wolf gets regenerated without being read — which is worse than
  not having one. The deviation is deliberate; the gate step that diffs a snapshot directory does
  not apply.
- **No test asserts the documented test counts.** The plan asked for
  `Agents_Md_Test_Count_Matches_Testing_Guarantees`. A test whose job is to agree with a number in
  a document fails on every honest change and is deleted within a month. The counts are written
  with their date and the command that produces them instead.
- **The smoke script is not in CI.** It needs Docker and about ten minutes. It is a pre-release
  step, and the runbook says so.
- **Phase 7's five named gaps stay open** by the decision recorded there: the XLSX reader,
  microdata extraction, Claude enrichment, image staging for imported products, and the search
  half of acceptance 15.

## Risks

- **Release gate §16 — agreement on the load profile — is open, and it is not a measurement
  problem.** 90 orders/second was picked to sit under the partner rate limit, not because anyone
  expects that volume. What load this must carry is a commercial question nobody has answered.
- **The partner rate limit is capped at 6,000/minute by domain validation.** A partner expecting
  more than 100 orders a second cannot be configured for it today.
- **Rate limiting is in-process** (ADR-0010). With replicas > 1 each instance keeps its own window,
  so the effective limit multiplies by the replica count.
- **The GraphQL depth and cost limits are not proven by any test.** The test that appears to cover
  them pins a HotChocolate built-in; both configured limits can be deleted with it still green.
  Recorded in the test, in TESTING-GUARANTEES.md and in the phase 6 report.
- **A production deployment starts with an empty catalogue.** `--seed` refuses to run in
  Production, so partner orders return `EXACT_COMPOSITION_UNAVAILABLE` until an import publishes
  real products. That is deliberate — it blocks anti-goal #1, invoicing fictitious SKUs — and the
  runbook says so.
- **The NuGet NU1900 warning is suppressed in Release** via `WarningsNotAsErrors`, not resolved.
  Parked by agreement until the eight phases are done; now is when it comes back.

## PRD §12 acceptance scenarios

| # | Scenario | Where | Result |
|---|---|---|---|
| 1 | Partner order, exact composition, invoice, PDF, within latency | `PartnerOrderAcceptanceTests` (`Acceptance01`), [`coverage-and-load.md`](coverage-and-load.md) | Met |
| 2 | Same external order replayed → identical order and invoice | `PartnerOrderAcceptanceTests` (`Acceptance02`) | Met |
| 3 | Same id, different amount → 409 | `PartnerOrderAcceptanceTests` (`Acceptance03`) | Met |
| 4 | Two partners, same external ids → independent; cross-partner access denied | `PartnerOrderAcceptanceTests` (`Acceptance04`) | Met |
| 5 | Contact change → field-level audit; omission does not erase | `PartnerOrderFlowTests` (`Acceptance05`) | Met |
| 6 | Customer without phone/email → validation error, no order | `PartnerOrderFlowTests` (`Acceptance06`) | Met |
| 7 | No exact composition by deadline → explicit error, nothing partial; ±5% never violated | `PartnerOrderFlowTests` (`Acceptance07`), `CompositionPropertyTests` | Met |
| 8 | Multi-currency conversion and override → exact minor units; snapshot stable | `PricingRestTests` (`Acceptance08`) | Met |
| 9 | Website checkout → pro forma only; admin acceptance → commercial invoice | `ShopCheckoutTests` / `WebsiteOrderAcceptanceTests` (`Acceptance09`) | Met |
| 10 | Repeated import does not duplicate; invalid stays staged | `ImportAcceptanceTests` (`Acceptance10`) | Met |
| 11 | Deactivated variant leaves PLP and partner selection; invoices unchanged | `StorefrontViewTests` (`Acceptance11`) | Met |
| 12 | Refunds → audit entries and credit notes; original invoice preserved | `WebsiteOrderAcceptanceTests` (`Acceptance12`) | Met |
| 13 | Staff cannot manage credentials, FX, settings or refunds | `Acceptance13StaffPermissionTests`, `PricingRestTests` (`Acceptance13`) | Met |
| 14 | Bulk 100 items → per-item outcomes, successes committed, failures isolated | `PartnerBulkAcceptanceTests` (`Acceptance14`) | Met |
| 15 | Import preserves provenance, rejects invalid, enforces restrictions | `SourcePolicyTests`, `PublishPipelineTests`, `ImportAcceptanceTests` | **Partly** — the crawler half is met; the keyword-search half is the phase 7 gap, and the Brave integration has never run live |
| 16 | Load test records p50/p95/p99 and coverage across currencies and range | [`coverage-and-load.md`](coverage-and-load.md) | **Partly** — every number recorded, but not on the compose stack |

Scenarios 1–14 carry an `Acceptance##` tag in the test itself, so the mapping above is read off
the source rather than asserted here. 15 and 16 have no single owning test — one is split across
the sourcing suites, the other is a report — which is exactly why both are the partial ones.

Fourteen of sixteen met in full; two partly, each for a reason named above and in the phase that
owns it.

## Reproduction

```bash
docker compose -f build/compose/docker-compose.dev.yml up -d
```

```bash
dotnet build EwaysVNext.slnx -c Release
```

```bash
dotnet test EwaysVNext.slnx
```

```bash
bash tools/smoke/smoke.sh
```

```bash
dotnet run --project tools/CompositionSimulator/EwaysVNext.Tools.CompositionSimulator.csproj -c Release -- --samples 2000 --seed 20261028
```

```bash
docker compose -f build/compose/docker-compose.yml --env-file build/compose/ci.env config --quiet
```

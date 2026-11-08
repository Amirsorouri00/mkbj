# Coverage and load

What the composition engine can actually hit, and what it costs to ask it over HTTP. Spec §10.5;
PRD §12 acceptance 16.

Two measurements, deliberately separate. The **simulator** drives the pure engine over a fixed
catalogue with no IO at all, so its numbers are reproducible on any machine from a seed. The **k6
run** drives the whole request — authentication, a live price quote, the engine, the order write,
the invoice — so its numbers describe this machine on this day and nothing more. Reading one as
the other is the mistake this split exists to prevent.

## 1. Environment

| | |
|---|---|
| Machine | Windows 11 (10.0.26200), 24 logical processors, 31.6 GB RAM |
| Docker | 29.5.2 |
| Git SHA | `201c71b` |
| Stack | `build/compose/docker-compose.yml` + `docker-compose.smoke.yml`, brought up by `tools/smoke/smoke.sh --keep` |
| Image | `eways-api:latest`, built by `build/docker/api.Dockerfile` from `mcr.microsoft.com/dotnet/aspnet@sha256:a9298ccc39c6258ae71228479e2f3f8d2ceb6639a9927262a4b0ff7d319a0e7e` |
| Catalogue | 400 sellable variants in each of USD, EUR, SGD, AED, GBP, from `--seed` |
| Commerce limits | min USD 10.00, max USD 1,000,000.00, composition deadline 8,000 ms, max 200 lines |

The load ran **on the production compose stack**: the real image, migrated by the real `--migrate`
against the real roles script, with k6 inside the stack's own network reaching the API by service
name. The database was queried as `eways_app`, the role that owns nothing — the same role the
application uses, under the production grants.

An earlier revision of this report measured a locally built host against the dev containers,
because `mcr.microsoft.com` refused every TLS handshake from this machine for most of that day and
the image could not be built. Those numbers have been replaced, not adjusted. The figures below
are from the arrangement that ships.

## 2. Representative distribution

Defined by `tools/CompositionSimulator`, which is the file the PRD's "documented distribution"
refers to:

- **Log-uniform over 10.00 – 1,000,000.00**, in minor units, with exact cents. Uniform would put
  roughly 90% of the draw above 100,000 and almost nothing at the cheap end, where the ±5% band is
  a few cents wide and an exact hit is hardest — measuring the easy half of the range and calling
  it coverage.
- **Plus the five mandated amounts** in every currency: 10.00, 17.43, 500.00, 12,845.99 and
  1,000,000.00.

2,000 random draws plus 5 mandated per currency = **10,025 compositions**, seed 20261028,
deadline 8,000 ms, max 20 lines.

The 99% target is measured against this and claimed no further.

## 3. Simulator coverage per currency

| Currency | Attempts | Success | p50 ms | p95 ms | p99 ms | Mean lines | Mean deviation (bps) |
|---|---:|---:|---:|---:|---:|---:|---:|
| USD | 2,005 | 100.00% | 0.40 | 0.67 | 0.81 | 4.6 | 3 |
| EUR | 2,005 | 100.00% | 0.11 | 0.13 | 0.23 | 4.6 | 3 |
| SGD | 2,005 | 100.00% | 0.11 | 0.15 | 0.25 | 4.7 | 2 |
| AED | 2,005 | 100.00% | 0.11 | 0.14 | 0.28 | 4.6 | 2 |
| GBP | 2,005 | 100.00% | 0.11 | 0.12 | 0.20 | 4.5 | 3 |

**Failure codes:** none. Every amount composed exactly.

**Mandated amounts:** all five composed exactly in all five currencies.

USD's p50 is higher than the rest only because it runs first and pays for the JIT.

A 100% rate with no failures at all deserves suspicion, so it was checked rather than believed.
Re-running with `--deadline-ms 0` — a budget that is already gone — produces
`COMPOSITION_TIMEOUT × 65` per currency and a 0.00% rate, so the counters, the failure table and
the uncovered-ranges section all demonstrably report a failure when one happens. The simulator
also asserts that every successful plan's line totals sum to exactly the amount asked for, and
throws if not, so "success" here means the cent matched, not that the engine returned something.

## 4. Uncovered ranges

Every log decade of the amount, in every currency, is at or above 99%. There are none.

## 5. Live load (k6)

`tests/load/partner-orders.js`, 5 minutes, constant arrival rate of **90 orders/second** across
10–50 VUs, amounts drawn from the same log-uniform distribution, currency picked per request. k6
ran as a container on the stack's own network, reaching the API at `http://api:8080`.

**On the arrival rate.** The first attempt used ten VUs looping freely. That issues roughly 2,150
orders a second against a partner whose own ceiling is 6,000 a minute, so 95.5% of the run came
back `429` and the percentiles described the rate limiter. 90/s sits just under the partner's
limit, which is what the PRD's latency target is about.

| Metric | Value |
|---|---|
| Requests | 27,001 (90.01/s, 5m00s) |
| `http_req_duration{name:create_order}` p50 | 20.82 ms |
| … p95 | **23.79 ms** |
| … p99 | 27.41 ms |
| … max | 63.43 ms |
| Successful orders (`order_composed_ms`) p95 | 23.84 ms |
| Unexpected failures (`order_refused`) | **0.00%** (0 of 27,001) |
| Policy refusals | 3.43% (928), all `AMOUNT_OUT_OF_RANGE` |

**Checks per currency** — all five at 100%, against a `rate>0.99` threshold:

| Currency | Passed |
|---|---:|
| USD | 100.00% (5,432 / 5,432) |
| EUR | 100.00% (5,403 / 5,403) |
| SGD | 100.00% (5,397 / 5,397) |
| AED | 100.00% (5,332 / 5,332) |
| GBP | 100.00% (5,437 / 5,437) |

**Thresholds: all passed.**

The containerized stack is about twice as fast as the locally built host an earlier revision
measured (p95 23.79 ms against 43.50 ms). That direction is the opposite of what an extra network
hop would suggest, and the honest answer is that the two runs are not a controlled comparison —
different runtime image, different filesystem, different logging sinks, and the Windows host was
also running the dev stack at the time. It is recorded because the number moved, not because the
cause is known.

**About the 928 refusals.** They are the seller's own minimum order amount, which is held in USD
while this draw picks an amount in the local currency: the bottom of the AED range is about
USD 2.72 and does not clear a ten-dollar floor, while the same figure in USD does. An earlier run
counted these as failures and reported AED at 89.18% against USD's 100.00% — a number that looks
like a currency-specific defect and is in fact the limit working. They are counted as the policy
refusals they are, alongside `EXACT_COMPOSITION_UNAVAILABLE`.

## 6. Live composition telemetry

From `sales.composition_attempts` on the stack's own database, read as `eways_app` — the engine's
own record of the same run.

| Currency | Attempts | Succeeded | p50 ms | p95 ms | p99 ms | Mean lines |
|---|---:|---:|---:|---:|---:|---:|
| USD | 5,444 | 5,444 | 5 | 6 | 7 | 4.6 |
| EUR | 5,351 | 5,351 | 5 | 6 | 7 | 4.6 |
| SGD | 5,302 | 5,302 | 5 | 6 | 7 | 4.6 |
| AED | 4,727 | 4,727 | 5 | 6 | 7 | 4.8 |
| GBP | 5,321 | 5,321 | 5 | 6 | 7 | 4.5 |

**Failures: none.** No row has `succeeded = false`, in any currency.

Two things are worth reading off this table. The engine takes about 6 ms of the request's 24 ms
under load, so roughly three quarters of the response time is the quote, the order write, the
invoice and the framework, not the composition — the composition engine is not the thing to
optimise. And mean lines agree with the simulator to within 0.2, which is the check that §3 and §6
describe the same engine on the same catalogue.

**Where the headroom runs out.** During the run `docker stats` showed the API at ~138% CPU and
Postgres at ~97% — that is one core's worth of database, with 24 available. At 90 orders/second
the database is the first thing to saturate, not the engine and not the application. Nobody has
measured where it actually tops out, and the arrival rate here was chosen to stay under the
partner rate limit rather than to find a ceiling.

## 7. Verdict against PRD targets

| Target | Result |
|---|---|
| **99% composition success on the documented distribution** | **Met.** 100.00% over 10,025 simulated compositions, every currency, every decade; and 100.00% over 26,145 live ones on the production stack. |
| **p95 under 10 s for a single order under the declared load** | **Met.** 23.79 ms at 90 orders/second for 5 minutes, on the compose stack — a margin of about 420×. |

**Release gate §16 is now one question, not two.** The measurement half is closed: this ran on the
production arrangement, which is what the gate asked for. What remains is commercial.

**The declared load is still a guess.** 90 orders/second was chosen to sit under the partner rate
limit, not because anyone expects that volume. Nobody has said what this system must carry, and
until somebody does, "p95 under load" is a sentence with an undefined subject. Two related facts
for whoever answers it:

- The partner rate limit is capped at 6,000 requests/minute by domain validation, so a partner
  expecting more than 100 orders a second cannot be configured for it today.
- Postgres was the first resource to saturate (§6). A higher target is a database question before
  it is an application one.

## 8. Reproduction

```bash
# 1. The simulator. No database, no host, no Docker — the engine is pure, so this needs nothing
#    running and gives the same report on any machine from the same seed.
dotnet run --project tools/CompositionSimulator/EwaysVNext.Tools.CompositionSimulator.csproj -c Release -- --samples 2000 --seed 20261028 --deadline-ms 8000 --out docs/reports/composition-coverage
```

```bash
# 2. The production stack, seeded, left running. Prints "14 passed, 0 failed" on the way.
bash tools/smoke/smoke.sh --keep
```

```bash
# 3. A Backoffice token. Direct grants go on, one user is created, and both are restored from a
#    trap. The admin password is the one the script generated into .env.smoke.
eval "$(bash tools/smoke/backoffice-token.sh http://127.0.0.1:18081 admin "$(grep '^KC_BOOTSTRAP_ADMIN_PASSWORD=' build/compose/.env.smoke | cut -d= -f2-)")"
```

```bash
# 4. A load partner at the maximum configurable rate limit, and a key for it. Note the scopes take
#    colons, and customers:write is required because the order creates the customer.
curl -fsS -X POST http://127.0.0.1:18080/api/v1/admin/partners -H "Authorization: Bearer $BACKOFFICE_TOKEN" -H 'Content-Type: application/json' -d '{"name":"Load Partner","scopes":["orders:create","orders:read","invoices:read","customers:read","customers:write"],"rateLimitPerMinute":6000,"bulkMaxItems":100}'
```

```bash
# 5. The load run, inside the stack's own network. `eways_default` is the compose project's
#    network — not `eways-dev_default`, which is the dev stack and resolves no `api` host.
docker run --rm -i --network eways_default -v "$PWD:/work" -w /work -e BASE_URL=http://api:8080 -e API_KEY="$PARTNER_KEY" -e RATE=90 -e DURATION=5m grafana/k6:0.54.0 run --summary-export=/work/docs/reports/k6-partner-orders.json tests/load/partner-orders.js
```

```bash
# 6. The server side of the same run, read as the application's own role.
docker compose -f build/compose/docker-compose.yml -f build/compose/docker-compose.smoke.yml --env-file build/compose/.env.smoke exec -T -e PGPASSWORD="$APP_DB_PASSWORD" postgres psql -U eways_app -d eways -At -F '|' -c "SELECT currency, count(*), count(*) FILTER (WHERE succeeded), round(percentile_cont(0.5) WITHIN GROUP (ORDER BY duration_ms)), round(percentile_cont(0.95) WITHIN GROUP (ORDER BY duration_ms)), round(percentile_cont(0.99) WITHIN GROUP (ORDER BY duration_ms)), round(avg(lines),1) FROM sales.composition_attempts GROUP BY currency ORDER BY currency;"
```

```bash
# 7. Tear it down. The smoke script does this from a trap unless --keep was passed.
docker compose -f build/compose/docker-compose.yml -f build/compose/docker-compose.smoke.yml --env-file build/compose/.env.smoke down -v
```

Raw output: [`composition-coverage.md`](composition-coverage.md),
[`composition-coverage.json`](composition-coverage.json),
[`k6-partner-orders.json`](k6-partner-orders.json).

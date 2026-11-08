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
| Git SHA | `e33097c` |
| Image | **not the production image** — see "What this run is not", below |
| Catalogue | 400 sellable variants in each of USD, EUR, SGD, AED, GBP (`catalog.mv_storefront_variant`), from `--seed` |
| Commerce limits | min USD 10.00, max USD 1,000,000.00, composition deadline 8,000 ms, max 200 lines |

### What this run is not

The plan calls for this on the `build/compose` production stack. It was run against the host
process instead, because `mcr.microsoft.com` refused every TLS handshake from this machine for the
duration of the work and the API image cannot be built without it. What changed, and what did not:

- **Same code, same configuration shape.** Release build, `ASPNETCORE_ENVIRONMENT=Staging`, the
  same `--migrate` and `--seed`, the same Postgres 17 and MinIO containers the dev stack uses.
- **Not the chiselled runtime image**, not Kestrel behind Caddy, and not the production grants —
  the load database was migrated and queried as `postgres`, not `eways_app`.
- **Not the network topology**: k6 ran in a container and reached the host over
  `host.docker.internal`, adding a hop the compose stack would not have.

The p95 figure below clears its target by two orders of magnitude, so none of those differences
is load-bearing for the verdict. They would matter for a number that was close. **The release
gate stays open until this is repeated on the compose stack.**

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
10–50 VUs, amounts drawn from the same log-uniform distribution, currency picked per request.

**On the arrival rate.** The first attempt used ten VUs looping freely. That issues roughly 2,150
orders a second against a partner whose own ceiling is 6,000 a minute, so 95.5% of the run came
back `429` and the percentiles described the rate limiter rather than the order path. 90/s sits
just under the partner's limit, which is what the PRD's latency target is about.

| Metric | Value |
|---|---|
| Requests | 27,000 (90.00/s, 5m00s) |
| `http_req_duration{name:create_order}` p50 | 35.80 ms |
| … p95 | **43.50 ms** |
| … p99 | 56.79 ms |
| … max | 93.50 ms |
| Successful orders (`order_composed_ms`) p95 | 43.71 ms |
| Unexpected failures (`order_refused`) | **0.00%** (0 of 27,000) |
| Policy refusals | 3.23% (873), all `AMOUNT_OUT_OF_RANGE` |

**Checks per currency** — all five pass the `rate>0.99` threshold:

| Currency | Passed |
|---|---:|
| USD | 100.00% (5,412 / 5,412) |
| EUR | 100.00% (5,448 / 5,448) |
| SGD | 100.00% (5,426 / 5,426) |
| AED | 100.00% (5,381 / 5,381) |
| GBP | 100.00% (5,333 / 5,333) |

**Thresholds: all passed.** `p(95)<10000` on the order request, `rate<0.01` on unexpected
failures, and `rate>0.99` on checks in each of the five currencies.

**About the 873 refusals.** They are the seller's own minimum order amount, which is held in USD
while this draw picks an amount in the local currency: the bottom of the AED range is about
USD 2.72 and does not clear a ten-dollar floor, while the same figure in USD does. An earlier run
counted these as failures and reported AED at 89.18% against USD's 100.00% — a number that looks
like a currency-specific defect and is in fact the limit working. They are now counted as the
policy refusals they are, alongside `EXACT_COMPOSITION_UNAVAILABLE`.

## 6. Live composition telemetry

From `sales.composition_attempts`, the engine's own record of the same 5-minute run — the server
side of the k6 numbers above.

| Currency | Attempts | Succeeded | p50 ms | p95 ms | p99 ms | Mean lines |
|---|---:|---:|---:|---:|---:|---:|
| USD | 5,412 | 5,412 | 8 | 10 | 14 | 4.6 |
| EUR | 5,418 | 5,418 | 8 | 10 | 14 | 4.6 |
| SGD | 5,292 | 5,292 | 8 | 10 | 13 | 4.7 |
| AED | 4,786 | 4,786 | 8 | 10 | 14 | 4.8 |
| GBP | 5,219 | 5,219 | 8 | 10 | 14 | 4.5 |

**Failures: none.** No row in the window has `succeeded = false`, in any currency.

Two things are worth reading off this table. The engine takes about 10 ms of the request's 43 ms
under load — four times its single-threaded cost in §3, which is what contention looks like — so
roughly three quarters of the response time is the quote, the order write, the invoice and the
framework, not the composition. And mean lines agree with the simulator to within 0.2, which is
the check that the two sections describe the same engine on the same catalogue.

## 7. Verdict against PRD targets

| Target | Result |
|---|---|
| **99% composition success on the documented distribution** | **Met.** 100.00% over 10,025 simulated compositions, every currency, every decade; and 100.00% over 26,127 live ones. |
| **p95 under 10 s for a single order under the declared load** | **Met.** 43.50 ms at 90 orders/second for 5 minutes — a margin of about 230×. |

**Release gate §16 — "agreement on the load profile" — stays open.** Two things are unsettled, and
neither is a measurement:

1. **The declared load is a guess.** 90 orders/second was chosen to sit under the partner rate
   limit, not because anyone expects that volume. The real profile is a commercial question.
2. **This did not run on the compose stack** (§1). The margin is wide enough that the verdict is
   not in doubt, but the gate asks for the production arrangement and has not had it.

Also worth recording: the partner rate limit is capped at 6,000 requests/minute by domain
validation (`Rate limit must be between 1 and 6000 requests per minute`). A partner expecting more
than 100 orders a second cannot be configured for it today.

## 8. Reproduction

```bash
# 1. The simulator. No database, no host, no Docker — the engine is pure.
dotnet run --project tools/CompositionSimulator/EwaysVNext.Tools.CompositionSimulator.csproj \
  -c Release -- --samples 2000 --seed 20261028 --deadline-ms 8000 \
  --out docs/reports/composition-coverage
```

```bash
# 2. A host to load. On the compose stack this is `KEEP=1 bash tools/smoke/smoke.sh`; this run
#    used the dev dependencies and a locally built host, for the reason in section 1.
docker compose -f build/compose/docker-compose.dev.yml up -d
```

```bash
# 3. Migrate and seed. Note the port: the dev stack publishes Postgres on 55432, because 5432 is
#    often taken by a local install that will accept the connection and refuse the password.
export ConnectionStrings__Default="Host=127.0.0.1;Port=55432;Database=eways_load;Username=postgres;Password=dev-postgres"
export ASPNETCORE_ENVIRONMENT=Staging Fx__Ecb__BaseUrl="http://127.0.0.1:1/"
dotnet run --project src/Host/EwaysVNext.Host -c Release -- --migrate
dotnet run --project src/Host/EwaysVNext.Host -c Release -- --seed
```

```bash
# 4. A Backoffice token, then a partner and a key. The script turns direct grants on, creates one
#    user, and puts both back from a trap.
eval "$(bash tools/smoke/backoffice-token.sh http://127.0.0.1:8081 admin admin)"
```

```bash
# 5. The load run itself.
docker run --rm -i -v "$PWD:/work" -w /work --add-host=host.docker.internal:host-gateway \
  -e BASE_URL=http://host.docker.internal:18080 -e API_KEY="$PARTNER_KEY" \
  -e RATE=90 -e DURATION=5m grafana/k6:0.54.0 \
  run --summary-export=/work/docs/reports/k6-partner-orders.json tests/load/partner-orders.js
```

```bash
# 6. The server side of the same run.
docker exec eways-dev-postgres-1 psql -U postgres -d eways_load -At -F '|' -c \
  "SELECT currency, count(*), count(*) FILTER (WHERE succeeded), round(percentile_cont(0.5) WITHIN GROUP (ORDER BY duration_ms)), round(percentile_cont(0.95) WITHIN GROUP (ORDER BY duration_ms)), round(percentile_cont(0.99) WITHIN GROUP (ORDER BY duration_ms)), round(avg(lines),1) FROM sales.composition_attempts WHERE at > now() - interval '6 minutes' GROUP BY currency ORDER BY currency;"
```

Raw output: [`composition-coverage.md`](composition-coverage.md),
[`composition-coverage.json`](composition-coverage.json),
[`k6-partner-orders.json`](k6-partner-orders.json).

# Composition coverage

Generated 2026-11-08 18:48 UTC from `tools/CompositionSimulator` — 2000 random amounts plus 5 mandated ones per currency, seed 20261028, deadline 8000 ms, max 20 lines.

The distribution is log-uniform over 10.00–1,000,000.00 plus the mandated amounts 10.00, 17.43, 500.00, 12,845.99 and 1,000,000.00. The 99% target is measured against this and claimed no further.

| Currency | Attempts | Success | p50 ms | p95 ms | p99 ms | Mean lines | Mean dev (bps) |
|---|---:|---:|---:|---:|---:|---:|---:|
| USD | 2005 | 100.00 % | 0.40 | 0.67 | 0.81 | 4.6 | 3 |
| EUR | 2005 | 100.00 % | 0.11 | 0.13 | 0.23 | 4.6 | 3 |
| SGD | 2005 | 100.00 % | 0.11 | 0.15 | 0.25 | 4.7 | 2 |
| AED | 2005 | 100.00 % | 0.11 | 0.14 | 0.28 | 4.6 | 2 |
| GBP | 2005 | 100.00 % | 0.11 | 0.12 | 0.20 | 4.5 | 3 |

## Failure codes

None. Every amount composed exactly.

## Uncovered ranges

Log decades of the amount, in minor units, whose success rate is below 99%.

None — every decade is at or above 99% in every currency.

## Mandated amounts

All five composed exactly in all five currencies.

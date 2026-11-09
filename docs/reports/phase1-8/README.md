# Gaps & Risks — All 8 Phases

Compiled from the gate reports on branch `feature/ecommerce-mvp` at `06e6ad1` (2026-11-08).

## Summary

| Metric | Count |
|---|---:|
| PRD acceptance scenarios met in full | 15 / 16 |
| PRD acceptance scenarios partial | 1 (scenario 15 — keyword search half) |
| Features not built (phase 7) | 5 |
| Design-scope deferrals | 5 |
| Carried risks (unique, deduplicated) | 14 |
| Tests | 2,220 (0 failures, 1 skipped) |

## Per-Phase Reports

| Phase | Title | Gaps | Risks | Report |
|---|---|---:|---:|---|
| 0 | Platform & Infrastructure | 0 | 0 | [phase-0-gaps.md](phase-0-gaps.md) |
| 1 | Identity & Tenancy | 0 | 3 | [phase-1-gaps.md](phase-1-gaps.md) |
| 2 | Catalog & Pricing | 0 | 3 | [phase-2-gaps.md](phase-2-gaps.md) |
| 3 | Storefront Views & Search | 2 | 3 | [phase-3-gaps.md](phase-3-gaps.md) |
| 4 | Partner Orders & Composition | 0* | 3 | [phase-4-gaps.md](phase-4-gaps.md) |
| 5 | Website Channel & Checkout | 0* | 3 | [phase-5-gaps.md](phase-5-gaps.md) |
| 6 | Admin Dashboard & GraphQL | 1 | 3 | [phase-6-gaps.md](phase-6-gaps.md) |
| 7 | Sourcing & Import Pipeline | **5** | 4 | [phase-7-gaps.md](phase-7-gaps.md) |
| 8 | Ops, Smoke, Load & Docs | 3 | 4 | [phase-8-gaps.md](phase-8-gaps.md) |

\* Items deferred from P4/P5 were resolved in P8 (simulator, load run, dashboard endpoint).

## Top-Priority Gaps (features not built)

All five live in **phase 7 (Sourcing)**. Each is a bounded task behind an existing port:

1. **XLSX file reader** — CSV/JSON work; spreadsheets are refused cleanly. One `IImportFileReader`
   implementation.
2. **Microdata extraction** — JSON-LD/OpenGraph work; microdata needs a full HTML parser.
3. **Claude enrichment** — the pipeline publishes raw source data only; enrichment port exists.
4. **Image staging** — `StagedRecord.ImageKeys` flows through publication but nothing downloads
   images. Imported products have no pictures.
5. **Keyword search (Brave)** — never tested against the live service; disables itself without an
   API key.

## Cross-Cutting Risks (recur across phases)

| Risk | Phases | Notes |
|---|---|---|
| In-process rate limiter | P1, P4, P6, P8 | Each replica keeps its own window. Must settle ADR-0010 before `replicas > 1`. |
| Cross-module writes not atomic | P2, P4, P5, P7 | By design. Orphans are harmless; the second operation's failure is logged and visible. |
| GraphQL depth/cost limits unprovable | P6, P8 | The test pins a framework built-in. Both limits can be deleted with the test still green. |
| ECB as sole FX provider | P2 | No second provider. 72h staleness bound, then 503. |
| Postgres saturates first | P8 | One core at 90 orders/s. The engine is not the bottleneck. |

## PRD §12 Acceptance Scenarios

| # | Scenario | Result |
|---|---|---|
| 1 | Partner order, exact composition, invoice, PDF, within latency | **Met** |
| 2 | Same external order replayed → identical order and invoice | **Met** |
| 3 | Same id, different amount → 409 | **Met** |
| 4 | Two partners, same ids → independent; cross-partner access denied | **Met** |
| 5 | Contact change → field-level audit; omission does not erase | **Met** |
| 6 | Customer without phone/email → validation error, no order | **Met** |
| 7 | No exact composition by deadline → explicit error, nothing partial | **Met** |
| 8 | Multi-currency conversion and override → exact minor units | **Met** |
| 9 | Website checkout → pro forma; admin acceptance → commercial invoice | **Met** |
| 10 | Repeated import does not duplicate; invalid stays staged | **Met** |
| 11 | Deactivated variant leaves PLP and partner selection | **Met** |
| 12 | Refunds → audit entries and credit notes; original invoice preserved | **Met** |
| 13 | Staff cannot manage credentials, FX, settings or refunds | **Met** |
| 14 | Bulk 100 items → per-item outcomes, successes committed | **Met** |
| 15 | Import preserves provenance, rejects invalid, enforces restrictions | **Partial** — keyword search half is a P7 gap |
| 16 | Load test records p50/p95/p99 and coverage across currencies | **Met** — recorded on the production compose stack |

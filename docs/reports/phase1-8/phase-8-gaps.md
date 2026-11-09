# Phase 8 — Ops, Smoke, Load & Docs: Gaps & Risks

**Gate:** Passed. 2,220 tests, 0 failures, 1 skipped. Release build clean.
Smoke: 14 passed, 0 failed on the production compose stack.

## Gaps / Skipped

| Gap | Status | Why |
|---|---|---|
| **Contract snapshots are in-code lists, not files** | By design | The plan called for `openapi-v1.json` and `schema.graphql` snapshot files with a `git status` diff check. A byte-level snapshot fails on reordered properties or reworded descriptions. Explicit arrays of routes and field names are used instead. |
| **No test asserts documented test counts** | By design | A test that agrees with a number in a document fails on every honest change and gets deleted within a month. Counts are written with their date and the producing command instead. |
| **Smoke script not in CI** | Not done | Needs Docker and ~10 minutes. It is a pre-release step documented in the runbook, not suitable for a fast CI gate. |
| **Phase 7's five gaps remain open** | Carried | XLSX reader, microdata extraction, Claude enrichment, image staging, and keyword search — all recorded in the P7 report. |

## Carried Risks

| Risk | Why it remains | Impact |
|---|---|---|
| **Release gate §16 has no declared load** | The measurement half is closed (ran on production compose stack). | 90 orders/s was picked to sit under the partner rate limit, not because anyone expects that volume. "p95 under load" still has an undefined subject — a commercial question. |
| **NuGet NU1900 suppressed, not resolved** | Artifactory returns 403 for the audit feed. `WarningsNotAsErrors` suppresses NU1900 in Release. | The audit guard itself works (`Newtonsoft.Json 9.0.1` → NU1903). Parked until the feed is fixed. |
| **Postgres saturates first under load** | During the load run: API ~138% CPU, Postgres ~97% (one core). | The composition engine is ~6 ms of a 24 ms request — not what to optimise. Nobody has measured where the database tops out. |
| **Production starts with an empty catalogue** | `--seed` refuses in Production (anti-goal #1 guard). | Partner orders return `EXACT_COMPOSITION_UNAVAILABLE` until a real import publishes products. The runbook says so. |

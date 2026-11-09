# Phase 7 — Sourcing & Import Pipeline: Gaps & Risks

**Gate:** Passed. 2,186 tests, 0 failures, 1 skipped. Release build clean.

Phase 7 carries the most gaps of any phase — five features not built, each named rather than
implied because each is a promise this phase does not keep.

## Gaps / Skipped

| Gap | Status | Why |
|---|---|---|
| **XLSX file reader** | Not built | CSV and JSON are supported. A spreadsheet upload is refused with `IMPORT_FILE_FORMAT_UNSUPPORTED` rather than misread. Adding it is one reader behind the existing `IImportFileReader` port — a bounded task, not architectural. |
| **Microdata extraction from crawled pages** | Not built | JSON-LD and OpenGraph are extracted. Microdata needs a full HTML parser; a half-working one silently misreads pages rather than skipping them. Pages with neither are reported as `NO_STRUCTURED_DATA` and queued for review. |
| **Claude enrichment of imported products** | Not built | The pipeline publishes what the source actually said and invents nothing. The enrichment port exists for later. Building it now adds cost and hallucination risk with no MVP payoff. |
| **Image staging into object storage** | Not built | `StagedRecord.ImageKeys` exists and is carried through publication, but nothing downloads source images into the staging bucket. Imported products have no pictures. The `StagingImageCleanup` recurring job is likewise absent. |
| **Keyword search half of PRD acceptance 15** | Not built | The crawler/provenance half is met. The Brave search integration has never run against the live service — without an API key the feature disables itself. Response mapping is pinned against a recorded payload shape. |
| **No GraphQL surface for imports** | By design scope | REST only. Every other module mirrors both. Adding it is straightforward but was not prioritized. |

## Carried Risks

| Risk | Why it remains | Impact |
|---|---|---|
| **Brave search integration never tested live** | Response mapping pinned against a recorded payload. | Catches a rename on this side, nothing on theirs. Without an API key the feature disables itself. Treat the first live run as the real test. |
| **Crawler has no integration test against a real HTTP server** | Decision logic (robots.txt, SSRF guard, extraction, politeness) is unit-tested. Mutation-checked. | What's untested is assembly: that the handler is on the client, that redirects are off. |
| **Politeness gate is per-worker, not per-deployment** | In-process gate. | Two workers can each honour a 1s delay and together hit a host twice a second. Acceptable at MVP scale (one worker). |
| **Published import is not transactional across modules** | Catalog commits, then Pricing, then the staged record is marked. | A price failure leaves a product with no price and a review item saying so — deliberate, because telling an admin "the import failed" when a product was created sends them looking wrong. |

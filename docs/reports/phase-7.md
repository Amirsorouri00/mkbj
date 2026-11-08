# Phase 7 — Sourcing

Administrators create import jobs that read uploaded files or crawl websites. Each listing is
staged once, keyed by a fingerprint of what it *is* rather than where it was found. The pipeline
normalizes it deterministically, matches it against the catalogue, validates it — rights
included — and publishes what is valid. Everything uncertain waits for a person.

Verified at `a7f62f5` on `feature/ecommerce-mvp`:

- `dotnet build EwaysVNext.slnx -c Release` — 0 errors, 0 warnings.
- `dotnet test EwaysVNext.slnx` — **2194 tests, 0 failures**, Docker up (1 skipped: an
  Identity assertion parked since phase 1). 181 of them are Sourcing's.

## What works

**Import twice, stage once.** The fingerprint takes identity from the strongest thing available
— the shop's own id scoped to its domain, then the canonical URL, then a GTIN, then brand and
name — and never from anything that merely says where the listing was found this time. A file
row's synthetic `urn:` carries the job id and row number, so fingerprinting it would make every
re-upload of one spreadsheet a fresh set of products. The unique index on the fingerprint is
what actually decides a race between two workers; the aggregate decides the ordinary case.

**Rights are the one part with legal weight.** Crawling, content use and image use are three
separate permissions because they are separately negotiated. A stored policy always overrules a
file attestation — the attestation is the word of whoever uploaded a spreadsheet; the policy is
the seller's own decision about a supplier. Denied content is an *Error*, because nobody in the
backoffice can grant a right the supplier withheld. Unknown rights are a *Review*, because
somebody must take responsibility before republishing. Images are dropped at publication when
they are denied — the last point at which they can be left behind, since they were downloaded
before anybody asked.

**Three severities, and the middle one is why a review queue exists.** An `Error` cannot be
published; a `Review` needs a person; a `Warning` is recorded and changes nothing. An uncertain
*match* is a review item as much as an uncertain field, because attaching a listing to the wrong
existing product rewrites something customers are already buying.

**A re-import never puts a withdrawn product back on sale**, and does not price one either —
pricing something nobody can buy would return it to the shop by the side door. A rejected
listing stays rejected: the administrator decided about the thing, not about one night's crawl.

**The crawler asks four questions before fetching a byte**: does the job allow this host, does
the seller's policy permit crawling it, does robots.txt allow the path, and has this host been
left alone long enough. Redirects are disabled on the crawl client, because a redirect to a
private address walks straight around the SSRF guard. The address check is on the resolved IP,
every resolved address, not the hostname.

**A step does a batch and returns.** That is what makes a job pausable: the decision to continue
is taken afresh between batches, where a cancellation can be noticed. The step sequence makes
at-least-once delivery safe — a redelivered step carries a number the job has moved past and
does nothing.

### Endpoints

| Route | Gate |
|---|---|
| `GET/POST /api/v1/admin/imports/jobs`, `/jobs/{id}` | `admin:staff` + `imports.manage` |
| `POST /jobs/{id}/{start,pause,resume,cancel}`, `PUT /jobs/{id}/mapping` | `admin:staff` + `imports.manage` |
| `GET /api/v1/admin/imports/records`, `/records/{id}` | `admin:staff` + `imports.manage` |
| `POST /records/{id}/{approve,merge,create-new,reject}` | `admin:staff` + `imports.manage` |
| `POST/PUT /api/v1/admin/imports/policies` | `admin:super` + `settings.manage` |

The dashboard's import section is composed by Admin, which now reads Sourcing's
`QueryApplication` alongside Sales' and Customer's.

## What is incomplete

Named rather than implied, because each is a promise this phase does not keep.

- **XLSX is not readable.** CSV and JSON are. A spreadsheet upload is refused with
  `IMPORT_FILE_FORMAT_UNSUPPORTED` rather than mis-read. Adding it is one reader behind the
  existing `IImportFileReader` port.
- **Microdata is not extracted.** JSON-LD and OpenGraph are. Reading microdata properly needs a
  full HTML parser, and a half-working one would silently mis-read pages rather than skip them;
  a page with neither is reported as `NO_STRUCTURED_DATA` and appears in the review queue.
- **Claude enrichment is not built.** The pipeline publishes what the source actually said and
  invents nothing, which is the behaviour the enrichment port was going to have to respect
  anyway.
- **Images are not staged into object storage.** `StagedRecord.ImageKeys` exists and is carried
  through publication, but nothing downloads a source image into the staging bucket yet, so
  imported products have no pictures. The `StagingImageCleanup` recurring job is likewise absent.
- **No GraphQL surface for imports.** REST only. Every other module mirrors both.
- **PRD acceptance 15 is not met in full.** Its crawler half is: provenance, restrictions and
  rights are enforced and tested. Its search half is not — see the risk below.

## Risks

- **The Brave search integration has never run against the live service.** The response mapping
  is pinned against a recorded payload shape, which catches a rename on this side and nothing on
  theirs. Without an API key the feature disables itself, so the exposure is limited to sellers
  who configure one. **Treat the first live run as the real test.**
- **The crawler has no integration test against a real HTTP server.** Its decision logic —
  robots.txt, address classification, extraction, politeness — is unit-tested thoroughly, and
  two mutation checks confirm the SSRF and ambiguity guards bite. What is untested is the
  assembly: that the handler is actually on the client, that redirects are actually off.
- **Politeness is per worker, not per deployment.** The gate is in process, so two workers can
  each honour a one-second delay and together hit a host twice a second.
- **A published import is not transactional across modules.** Catalog commits, then Pricing,
  then the staged record is marked. A price failure leaves a product with no price and a review
  item saying so — deliberate, because telling an administrator "the import failed" when a
  product was created sends them looking in the wrong place.
- **`RecordsPublished7d` counts by the record's last-updated time**, which for a published
  record is when it was published — approximate by a hair if a reviewer later edits one.

## Reproduction

```bash
docker compose -f build/compose/docker-compose.dev.yml up -d
dotnet build EwaysVNext.slnx -c Release
dotnet run --project src/Host/EwaysVNext.Host -- --migrate
dotnet test EwaysVNext.slnx
dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "*.FileImportEndToEndTests"
dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "*.CrawlerSafetyTests"
```

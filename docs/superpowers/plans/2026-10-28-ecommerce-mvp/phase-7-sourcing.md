# Phase 7 — Sourcing Implementation Plan

> Part of docs/superpowers/plans/2026-10-28-ecommerce-mvp.md — read its Global Constraints, Review Focus and Cross-phase contracts first.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the Sourcing module end to end. Admins create import jobs that crawl websites,
search the web (Brave) or read CSV/XLSX/JSON files. Each listing is staged once, keyed by a
fingerprint. The pipeline normalizes it deterministically, optionally enriches it with Claude
(only facts with a raw source path survive), matches it against the catalog, validates it
(rights included) and auto-publishes valid records through Catalog and Pricing ports. Ambiguous
and invalid records stay in a review queue. Admins work that queue over REST and GraphQL. Jobs
are resumable, pausable and cancellable, and a re-import never duplicates entities or
reactivates an admin-deactivated product.

**Spec sections:** §5.5 (all of it), §3.3 (MinIO buckets, `pg_trgm`), §3.6 (Sourcing rows),
§6.4 (audit), §7 (jobs: `ImportJobStep`, `StagingImageCleanup`, at-least-once ⇒ idempotent),
§8 (refresh trigger), §9.3 (admin imports), §13, §16 (rights policy is a go-live gate). PRD §4.2–4.4,
acceptance 10 and 15.

**Assumed from phases 0–3** (exact master-plan contracts): `IObjectStorage`, `StorageBuckets`,
`IJobQueue`, `IJobHandler<T>`, `JobContext`, `JobOutcome`, `AddJobHandler`, `AddRecurringJob`,
`JobTypes.ImportJobStep`/`StagingImageCleanup`, `IAuditTrail`, `AdminActor`, policies
`"admin:staff"`/`"admin:super"`, `ICatalogPublishingPort`, `PublishProductCommand`,
`PublishedProduct`, `IStorefrontRefreshTrigger`, `ISellableVariantPort`, `IPricePublishingPort`,
`PriceProvenance`, Catalog `Brand`/`Category`/`AttributeBlueprint` aliases and canonical keys,
`MeasurementUnit`, `PostgresFixture` (Testcontainers Postgres + MinIO, `CreateFactory()` like
today's `LocalDbFixture`, collection `PostgresSuite.Name`), `AdminJwtFactory.CreateStaffToken()`
/ `CreateSuperAdminToken()`, and `HexagonalBoundaryTests` (`ModuleNames` + exception `[Theory]`).

**Command shorthands used below:**
- `UT <Class>` = `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter-class "EwaysVNext.UnitTests.Sourcing.<Class>"`
- `IT <Class>` = `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter-class "EwaysVNext.IntegrationTests.Sourcing.<Class>"` (Docker running)
- `AT` = `dotnet test --project tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj`
- Every commit is `git commit -m "<subject>" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`.
  Only the subject is written in the steps.

**Configuration introduced (all optional; secrets only from env/.env):**

| Key | Default | Meaning |
|---|---|---|
| `Sourcing:Limits:MaxPages` / `MaxDepth` / `MaxRecords` | 200 / 2 / 1000 | Job defaults. Hard caps 5000 / 5 / 10000 |
| `Sourcing:BatchSize` | 50 | Records per pipeline step (spec §5.5) |
| `Sourcing:Crawler:UserAgent` | `EwaysVNextBot/1.0` | robots.txt product token is `EwaysVNextBot` |
| `Sourcing:Crawler:PolitenessDelayMs` | 1000 | Per-host minimum gap; robots `Crawl-delay` wins when larger |
| `Sourcing:Crawler:MaxHtmlBytes` / `MaxImageBytes` | 2 MiB / 10 MiB | Response caps |
| `Sourcing:Crawler:AllowPrivateNetworks` | `false` | SSRF guard; integration tests set `true` |
| `Sourcing:Files:MaxBytes` | 20 MiB | Upload cap |
| `Sourcing:StagingRetention` | `7.00:00:00` | Cleanup of staged images for terminal records |
| `Brave:ApiKey`, `Brave:BaseUrl` | —, `https://api.search.brave.com` | Search disabled without key |
| `Anthropic:ApiKey`, `Anthropic:BaseUrl`, `Anthropic:Model` | —, SDK default, `claude-sonnet-5-5` | Enrichment disabled without key |

---

## File Structure

```
src/Modules/Sourcing/
├── Application/EwaysVNext.Modules.Sourcing.Application.csproj   (refs: Domain, Outcomes, Persistence[IUnitOfWork], Core.Application, Audit, Core)
│   ├── DependencyInjection.cs                       AddSourcingApplication()
│   ├── Domain/
│   │   ├── SourcePolicies/  SourcePolicy.cs, SourceDomain.cs, RightsDecision.cs
│   │   ├── ImportJobs/      ImportJob.cs, ImportMode.cs, ImportJobStatus.cs, PublishPolicy.cs,
│   │   │                    ImportLimits.cs, ImportCounters.cs, ImportCheckpoint.cs,
│   │   │                    ImportColumnMapping.cs, ImportFileFormat.cs
│   │   ├── StagedRecords/   StagedRecord.cs, StagedRecordStatus.cs, Fingerprint.cs,
│   │   │                    MatchResult.cs, ValidationIssue.cs, ValidationCodes.cs
│   │   └── SourceListings/  SourceListing.cs, PriceObservation.cs
│   ├── Discovery/           RawListing.cs, ImportJobSpec.cs, ImportRowMapper.cs, ImportColumns.cs
│   ├── Normalization/       NormalizedProduct.cs, DeterministicNormalizer.cs, PriceParser.cs,
│   │                        GtinValidator.cs, NameKey.cs, UnitConverter.cs, JsonPointer.cs,
│   │                        EnrichmentMerger.cs
│   ├── Matching/            RecordMatcher.cs
│   ├── Validation/          RecordValidator.cs
│   ├── Pipeline/            ImportPipeline.cs, StagingService.cs, RecordProcessor.cs, RecordPublisher.cs
│   ├── Services/            ImportJobService.cs, ImportFileService.cs, StagedRecordReviewService.cs,
│   │                        SourcePolicyService.cs
│   └── Ports/
│       ├── Inbound/         IImportJobService.cs, IImportFileService.cs, IStagedRecordReviewService.cs,
│       │                    ISourcePolicyService.cs, IImportPipeline.cs, SourcingCommands.cs
│       └── Outbound/        ISourceAdapter.cs, ISearchProvider.cs, IEnrichmentProvider.cs,
│                            IImageStager.cs, IMediaPublisher.cs, IImportFileReader.cs,
│                            ICatalogLookup.cs, ICatalogMatcher.cs, ICatalogPublisher.cs,
│                            IPricePublisher.cs, IStorefrontRefresher.cs, IImportStepScheduler.cs,
│                            ISourcingPersistence.cs, IImportJobRepository.cs,
│                            IStagedRecordRepository.cs, ISourceListingRepository.cs,
│                            ISourcePolicyRepository.cs
├── Adapter/Outbound/  (refs: Application, Database, Storage, Jobs, Core.Application,
│   │                   Catalog.Application, Pricing.Application, AngleSharp, CsvHelper, ClosedXML, Anthropic)
│   ├── DependencyInjection.cs                       AddSourcingOutboundAdapters(IConfiguration)
│   ├── Persistence/        ImportJobRepository.cs, StagedRecordRepository.cs, SourceListingRepository.cs,
│   │                       SourcePolicyRepository.cs, SourcingPersistence.cs
│   ├── Crawling/           SafeHttpHandler.cs, UrlCanonicalizer.cs, RobotsTxt.cs, RobotsCache.cs,
│   │                       PolitenessGate.cs, StructuredDataExtractor.cs, CrawlCursor.cs,
│   │                       WebsiteCrawlerAdapter.cs, CrawlerOptions.cs
│   ├── Search/             BraveSearchProvider.cs, BraveOptions.cs, WebSearchSourceAdapter.cs
│   ├── Files/              CsvImportReader.cs, XlsxImportReader.cs, JsonImportReader.cs,
│   │                       ImportFileReader.cs, FileImportAdapter.cs, ImportTemplates.cs,
│   │                       Templates/import-row.schema.json (EmbeddedResource)
│   ├── Enrichment/         AnthropicOptions.cs, ClaudeEnrichmentProvider.cs, DisabledEnrichmentProvider.cs,
│   │                       EnrichmentPrompt.cs
│   ├── Media/              ObjectStorageImageStager.cs, CatalogMediaPublisher.cs
│   ├── Gateways/           CatalogLookupGateway.cs, CatalogMatcherGateway.cs, CatalogPublishingGateway.cs,
│   │                       PricePublishingGateway.cs, StorefrontRefreshGateway.cs
│   └── Jobs/               ImportJobStepPayload.cs, ImportJobStepHandler.cs, ImportStepScheduler.cs,
│                           StagingImageCleanupHandler.cs
├── Adapter/Inbound/Rest/  ImportJobEndpoints.cs, ImportFileEndpoints.cs, ImportTemplateEndpoints.cs,
│                          StagedRecordEndpoints.cs, SourcePolicyEndpoints.cs, SourcingRestDtos.cs,
│                          AdminActorFactory.cs, DependencyInjection.cs
├── Adapter/Inbound/GraphQL/ SourcingQueries.cs, SourcingMutations.cs, SourcingGraphQLInputs.cs,
│                            DependencyInjection.cs
├── QueryApplication/      ISourcingAdminQueries.cs, SourcingAdminQueries.cs, SourcingAdminDtos.cs,
│                          DependencyInjection.cs
└── Installer/ModuleInstaller.cs                     AddSourcingModule(IConfiguration)

src/Modules/Catalog/Application/Sourcing/          ICatalogImportLookupPort.cs, ICatalogMatchPort.cs, CatalogSourcingDtos.cs
src/Modules/Catalog/Adapter/Outbound/Sourcing/     CatalogImportLookup.cs, CatalogMatcher.cs
src/Database/Sourcing/Configurations/              ImportJobConfiguration.cs, StagedRecordConfiguration.cs,
                                                   SourceListingConfiguration.cs, SourcePolicyConfiguration.cs
src/Database/Migrations/<ts>_AddSourcing.cs        (+ <ts>_AddProductTitleTrigramIndex.cs)

tests/UnitTests/Sourcing/                           one file per test class named in the tasks
tests/UnitTests/Sourcing/Fixtures/                  product-jsonld.html, product-microdata.html, product-og.html,
                                                    no-structured-data.html, product-group.html, robots.txt,
                                                    anthropic-enrichment-response.json, brave-response.json, pixel.png
tests/UnitTests/Sourcing/FixtureHttpServer.cs       Kestrel on 127.0.0.1:0 serving Fixtures/ + request log
tests/IntegrationTests/Sourcing/                    SourcingPersistenceTests, CatalogMatcherTests,
                                                    ImportResumabilityTests, SourcingAdminApiTests,
                                                    SourcingGraphQLTests, ImportAcceptance10Tests,
                                                    ImportAcceptance15Tests, ReimportDeactivatedTests,
                                                    ImportTestDriver.cs
docs/reports/phase-7.md
```

**Decision: the test HTTP server is Kestrel, not WireMock.Net.** The test projects already
reference `Microsoft.AspNetCore.App`. A 60-line `FixtureHttpServer` on `127.0.0.1:0` serves
static fixtures, records every request path and host, and can serve per-host routes. That is all
the crawler, Brave and Anthropic tests need, and it adds no package.

**Decision: Claude enrichment uses the official `Anthropic` NuGet SDK, not raw HttpClient.**
- It gives typed `OutputConfig { Format = new JsonOutputFormat { Schema = ... } }` structured
  output and typed `StopReason`.
- It retries 408/409/429/5xx itself (2 retries).
- Hand-written wire DTOs would drift from the API.
- It stays testable because `AnthropicClient { ApiKey, BaseUrl }` can point at the local fixture
  server, so the recorded-response test exercises the real SDK deserialization path with no live
  call.
- The client is constructed **only** when `Anthropic:ApiKey` is set. Constructing it otherwise
  would let the SDK pick up an ambient `ANTHROPIC_API_KEY` or an `ant` profile from disk.

---

### Task 1: Scaffold the Sourcing module and its sanctioned references

**Files:**
- Create: the six `src/Modules/Sourcing/**/EwaysVNext.Modules.Sourcing.*.csproj`, `Installer/ModuleInstaller.cs`, each layer's `DependencyInjection.cs`
- Modify: `EwaysVNext.slnx`, `Directory.Packages.props`, `src/Host/EwaysVNext.Host/Program.cs`, `src/Host/EwaysVNext.Host/EwaysVNext.Host.csproj`, `tests/ArchitectureTests/HexagonalBoundaryTests.cs`, `tests/UnitTests/EwaysVNext.UnitTests.csproj`, `tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj`

**Interfaces:**
- Produces: `public static IServiceCollection AddSourcingModule(this IServiceCollection services, IConfiguration configuration)`
- Produces: arch exception rows `Sourcing.Adapter.Outbound → Catalog.Application` and `Sourcing.Adapter.Outbound → Pricing.Application` (spec §3.6)

- [ ] **Step 1: Write the failing architecture rows.** In `HexagonalBoundaryTests.cs`, add `"Sourcing"` to `ModuleNames` and add these rows to phase 0's `SanctionedReferences` (`TheoryData<string,string>` of `(consumerModule, providerModule)`):
```csharp
{ "Sourcing", "Catalog" },
{ "Sourcing", "Pricing" },
```
Add this fact:
```csharp
[Fact]
public void Only_Sourcing_Outbound_May_Reference_Catalog_Or_Pricing_From_Sourcing()
{
    foreach (var asm in SourcingAssemblies().Where(a => !a.GetName().Name!.EndsWith(".Adapter.Outbound")))
        Types.InAssembly(asm).Should()
            .NotHaveDependencyOnAny("EwaysVNext.Modules.Catalog", "EwaysVNext.Modules.Pricing")
            .GetResult().IsSuccessful.Should().BeTrue(asm.GetName().Name);
}
```
- [ ] **Step 2: Run** `AT`. Expected: FAIL (the Sourcing assemblies do not exist, so the build fails).
- [ ] **Step 3: Scaffold the projects.** Run `./New-EwaysModule.ps1 -ModuleName Sourcing -SkipReferences`. The script's `$bb` table points at building blocks that no longer exist, so wire the references by hand as listed in File Structure. Then:
  - Add `Adapter/Inbound/GraphQL` (same csproj shape as Catalog's GraphQL project).
  - Add package versions to `Directory.Packages.props`: `AngleSharp 1.3.0`, `CsvHelper 33.1.0`, `ClosedXML 0.105.0`, `Npgsql.EntityFrameworkCore.PostgreSQL.Trigrams` at the same version as the Npgsql EF provider, and `Anthropic` at the version printed by `dotnet package search Anthropic --exact-match` (latest stable).
  - Write `AddSourcingModule` to call `AddSourcingApplication()`, `AddSourcingOutboundAdapters(configuration)`, `AddSourcingQueryApplication()`, `AddSourcingRestAdapter()`, `AddSourcingGraphQL()`, then `.AddPersistenceInterceptors().AddUserProfileService().AddUnitOfWork<EwaysVNextDbContext>().AddEvents(typeof(SourcingApplicationMarker).Assembly)`, the same as Catalog.
  - Call it from `Program.cs`.
- [ ] **Step 4: Run** `AT`. Expected: PASS.
- [ ] **Step 5: Commit.** Subject: `feat(sourcing): scaffold Sourcing module and sanctioned Catalog/Pricing references`

---

### Task 2: `SourcePolicy` aggregate and rights decision

**Files:**
- Create: `Application/Domain/SourcePolicies/{SourcePolicy,SourceDomain,RightsDecision}.cs`
- Test: `tests/UnitTests/Sourcing/SourcePolicyTests.cs`

**Interfaces:**
- Produces:
```csharp
public static class SourceDomain { public static string Normalize(string hostOrUrl); } // lower-case IDN→punycode host, no port, strips one leading "www."
public sealed class SourcePolicy : AggregateRoot<long> {
  string Domain; bool CrawlAllowed; bool ContentUseAllowed; bool ImageUseAllowed; string? Notes; DateTimeOffset CreatedAt; DateTimeOffset? UpdatedAt;
  public static SourcePolicy Create(long id, string domain, bool crawlAllowed, bool contentUseAllowed, bool imageUseAllowed, string? notes, DateTimeOffset now);
  public void Update(bool crawlAllowed, bool contentUseAllowed, bool imageUseAllowed, string? notes, DateTimeOffset now);
}
public enum RightsStatus { Allowed, Unknown, ContentDenied, ImagesDenied }
public sealed record RightsDecision(RightsStatus Status, string UsageRightsLabel) {
  public static RightsDecision For(SourcePolicy? policy, bool fileRightsAttested, long jobId);
}
```

- [ ] **Step 1: Write the failing tests.**
```csharp
[Theory]
[InlineData("https://WWW.Shop.Example.com:8443/p/1", "shop.example.com")]
[InlineData("www.example.com", "example.com")]
[InlineData("www.www.example.com", "www.example.com")]
public void Normalize_Lowercases_Strips_Port_And_One_Www(string input, string expected) =>
    SourceDomain.Normalize(input).Should().Be(expected);

[Fact] public void No_Policy_Is_RightsUnknown() =>
    RightsDecision.For(null, fileRightsAttested: false, jobId: 7).Status.Should().Be(RightsStatus.Unknown);
[Fact] public void No_Policy_But_File_Attested_Is_Allowed() =>
    RightsDecision.For(null, true, 7).Should().Be(new RightsDecision(RightsStatus.Allowed, "attested:job:7"));
[Fact] public void Content_Denied_Wins_Over_Images() =>
    RightsDecision.For(Policy(content: false, images: false), false, 7).Status.Should().Be(RightsStatus.ContentDenied);
[Fact] public void Images_Denied() =>
    RightsDecision.For(Policy(content: true, images: false), false, 7).Status.Should().Be(RightsStatus.ImagesDenied);
[Fact] public void Both_Allowed_Labels_The_Policy() =>
    RightsDecision.For(Policy(content: true, images: true), false, 7).UsageRightsLabel.Should().Be("policy:shop.example.com");
[Fact] public void Policy_Overrides_File_Attestation() =>
    RightsDecision.For(Policy(content: false, images: true), fileRightsAttested: true, 7).Status.Should().Be(RightsStatus.ContentDenied);
[Fact] public void Create_Rejects_Blank_Domain() =>
    FluentActions.Invoking(() => SourcePolicy.Create(1, " ", true, true, true, null, TestTime.Now))
        .Should().Throw<DomainException>().Which.Code.Should().Be("SOURCE_POLICY_DOMAIN_REQUIRED");
```
- [ ] **Step 2: Run** `UT SourcePolicyTests`. Expected: FAIL (compile errors).
- [ ] **Step 3: Implement** the signatures above. `Create` stores `SourceDomain.Normalize(domain)`. `Notes` can be at most 2000 characters (`SOURCE_POLICY_NOTES_TOO_LONG`).
- [ ] **Step 4: Run** `UT SourcePolicyTests`. Expected: PASS.
- [ ] **Step 5: Commit.** Subject: `feat(sourcing): SourcePolicy aggregate and rights decision`

---

### Task 3: `ImportJob` aggregate (modes, limits, state machine, checkpoint)

**Files:**
- Create: `Application/Domain/ImportJobs/*.cs`
- Test: `tests/UnitTests/Sourcing/ImportJobTests.cs`

**Interfaces:**
- Produces:
```csharp
public enum ImportMode { Crawl, WebSearch, Combined, File }
public enum PublishPolicy { AutoPublishValidated = 0, ManualReview = 1 }          // default AutoPublishValidated
public enum ImportJobStatus { Draft, Queued, Running, Paused, Completed, Failed, Cancelled }
public enum ImportFileFormat { Csv, Xlsx, Json }
public enum ImportPhase { Discover, Process, Done }
public sealed record ImportLimits(int MaxPages, int MaxDepth, int MaxRecords) {
  public static ImportLimits Default { get; } = new(200, 2, 1000);
  public void Validate(); // 1..5000, 0..5, 1..10000 else DomainException IMPORT_JOB_LIMIT_INVALID
}
public sealed record ImportCounters(int Discovered, int Extracted, int Valid, int Invalid, int Duplicate, int Published, int Review);
public sealed record ImportCheckpoint(ImportPhase Phase, string? AdapterCursor, long ProcessedThroughRecordId) {
  public static ImportCheckpoint Start { get; } = new(ImportPhase.Discover, null, 0);
}
public sealed record ImportColumnMapping(IReadOnlyDictionary<string, string> SourceToField); // values: ImportColumns.All member, "attr:<key>", or "ignore"
public sealed record CreateImportJobSpec(ImportMode Mode, IReadOnlyList<string> Sources, IReadOnlyList<string> Queries,
  long? TargetCategoryId, IReadOnlyList<string> IncludeDomains, IReadOnlyList<string> ExcludeDomains,
  ImportLimits Limits, PublishPolicy PublishPolicy, bool FileRightsAttested);
public sealed class ImportJob : AggregateRoot<long> {
  // + Mode, Sources, Queries, TargetCategoryId, IncludeDomains, ExcludeDomains, Limits, PublishPolicy, FileRightsAttested,
  //   FileObjectKey?, FileFormat?, Mapping?, Status, Counters, Checkpoint, StepSequence (long),
  //   ErrorCode?, ErrorMessage?, CreatedBy (Guid), CreatedAt, StartedAt?, FinishedAt?, UpdatedAt?
  public static ImportJob Create(long id, CreateImportJobSpec spec, Guid createdBy, DateTimeOffset now); // File → Draft, else Queued
  public void AttachFile(string objectKey, ImportFileFormat format, DateTimeOffset now);  // Draft only
  public void SetMapping(ImportColumnMapping mapping, DateTimeOffset now);                // Draft only
  public void Start(DateTimeOffset now);                 // Draft → Queued; File needs file + mapping
  public void MarkRunning(DateTimeOffset now);           // Queued|Running → Running (idempotent), sets StartedAt once
  public void Pause(DateTimeOffset now);                 // Queued|Running → Paused
  public void Resume(DateTimeOffset now);                // Paused → Queued
  public void Cancel(DateTimeOffset now);                // Draft|Queued|Running|Paused → Cancelled
  public void Complete(DateTimeOffset now);              // Running → Completed
  public void Fail(string code, string message, DateTimeOffset now); // non-terminal → Failed
  public long AdvanceCheckpoint(ImportCheckpoint next, ImportCounters counters, DateTimeOffset now); // Running only; returns ++StepSequence
  public long BumpStepSequence(DateTimeOffset now);      // used by Resume/PublishValid scheduling
  public bool AcceptsWork { get; }                       // Status is Queued or Running
  public bool IsTerminal { get; }                        // Completed|Failed|Cancelled
}
```

- [ ] **Step 1: Write the failing tests.**
```csharp
[Fact] public void Default_Publish_Policy_Is_AutoPublishValidated() => default(PublishPolicy).Should().Be(PublishPolicy.AutoPublishValidated);
[Fact] public void Crawl_Requires_A_Source() => Creating(Spec(ImportMode.Crawl, sources: [])).Should().Throw<DomainException>().Which.Code.Should().Be("IMPORT_JOB_SOURCES_REQUIRED");
[Fact] public void WebSearch_Requires_A_Query() => Creating(Spec(ImportMode.WebSearch, queries: [])).Should().Throw<DomainException>().Which.Code.Should().Be("IMPORT_JOB_QUERIES_REQUIRED");
[Fact] public void Combined_Requires_Both() => Creating(Spec(ImportMode.Combined, sources: ["https://a.example/"], queries: [])).Should().Throw<DomainException>();
[Fact] public void Source_Must_Be_Absolute_Http() => Creating(Spec(ImportMode.Crawl, sources: ["ftp://a.example/"])).Should().Throw<DomainException>().Which.Code.Should().Be("IMPORT_JOB_SOURCE_INVALID");
[Theory, InlineData(0, 2, 10), InlineData(5001, 2, 10), InlineData(10, 6, 10), InlineData(10, 2, 10001)]
public void Limits_Are_Bounded(int pages, int depth, int records) =>
    Creating(Spec(ImportMode.Crawl, limits: new(pages, depth, records))).Should().Throw<DomainException>().Which.Code.Should().Be("IMPORT_JOB_LIMIT_INVALID");
[Fact] public void File_Job_Starts_As_Draft_And_Needs_File_And_Mapping()
{
    var job = ImportJob.Create(1, Spec(ImportMode.File), Guid.NewGuid(), Now);
    job.Status.Should().Be(ImportJobStatus.Draft);
    FluentActions.Invoking(() => job.Start(Now)).Should().Throw<DomainException>().Which.Code.Should().Be("IMPORT_JOB_FILE_REQUIRED");
    job.AttachFile("uploads/1/a.csv", ImportFileFormat.Csv, Now); job.SetMapping(Mapping(), Now); job.Start(Now);
    job.Status.Should().Be(ImportJobStatus.Queued);
}
[Fact] public void Cancelled_Job_Accepts_No_Work_And_Cannot_Resume()
{
    var job = Running(); job.Cancel(Now);
    job.AcceptsWork.Should().BeFalse();
    FluentActions.Invoking(() => job.Resume(Now)).Should().Throw<DomainException>().Which.Code.Should().Be("IMPORT_JOB_TRANSITION_INVALID");
    FluentActions.Invoking(() => job.AdvanceCheckpoint(ImportCheckpoint.Start, Zero, Now)).Should().Throw<DomainException>();
}
[Fact] public void Pause_Then_Resume_Requeues() { var j = Running(); j.Pause(Now); j.AcceptsWork.Should().BeFalse(); j.Resume(Now); j.Status.Should().Be(ImportJobStatus.Queued); }
[Fact] public void AdvanceCheckpoint_Increments_StepSequence_And_Stores_Counters()
{
    var j = Running();
    j.AdvanceCheckpoint(new(ImportPhase.Process, "c1", 99), new(10, 9, 0, 0, 1, 0, 0), Now).Should().Be(1);
    j.Checkpoint.Should().Be(new ImportCheckpoint(ImportPhase.Process, "c1", 99));
    j.Counters.Discovered.Should().Be(10);
}
[Fact] public void Completed_Is_Terminal_And_Sets_FinishedAt() { var j = Running(); j.Complete(Now); j.IsTerminal.Should().BeTrue(); j.FinishedAt.Should().Be(Now); }
```
- [ ] **Step 2: Run** `UT ImportJobTests`. Expected: FAIL (compile).
- [ ] **Step 3: Implement** the aggregate. Domains in `IncludeDomains`/`ExcludeDomains` go through `SourceDomain.Normalize`. Sources: at most 50; queries: at most 10, each 1–200 characters. Every illegal transition throws `IMPORT_JOB_TRANSITION_INVALID`.
- [ ] **Step 4: Run** `UT ImportJobTests`. Expected: PASS.
- [ ] **Step 5: Commit.** Subject: `feat(sourcing): ImportJob aggregate with limits, state machine and checkpoint`

---

### Task 4: `RawListing`, `Fingerprint` and the `StagedRecord` aggregate

**Files:**
- Create: `Application/Discovery/{RawListing,ImportJobSpec}.cs`, `Application/Domain/StagedRecords/*.cs`
- Test: `tests/UnitTests/Sourcing/StagedRecordTests.cs`, `tests/UnitTests/Sourcing/FingerprintTests.cs`

**Interfaces:**
- Produces (the `RawListing` definition every adapter must honour):
```csharp
namespace EwaysVNext.Modules.Sourcing.Application.Discovery;
public enum ExtractionMethod { JsonLd, Microdata, OpenGraph, Csv, Xlsx, Json, None }
/// <summary>One product-shaped record as found at the source, before any normalization.</summary>
public sealed record RawListing(
    string SourceUrl,                 // absolute http(s) URL; file rows: "urn:eways:import:{jobId}:row:{n}" (provenance only, never fingerprinted)
    string? SourceDomain,             // SourceDomain.Normalize(host) for web; file: normalized source_domain cell or null
    string? SourceId,                 // schema.org sku/productID; file: source_id cell
    DateTimeOffset CollectedAt,
    ExtractionMethod Method,
    JsonObject Document,              // schema.org Product/ProductGroup-shaped; every provenance path is an RFC 6901 pointer into it
    JsonObject? Original,             // untouched source node or row (audit only)
    IReadOnlyList<string> ImageUrls,  // absolute, document order, de-duplicated
    string? ExtractionError,          // null or a ValidationCodes value, e.g. "NO_STRUCTURED_DATA"
    string Checkpoint);               // opaque adapter cursor positioned AFTER this listing
public sealed record ImportJobSpec(long JobId, ImportMode Mode, IReadOnlyList<string> Sources, IReadOnlyList<string> Queries,
  IReadOnlyList<string> IncludeDomains, IReadOnlyList<string> ExcludeDomains, ImportLimits Limits, long? TargetCategoryId,
  bool FileRightsAttested, string? FileObjectKey, ImportFileFormat? FileFormat, ImportColumnMapping? Mapping);

public sealed record Fingerprint(string Value) { public static Fingerprint For(RawListing listing); } // 64 lower-hex SHA-256
public enum StagedRecordStatus { Staged, Valid, Invalid, NeedsReview, Published, Rejected }
public enum IssueSeverity { Error, Review, Warning }
public sealed record ValidationIssue(string Code, IssueSeverity Severity, string? Detail = null);
public enum MatchKind { New, ExactMatch, Fuzzy, Ambiguous }
public sealed record FuzzyCandidateRef(long ProductId, string Title, double Similarity);
public sealed record MatchResult(MatchKind Kind, long? ProductId, string? MatchedBy, IReadOnlyList<FuzzyCandidateRef> Candidates) {
  public static MatchResult New { get; } = new(MatchKind.New, null, null, []);
}
public enum RestageOutcome { NewInThisJob, AlreadyInThisJob, Duplicate, KeptRejected }
public sealed class StagedRecord : AggregateRoot<long> {
  // Fingerprint, FirstJobId, LastJobId, SourceUrl, SourceDomain?, RawJson (jsonb), RawHash, NormalizedJson? (jsonb),
  // ImageKeys (text[]), Match (jsonb), Issues (jsonb), Status, ReviewedBy?, ReviewNote?, CategoryOverrideId?,
  // RightsApprovedBy?, PublishedProductId?, PublishedVariantIds (bigint[]), PublishSkippedDeactivated, SourceListingId?,
  // StagingCleanedAt?, CreatedAt, UpdatedAt?
  public static StagedRecord Stage(long id, long jobId, Fingerprint fingerprint, RawListing listing, string rawJson, DateTimeOffset now);
  public RestageOutcome Restage(long jobId, RawListing listing, string rawJson, DateTimeOffset now);
  public void RecordProcessing(string normalizedJson, IReadOnlyList<string> imageKeys, MatchResult match, IReadOnlyList<ValidationIssue> issues, DateTimeOffset now);
  public void MarkPublished(long productId, IReadOnlyList<long> variantIds, bool skippedBecauseDeactivated, long sourceListingId, DateTimeOffset now);
  public void MarkPublishFailed(string errorCode, DateTimeOffset now);
  public void Approve(Guid reviewer, long? categoryOverrideId, DateTimeOffset now);
  public void MergeInto(Guid reviewer, long productId, DateTimeOffset now);
  public void ChooseCreateNew(Guid reviewer, DateTimeOffset now);
  public void Reject(Guid reviewer, string reason, DateTimeOffset now);
  public static StagedRecordStatus DeriveStatus(MatchResult match, IReadOnlyList<ValidationIssue> issues);
}
```
- Fingerprint algorithm (body specified because the signature does not determine it). Build the identity string, NFKC-normalize it, then hash: `"web|{domain}|{sourceId}"` when both the domain and `SourceId` are present. Otherwise, for `http(s)` URLs, use `"url|{UrlCanonicalizer form: lower-case scheme+host, no default port, no fragment, utm_* removed, query keys sorted}"`. For file rows with no source id, take the first available of `"gtin|{digits}"` (any of gtin/gtin8/12/13/14 in the Document), `"mpn|{NameKey(brand)}|{mpn}"`, or `"title|{NameKey(brand)}|{NameKey(name)}"`. Hash with `SHA256.HashData(UTF8)` and render as lower hex. The canonicalizer lives in Application (`Discovery/UrlForm.cs`, pure); the crawler reuses it.
- `DeriveStatus`: any `Error` → `Invalid`. Otherwise, any `Review`, or a `Fuzzy`/`Ambiguous` match → `NeedsReview`. Otherwise → `Valid`. `Warning` issues never affect status.

- [ ] **Step 1: Write the failing tests.**
```csharp
// FingerprintTests
[Fact] public void Same_Domain_And_SourceId_Same_Fingerprint_Across_Urls() =>
    Fingerprint.For(Web("https://a.example/p?id=1", "a.example", "SKU-1")).Should().Be(Fingerprint.For(Web("https://a.example/other", "a.example", "SKU-1")));
[Fact] public void Url_Canonicalization_Ignores_Fragment_Utm_And_Query_Order() =>
    Fingerprint.For(Web("https://A.example:443/p?b=2&a=1&utm_source=x#top", "a.example", null))
      .Should().Be(Fingerprint.For(Web("https://a.example/p?a=1&b=2", "a.example", null)));
[Fact] public void File_Rows_Never_Use_The_Urn() =>
    Fingerprint.For(FileRow(jobId: 1, row: 5, gtin: "4006381333931")).Should().Be(Fingerprint.For(FileRow(jobId: 2, row: 9, gtin: "4006381333931")));
[Fact] public void Value_Is_64_Lower_Hex() => Fingerprint.For(Web("https://a.example/p", "a.example", null)).Value.Should().MatchRegex("^[0-9a-f]{64}$");

// StagedRecordTests
[Fact] public void Restage_In_Same_Job_Is_Not_A_Duplicate() => Staged(jobId: 1).Restage(1, L(), "{}", Now).Should().Be(RestageOutcome.AlreadyInThisJob);
[Fact] public void Restage_From_Another_Job_Is_Duplicate_And_Resets_To_Staged()
{
    var r = Published(jobId: 1);
    r.Restage(2, L(), "{\"name\":\"v2\"}", Now).Should().Be(RestageOutcome.Duplicate);
    r.Status.Should().Be(StagedRecordStatus.Staged); r.LastJobId.Should().Be(2); r.FirstJobId.Should().Be(1);
}
[Fact] public void Rejected_Record_Stays_Rejected_On_Restage() { var r = Rejected(); r.Restage(2, L(), "{}", Now).Should().Be(RestageOutcome.KeptRejected); r.Status.Should().Be(StagedRecordStatus.Rejected); }
[Fact] public void Error_Issue_Means_Invalid() => StagedRecord.DeriveStatus(MatchResult.New, [new("PRICE_INVALID", IssueSeverity.Error)]).Should().Be(StagedRecordStatus.Invalid);
[Fact] public void Fuzzy_Match_Means_NeedsReview() => StagedRecord.DeriveStatus(new(MatchKind.Fuzzy, null, null, [new(5, "t", 0.7)]), []).Should().Be(StagedRecordStatus.NeedsReview);
[Fact] public void Warning_Only_Is_Valid() => StagedRecord.DeriveStatus(MatchResult.New, [new("IDENTIFIER_INVALID", IssueSeverity.Warning)]).Should().Be(StagedRecordStatus.Valid);
[Fact] public void Approve_Refuses_Records_With_Errors() => FluentActions.Invoking(() => Invalid().Approve(Guid.NewGuid(), null, Now)).Should().Throw<DomainException>().Which.Code.Should().Be("STAGED_RECORD_HAS_ERRORS");
[Fact] public void Approve_Clears_Review_Issues_And_Records_Reviewer()
{
    var who = Guid.NewGuid(); var r = NeedsReview("RIGHTS_UNKNOWN");
    r.Approve(who, null, Now);
    r.Status.Should().Be(StagedRecordStatus.Valid); r.ReviewedBy.Should().Be(who); r.RightsApprovedBy.Should().Be(who);
}
[Fact] public void MergeInto_Sets_Exact_Match_By_Admin() { var r = NeedsReview("FUZZY_MATCH"); r.MergeInto(Guid.NewGuid(), 42, Now); r.Match.Should().Be(new MatchResult(MatchKind.ExactMatch, 42, "admin-merge", [])); }
[Fact] public void Published_Record_Cannot_Be_Rejected() => FluentActions.Invoking(() => Published(1).Reject(Guid.NewGuid(), "x", Now)).Should().Throw<DomainException>();
[Fact] public void MarkPublishFailed_Routes_To_Review() { var r = Valid(); r.MarkPublishFailed("CURRENCY_NOT_ENABLED", Now); r.Status.Should().Be(StagedRecordStatus.NeedsReview); r.Issues.Should().Contain(new ValidationIssue("PUBLISH_FAILED", IssueSeverity.Review, "CURRENCY_NOT_ENABLED")); }
```
- [ ] **Step 2: Run** `UT FingerprintTests` and `UT StagedRecordTests`. Expected: FAIL (compile).
- [ ] **Step 3: Implement.**
  - `Approve` sets `RightsApprovedBy` only when a `RIGHTS_UNKNOWN` issue existed. It removes every `Review` issue, and keeps `Warning` issues.
  - `ChooseCreateNew` sets `Match = MatchResult.New`.
  - Only `Valid` records may be `MarkPublished`. The review service calls `Approve`, `MergeInto` or `ChooseCreateNew` first, so the record is `Valid` by then.
- [ ] **Step 4: Run** both. Expected: PASS.
- [ ] **Step 5: Commit.** Subject: `feat(sourcing): RawListing contract, fingerprint and StagedRecord aggregate`

---

### Task 5: `SourceListing` aggregate with `PriceObservation`

**Files:**
- Create: `Application/Domain/SourceListings/{SourceListing,PriceObservation}.cs`
- Test: `tests/UnitTests/Sourcing/SourceListingTests.cs`

**Interfaces:**
- Produces:
```csharp
public sealed class SourceListing : AggregateRoot<long> {
  // Url (unique), Domain, SourceId? (unique with Domain), CollectedAt, Title, ImageUrls (text[]), Rights (string label), StagedRecordId
  public static SourceListing Create(long id, string url, string domain, string? sourceId, string title, IReadOnlyList<string> imageUrls, string rights, long stagedRecordId, DateTimeOffset collectedAt);
  public void Refresh(string title, IReadOnlyList<string> imageUrls, string rights, DateTimeOffset collectedAt);
  public PriceObservation Observe(long observationId, string variantKey, long amountMinor, string currency, bool? taxIncluded, DateTimeOffset collectedAt); // idempotent per (variantKey, collectedAt)
  public IReadOnlyCollection<PriceObservation> Observations { get; }
}
public sealed class PriceObservation : Entity<long> { long SourceListingId; string VariantKey; long AmountMinor; string Currency; bool? TaxIncluded; DateTimeOffset CollectedAt; }
```
`VariantKey` is the variant signature hash (Task 12). File rows use `Url = "urn:eways:import:{fingerprint}"` and `Domain = source_domain ?? "file"`.

- [ ] **Step 1: Write the failing tests.**
```csharp
[Fact] public void Observe_Twice_Same_Key_And_Time_Returns_The_Same_Observation()
{
    var l = Listing(); var a = l.Observe(10, "sig", 1999, "SGD", true, Now); var b = l.Observe(11, "sig", 1999, "SGD", true, Now);
    b.Should().BeSameAs(a); l.Observations.Should().HaveCount(1);
}
[Fact] public void Observe_Later_Appends() { var l = Listing(); l.Observe(10, "sig", 1999, "SGD", null, Now); l.Observe(11, "sig", 1899, "SGD", null, Now.AddDays(1)); l.Observations.Should().HaveCount(2); }
[Fact] public void Observe_Rejects_Non_Positive_Amount() => FluentActions.Invoking(() => Listing().Observe(10, "s", 0, "SGD", null, Now)).Should().Throw<DomainException>().Which.Code.Should().Be("PRICE_OBSERVATION_AMOUNT_INVALID");
[Fact] public void Observe_Rejects_Lowercase_Currency() => FluentActions.Invoking(() => Listing().Observe(10, "s", 1, "sgd", null, Now)).Should().Throw<DomainException>();
[Fact] public void Create_Normalizes_Domain() => SourceListing.Create(1, "https://x/p", "WWW.Shop.Example", null, "t", [], "policy:shop.example", 1, Now).Domain.Should().Be("shop.example");
```
- [ ] **Step 2: Run** `UT SourceListingTests`. Expected: FAIL.
- [ ] **Step 3: Implement.** The currency must match `^[A-Z]{3}$`.
- [ ] **Step 4: Run** `UT SourceListingTests`. Expected: PASS.
- [ ] **Step 5: Commit.** Subject: `feat(sourcing): SourceListing and PriceObservation`

---

### Task 6: Persistence: configurations, repositories, migration, DB constraints

**Files:**
- Create: `src/Database/Sourcing/Configurations/*.cs`, `Adapter/Outbound/Persistence/*.cs`, `Application/Ports/Outbound/{IImportJobRepository,IStagedRecordRepository,ISourceListingRepository,ISourcePolicyRepository,ISourcingPersistence}.cs`
- Modify: `src/Database/EwaysVNextDbContext.cs` (four `DbSet`s), `src/Database/PersistenceDependencyInjection.cs` (`o.UseTrigrams()` on the Npgsql options builder)
- Create: migration `dotnet ef migrations add AddSourcing -p src/Database -s src/Host/EwaysVNext.Host`
- Test: `tests/IntegrationTests/Sourcing/SourcingPersistenceTests.cs`

**Interfaces:**
- Produces:
```csharp
public interface IImportJobRepository {
  Task<ImportJob?> GetAsync(long id, CancellationToken ct = default);
  Task<ImportJobStatus?> GetStatusAsync(long id, CancellationToken ct = default);   // AsNoTracking, fresh read
  Task AddAsync(ImportJob job, CancellationToken ct = default);
}
public interface IStagedRecordRepository {
  Task<IReadOnlyDictionary<string, StagedRecord>> GetByFingerprintsAsync(IReadOnlyCollection<string> fingerprints, CancellationToken ct = default);
  Task<StagedRecord?> GetAsync(long id, CancellationToken ct = default);
  Task<IReadOnlyList<StagedRecord>> GetNextForJobAsync(long jobId, StagedRecordStatus status, long afterId, int take, CancellationToken ct = default); // ORDER BY id
  Task<IReadOnlyList<StagedRecord>> GetManyAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default);
  Task<ImportCounters> CountForJobAsync(long jobId, ImportCounters discoveryCounters, CancellationToken ct = default); // Valid/Invalid/Review/Published recounted from rows with last_job_id = jobId
  Task AddAsync(StagedRecord record, CancellationToken ct = default);
}
public interface ISourceListingRepository {
  Task<SourceListing?> FindAsync(string url, string domain, string? sourceId, CancellationToken ct = default); // url OR (domain, source_id)
  Task AddAsync(SourceListing listing, CancellationToken ct = default);
}
public interface ISourcePolicyRepository {
  Task<SourcePolicy?> FindByDomainAsync(string normalizedDomain, CancellationToken ct = default);
  Task<IReadOnlyDictionary<string, SourcePolicy>> FindByDomainsAsync(IReadOnlyCollection<string> normalizedDomains, CancellationToken ct = default);
  Task<SourcePolicy?> GetAsync(long id, CancellationToken ct = default);
  Task AddAsync(SourcePolicy policy, CancellationToken ct = default);
  void Remove(SourcePolicy policy);
}
public interface ISourcingPersistence {           // translates EF/Npgsql failures so Application stays EF-free
  Task<Result> SaveAsync(CancellationToken ct = default); // 23505 → Error("SOURCING_UNIQUE_CONFLICT", …, 409); DbUpdateConcurrencyException → Error.ConcurrencyConflict
  void DiscardChanges();                                 // ChangeTracker.Clear()
}
```
DB shape (schema `sourcing`, snake_case):
- Every root uses `ValueGeneratedNever()` and `xmin` as its concurrency token.
- `staged_records`: `UNIQUE(fingerprint)`; `raw`, `normalized`, `match` and `issues` are `jsonb`; `image_keys text[]`; index `(last_job_id, status, id)`.
- `source_listings`: `UNIQUE(url)`, `UNIQUE(domain, source_id) WHERE source_id IS NOT NULL`.
- `price_observations`: `UNIQUE(source_listing_id, variant_key, collected_at)`.
- `source_policies`: `UNIQUE(domain)`.
- `import_jobs`: `checkpoint jsonb`; counters as 7 `int` columns (owned type); `sources`, `queries`, `include_domains` and `exclude_domains` as `text[]`; `mapping jsonb`; check constraint `ck_import_jobs_status` over the enum names.

- [ ] **Step 1: Write the failing tests** (`[Collection(PostgresSuite.Name)]`).
```csharp
[Fact] public async Task Fingerprint_Is_Unique_Across_Jobs()
{
    await SaveAsync(StagedRecord.Stage(Id(), 1, new Fingerprint(Fp), L(), "{}", Now));
    var act = () => SaveAsync(StagedRecord.Stage(Id(), 2, new Fingerprint(Fp), L(), "{}", Now));
    (await act.Should().ThrowAsync<DbUpdateException>()).Which.InnerException.Should().BeOfType<PostgresException>()
        .Which.SqlState.Should().Be("23505");
}
[Fact] public async Task SourcingPersistence_Maps_Unique_Violation_To_Conflict_Result()
{
    // same duplicate through ISourcingPersistence.SaveAsync()
    (await persistence.SaveAsync()).Error!.Code.Should().Be("SOURCING_UNIQUE_CONFLICT");
}
[Fact] public async Task Listing_Url_And_Domain_SourceId_Are_Unique() { /* two inserts each → 23505 */ }
[Fact] public async Task Jsonb_Roundtrips_Match_Issues_And_Checkpoint()
{
    // stage, RecordProcessing with Fuzzy match + 2 issues, AdvanceCheckpoint; reload in new scope
    reloaded.Match.Candidates.Should().ContainSingle().Which.Similarity.Should().Be(0.71);
    job.Checkpoint.Should().Be(new ImportCheckpoint(ImportPhase.Process, "{\"q\":[]}", 42));
}
[Fact] public async Task CountForJob_Recounts_Statuses_Of_Last_Job_Only() { /* 2 Valid, 1 Invalid in job 5; 1 Valid moved to job 6 → Valid=1 */ }
```
- [ ] **Step 2: Run** `IT SourcingPersistenceTests`. Expected: FAIL (compile, then no tables).
- [ ] **Step 3: Implement** the configurations, repositories and `SourcingPersistence`. Add the migration and inspect it: it must create the unique and filtered indexes above and the `jsonb` columns, and it must contain no `IDENTITY`. Register the repositories as scoped in `AddSourcingOutboundAdapters`.
- [ ] **Step 4: Run** `IT SourcingPersistenceTests`. Expected: PASS.
- [ ] **Step 5: Commit.** Subject: `feat(sourcing): persistence, unique constraints and AddSourcing migration`

---

### Task 7: Structured-data extraction (JSON-LD, microdata, OpenGraph)

**Files:**
- Create: `Adapter/Outbound/Crawling/StructuredDataExtractor.cs`, `Application/Discovery/UrlForm.cs`
- Test: `tests/UnitTests/Sourcing/StructuredDataExtractorTests.cs`, fixtures `product-jsonld.html`, `product-group.html`, `product-microdata.html`, `product-og.html`, `no-structured-data.html`

**Interfaces:**
- Produces:
```csharp
public static class StructuredDataExtractor {
  /// Returns one entry per schema.org Product/ProductGroup found; empty when none.
  public static IReadOnlyList<ExtractedProduct> Extract(string html, Uri pageUrl);
}
public sealed record ExtractedProduct(ExtractionMethod Method, JsonObject Document, JsonObject Original, string? SourceId, IReadOnlyList<string> ImageUrls, Uri CanonicalUrl);
```
- Rules:
  - Priority is JSON-LD, then microdata, then OpenGraph. The first method that yields at least one Product wins.
  - JSON-LD: parse every `script[type="application/ld+json"]`. Accept arrays and `@graph`. `@type` may be a string or an array, and may be the full `http(s)://schema.org/Product` IRI. Malformed JSON in one script is skipped, not fatal.
  - Microdata: `[itemscope][itemtype$="schema.org/Product"]` becomes the same JSON shape (`itemprop` → property; nested `itemscope` → object).
  - OpenGraph applies only when `og:type` is `product` or `product.item`. It maps `og:title`→`name`, `og:image`→`image`, `product:price:amount`→`offers.price`, `product:price:currency`→`offers.priceCurrency`, `product:availability`→`offers.availability`.
  - Image URLs are resolved against the page URL. `SourceId` = `sku` ?? `productID`. `CanonicalUrl` = Document `url` ?? `<link rel=canonical>` ?? the page URL.
  - **No heuristic price scraping:** text that only *looks* like a price is never read.

- [ ] **Step 1: Write the failing tests.**
```csharp
[Fact] public void JsonLd_Product_Is_Extracted_With_Offer()
{
    var p = Extract("product-jsonld.html", "https://shop.example/p/drill").Should().ContainSingle().Subject;
    p.Method.Should().Be(ExtractionMethod.JsonLd);
    p.Document["name"]!.GetValue<string>().Should().Be("Acme Cordless Drill 18V");
    p.Document["offers"]!["price"]!.ToString().Should().Be("129.90");
    p.Document["offers"]!["priceCurrency"]!.GetValue<string>().Should().Be("SGD");
    p.SourceId.Should().Be("ACM-D18");
    p.ImageUrls.Should().Equal("https://shop.example/img/drill.png");
}
[Fact] public void Graph_And_Iri_Types_Are_Recognised() => Extract("product-group.html", Url).Should().ContainSingle().Which.Document["hasVariant"]!.AsArray().Should().HaveCount(2);
[Fact] public void Microdata_Used_When_No_JsonLd() => Extract("product-microdata.html", Url).Single().Method.Should().Be(ExtractionMethod.Microdata);
[Fact] public void OpenGraph_Used_Last() => Extract("product-og.html", Url).Single().Document["offers"]!["price"]!.ToString().Should().Be("49.00");
[Fact] public void Page_With_Prices_In_Text_But_No_Structured_Data_Yields_Nothing() => Extract("no-structured-data.html", Url).Should().BeEmpty(); // fixture contains "S$129.90" in a <span>
[Fact] public void Malformed_JsonLd_Script_Is_Skipped_Not_Fatal() => StructuredDataExtractor.Extract("<script type=\"application/ld+json\">{oops</script>" + ValidProductScript, new Uri(Url)).Should().ContainSingle();
```
- [ ] **Step 2: Run** `UT StructuredDataExtractorTests`. Expected: FAIL.
- [ ] **Step 3: Implement** with AngleSharp `HtmlParser` and `System.Text.Json.Nodes`.
- [ ] **Step 4: Run** `UT StructuredDataExtractorTests`. Expected: PASS.
- [ ] **Step 5: Commit.** Subject: `feat(sourcing): schema.org structured-data extraction (JSON-LD, microdata, OG)`

---

### Task 8: robots.txt, politeness and the SSRF-safe HTTP handler

**Files:**
- Create: `Adapter/Outbound/Crawling/{RobotsTxt,RobotsCache,PolitenessGate,SafeHttpHandler,CrawlerOptions}.cs`
- Test: `tests/UnitTests/Sourcing/RobotsTxtTests.cs`, `PolitenessGateTests.cs`, `SafeHttpHandlerTests.cs`

**Interfaces:**
- Produces:
```csharp
public sealed class RobotsTxt {
  public static RobotsTxt Parse(string content, string productToken);   // RFC 9309 groups; product token match else "*"
  public static RobotsTxt AllowAll { get; }                            // 4xx / missing
  public static RobotsTxt DisallowAll { get; }                         // 5xx / network error
  public bool IsAllowed(string pathAndQuery);                          // longest match wins; Allow wins ties; supports * and $
  public TimeSpan? CrawlDelay { get; }
}
public sealed class RobotsCache(HttpClient http, CrawlerOptions options) { public Task<RobotsTxt> GetAsync(Uri origin, CancellationToken ct = default); } // per scheme+host+port, per job instance
public sealed class PolitenessGate(Func<TimeSpan, CancellationToken, Task> delay, Func<DateTimeOffset> now) {
  public Task WaitTurnAsync(string host, TimeSpan minimumGap, CancellationToken ct = default);
}
public static class SafeHttpHandler { public static SocketsHttpHandler Create(CrawlerOptions options); } // ConnectCallback rejects loopback/private/link-local/CGNAT/ULA/multicast IPs unless AllowPrivateNetworks; MaxAutomaticRedirections = 5
```
The connect-time IP check runs against the address actually dialled, so DNS rebinding cannot bypass it. The `now` delegate is wired to `IClock.UtcNow` in DI, which keeps the ambient-clock scan green.

- [ ] **Step 1: Write the failing tests.**
```csharp
[Fact] public void Specific_Group_Beats_Star()
{
    var r = RobotsTxt.Parse("User-agent: *\nDisallow: /\n\nUser-agent: EwaysVNextBot\nDisallow: /private/\nCrawl-delay: 3", "EwaysVNextBot");
    r.IsAllowed("/p/1").Should().BeTrue(); r.IsAllowed("/private/x").Should().BeFalse(); r.CrawlDelay.Should().Be(TimeSpan.FromSeconds(3));
}
[Fact] public void Longest_Match_Wins_And_Allow_Wins_Ties()
{
    var r = RobotsTxt.Parse("User-agent: *\nDisallow: /shop\nAllow: /shop/products\nDisallow: /*.pdf$", "EwaysVNextBot");
    r.IsAllowed("/shop/products/1").Should().BeTrue(); r.IsAllowed("/shop/cart").Should().BeFalse();
    r.IsAllowed("/a/b.pdf").Should().BeFalse(); r.IsAllowed("/a/b.pdf?x=1").Should().BeTrue();
}
[Fact] public void DisallowAll_Blocks_Everything() => RobotsTxt.DisallowAll.IsAllowed("/").Should().BeFalse();
[Fact] public async Task Second_Request_To_Same_Host_Waits_The_Gap()
{
    var waits = new List<TimeSpan>(); var t = Now;
    var gate = new PolitenessGate((d, _) => { waits.Add(d); t += d; return Task.CompletedTask; }, () => t);
    await gate.WaitTurnAsync("a.example", TimeSpan.FromSeconds(1)); await gate.WaitTurnAsync("a.example", TimeSpan.FromSeconds(1));
    await gate.WaitTurnAsync("b.example", TimeSpan.FromSeconds(1));
    waits.Should().Equal(TimeSpan.FromSeconds(1));
}
[Fact] public async Task Loopback_Is_Refused_By_Default()
{
    using var server = await FixtureHttpServer.StartAsync();
    using var http = new HttpClient(SafeHttpHandler.Create(new CrawlerOptions { AllowPrivateNetworks = false }));
    await FluentActions.Awaiting(() => http.GetAsync(server.Url("/robots.txt"))).Should().ThrowAsync<HttpRequestException>();
    server.Requests.Should().BeEmpty();
}
[Fact] public async Task Loopback_Allowed_When_Configured() { /* AllowPrivateNetworks = true → 200 */ }
```
- [ ] **Step 2: Run** `UT RobotsTxtTests`, `UT PolitenessGateTests`, `UT SafeHttpHandlerTests`. Expected: FAIL.
- [ ] **Step 3: Implement** the parser. Rule matching translates `*` to `.*` and a trailing `$` to an end anchor, compares path+query case-sensitively, and ranks matches by pattern length. Also create `FixtureHttpServer` in tests: `StartAsync(params (string Host, string Path, string ContentType, byte[] Body, int Status)[] routes)`, `Url(path, host = "127.0.0.1")`, `Requests` (thread-safe list of `(Host, PathAndQuery, At)`).
- [ ] **Step 4: Run** the three. Expected: PASS.
- [ ] **Step 5: Commit.** Subject: `feat(sourcing): robots.txt, per-host politeness and SSRF-safe crawler handler`

---

### Task 9: `ISourceAdapter` and `WebsiteCrawlerAdapter`

**Files:**
- Create: `Application/Ports/Outbound/ISourceAdapter.cs`, `Adapter/Outbound/Crawling/{CrawlCursor,WebsiteCrawlerAdapter}.cs`
- Test: `tests/UnitTests/Sourcing/WebsiteCrawlerAdapterTests.cs` (uses `FixtureHttpServer`)

**Interfaces:**
- Produces:
```csharp
public interface ISourceAdapter {
  bool Supports(ImportMode mode);
  IAsyncEnumerable<RawListing> Discover(ImportJobSpec job, string? checkpoint, CancellationToken ct = default);
}
public sealed record CrawlFrontierEntry(string Url, int Depth, bool FollowLinks, bool IsSeed);
public sealed record CrawlCursor(IReadOnlyList<CrawlFrontierEntry> Queue, IReadOnlyList<string> Visited, int PagesFetched, int ListingsYielded);
public sealed class WebsiteCrawlerAdapter : ISourceAdapter { // Supports: Crawl
  public IAsyncEnumerable<RawListing> Discover(ImportJobSpec job, string? checkpoint, CancellationToken ct = default);
  internal IAsyncEnumerable<RawListing> CrawlAsync(ImportJobSpec job, CrawlCursor start, CancellationToken ct); // reused by WebSearchSourceAdapter
}
```
- Crawl loop (specified because the signature does not determine it):
  1. Dequeue in BFS order.
  2. Skip any URL that is already visited, outside the allowed domains, on an excluded domain, or on a host whose `SourcePolicy.CrawlAllowed == false`.
     - Allowed domains are `IncludeDomains` when non-empty. Otherwise they are the hosts of the seeds.
     - Fetch the host policy once via `ISourcePolicyRepository.FindByDomainAsync`.
  3. Check robots via `RobotsCache`, then call `PolitenessGate.WaitTurnAsync(host, max(PolitenessDelayMs, robots.CrawlDelay))`.
  4. Send a GET with `Sourcing:Crawler:UserAgent`. Accept only `text/html` up to `MaxHtmlBytes`.
  5. Increment `PagesFetched`. Stop when it reaches `MaxPages`, or when `ListingsYielded` reaches `MaxRecords`.
  6. Run `StructuredDataExtractor.Extract`, and yield one `RawListing` per product.
     - `Checkpoint` = the serialized cursor after this page, with the page marked visited.
     - Listings on the same page share that cursor, so a resume re-yields the page. That is safe because staging is idempotent by fingerprint.
  7. If the page is a **seed** and yields no product, yield one listing with `Method = None`, an empty `Document`, and `ExtractionError = "NO_STRUCTURED_DATA"`. Non-seed pages without products are traversed silently.
  8. If `Depth < MaxDepth` and `FollowLinks`, enqueue `a[href]` http(s) links in canonical form (`UrlForm`).
  9. A blocked seed (robots, policy or domain filter) yields a listing with `ExtractionError = "SOURCE_BLOCKED"` and `Detail` set to the reason, stored as `Document["_blocked"]`. That makes the restriction visible in review rather than silent.

- [ ] **Step 1: Write the failing tests.** The server routes on host `127.0.0.1`:
  - `/robots.txt` disallows `/private/`.
  - `/` links to `/p/drill`, `/private/secret`, `/about` and `http://localhost:{port}/p/elsewhere`.
  - `/p/drill` serves `product-jsonld.html`, and `/about` serves `no-structured-data.html`.
```csharp
[Fact] public async Task Crawls_Product_Respecting_Robots_And_Domain_Filter()
{
    var listings = await Collect(Spec(sources: [server.Url("/")], maxDepth: 1));
    listings.Should().ContainSingle(l => l.ExtractionError == null).Which.SourceId.Should().Be("ACM-D18");
    server.Requests.Select(r => r.PathAndQuery).Should().NotContain("/private/secret");
    server.Requests.Should().NotContain(r => r.Host == "localhost");          // not in the seed host set
}
[Fact] public async Task Seed_Without_Structured_Data_Is_Invalid_Listing() =>
    (await Collect(Spec(sources: [server.Url("/about")]))).Should().ContainSingle().Which.ExtractionError.Should().Be("NO_STRUCTURED_DATA");
[Fact] public async Task Non_Seed_Page_Without_Data_Yields_Nothing() =>
    (await Collect(Spec(sources: [server.Url("/")], maxDepth: 1))).Should().NotContain(l => l.SourceUrl.EndsWith("/about"));
[Fact] public async Task MaxPages_Is_Enforced() { await Collect(Spec(sources: [server.Url("/")], maxDepth: 1, maxPages: 2)); server.Requests.Count(r => r.PathAndQuery != "/robots.txt").Should().Be(2); }
[Fact] public async Task MaxDepth_Zero_Fetches_Only_Seeds() { await Collect(Spec(sources: [server.Url("/")], maxDepth: 0)); server.Requests.Select(r => r.PathAndQuery).Should().BeEquivalentTo("/robots.txt", "/"); }
[Fact] public async Task Policy_CrawlAllowed_False_Blocks_Host()
{
    policies.Add(SourcePolicy.Create(1, "127.0.0.1", crawlAllowed: false, true, true, null, Now));
    (await Collect(Spec(sources: [server.Url("/p/drill")]))).Single().ExtractionError.Should().Be("SOURCE_BLOCKED");
    server.Requests.Should().BeEmpty();
}
[Fact] public async Task Resume_From_Checkpoint_Does_Not_Refetch_Visited_Pages()
{
    var first = await Collect(Spec(sources: [server.Url("/")], maxDepth: 1), take: 1);
    server.ClearRequests();
    await Collect(Spec(sources: [server.Url("/")], maxDepth: 1), checkpoint: first[^1].Checkpoint);
    server.Requests.Select(r => r.PathAndQuery).Should().NotContain("/p/drill");
}
```
- [ ] **Step 2: Run** `UT WebsiteCrawlerAdapterTests`. Expected: FAIL.
- [ ] **Step 3: Implement.** Register `HttpClient` name `"sourcing-crawler"` with `SafeHttpHandler.Create` and a 30 s timeout. The adapter gets `IClock`, `ISourcePolicyRepository`, `IOptions<CrawlerOptions>` and `IHttpClientFactory`.
- [ ] **Step 4: Run** `UT WebsiteCrawlerAdapterTests`. Expected: PASS.
- [ ] **Step 5: Commit.** Subject: `feat(sourcing): website crawler adapter with checkpointed frontier`

---

### Task 10: `ISearchProvider`, Brave adapter, web-search and combined modes

**Files:**
- Create: `Application/Ports/Outbound/ISearchProvider.cs`, `Adapter/Outbound/Search/{BraveOptions,BraveSearchProvider,WebSearchSourceAdapter}.cs`
- Test: `tests/UnitTests/Sourcing/BraveSearchProviderTests.cs`, `WebSearchSourceAdapterTests.cs`, fixture `brave-response.json`

**Interfaces:**
- Produces:
```csharp
public interface ISearchProvider {
  bool IsConfigured { get; }
  Task<Result<IReadOnlyList<SearchHit>>> SearchAsync(string query, int offset, int count, CancellationToken ct = default);
}
public sealed record SearchHit(string Url, string? Title);
public static class SourcingErrors { public static readonly Error SearchProviderNotConfigured =
  new("SEARCH_PROVIDER_NOT_CONFIGURED", "Web search is disabled: set Brave:ApiKey (env Brave__ApiKey).", 400); }
public sealed class WebSearchSourceAdapter : ISourceAdapter { } // Supports: WebSearch, Combined
```
- Brave call: `GET {BaseUrl}/res/v1/web/search?q={query}&count={count}&offset={offset}` with headers `X-Subscription-Token: {ApiKey}` and `Accept: application/json`. Read `web.results[].url`.
- `WebSearchSourceAdapter`:
  - With no checkpoint, it collects hits for each query (pages of 20, offset 0..4) until `MaxRecords` URLs. It drops URLs outside include/exclude, de-duplicates by `UrlForm`, and makes each one a frontier entry `(url, Depth 0, FollowLinks: false, IsSeed: true)`.
  - `Combined` also adds `Sources` as `(url, 0, FollowLinks: true, IsSeed: true)`.
  - It then delegates to `WebsiteCrawlerAdapter.CrawlAsync`. The checkpoint is the crawler cursor, so search is not repeated on resume.
  - When `!IsConfigured`, `Discover` throws `SourcingConfigurationException(SourcingErrors.SearchProviderNotConfigured)`. The pipeline turns that into a failed job (Task 17).

- [ ] **Step 1: Write the failing tests.**
```csharp
[Fact] public async Task Sends_Token_And_Parses_Results()
{
    using var s = await FixtureHttpServer.StartAsync(("127.0.0.1", "/res/v1/web/search", "application/json", Fixture("brave-response.json"), 200));
    var hits = (await Provider(s, apiKey: "k").SearchAsync("cordless drill", 0, 20)).Value!;
    hits.Select(h => h.Url).Should().Equal("https://shop.example/p/drill", "https://blocked.example/p/1");
    s.Requests.Single().Headers["X-Subscription-Token"].Should().Be("k");
}
[Fact] public void Not_Configured_Without_Key() => Provider(null, apiKey: null).IsConfigured.Should().BeFalse();
[Fact] public async Task WebSearch_Without_Key_Fails_Clearly() =>
    (await FluentActions.Awaiting(() => Collect(new WebSearchSourceAdapter(Unconfigured(), crawler), Spec(ImportMode.WebSearch))).Should()
        .ThrowAsync<SourcingConfigurationException>()).Which.Error.Code.Should().Be("SEARCH_PROVIDER_NOT_CONFIGURED");
[Fact] public async Task Excluded_Domains_Never_Reach_The_Crawler()
{
    var spec = Spec(ImportMode.WebSearch, exclude: ["blocked.example"]);
    await Collect(Adapter(FakeHits("https://shop.example/p/drill", "https://blocked.example/p/1")), spec);
    crawler.Seeds.Should().Equal(new CrawlFrontierEntry("https://shop.example/p/drill", 0, false, true));
}
[Fact] public async Task Combined_Seeds_Sources_With_Link_Following() { /* sources → FollowLinks true; hits → false */ }
```
- [ ] **Step 2: Run** `UT BraveSearchProviderTests`, `UT WebSearchSourceAdapterTests`. Expected: FAIL.
- [ ] **Step 3: Implement.** To test the search adapter without the network, `WebsiteCrawlerAdapter.CrawlAsync` is `internal virtual`; add `InternalsVisibleTo("EwaysVNext.UnitTests")`.
- [ ] **Step 4: Run** both. Expected: PASS.
- [ ] **Step 5: Commit.** Subject: `feat(sourcing): Brave search provider and web-search/combined source adapter`

---

### Task 11: File import: canonical columns, readers, row mapping, templates

**Files:**
- Create: `Application/Discovery/{ImportColumns,ImportRowMapper}.cs`, `Application/Ports/Outbound/IImportFileReader.cs`, `Adapter/Outbound/Files/*.cs`, `Templates/import-row.schema.json`
- Test: `tests/UnitTests/Sourcing/ImportRowMapperTests.cs`, `ImportFileReaderTests.cs`, `ImportTemplatesTests.cs`

**Interfaces:**
- Produces:
```csharp
public static class ImportColumns {
  public static IReadOnlyList<string> All { get; } = ["group_key","title","brand","category_path","short_description","description",
    "gtin","upc","ean","mpn","sku","source_url","source_domain","source_id","price","currency","tax_included","availability","image_urls"];
  public const string AttributePrefix = "attr:";  public const string Ignore = "ignore";
  public static IReadOnlyList<string> RequiredForPreview { get; } = ["title","brand","price","currency","image_urls"];
}
public sealed record ImportFileHeader(IReadOnlyList<string> Columns);
public sealed record ImportRow(int RowNumber, IReadOnlyDictionary<string, string?> Cells);
public interface IImportFileReader {
  Task<Result<ImportFileHeader>> ReadHeaderAsync(string objectKey, ImportFileFormat format, CancellationToken ct = default);
  IAsyncEnumerable<ImportRow> ReadRowsAsync(string objectKey, ImportFileFormat format, int skipRows, CancellationToken ct = default);
}
public static class ImportRowMapper {
  /// Rows sharing a contiguous non-empty group_key become one ProductGroup with hasVariant; otherwise one Product per row.
  public static RawListing ToListing(long jobId, IReadOnlyList<ImportRow> group, ImportColumnMapping mapping, ImportFileFormat format, DateTimeOffset now, string checkpoint);
}
public sealed class FileImportAdapter : ISourceAdapter { } // Supports: File; checkpoint = last consumed RowNumber
public static class ImportTemplates { public static byte[] Csv(); public static byte[] Xlsx(); public static string JsonSchema(); }
```
- Mapping into the Document:
  - `title`→`name`, `short_description`→`disambiguatingDescription`, `brand`→`brand.name`, `category_path`→`category` (segments separated by `>`).
  - `gtin`/`upc`/`ean`→`gtin`/`gtin12`/`gtin13`; `mpn`, `sku`; `source_id`→`productID`.
  - `price`/`currency`/`tax_included`/`availability`→`offers.{price,priceCurrency,valueAddedTaxIncluded,availability}`.
  - `image_urls` (`|`-separated)→`image[]`; `attr:<k>`→`additionalProperty[{name:k,value}]`.
  - Cells are kept as **strings**; the mapper never parses numbers. `Original` = the row keyed by source header.
- A `group_key` that reappears after a different key yields a listing with `ExtractionError = "GROUP_KEY_NOT_CONTIGUOUS"`.
- Readers:
  - CsvHelper with `InvariantCulture`, header required, and BOM tolerated.
  - ClosedXML reads the first worksheet, row 1 as headers, with cell values via `GetFormattedString()` so a price stays `"129.90"`.
  - JSON is an array of objects whose properties are the canonical names. Unknown properties other than `attr:*` are rejected as `IMPORT_FILE_SCHEMA_VIOLATION` with the row number.

- [ ] **Step 1: Write the failing tests.**
```csharp
[Fact] public void Row_Maps_To_Schema_Org_Shape_With_String_Price()
{
    var l = ImportRowMapper.ToListing(9, [Row(1, title: "Acme Drill", brand: "ACME", price: "129.90", currency: "SGD", images: "https://i.example/a.png|https://i.example/b.png", attrs: ("attr:voltage", "18 V"))], IdentityMapping, ImportFileFormat.Csv, Now, "1");
    l.Document["offers"]!["price"]!.GetValue<string>().Should().Be("129.90");
    l.Document["additionalProperty"]![0]!["name"]!.GetValue<string>().Should().Be("voltage");
    l.ImageUrls.Should().HaveCount(2); l.SourceUrl.Should().Be("urn:eways:import:9:row:1"); l.Method.Should().Be(ExtractionMethod.Csv);
}
[Fact] public void Contiguous_Group_Key_Becomes_ProductGroup() => ImportRowMapper.ToListing(9, [Row(1, group: "G1", attrs: ("attr:color","Red")), Row(2, group: "G1", attrs: ("attr:color","Blue"))], IdentityMapping, ImportFileFormat.Csv, Now, "2").Document["hasVariant"]!.AsArray().Should().HaveCount(2);
[Fact] public void Ignored_Columns_Do_Not_Reach_Document() { /* mapping "Internal Notes"→"ignore" */ }
[Fact] public async Task Xlsx_Keeps_Price_As_Formatted_String() { /* template-built workbook with 129.9 formatted "0.00" → "129.90" */ }
[Fact] public async Task Json_Unknown_Property_Is_Schema_Violation() { /* {"title":"x","colour":"red"} → error code IMPORT_FILE_SCHEMA_VIOLATION, row 1 */ }
[Fact] public void Csv_Template_Header_Equals_Canonical_Columns() => ParseCsvHeader(ImportTemplates.Csv()).Should().Equal(ImportColumns.All.Append("attr:color"));
[Fact] public void Xlsx_Template_Header_Equals_Canonical_Columns() => FirstRow(ImportTemplates.Xlsx()).Should().Equal(ImportColumns.All.Append("attr:color"));
[Fact] public void Json_Schema_Properties_Equal_Canonical_Columns()
{
    var schema = JsonNode.Parse(ImportTemplates.JsonSchema())!;
    schema["items"]!["properties"]!.AsObject().Select(p => p.Key).Should().BeEquivalentTo(ImportColumns.All);
    schema["items"]!["patternProperties"]!.AsObject().Single().Key.Should().Be("^attr:[a-z0-9_]+$");
}
```
- [ ] **Step 2: Run** `UT ImportRowMapperTests`, `UT ImportFileReaderTests`, `UT ImportTemplatesTests`. Expected: FAIL.
- [ ] **Step 3: Implement.** Readers fetch through `IObjectStorage.GetAsync(StorageBuckets.ImportStaging, key)`; tests use an in-memory `IObjectStorage` fake (`tests/UnitTests/Sourcing/InMemoryObjectStorage.cs`). The schema is JSON Schema 2020-12 (`type: array`, `additionalProperties: false`), loaded as an embedded resource.
- [ ] **Step 4: Run** the three. Expected: PASS.
- [ ] **Step 5: Commit.** Subject: `feat(sourcing): CSV/XLSX/JSON file import, row mapping and templates`

---

### Task 12: Catalog read ports for import, and deterministic normalization

**Files:**
- Create: `src/Modules/Catalog/Application/Sourcing/{ICatalogImportLookupPort,CatalogSourcingDtos}.cs`, `src/Modules/Catalog/Adapter/Outbound/Sourcing/CatalogImportLookup.cs`
- Create: `Application/Ports/Outbound/ICatalogLookup.cs`, `Adapter/Outbound/Gateways/CatalogLookupGateway.cs`
- Create: `Application/Normalization/{NormalizedProduct,DeterministicNormalizer,PriceParser,GtinValidator,NameKey,UnitConverter,JsonPointer}.cs`
- Modify: `MeasurementUnit` gains `Dimension` (string?, max 32) and `ToBaseFactor` (decimal?, `numeric(28,10)`), with a migration `AddMeasurementUnitConversion`. If phase 2–3 already added equivalents, map `UnitRef` to them and skip this modification.
- Test: `tests/UnitTests/Sourcing/{PriceParserTests,GtinValidatorTests,DeterministicNormalizerTests}.cs`

**Interfaces:**
- Produces (Catalog, consumed only by the Sourcing gateway):
```csharp
namespace EwaysVNext.Modules.Catalog.Application.Sourcing;
public interface ICatalogImportLookupPort {
  Task<IReadOnlyList<BrandRef>> FindBrandsAsync(IReadOnlyCollection<string> nameKeys, CancellationToken ct = default); // canonical name or alias
  Task<CategoryRef?> GetCategoryAsync(long categoryId, CancellationToken ct = default);
  Task<CategoryRef?> ResolveCategoryPathAsync(IReadOnlyList<string> segmentKeys, CancellationToken ct = default); // canonical key or alias per level, from root
  Task<IReadOnlyList<AttributeRef>> GetCategoryAttributesAsync(long categoryId, CancellationToken ct = default); // effective: global + assigned − excluded
  Task<IReadOnlyList<UnitRef>> GetUnitsAsync(CancellationToken ct = default);
  string VariantSignatureHash(IReadOnlyDictionary<string, string> canonicalAttributes); // delegates to Catalog's own signature function
}
public sealed record BrandRef(long BrandId, string CanonicalName);
public sealed record CategoryRef(long CategoryId, IReadOnlyList<string> CanonicalPath, bool IsActive);
public sealed record AttributeRef(string CanonicalKey, IReadOnlyList<string> Aliases, string DataType, string? UnitSymbol, bool IsRequired, bool IsVariantDefining, IReadOnlyList<string> Options);
public sealed record UnitRef(string Symbol, string Name, string? Dimension, decimal? ToBaseFactor);
```
- Produces (Sourcing): `ICatalogLookup` mirrors the port 1:1 with Sourcing-local record copies (rule 2). Also:
```csharp
public static class NameKey { public static string Of(string s); }  // NFKC, lower-invariant, & → "and", punctuation → space, collapse spaces, trim
public static class PriceParser { public static Result<long> ToMinor(JsonNode? price); } // ^\d+(\.\d{1,2})?$ on the string form; JSON number via its raw text; else PRICE_INVALID; 0 → PRICE_INVALID
public static class GtinValidator { public static (string Type, string Digits)? Classify(string raw); } // GS1 mod-10; 8/14→GTIN, 12→UPC, 13→EAN
public sealed record Sourced<T>(T Value, string SourcePath, FieldOrigin Origin);
public enum FieldOrigin { Deterministic, Enriched }
public sealed record NormalizedVariant(IReadOnlyDictionary<string, Sourced<string>> Attributes, string SignatureHash, string? Sku,
  long? PriceMinor, string? Currency, bool? TaxIncluded, string? Availability, string? PriceSourcePath);
public sealed record NormalizedProduct(Sourced<string>? Title, Sourced<string>? ShortDescription, Sourced<string>? Description,
  Sourced<string>? BrandName, long? BrandId, long? CategoryId, IReadOnlyList<string>? CategoryPath,
  IReadOnlyList<(string Type, string Value)> Identifiers, string? Mpn, IReadOnlyList<NormalizedVariant> Variants,
  IReadOnlyDictionary<string, Sourced<string>> ProductAttributes, IReadOnlyDictionary<string, string> UnmappedAttributes);
public sealed class DeterministicNormalizer(ICatalogLookup catalog) {
  public Task<(NormalizedProduct Product, IReadOnlyList<ValidationIssue> Issues)> NormalizeAsync(RawListing listing, ImportJobSpec job, long? categoryOverrideId, CancellationToken ct = default);
}
public static class UnitConverter { public static string? Convert(string value, string? fromSymbol, UnitRef target, IReadOnlyList<UnitRef> units); } // same Dimension only; decimal math; trims trailing zeros
```
- Normalizer rules:
  - Text: whitespace collapsed and trimmed; title capped at 300 characters.
  - Brand: `brand.name` ?? `brand` (string) ?? `manufacturer.name`.
  - Category: `categoryOverrideId` ?? `job.TargetCategoryId` ?? `category` split on `>` or `/`, each segment through `NameKey`.
  - Attributes: `additionalProperty[i].name` → canonical key via alias match on `NameKey`. Unknown names go to `UnmappedAttributes`.
  - Number attributes: `"18 V"` splits into value and unit, then converts to the blueprint unit.
  - Option attributes must case-insensitively equal an option, else issue `ATTRIBUTE_VALUE_INVALID` (Review).
  - Variants: `hasVariant[]` if present, otherwise a single variant. Variant-defining attributes form the signature, and `SignatureHash` = `catalog.VariantSignatureHash`. Duplicate signatures in one record raise `VARIANT_SIGNATURE_DUPLICATE` (Error).
  - Every `Sourced` value carries the JSON pointer it was read from, e.g. `/hasVariant/1/offers/price`.
  - **Catalog entities are never created here.** Categories and attribute definitions are resolved or reported, never invented. Brands that do not resolve keep `BrandId = null` and are created by the publishing port under their canonical name.

- [ ] **Step 1: Write the failing tests.**
```csharp
[Theory, InlineData("129.90", 12990), InlineData("129", 12900), InlineData("0.5", 50)]
public void Price_Accepts_Up_To_Two_Decimals(string raw, long minor) => PriceParser.ToMinor(JsonValue.Create(raw)).Value.Should().Be(minor);
[Theory, InlineData("129.999"), InlineData("1e3"), InlineData("-5.00"), InlineData(" 12.00"), InlineData("1,299.00"), InlineData("0"), InlineData("")]
public void Price_Never_Rounds_Or_Coerces(string raw) => PriceParser.ToMinor(JsonValue.Create(raw)).Error!.Code.Should().Be("PRICE_INVALID");
[Fact] public void Json_Number_Uses_Its_Raw_Text() => PriceParser.ToMinor(JsonNode.Parse("12.345")).Error!.Code.Should().Be("PRICE_INVALID");
[Theory, InlineData("4006381333931", "EAN"), InlineData("036000291452", "UPC"), InlineData("96385074", "GTIN")]
public void Gtin_Classified(string raw, string type) => GtinValidator.Classify(raw)!.Value.Type.Should().Be(type);
[Fact] public void Bad_Check_Digit_Rejected() => GtinValidator.Classify("4006381333932").Should().BeNull();

[Fact] public async Task Brand_Alias_Resolves_To_Canonical_Brand()
{
    catalog.Brands.Add(new BrandRef(7, "acme"), aliases: ["acme inc", "acme corporation"]);
    var (p, _) = await Normalize(Doc(brand: "  ACME, Inc. "));
    p.BrandId.Should().Be(7); p.BrandName!.SourcePath.Should().Be("/brand/name");
}
[Fact] public async Task Unknown_Category_Is_Review_Issue_Not_Created()
{
    var (_, issues) = await Normalize(Doc(category: "Tools > Unknown"));
    issues.Should().ContainSingle(i => i.Code == "CATEGORY_UNRESOLVED" && i.Severity == IssueSeverity.Review);
    catalog.CreatedAnything.Should().BeFalse();
}
[Fact] public async Task Attribute_Alias_And_Unit_Conversion()
{
    catalog.Attributes(category: 3, new AttributeRef("weight", ["wt", "net weight"], "Number", "kg", false, false, []));
    catalog.Units(new UnitRef("g", "gram", "mass", 0.001m), new UnitRef("kg", "kilogram", "mass", 1m));
    var (p, _) = await Normalize(Doc(category: 3, props: ("Net Weight", "1500 g")));
    p.ProductAttributes["weight"].Value.Should().Be("1.5"); p.ProductAttributes["weight"].SourcePath.Should().Be("/additionalProperty/0/value");
}
[Fact] public async Task Variants_Get_Catalog_Signatures_And_Own_Prices()
{
    var (p, _) = await Normalize(ProductGroupDoc(("Red", "10.00"), ("Blue", "11.00")));
    p.Variants.Select(v => v.PriceMinor).Should().Equal(1000, 1100);
    p.Variants.Select(v => v.SignatureHash).Should().OnlyHaveUniqueItems();
    p.Variants[1].PriceSourcePath.Should().Be("/hasVariant/1/offers/price");
}
[Fact] public async Task Same_Signature_Twice_Is_Error() => (await Normalize(ProductGroupDoc(("Red", "1.00"), ("red", "1.00")))).Issues.Should().Contain(i => i.Code == "VARIANT_SIGNATURE_DUPLICATE");
```
- [ ] **Step 2: Run** `UT PriceParserTests`, `UT GtinValidatorTests`, `UT DeterministicNormalizerTests`. Expected: FAIL.
- [ ] **Step 3: Implement** the Catalog port. It is a read-only EF projection in `Catalog.Adapter.Outbound/Sourcing/CatalogImportLookup.cs` over the phase 2–3 brand/category/attribute alias tables. Then implement the gateway and the normalizer. Make Catalog's variant-signature function public static if it is not already, and route `VariantSignatureHash` to it, so that Sourcing and Catalog cannot disagree on a hash.
- [ ] **Step 4: Run** the three, then `AT`. Expected: PASS.
- [ ] **Step 5: Commit.** Subject: `feat(sourcing): deterministic normalization over Catalog alias, attribute and unit data`

---

### Task 13: Enrichment port and the "no invented facts" merger

**Files:**
- Create: `Application/Ports/Outbound/IEnrichmentProvider.cs`, `Application/Normalization/EnrichmentMerger.cs`, `Adapter/Outbound/Enrichment/DisabledEnrichmentProvider.cs`
- Test: `tests/UnitTests/Sourcing/EnrichmentMergerTests.cs`

**Interfaces:**
- Produces:
```csharp
public interface IEnrichmentProvider {
  bool IsEnabled { get; }
  Task<EnrichmentResult> EnrichAsync(EnrichmentRequest request, CancellationToken ct = default);
}
public sealed record EnrichmentRequest(JsonObject Document, IReadOnlyList<string> MissingTargets, IReadOnlyList<AttributeHint> Attributes, IReadOnlyList<string> CategoryPaths);
public sealed record AttributeHint(string Key, string DataType, IReadOnlyList<string> Options);
public sealed record EnrichmentResult(IReadOnlyList<EnrichedField> Fields, string? SkippedReason);
public sealed record EnrichedField(string Target, string Value, string? SourcePath); // Target: "brand" | "category" | "attributes.{key}" | "variants[{i}].attributes.{key}"
public static class EnrichmentMerger {
  public static (NormalizedProduct Product, IReadOnlyList<string> Discarded) Merge(NormalizedProduct product, JsonObject document, EnrichmentResult result);
}
```
- Acceptance rule (the guard; body specified). An enriched field is accepted only when **all** of these hold:
  1. `SourcePath` is non-empty and resolves via `JsonPointer.Resolve(document, path)` to a string or number node.
  2. `Normalize(value)` is a substring of `Normalize(node text)`, where `Normalize` = `NameKey.Of` with all spaces removed.
  3. The target is in `request.MissingTargets`. Enrichment never overwrites a deterministic value.
  4. For an option attribute, the value equals an allowed option.
  Accepted fields become `Sourced(value, path, FieldOrigin.Enriched)`. Everything else is listed in `Discarded` as `"{target}: {reason}"`, and the pipeline stores that list in `Normalized["_enrichmentDiscarded"]` for reviewers.

- [ ] **Step 1: Write the failing tests** (the spec §13 "no invented facts" rule).
```csharp
// Document: { "name": "Acme Cordless Drill 18V Red", "description": "Ships with 2 batteries" }
[Fact] public void Fact_Present_At_Its_Path_Is_Accepted()
{
    var (p, d) = Merge(Fields(("attributes.color", "Red", "/name")), missing: ["attributes.color"]);
    p.ProductAttributes["color"].Should().Be(new Sourced<string>("Red", "/name", FieldOrigin.Enriched)); d.Should().BeEmpty();
}
[Fact] public void Value_Absent_From_Its_Path_Is_Discarded() => Merge(Fields(("attributes.color", "Blue", "/name")), ["attributes.color"]).Discarded.Should().ContainSingle().Which.Should().StartWith("attributes.color");
[Fact] public void Field_Without_Source_Path_Is_Discarded() => Merge(Fields(("attributes.weight", "2 kg", null)), ["attributes.weight"]).Product.ProductAttributes.Should().NotContainKey("weight");
[Fact] public void Path_That_Does_Not_Resolve_Is_Discarded() => Merge(Fields(("brand", "Acme", "/manufacturer/name")), ["brand"]).Product.BrandName.Should().BeNull();
[Fact] public void Invented_Number_With_Real_Path_Is_Discarded() => Merge(Fields(("attributes.voltage", "20V", "/name")), ["attributes.voltage"]).Discarded.Should().HaveCount(1);
[Fact] public void Never_Overwrites_A_Deterministic_Value() => Merge(Fields(("brand", "Cordless", "/name")), missing: [], brand: "Acme").Product.BrandName!.Value.Should().Be("Acme");
[Fact] public void Option_Must_Be_Allowed() => Merge(Fields(("attributes.color", "Red", "/name")), ["attributes.color"], options: ["Black"]).Discarded.Should().HaveCount(1);
[Fact] public void Disabled_Provider_Reports_Disabled() => new DisabledEnrichmentProvider().IsEnabled.Should().BeFalse();
```
- [ ] **Step 2: Run** `UT EnrichmentMergerTests`. Expected: FAIL.
- [ ] **Step 3: Implement.** `JsonPointer.Resolve` follows RFC 6901 (`~0`/`~1` escapes; array indexes).
- [ ] **Step 4: Run** `UT EnrichmentMergerTests`. Expected: PASS. **Mutation check:** delete condition 2, re-run, watch `Value_Absent_From_Its_Path_Is_Discarded` and `Invented_Number_With_Real_Path_Is_Discarded` fail, then restore.
- [ ] **Step 5: Commit.** Subject: `feat(sourcing): enrichment port and provenance-enforcing merger`

---

### Task 14: Claude enrichment adapter (official Anthropic SDK)

**Files:**
- Create: `Adapter/Outbound/Enrichment/{AnthropicOptions,EnrichmentPrompt,ClaudeEnrichmentProvider}.cs`
- Test: `tests/UnitTests/Sourcing/ClaudeEnrichmentProviderTests.cs`, fixture `anthropic-enrichment-response.json`

**Interfaces:**
- Produces:
```csharp
public sealed class AnthropicOptions { public string? ApiKey { get; set; } public string? BaseUrl { get; set; } public string Model { get; set; } = "claude-sonnet-5-5"; public int MaxTokens { get; set; } = 4000; }
public sealed class ClaudeEnrichmentProvider(IOptions<AnthropicOptions> options, ILogger<ClaudeEnrichmentProvider> log) : IEnrichmentProvider {
  public bool IsEnabled { get; }   // !string.IsNullOrWhiteSpace(options.Value.ApiKey)
  public Task<EnrichmentResult> EnrichAsync(EnrichmentRequest request, CancellationToken ct = default);
}
public static class EnrichmentPrompt { public const string System = "..."; public static string User(EnrichmentRequest r); public static Dictionary<string, JsonElement> OutputSchema(); }
```
- Request shape:
  - `new AnthropicClient { ApiKey = o.ApiKey, BaseUrl = o.BaseUrl ?? default }`, built lazily and only when enabled.
  - `client.Messages.Create(new MessageCreateParams { Model = o.Model, MaxTokens = o.MaxTokens, System = EnrichmentPrompt.System, Messages = [user], OutputConfig = new OutputConfig { Effort = Effort.Low, Format = new JsonOutputFormat { Schema = EnrichmentPrompt.OutputSchema() } } })`.
  - No `temperature`/`top_p`: they are rejected by the API. No `thinking` field: the default is adaptive. No prefill.
- Output schema: `{ type: object, additionalProperties: false, required: [fields], properties: { fields: { type: array, items: { type: object, additionalProperties: false, required: [target, value, sourcePath], properties: { target: {type: string}, value: {type: string}, sourcePath: {type: string} } } } } }`.
- System prompt (exact intent): "You re-map facts that are literally present in the given product JSON. Never infer, translate, estimate or add facts. For every field, give sourcePath as the RFC 6901 JSON pointer of the string that contains the value. Only fill these targets: … If a target is not stated in the JSON, omit it."
- Outcomes:
  - `StopReason` `refusal` or `max_tokens` → `EnrichmentResult([], "refusal" | "max_tokens")`.
  - Any SDK exception → logged at Warning → `EnrichmentResult([], "error:{type}")`. Enrichment is optional, so a failure never fails the record.
- The server-side refusal fallback is deliberately not enabled: a refusal only costs optional enrichment.

- [ ] **Step 1: Write the failing tests.** The fixture is a hand-authored Messages API response in the documented wire shape: `content[0].text` holds `{"fields":[{"target":"attributes.color","value":"Red","sourcePath":"/name"},{"target":"attributes.weight","value":"2 kg","sourcePath":"/name"}]}`, with `stop_reason: "end_turn"`.
```csharp
[Fact] public async Task Parses_Recorded_Response_And_Sends_Structured_Output_Request()
{
    using var s = await FixtureHttpServer.StartAsync(("127.0.0.1", "/v1/messages", "application/json", Fixture("anthropic-enrichment-response.json"), 200));
    var result = await Provider(apiKey: "test-key", baseUrl: s.Url("")).EnrichAsync(Request(missing: ["attributes.color", "attributes.weight"]));
    result.Fields.Should().HaveCount(2);
    var body = JsonNode.Parse(s.Requests.Single().Body)!;
    body["model"]!.GetValue<string>().Should().Be("claude-sonnet-5-5");
    body["output_config"]!["format"]!["type"]!.GetValue<string>().Should().Be("json_schema");
    body["output_config"]!["format"]!["schema"]!["properties"]!["fields"]!["items"]!["required"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Contain("sourcePath");
    body.AsObject().ContainsKey("temperature").Should().BeFalse();
    s.Requests.Single().Headers["x-api-key"].Should().Be("test-key");
}
[Fact] public async Task Recorded_Invented_Field_Is_Dropped_By_The_Merger()
{
    // Document name = "Acme Drill Red"; the fixture's "2 kg" at /name is invented
    var merged = EnrichmentMerger.Merge(Product(), Doc("Acme Drill Red"), await RecordedResult());
    merged.Product.ProductAttributes.Keys.Should().Equal("color"); merged.Discarded.Should().ContainSingle(d => d.StartsWith("attributes.weight"));
}
[Fact] public async Task Disabled_Without_Key_Makes_No_Call()
{
    using var s = await FixtureHttpServer.StartAsync();
    var p = Provider(apiKey: null, baseUrl: s.Url(""));
    p.IsEnabled.Should().BeFalse();
    (await p.EnrichAsync(Request())).SkippedReason.Should().Be("disabled"); s.Requests.Should().BeEmpty();
}
[Fact] public async Task Refusal_Yields_No_Fields() { /* fixture variant with stop_reason "refusal" → Fields empty, SkippedReason "refusal" */ }
[Fact] public async Task Http_500_Yields_No_Fields_Not_Exception() { /* server 500 ×3 → SkippedReason starts with "error:" */ }
```
- [ ] **Step 2: Run** `UT ClaudeEnrichmentProviderTests`. Expected: FAIL.
- [ ] **Step 3: Implement.** Register `IEnrichmentProvider` as `ClaudeEnrichmentProvider` when `Anthropic:ApiKey` is set, else `DisabledEnrichmentProvider`. Extend `FixtureHttpServer` to capture request bodies and headers. If a type name in the request shape differs in the pinned SDK version, follow the compiler error to the SDK's name; the wire assertions above are the contract.
- [ ] **Step 4: Run** `UT ClaudeEnrichmentProviderTests`. Expected: PASS.
- [ ] **Step 5: Commit.** Subject: `feat(sourcing): Claude enrichment adapter with structured JSON output`

---

### Task 15: Matching: Catalog match port (`pg_trgm`) and `RecordMatcher`

**Files:**
- Create: `src/Modules/Catalog/Application/Sourcing/ICatalogMatchPort.cs`, `src/Modules/Catalog/Adapter/Outbound/Sourcing/CatalogMatcher.cs`, migration `AddProductTitleTrigramIndex`
- Create: `Application/Ports/Outbound/ICatalogMatcher.cs`, `Application/Matching/RecordMatcher.cs`, `Adapter/Outbound/Gateways/CatalogMatcherGateway.cs`
- Test: `tests/UnitTests/Sourcing/RecordMatcherTests.cs`, `tests/IntegrationTests/Sourcing/CatalogMatcherTests.cs`

**Interfaces:**
- Produces (Catalog):
```csharp
public interface ICatalogMatchPort {
  Task<IReadOnlyList<CatalogExactMatch>> FindExactAsync(CatalogMatchKeys keys, CancellationToken ct = default); // one row per key type that hit
  Task<IReadOnlyList<CatalogFuzzyCandidate>> FindFuzzyAsync(long brandId, string title, double minSimilarity, int limit, CancellationToken ct = default);
  Task<CatalogProductSnapshot?> GetProductSnapshotAsync(long productId, CancellationToken ct = default);
}
public sealed record ProductIdentifierRef(string Type, string Value);
public sealed record CatalogMatchKeys(IReadOnlyList<ProductIdentifierRef> GlobalIds, long? BrandId, string? Mpn, string? SourceDomain, string? SourceId);
public sealed record CatalogExactMatch(long ProductId, string MatchedBy); // "gtin" | "brand+mpn" | "source"
public sealed record CatalogFuzzyCandidate(long ProductId, string Title, double Similarity);
public sealed record CatalogVariantSnapshot(long VariantId, string Sku, string SignatureHash, IReadOnlyDictionary<string, string> Attributes, bool IsActive, bool DeactivatedByAdmin);
public sealed record CatalogProductSnapshot(long ProductId, string Title, string? ShortDescription, string? Description, long BrandId, string BrandName,
  long PrimaryCategoryId, string Status, bool DeactivatedByAdmin, IReadOnlyList<ProductIdentifierRef> Identifiers, IReadOnlyList<CatalogVariantSnapshot> Variants);
```
- Produces (Sourcing): `ICatalogMatcher` (a local mirror), and
```csharp
public sealed class RecordMatcher(ICatalogMatcher catalog) {
  public const double FuzzyThreshold = 0.6;
  public Task<(MatchResult Match, IReadOnlyList<ValidationIssue> Issues)> MatchAsync(NormalizedProduct product, RawListing listing, CancellationToken ct = default);
}
```
- Algorithm:
  1. Query exact matches with all keys at once.
  2. If they name **more than one** distinct product → `Ambiguous` plus issue `AMBIGUOUS_MATCH` (Review).
  3. If they name exactly one → `ExactMatch(productId, MatchedBy = highest-priority key: gtin > brand+mpn > source)`.
  4. Otherwise, if `BrandId` is known, query fuzzy candidates at `similarity ≥ 0.6`, same brand, limit 5. Any hit → `Fuzzy(candidates)` plus issue `FUZZY_MATCH` (Review). Fuzzy candidates are **never** auto-merged.
  5. Otherwise → `New`.
- Variant matching is done by Catalog inside the publishing port on `(product, signature hash)`. Sourcing only displays it, in the diff (Task 20).
- Catalog adapter: `EF.Functions.TrigramsSimilarity(p.Title, title) >= min && p.BrandId == brandId`, ordered descending. The migration adds `CREATE INDEX ix_products_title_trgm ON catalog.products USING gin (title gin_trgm_ops)`.

- [ ] **Step 1: Write the failing tests.**
```csharp
// RecordMatcherTests (fake ICatalogMatcher)
[Fact] public async Task Gtin_Beats_Mpn_When_Same_Product() => (await Match(exact: [(5, "brand+mpn"), (5, "gtin")])).Match.Should().Be(new MatchResult(MatchKind.ExactMatch, 5, "gtin", []));
[Fact] public async Task Conflicting_Exact_Keys_Are_Ambiguous() => (await Match(exact: [(5, "gtin"), (6, "brand+mpn")])).Issues.Should().Contain(i => i.Code == "AMBIGUOUS_MATCH");
[Fact] public async Task Fuzzy_Goes_To_Review_Never_Merged()
{
    var (m, issues) = await Match(exact: [], fuzzy: [(9, "Acme Drill 18V", 0.72)]);
    m.Kind.Should().Be(MatchKind.Fuzzy); m.ProductId.Should().BeNull(); issues.Should().Contain(i => i.Code == "FUZZY_MATCH");
}
[Fact] public async Task Passes_Threshold_And_Brand() { await Match(brandId: 7); catalog.LastFuzzyCall.Should().Be((7L, "Acme Cordless Drill", 0.6, 5)); }
[Fact] public async Task No_Brand_No_Fuzzy() { await Match(brandId: null); catalog.FuzzyCalls.Should().Be(0); }

// CatalogMatcherTests (Postgres; seed via phase 2–3 Catalog admin services)
[Fact] public async Task Similar_Title_Same_Brand_Is_Candidate() => (await port.FindFuzzyAsync(acme, "Acme Cordless Drill 18 V Kit", 0.6, 5)).Should().ContainSingle(c => c.ProductId == drillId);
[Fact] public async Task Same_Title_Other_Brand_Is_Not() => (await port.FindFuzzyAsync(bosch, "Acme Cordless Drill 18V", 0.6, 5)).Should().BeEmpty();
[Fact] public async Task Exact_By_Gtin_And_Source() { /* identifiers seeded on product → FindExactAsync returns "gtin" and "source" rows for same id */ }
[Fact] public async Task Fuzzy_Query_Uses_The_Trigram_Index() { /* EXPLAIN on the generated SQL contains "ix_products_title_trgm" after ANALYZE with ≥ 1000 seeded rows */ }
```
- [ ] **Step 2: Run** `UT RecordMatcherTests` and `IT CatalogMatcherTests`. Expected: FAIL.
- [ ] **Step 3: Implement** the port, the gateway and the matcher.
- [ ] **Step 4: Run** both, then `AT`. Expected: PASS.
- [ ] **Step 5: Commit.** Subject: `feat(sourcing): exact and pg_trgm fuzzy matching; fuzzy always routes to review`

---

### Task 16: Validation rules (spec §5.5 step 6)

**Files:**
- Create: `Application/Validation/RecordValidator.cs`, `Application/Domain/StagedRecords/ValidationCodes.cs`
- Test: `tests/UnitTests/Sourcing/RecordValidatorTests.cs`

**Interfaces:**
- Produces:
```csharp
public static class ValidationCodes { // value = name; severity in brackets
  NO_STRUCTURED_DATA[E], SOURCE_BLOCKED[E], GROUP_KEY_NOT_CONTIGUOUS[E], TITLE_MISSING[E], BRAND_MISSING[E], CATEGORY_UNRESOLVED[R],
  CATEGORY_INACTIVE[R], REQUIRED_ATTRIBUTE_MISSING[E], ATTRIBUTE_VALUE_INVALID[R], PRICE_ABSENT[E], PRICE_INVALID[E],
  CURRENCY_UNSUPPORTED[E], IMAGE_MISSING[E], RIGHTS_DENIED[E], IMAGE_RIGHTS_DENIED[E], RIGHTS_UNKNOWN[R],
  SOURCE_UNAVAILABLE[E], VARIANT_SIGNATURE_DUPLICATE[E], FUZZY_MATCH[R], AMBIGUOUS_MATCH[R], IDENTIFIER_INVALID[W], PUBLISH_FAILED[R]
}
public static class SupportedCurrencies { public static IReadOnlySet<string> All { get; } = new HashSet<string> { "USD", "EUR", "SGD", "AED", "GBP" }; }
public sealed record StagedImage(string Key, string ContentType, string Sha256, string SourceUrl);
public static class RecordValidator {
  public static IReadOnlyList<ValidationIssue> Validate(RawListing listing, NormalizedProduct product, IReadOnlyList<AttributeRef> categoryAttributes,
    IReadOnlyList<StagedImage> images, RightsDecision rights, IReadOnlyList<ValidationIssue> priorIssues);
}
```
- Rules:
  - Required attributes are checked only when a category resolved. A product-level or every-variant value counts.
  - Each variant needs a price (`PRICE_ABSENT`/`PRICE_INVALID`) and a supported currency.
  - Availability `OutOfStock`, `Discontinued` or `SoldOut` (schema.org IRI suffix) → `SOURCE_UNAVAILABLE`. When availability is absent the rule does not apply.
  - Images: `ContentDenied` → `RIGHTS_DENIED`; `ImagesDenied` → `IMAGE_RIGHTS_DENIED` plus `IMAGE_MISSING`; `Unknown` → `RIGHTS_UNKNOWN`; zero staged images → `IMAGE_MISSING`.
  - `priorIssues` (normalizer and matcher) are carried through, de-duplicated by `(Code, Detail)`.

- [ ] **Step 1: Write the failing tests.** One `[Fact]` per code, each building the minimal failing input and asserting `Contain(new ValidationIssue(code, severity, detail))`. Plus:
```csharp
[Fact] public void Clean_Record_Has_No_Issues() => RecordValidator.Validate(Listing(), GoodProduct(), Attrs(), [Image()], Allowed, []).Should().BeEmpty();
[Fact] public void No_Policy_Is_Review_Never_Valid() => StagedRecord.DeriveStatus(MatchResult.New, RecordValidator.Validate(Listing(), GoodProduct(), Attrs(), [Image()], Unknown, [])).Should().Be(StagedRecordStatus.NeedsReview);
[Fact] public void Unsupported_Currency() => Validate(GoodProduct(currency: "JPY")).Should().Contain(i => i.Code == "CURRENCY_UNSUPPORTED");
[Fact] public void Out_Of_Stock_Is_Invalid() => Validate(GoodProduct(availability: "https://schema.org/OutOfStock")).Should().Contain(i => i.Code == "SOURCE_UNAVAILABLE");
[Fact] public void Required_Attribute_Missing_Names_The_Key() => Validate(GoodProduct(), attrs: [Req("voltage")]).Should().Contain(new ValidationIssue("REQUIRED_ATTRIBUTE_MISSING", IssueSeverity.Error, "voltage"));
```
- [ ] **Step 2: Run** `UT RecordValidatorTests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `UT RecordValidatorTests`. Expected: PASS.
- [ ] **Step 5: Commit.** Subject: `feat(sourcing): record validation rules incl. rights and availability`

---

### Task 17: Image staging and catalog-media publishing

**Files:**
- Create: `Application/Ports/Outbound/{IImageStager,IMediaPublisher}.cs`, `Adapter/Outbound/Media/{ObjectStorageImageStager,CatalogMediaPublisher}.cs`
- Test: `tests/UnitTests/Sourcing/ImageStagerTests.cs`

**Interfaces:**
- Produces:
```csharp
public interface IImageStager { Task<IReadOnlyList<StagedImage>> StageAsync(IReadOnlyList<string> imageUrls, int max, CancellationToken ct = default); } // max 10 per record
public interface IMediaPublisher { Task<string> PublishAsync(StagedImage image, CancellationToken ct = default); } // returns catalog-media key
```
- The stager downloads each URL through the `"sourcing-crawler"` client, so the SSRF guard and size cap apply. It sniffs the type with `IFileTypeDetector` and accepts only Jpeg/Png/Webp/Gif, ignoring the declared content type. Keys are content-addressed: `images/{sha256}.{ext}` in `import-staging`. `ExistsAsync` short-circuits the put. Failures are skipped per image and never throw.
- The publisher maps to `products/{sha256}.{ext}` in `catalog-media` via `CopyAsync`, guarded by `ExistsAsync`. It is idempotent.

- [ ] **Step 1: Write the failing tests.**
```csharp
[Fact] public async Task Stages_Png_Content_Addressed() { var imgs = await Stager().StageAsync([server.Url("/img/pixel.png")], 10); imgs.Single().Key.Should().MatchRegex("^images/[0-9a-f]{64}\\.png$"); storage.Keys(StorageBuckets.ImportStaging).Should().ContainSingle(); }
[Fact] public async Task Html_Served_As_Image_Is_Rejected() => (await Stager().StageAsync([server.Url("/fake.png")], 10)).Should().BeEmpty(); // body is HTML, Content-Type image/png
[Fact] public async Task Same_Image_Twice_Stored_Once() { await Stager().StageAsync([U1, U1Copy], 10); storage.PutCount.Should().Be(1); }
[Fact] public async Task Oversize_Image_Skipped() { /* MaxImageBytes = 10 → empty */ }
[Fact] public async Task Publish_Copies_Once_Into_Catalog_Media() { var k = await Publisher().PublishAsync(img); await Publisher().PublishAsync(img); k.Should().Be($"products/{img.Sha256}.png"); storage.CopyCount.Should().Be(1); }
```
- [ ] **Step 2: Run** `UT ImageStagerTests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `UT ImageStagerTests`. Expected: PASS.
- [ ] **Step 5: Commit.** Subject: `feat(sourcing): content-addressed image staging and catalog-media publishing`

---

### Task 18: Publishing gateways and `RecordPublisher`

**Files:**
- Create: `Application/Ports/Outbound/{ICatalogPublisher,IPricePublisher,IStorefrontRefresher}.cs`, `Application/Pipeline/RecordPublisher.cs`, `Adapter/Outbound/Gateways/{CatalogPublishingGateway,PricePublishingGateway,StorefrontRefreshGateway}.cs`
- Test: `tests/UnitTests/Sourcing/RecordPublisherTests.cs`, `tests/UnitTests/Sourcing/CatalogPublishingGatewayTests.cs`

**Interfaces:**
- Consumes: `ICatalogPublishingPort.UpsertProductAsync(PublishProductCommand)`, `IPricePublishingPort.SetReferencePriceAsync(long, long, string, PriceProvenance)`, `IStorefrontRefreshTrigger.RequestRefreshAsync()`
- Produces:
```csharp
public sealed record ProductPublishRequest(long? TargetProductId, string BrandName, IReadOnlyList<string> BrandAliases, long? BrandId,
  long CategoryId, string Title, string? ShortDescription, string? Description, IReadOnlyList<(string Type, string Value)> Identifiers, string? Mpn,
  IReadOnlyList<PublishMedia> Media, IReadOnlyList<PublishVariant> Variants, string SourceDomain, string? SourceId, long SourceListingId);
public sealed record PublishMedia(string ObjectKey, int SortOrder, string? Alt, string SourceUrl, string UsageRights);
public sealed record PublishVariant(IReadOnlyDictionary<string, string> Attributes, string SignatureHash, string? Sku);
public sealed record CatalogPublishOutcome(long ProductId, bool Created, bool SkippedBecauseDeactivated, IReadOnlyList<long> VariantIdsInRequestOrder);
public interface ICatalogPublisher { Task<Result<CatalogPublishOutcome>> UpsertAsync(ProductPublishRequest request, CancellationToken ct = default); }
public sealed record PriceProvenanceInfo(long PriceObservationId, string SourceUrl, DateTimeOffset CollectedAt, long OriginalAmountMinor, string OriginalCurrency, bool? TaxIncluded);
public interface IPricePublisher { Task<Result> SetReferencePriceAsync(long variantId, long amountMinor, string currency, PriceProvenanceInfo provenance, CancellationToken ct = default); }
public interface IStorefrontRefresher { Task RequestRefreshAsync(CancellationToken ct = default); }
public sealed class RecordPublisher {
  /// Publishes one Valid record. Returns false (and records PUBLISH_FAILED) on a port failure. Caller saves the record.
  public Task<bool> PublishAsync(StagedRecord record, RawListing listing, NormalizedProduct product, IReadOnlyList<StagedImage> images, RightsDecision rights, CancellationToken ct = default);
}
```
- `RecordPublisher.PublishAsync` order. Every step is idempotent, so an at-least-once re-run is safe:
  1. Upsert the `SourceListing` via `ISourceListingRepository.FindAsync` (refresh, or create with a new id). Call `Observe` per variant with `VariantKey = SignatureHash` and `CollectedAt = listing.CollectedAt`, then `ISourcingPersistence.SaveAsync()`. On `SOURCING_UNIQUE_CONFLICT`, `DiscardChanges`, reload and retry once.
  2. `IMediaPublisher.PublishAsync` for each image, in order.
  3. `ICatalogPublisher.UpsertAsync`:
     - `TargetProductId` is set when `Match.Kind == ExactMatch && MatchedBy == "admin-merge"`.
     - `CategoryId` = the record's category override or the resolved category.
     - `UsageRights` = `rights.UsageRightsLabel`, or `"admin-approved:{RightsApprovedBy}"` when approved.
  4. For each variant with its `CatalogPublishOutcome` id, call `SetReferencePriceAsync(variantId, PriceMinor, Currency, provenance(observation))`. This happens even when `SkippedBecauseDeactivated`: fields update and nothing reactivates (spec §5.5 step 7).
  5. `record.MarkPublished(...)`.
  A failure in 3 or 4 leads to `record.MarkPublishFailed(error.Code)`. The Catalog write is not rolled back (AGENTS rule 7). A later publish converges it.
- `CatalogPublishingGateway` maps `ProductPublishRequest` to `PublishProductCommand` field by field, using the names phase 2–3 defined. It then maps `PublishedProduct.Variants` back to request order by recomputing each requested `SignatureHash` with Catalog's signature function. It also carries `TargetProductId` (cross-phase addition, see report).

- [ ] **Step 1: Write the failing tests.**
```csharp
[Fact] public async Task Publishes_Listing_Media_Product_Then_Prices_With_Observation_Provenance()
{
    (await Publisher().PublishAsync(ValidRecord(), Listing(), Product(variants: 2), [Img()], Allowed)).Should().BeTrue();
    catalog.Requests.Single().Media.Single().ObjectKey.Should().StartWith("products/");
    prices.Calls.Should().HaveCount(2);
    prices.Calls[0].Provenance.Should().Be(new PriceProvenanceInfo(listings.Single().Observations.First().Id, "https://shop.example/p/drill", Collected, 12990, "SGD", true));
    record.Status.Should().Be(StagedRecordStatus.Published);
}
[Fact] public async Task Deactivated_Product_Is_Updated_But_Flag_Is_Kept()
{
    catalog.NextOutcome = new CatalogPublishOutcome(5, false, SkippedBecauseDeactivated: true, [51]);
    await Publisher().PublishAsync(ValidRecord(), Listing(), Product(), [Img()], Allowed);
    record.PublishSkippedDeactivated.Should().BeTrue(); prices.Calls.Should().ContainSingle();
}
[Fact] public async Task Catalog_Failure_Routes_To_Review_And_Sets_No_Price()
{
    catalog.NextError = new Error("VALIDATION_FAILED", "x", 400);
    (await Publisher().PublishAsync(ValidRecord(), Listing(), Product(), [Img()], Allowed)).Should().BeFalse();
    record.Status.Should().Be(StagedRecordStatus.NeedsReview); prices.Calls.Should().BeEmpty();
}
[Fact] public async Task Admin_Merge_Sends_Target_Product() { /* record.MergeInto(…, 42) → request.TargetProductId == 42 */ }
[Fact] public async Task Republish_Does_Not_Duplicate_Observation() { /* publish twice → listings.Single().Observations.Count == variants */ }
// CatalogPublishingGatewayTests
[Fact] public async Task Maps_Variant_Ids_Back_To_Request_Order_By_Signature() { /* port returns variants in reverse order → VariantIdsInRequestOrder matches request */ }
```
- [ ] **Step 2: Run** `UT RecordPublisherTests`, `UT CatalogPublishingGatewayTests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** both. Expected: PASS.
- [ ] **Step 5: Commit.** Subject: `feat(sourcing): publishing through Catalog and Pricing ports with provenance`

---

### Task 19: The pipeline: staging, processing, checkpoints and the `ImportJobStep` handler

**Files:**
- Create: `Application/Pipeline/{StagingService,RecordProcessor,ImportPipeline}.cs`, `Application/Ports/Inbound/IImportPipeline.cs`, `Application/Ports/Outbound/IImportStepScheduler.cs`, `Adapter/Outbound/Jobs/{ImportJobStepPayload,ImportJobStepHandler,ImportStepScheduler}.cs`
- Test: `tests/UnitTests/Sourcing/ImportPipelineTests.cs`

**Interfaces:**
- Consumes: `IJobHandler<TPayload>`, `JobContext.HeartbeatAsync`, `JobOutcome`, `IJobQueue.EnqueueAsync`, `JobTypes.ImportJobStep`
- Produces:
```csharp
public enum ImportStepKind { Run, PublishValid }
public sealed record ImportJobStepPayload(long ImportJobId, ImportStepKind Kind);
public sealed record ImportStepResult(bool MoreWork, long NextStepSequence);
public interface IImportPipeline { Task<ImportStepResult> RunStepAsync(long importJobId, ImportStepKind kind, CancellationToken ct = default); }
public interface IImportStepScheduler { Task ScheduleAsync(long importJobId, ImportStepKind kind, long stepSequence, CancellationToken ct = default); }
// ImportStepScheduler: IJobQueue.EnqueueAsync(JobTypes.ImportJobStep, payload, runAt: null, dedupeKey: $"import:{jobId}:{kind}:{stepSequence}")
public sealed class ImportJobStepHandler(IImportPipeline pipeline, IImportStepScheduler scheduler) : IJobHandler<ImportJobStepPayload> {
  public string Type => JobTypes.ImportJobStep;
  public Task<JobOutcome> HandleAsync(ImportJobStepPayload payload, JobContext context); // heartbeat every 30 s while the step runs
}
```
- `RunStepAsync(Run)`, one step:
  1. Load the job. If `!AcceptsWork`, return `MoreWork = false`.
  2. `MarkRunning` and save.
  3. **Discover phase.** Pick the `ISourceAdapter` where `Supports(mode)`. Enumerate from `Checkpoint.AdapterCursor` until `BatchSize` (50) listings are read or the stream ends. `StagingService.StageAsync(job, batch)`:
     - Load existing records by fingerprint. `Stage` new ones; `Restage` existing ones, counting `Duplicate` only for `RestageOutcome.Duplicate` and `Discovered`/`Extracted` only for `NewInThisJob`/`Duplicate`.
     - Save. On a conflict, `DiscardChanges` and retry once.
     - **Then**, in a second save, `AdvanceCheckpoint(cursor = last listing.Checkpoint)`. When the stream has ended, the phase becomes `Process` with `AdapterCursor = null`.
  4. **Process phase.** Take `GetNextForJobAsync(job, Staged, afterId: Checkpoint.ProcessedThroughRecordId, take: 50)`. For each record, `RecordProcessor.ProcessAsync`:
     - Normalize, then enrich if enabled and targets are missing, then merge.
     - Stage images, look up the policy for the domain, take the `RightsDecision`, match, validate, and call `RecordProcessing`. Save.
     - If the record is `Valid` and the policy is `AutoPublishValidated`, **re-read the job status with `GetStatusAsync`**; if it is not `Running`, stop the batch. Otherwise `RecordPublisher.PublishAsync` and save.
     - After the batch: if anything was published, call `IStorefrontRefresher.RequestRefreshAsync()`. Then `AdvanceCheckpoint(ProcessedThroughRecordId = last id)` with counters from `CountForJobAsync`. An empty batch → `Complete`.
  5. `SourcingConfigurationException` → `job.Fail(error.Code, error.Message)`, `MoreWork = false`.
- `RunStepAsync(PublishValid)`: works on `Valid` records of the job in batches of 50, with the same status re-check and refresh. It applies whatever the job's policy (it is the admin's explicit "publish valid").
- Rule 7: every save covers one aggregate, or a batch of independent `StagedRecord`s. The job's checkpoint is saved separately **after** staging. That is safe because staging is idempotent by fingerprint.

- [ ] **Step 1: Write the failing tests** (fakes for every port; an in-memory repository set; `FakeSourceAdapter` yields N listings with checkpoints `"1".."N"`).
```csharp
[Fact] public async Task Discover_Stages_In_Batches_Of_50_And_Checkpoints()
{
    var r = await pipeline.RunStepAsync(job.Id, ImportStepKind.Run);
    staged.Count.Should().Be(50); job.Checkpoint.AdapterCursor.Should().Be("50"); r.MoreWork.Should().BeTrue();
}
[Fact] public async Task Runs_To_Completion_And_Auto_Publishes_Only_Valid()
{
    adapter.Listings = [Good("a"), Good("b"), InvalidPrice("c"), NoPolicy("d")];
    await RunUntilIdle();
    job.Status.Should().Be(ImportJobStatus.Completed);
    job.Counters.Should().Be(new ImportCounters(Discovered: 4, Extracted: 4, Valid: 0, Invalid: 1, Duplicate: 0, Published: 2, Review: 1));
    catalog.Requests.Should().HaveCount(2); refresher.Calls.Should().Be(1);
}
[Fact] public async Task ManualReview_Policy_Publishes_Nothing_Until_PublishValid()
{
    await RunUntilIdle(policy: PublishPolicy.ManualReview);
    catalog.Requests.Should().BeEmpty(); job.Counters.Valid.Should().Be(2);
    await RunUntilIdle(kind: ImportStepKind.PublishValid); catalog.Requests.Should().HaveCount(2);
}
[Fact] public async Task Cancel_Mid_Batch_Stops_Publishing()
{
    catalog.OnUpsert = n => { if (n == 1) jobs.ForceStatus(job.Id, ImportJobStatus.Cancelled); };
    await RunUntilIdle(); catalog.Requests.Should().HaveCount(1);
}
[Fact] public async Task Paused_Job_Does_No_Work() { job.Pause(Now); (await pipeline.RunStepAsync(job.Id, ImportStepKind.Run)).MoreWork.Should().BeFalse(); adapter.Enumerations.Should().Be(0); }
[Fact] public async Task Crash_After_Staging_Before_Checkpoint_Restages_Without_Duplicates_Or_Double_Counts()
{
    persistence.FailNthSave(2);                       // the checkpoint save of step 1
    await FluentActions.Awaiting(() => pipeline.RunStepAsync(job.Id, ImportStepKind.Run)).Should().ThrowAsync<Exception>();
    await RunUntilIdle();
    staged.Select(s => s.Fingerprint).Should().OnlyHaveUniqueItems(); job.Counters.Discovered.Should().Be(adapter.Listings.Count); job.Counters.Duplicate.Should().Be(0);
}
[Fact] public async Task WebSearch_Without_Key_Fails_Job_With_Clear_Error()
{
    adapter.Throw(new SourcingConfigurationException(SourcingErrors.SearchProviderNotConfigured));
    await pipeline.RunStepAsync(job.Id, ImportStepKind.Run);
    job.Status.Should().Be(ImportJobStatus.Failed); job.ErrorCode.Should().Be("SEARCH_PROVIDER_NOT_CONFIGURED");
}
[Fact] public async Task Handler_Schedules_Next_Step_With_Unique_Dedupe_Key()
{
    await handler.HandleAsync(new(job.Id, ImportStepKind.Run), Ctx());
    queue.Enqueued.Single().DedupeKey.Should().Be($"import:{job.Id}:Run:1");
}
```
- [ ] **Step 2: Run** `UT ImportPipelineTests`. Expected: FAIL.
- [ ] **Step 3: Implement.** Register with `services.AddJobHandler<ImportJobStepHandler, ImportJobStepPayload>()`. `BatchSize` comes from `Sourcing:BatchSize`.
- [ ] **Step 4: Run** `UT ImportPipelineTests`. Expected: PASS. **Mutation check:** move the checkpoint save before the staging save, and watch `Crash_After_Staging_…` fail; restore. Remove the status re-read, and watch `Cancel_Mid_Batch_…` fail; restore.
- [ ] **Step 5: Commit.** Subject: `feat(sourcing): checkpointed, resumable import pipeline and ImportJobStep handler`

---

### Task 20: Application services: jobs, files and preview, review queue, source policies

**Files:**
- Create: `Application/Ports/Inbound/*.cs`, `Application/Services/*.cs`, `Application/DependencyInjection.cs`
- Test: `tests/UnitTests/Sourcing/{ImportJobServiceTests,ImportFileServiceTests,StagedRecordReviewServiceTests,SourcePolicyServiceTests}.cs`

**Interfaces:**
- Consumes: `IAuditTrail.Record(AuditActor, string, string, string, object?, object?)`, `AdminActor.ToAudit()`
- Produces:
```csharp
public sealed record CreateImportJobCommand(ImportMode Mode, IReadOnlyList<string> Sources, IReadOnlyList<string> Queries, long? TargetCategoryId,
  IReadOnlyList<string> IncludeDomains, IReadOnlyList<string> ExcludeDomains, int? MaxPages, int? MaxDepth, int? MaxRecords,
  PublishPolicy? PublishPolicy, bool FileRightsAttested);
public interface IImportJobService {
  Task<Result<long>> CreateAsync(CreateImportJobCommand command, AdminActor actor, CancellationToken ct = default); // non-File: Queued + scheduled
  Task<Result> StartAsync(long jobId, AdminActor actor, CancellationToken ct = default);   // File Draft → Queued; WebSearch/Combined fail fast with SEARCH_PROVIDER_NOT_CONFIGURED
  Task<Result> PauseAsync(long jobId, AdminActor actor, CancellationToken ct = default);
  Task<Result> ResumeAsync(long jobId, AdminActor actor, CancellationToken ct = default);
  Task<Result> CancelAsync(long jobId, AdminActor actor, CancellationToken ct = default);
  Task<Result> PublishValidAsync(long jobId, AdminActor actor, CancellationToken ct = default);
}
public sealed record MappingPreview(IReadOnlyList<string> Columns, IReadOnlyList<string> UnmappedColumns, IReadOnlyList<string> MissingRequiredFields, IReadOnlyList<PreviewRow> Rows);
public sealed record PreviewRow(int RowNumber, JsonObject Document, IReadOnlyList<ValidationIssue> Issues);
public interface IImportFileService {
  Task<Result> UploadAsync(long jobId, Stream content, string fileName, long length, AdminActor actor, CancellationToken ct = default); // → import-staging uploads/{jobId}/{id}.{ext}
  Task<Result<MappingPreview>> PreviewAsync(long jobId, ImportColumnMapping? mapping, int take = 20, CancellationToken ct = default); // null mapping = header-name identity
  Task<Result> SetMappingAsync(long jobId, ImportColumnMapping mapping, AdminActor actor, CancellationToken ct = default);
}
public sealed record ReviewItemOutcome(long RecordId, bool Succeeded, string? ErrorCode);
public sealed record StagedRecordDiff(long RecordId, long? ProductId, IReadOnlyList<FieldDiff> Fields, IReadOnlyList<VariantDiff> Variants);
public sealed record FieldDiff(string Field, string? Incoming, string? Current, string? SourcePath);
public sealed record VariantDiff(string SignatureHash, IReadOnlyDictionary<string, string> Attributes, long? ExistingVariantId, long? IncomingPriceMinor, string? Currency, bool ExistingDeactivatedByAdmin);
public interface IStagedRecordReviewService {
  Task<Result<StagedRecordDiff>> GetDiffAsync(long recordId, long? againstProductId = null, CancellationToken ct = default); // default: matched product or first fuzzy candidate
  Task<Result> ApproveAsync(long recordId, long? categoryId, AdminActor actor, CancellationToken ct = default);   // Approve + publish now
  Task<Result> MergeAsync(long recordId, long productId, AdminActor actor, CancellationToken ct = default);       // MergeInto + publish now
  Task<Result> CreateNewAsync(long recordId, AdminActor actor, CancellationToken ct = default);                  // ChooseCreateNew + publish now
  Task<Result> RejectAsync(long recordId, string reason, AdminActor actor, CancellationToken ct = default);
  Task<Result<IReadOnlyList<ReviewItemOutcome>>> BulkApproveAsync(IReadOnlyList<long> recordIds, AdminActor actor, CancellationToken ct = default); // ≤ 100, per-item outcomes
}
public sealed record SourcePolicyInput(string Domain, bool CrawlAllowed, bool ContentUseAllowed, bool ImageUseAllowed, string? Notes);
public interface ISourcePolicyService {
  Task<Result<long>> CreateAsync(SourcePolicyInput input, AdminActor actor, CancellationToken ct = default);   // 409 SOURCE_POLICY_DOMAIN_CONFLICT
  Task<Result> UpdateAsync(long id, SourcePolicyInput input, AdminActor actor, CancellationToken ct = default); // domain immutable
  Task<Result> DeleteAsync(long id, AdminActor actor, CancellationToken ct = default);
}
```
- Every mutating service method follows the same pattern: catch `DomainException` → `Result` (rule 9); one `IAuditTrail.Record` in the same save (spec §6.4); one `SaveChangesAsync`.
  - Audit actions: `sourcing.job.create|start|pause|resume|cancel|publish-valid`, `sourcing.file.upload|mapping`, `sourcing.record.approve|merge|create-new|reject`, `sourcing.policy.create|update|delete`.
- Upload: at most `Sourcing:Files:MaxBytes` (`IMPORT_FILE_TOO_LARGE`, 413). Format by sniff plus extension: Xlsx needs `FileType.Xlsx`; Csv and Json need a `.csv`/`.json` name and no binary signature. Anything else → `IMPORT_FILE_UNSUPPORTED` (415).
- Review publish runs `RecordProcessor` re-validation first. A reviewer cannot publish a record whose hard errors appeared since staging.

- [ ] **Step 1: Write the failing tests.**
```csharp
[Fact] public async Task Create_Defaults_To_AutoPublishValidated_And_Schedules() { var id = (await svc.CreateAsync(Cmd(ImportMode.Crawl), Staff)).Value; jobs[id].PublishPolicy.Should().Be(PublishPolicy.AutoPublishValidated); scheduler.Calls.Should().ContainSingle(c => c.JobId == id && c.Kind == ImportStepKind.Run); audit.Actions.Should().Contain("sourcing.job.create"); }
[Fact] public async Task Start_WebSearch_Without_Brave_Key_Is_Clear_400() => (await svc.StartAsync(DraftOrQueued(ImportMode.WebSearch), Staff)).Error!.Code.Should().Be("SEARCH_PROVIDER_NOT_CONFIGURED");
[Fact] public async Task Domain_Exception_Becomes_Result_Not_Throw() => (await svc.CreateAsync(Cmd(ImportMode.Crawl, maxPages: 0), Staff)).Error!.Code.Should().Be("IMPORT_JOB_LIMIT_INVALID");
[Fact] public async Task Upload_Rejects_Pdf_Renamed_Csv() => (await files.UploadAsync(fileJob, Pdf(), "x.csv", 100, Staff)).Error!.Code.Should().Be("IMPORT_FILE_UNSUPPORTED");
[Fact] public async Task Preview_Reports_Missing_Required_And_Unmapped() { var p = (await files.PreviewAsync(fileJob, Map(("Name", "title")))).Value!; p.MissingRequiredFields.Should().Contain(["brand", "price", "currency", "image_urls"]); p.UnmappedColumns.Should().Contain("Colour"); p.Rows.Should().HaveCountLessThanOrEqualTo(20); }
[Fact] public async Task Preview_Row_Shows_Price_Issue() => (await files.PreviewAsync(fileJob, Identity)).Value!.Rows[1].Issues.Should().Contain(i => i.Code == "PRICE_INVALID");
[Fact] public async Task Approve_Publishes_And_Audits() { await review.ApproveAsync(needsReview, 3, Staff); catalog.Requests.Should().ContainSingle(); audit.Actions.Should().Contain("sourcing.record.approve"); }
[Fact] public async Task Approve_Invalid_Record_Is_Refused() => (await review.ApproveAsync(invalidRecord, null, Staff)).Error!.Code.Should().Be("STAGED_RECORD_HAS_ERRORS");
[Fact] public async Task Merge_Targets_Chosen_Product() { await review.MergeAsync(fuzzyRecord, 42, Staff); catalog.Requests.Single().TargetProductId.Should().Be(42); }
[Fact] public async Task Bulk_Approve_Isolates_Failures() => (await review.BulkApproveAsync([ok1, invalidRecord, ok2], Staff)).Value!.Select(o => o.Succeeded).Should().Equal(true, false, true);
[Fact] public async Task Bulk_Approve_Caps_At_100() => (await review.BulkApproveAsync(Enumerable.Range(1, 101).Select(i => (long)i).ToList(), Staff)).Error!.Code.Should().Be("VALIDATION_FAILED");
[Fact] public async Task Diff_Shows_Incoming_Vs_Current() { var d = (await review.GetDiffAsync(exactRecord)).Value!; d.Fields.Should().Contain(f => f.Field == "title" && f.Incoming == "Acme Drill v2" && f.Current == "Acme Drill"); }
[Fact] public async Task Duplicate_Policy_Domain_Is_409() { await policies.CreateAsync(new("shop.example", true, true, true, null), Staff); (await policies.CreateAsync(new("WWW.shop.example", true, true, true, null), Staff)).Error!.HttpStatus.Should().Be(409); }
```
- [ ] **Step 2: Run** the four `UT` classes. Expected: FAIL.
- [ ] **Step 3: Implement.** `AddSourcingApplication()` registers the services as scoped, plus `StagingService`, `RecordProcessor`, `RecordPublisher`, `DeterministicNormalizer`, `RecordMatcher` and `ImportPipeline`. All services are `sealed` (rule 5).
- [ ] **Step 4: Run** the four. Expected: PASS.
- [ ] **Step 5: Commit.** Subject: `feat(sourcing): job, file/preview, review-queue and source-policy services`

---

### Task 21: Read side: `ISourcingAdminQueries`, including the dashboard counts

**Files:**
- Create: `QueryApplication/{ISourcingAdminQueries,SourcingAdminQueries,SourcingAdminDtos,DependencyInjection}.cs`
- Test: `tests/IntegrationTests/Sourcing/SourcingAdminQueriesTests.cs`

**Interfaces:**
- Produces (consumed by phase 6 Admin dashboard through `Admin.Adapter.Outbound → Sourcing.QueryApplication`):
```csharp
public sealed record ImportJobFilter(ImportJobStatus? Status, ImportMode? Mode, DateTimeOffset? CreatedFrom, DateTimeOffset? CreatedTo);
public sealed record ImportJobDto(long Id, string Mode, string Status, string PublishPolicy, IReadOnlyList<string> Sources, IReadOnlyList<string> Queries,
  long? TargetCategoryId, int MaxPages, int MaxDepth, int MaxRecords, int Discovered, int Extracted, int Valid, int Invalid, int Duplicate,
  int Published, int Review, string Phase, string? ErrorCode, string? ErrorMessage, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt);
public sealed record StagedRecordFilter(long? JobId, StagedRecordStatus? Status, string? Domain, string? IssueCode, string? Q);
public sealed record StagedRecordDto(long Id, long LastJobId, string Status, string SourceUrl, string? SourceDomain, string? Title, string? BrandName,
  string MatchKind, long? MatchedProductId, IReadOnlyList<string> IssueCodes, DateTimeOffset UpdatedAt);
public sealed record StagedRecordDetailDto(StagedRecordDto Summary, string RawJson, string? NormalizedJson, string MatchJson, string IssuesJson,
  IReadOnlyList<string> ImageKeys, Guid? ReviewedBy, long? PublishedProductId, bool PublishSkippedDeactivated);
public sealed record SourcePolicyDto(long Id, string Domain, bool CrawlAllowed, bool ContentUseAllowed, bool ImageUseAllowed, string? Notes, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);
public sealed record SourcingDashboardCounts(int JobsQueued, int JobsRunning, int JobsPaused, int JobsFailedSince, int JobsCompletedSince,
  int RecordsNeedsReview, int RecordsValidUnpublished, int RecordsInvalid, int RecordsPublishedSince);
public interface ISourcingAdminQueries {
  IQueryable<ImportJobDto> SearchJobs(ImportJobFilter? filter);                       // newest first
  Task<Result<ImportJobDto>> GetJobAsync(long jobId, CancellationToken ct = default);
  IQueryable<StagedRecordDto> SearchRecords(StagedRecordFilter? filter);              // id desc; Q = trigram/ILIKE on normalized title
  Task<Result<StagedRecordDetailDto>> GetRecordAsync(long recordId, CancellationToken ct = default);
  Task<IReadOnlyList<SourcePolicyDto>> ListPoliciesAsync(CancellationToken ct = default);
  Task<SourcingDashboardCounts> GetDashboardCountsAsync(DateTimeOffset since, CancellationToken ct = default);
}
```
Projections are hand-written `Select`s (DDD-GUIDELINES §9) with no aggregate loading (rule 6). `IssueCodes` is projected from `jsonb` via a stored generated column `issue_codes text[]` (`jsonb_path_query_array(issues, '$[*].code')`), which needs an additive migration.

- [ ] **Step 1: Write the failing tests.**
```csharp
[Fact] public async Task Dashboard_Counts_Statuses_And_Window()
{
    // seed: jobs Queued 1, Running 1, Failed (yesterday) 1, Failed (10 days ago) 1; records NeedsReview 2, Valid 1, Invalid 3, Published today 4
    (await q.GetDashboardCountsAsync(Now.AddDays(-7))).Should().Be(new SourcingDashboardCounts(1, 1, 0, 1, 0, 2, 1, 3, 4));
}
[Fact] public async Task Records_Filter_By_Issue_Code() => (await q.SearchRecords(new(null, null, null, "RIGHTS_UNKNOWN", null)).ToListAsync()).Should().OnlyContain(r => r.IssueCodes.Contains("RIGHTS_UNKNOWN"));
[Fact] public async Task Unknown_Job_Is_NotFound() => (await q.GetJobAsync(999)).Error!.HttpStatus.Should().Be(404);
[Fact] public async Task SearchRecords_Translates_On_Npgsql() => await q.SearchRecords(new(1, StagedRecordStatus.NeedsReview, "shop.example", "FUZZY_MATCH", "drill")).Take(5).ToListAsync(); // no client evaluation
```
- [ ] **Step 2: Run** `IT SourcingAdminQueriesTests`. Expected: FAIL.
- [ ] **Step 3: Implement**, plus the migration `AddStagedRecordIssueCodes`.
- [ ] **Step 4: Run** `IT SourcingAdminQueriesTests`, then `AT` (QueryApplication isolation). Expected: PASS.
- [ ] **Step 5: Commit.** Subject: `feat(sourcing): admin read side and dashboard counts`

---

### Task 22: Admin REST endpoints (`/api/v1/admin/imports/...`)

**Files:**
- Create: `Adapter/Inbound/Rest/*.cs`
- Test: `tests/IntegrationTests/Sourcing/SourcingAdminApiTests.cs`

**Interfaces:**
- Produces (all `RequireAuthorization("admin:staff")`, all responses via `EndpointResult`, ids as `[Snowflake]` strings):

| Method & path | Port call | Success |
|---|---|---|
| `POST /api/v1/admin/imports` | `IImportJobService.CreateAsync` | 201 `{id}` |
| `GET /api/v1/admin/imports?status&mode&page&pageSize` | `SearchJobs` | 200 page |
| `GET /api/v1/admin/imports/{id:long}` | `GetJobAsync` (progress) | 200 |
| `POST /api/v1/admin/imports/{id:long}/file` (multipart `file`) | `UploadAsync` | 204 |
| `POST /api/v1/admin/imports/{id:long}/mapping/preview` `{mapping?}` | `PreviewAsync` | 200 |
| `PUT /api/v1/admin/imports/{id:long}/mapping` `{mapping}` | `SetMappingAsync` | 204 |
| `POST /api/v1/admin/imports/{id:long}/{start\|pause\|resume\|cancel\|publish-valid}` | job service | 204 |
| `GET /api/v1/admin/imports/records?jobId&status&domain&issue&q&page&pageSize` | `SearchRecords` | 200 page |
| `GET /api/v1/admin/imports/records/{recordId:long}` | `GetRecordAsync` | 200 |
| `GET /api/v1/admin/imports/records/{recordId:long}/diff?productId` | `GetDiffAsync` | 200 |
| `POST /api/v1/admin/imports/records/{recordId:long}/approve` `{categoryId?}` | `ApproveAsync` | 204 |
| `POST .../records/{recordId:long}/merge` `{productId}` | `MergeAsync` | 204 |
| `POST .../records/{recordId:long}/create-new` | `CreateNewAsync` | 204 |
| `POST .../records/{recordId:long}/reject` `{reason}` | `RejectAsync` | 204 |
| `POST /api/v1/admin/imports/records/bulk-approve` `{recordIds[≤100]}` | `BulkApproveAsync` | 200 `{items:[{recordId,succeeded,errorCode}]}` |
| `GET /api/v1/admin/imports/templates/{format:regex(^(csv\|xlsx\|json-schema)$)}` | `ImportTemplates` | 200 file: `text/csv`, `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, `application/schema+json` |
| `GET/POST /api/v1/admin/imports/source-policies`, `PUT/DELETE /api/v1/admin/imports/source-policies/{id:long}` | `ISourcePolicyService`, `ListPoliciesAsync` | 200/201/204 |

`AdminActorFactory.From(ClaimsPrincipal)` builds `new AdminActor(Guid.Parse(sub), user.IsInRole("super_admin"))`. The multipart endpoint uses `.DisableAntiforgery()` (bearer-auth API) and a request size limit of `Sourcing:Files:MaxBytes + 64 KiB`.

- [ ] **Step 1: Write the failing tests** (`[Collection(PostgresSuite.Name)]`, `_fixture.CreateFactory()`).
```csharp
[Fact] public async Task Anonymous_401_Customer_403_Staff_200()
{
    (await Anon.GetAsync("/api/v1/admin/imports")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await Customer.GetAsync("/api/v1/admin/imports")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await Staff.GetAsync("/api/v1/admin/imports")).StatusCode.Should().Be(HttpStatusCode.OK);
}
[Fact] public async Task Templates_Download_With_Correct_Types()
{
    (await Staff.GetAsync("/api/v1/admin/imports/templates/csv")).Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
    (await Staff.GetAsync("/api/v1/admin/imports/templates/xlsx")).Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    (await Staff.GetAsync("/api/v1/admin/imports/templates/json-schema")).Content.Headers.ContentType!.MediaType.Should().Be("application/schema+json");
    (await Staff.GetAsync("/api/v1/admin/imports/templates/pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);
}
[Fact] public async Task File_Flow_Create_Upload_Preview_Map_Start()
{
    var id = await CreateFileJob(rightsAttested: true);
    (await Staff.PostAsync($"/api/v1/admin/imports/{id}/file", Multipart("products.csv", ImportTemplates.Csv()))).StatusCode.Should().Be(HttpStatusCode.NoContent);
    var preview = await Staff.PostAsJsonAsync($"/api/v1/admin/imports/{id}/mapping/preview", new { });
    (await preview.Content.ReadFromJsonAsync<JsonObject>())!["rows"]!.AsArray().Should().NotBeEmpty();
    (await Staff.PutAsJsonAsync($"/api/v1/admin/imports/{id}/mapping", new { mapping = Identity })).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await Staff.PostAsync($"/api/v1/admin/imports/{id}/start", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await Staff.GetFromJsonAsync<JsonObject>($"/api/v1/admin/imports/{id}"))!["status"]!.GetValue<string>().Should().Be("Queued");
}
[Fact] public async Task Upload_Is_Stored_In_Import_Staging() { /* after upload, IObjectStorage.ExistsAsync("import-staging", key from job row) is true */ }
[Fact] public async Task Error_Body_Has_Code_And_Correlation() { var r = await Staff.PostAsJsonAsync("/api/v1/admin/imports", new { mode = "Crawl", sources = Array.Empty<string>() }); var b = await r.Content.ReadFromJsonAsync<JsonObject>(); b!["code"]!.GetValue<string>().Should().Be("IMPORT_JOB_SOURCES_REQUIRED"); b["correlationId"].Should().NotBeNull(); r.Headers.Contains("X-Correlation-Id").Should().BeTrue(); }
[Fact] public async Task Staff_Can_Crud_Source_Policies() { /* POST 201 → GET contains → PUT 204 → DELETE 204 */ }
[Fact] public async Task Bulk_Approve_Over_100_Is_400() { /* 101 ids → 400 VALIDATION_FAILED */ }
```
- [ ] **Step 2: Run** `IT SourcingAdminApiTests`. Expected: FAIL.
- [ ] **Step 3: Implement** the endpoints as `IEndpoint` classes, grouped by file as listed.
- [ ] **Step 4: Run** `IT SourcingAdminApiTests`. Expected: PASS.
- [ ] **Step 5: Commit.** Subject: `feat(sourcing): admin REST endpoints for imports, review queue, templates and policies`

---

### Task 23: Admin GraphQL

**Files:**
- Create: `Adapter/Inbound/GraphQL/*.cs`
- Test: `tests/IntegrationTests/Sourcing/SourcingGraphQLTests.cs`, and an addition to the phase-0 SDL snapshot test

**Interfaces:**
- Produces on the shared `/graphql`. Every field is `[Authorize(Policy = "admin:staff")]`; all are thin calls to the same ports as Task 22.
  - Queries: `importJobs(filter): ImportJobDto` connection (`[UsePaging(IncludeTotalCount = true, MaxPageSize = 100)]`), `importJob(id: ID!)`, `stagedRecords(filter)` connection, `stagedRecord(id: ID!)`, `stagedRecordDiff(id: ID!, productId: ID)`, `sourcePolicies`, `importMappingPreview(jobId: ID!, mapping: [MappingEntryInput!])`.
  - Mutations: `createImportJob(input)`, `uploadImportFile(jobId: ID!, file: Upload!)`, `setImportMapping`, `startImportJob`, `pauseImportJob`, `resumeImportJob`, `cancelImportJob`, `publishValidImportRecords`, `approveStagedRecord(id, categoryId)`, `mergeStagedRecord(id, productId)`, `createNewFromStagedRecord(id)`, `rejectStagedRecord(id, reason)`, `bulkApproveStagedRecords(ids: [ID!]!) : [ReviewItemOutcome!]!`, `createSourcePolicy`, `updateSourcePolicy`, `deleteSourcePolicy`.
  - Errors go through the existing `ResultGraphQLExtensions.ValueOrThrow` and `GraphQLErrorFilter`.
- `AddSourcingGraphQL()` calls `.AddType<UploadType>()` on the shared builder. If the phase-0/6 bootstrap already registers `Upload`, omit that call.

- [ ] **Step 1: Write the failing tests.**
```csharp
[Fact] public async Task Customer_Token_Is_Denied() => (await Gql(Customer, "{ importJobs { totalCount } }"))["errors"]![0]!["extensions"]!["code"]!.GetValue<string>().Should().Be("AUTH_NOT_AUTHORIZED");
[Fact] public async Task Create_Then_Query_Progress()
{
    var id = (await Gql(Staff, "mutation { createImportJob(input: { mode: CRAWL, sources: [\"https://shop.example/\"] }) }"))["data"]!["createImportJob"]!.GetValue<string>();
    (await Gql(Staff, $"{{ importJob(id: \"{id}\") {{ status publishPolicy discovered }} }}"))["data"]!["importJob"]!["publishPolicy"]!.GetValue<string>().Should().Be("AUTO_PUBLISH_VALIDATED");
}
[Fact] public async Task Upload_Via_Multipart_Spec() { /* operations+map+file parts, header GraphQL-Preflight: 1 → true */ }
[Fact] public async Task Bulk_Approve_Returns_Per_Item_Outcomes() { /* … */ }
```
Also update the SDL snapshot.
- [ ] **Step 2: Run** `IT SourcingGraphQLTests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `IT SourcingGraphQLTests` and the SDL snapshot test. Expected: PASS.
- [ ] **Step 5: Commit.** Subject: `feat(sourcing): admin GraphQL for imports, review queue and source policies`

---

### Task 24: `StagingImageCleanup` recurring job

**Files:**
- Create: `Adapter/Outbound/Jobs/StagingImageCleanupHandler.cs`; add `IStagedRecordRepository.GetCleanupCandidatesAsync` and `GetKeysStillReferencedAsync`
- Test: `tests/IntegrationTests/Sourcing/StagingImageCleanupTests.cs`

**Interfaces:**
- Consumes: `AddRecurringJob(JobTypes.StagingImageCleanup, TimeSpan.FromHours(6))`
- Produces:
```csharp
public sealed record StagingImageCleanupPayload();
public sealed class StagingImageCleanupHandler : IJobHandler<StagingImageCleanupPayload> { public string Type => JobTypes.StagingImageCleanup; }
// IStagedRecordRepository additions:
Task<IReadOnlyList<StagedRecord>> GetCleanupCandidatesAsync(DateTimeOffset olderThan, int take, CancellationToken ct = default); // Published|Rejected, UpdatedAt < olderThan, StagingCleanedAt null
Task<IReadOnlySet<string>> GetKeysStillReferencedAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default);   // by non-terminal records (image_keys && @keys)
```
- Algorithm per run, in batches of 200:
  1. Load the candidates.
  2. Delete each image key from `import-staging` **unless** a non-terminal record still references it. Content-addressed keys are shared.
  3. Stamp `StagingCleanedAt` and save.
  Uploaded files under `uploads/{jobId}/` are deleted for terminal jobs older than retention (`ImportJob.FileObjectKey` cleared via a new `ImportJob.ForgetFile(now)`). The job is idempotent: deleting a missing key is a no-op.

- [ ] **Step 1: Write the failing tests.**
```csharp
[Fact] public async Task Deletes_Old_Terminal_Keys_But_Keeps_Shared_Ones()
{
    // A Published 8 days ago with keys [k1,k2]; B NeedsReview with [k2]
    await RunCleanup(now: Now);
    (await storage.ExistsAsync("import-staging", "images/k1.png")).Should().BeFalse();
    (await storage.ExistsAsync("import-staging", "images/k2.png")).Should().BeTrue();
    (await Reload(a)).StagingCleanedAt.Should().Be(Now);
}
[Fact] public async Task Recent_Records_Untouched() { /* Published 1 day ago → key still exists */ }
[Fact] public async Task Second_Run_Is_No_Op() { /* delete count 0 */ }
```
- [ ] **Step 2: Run** `IT StagingImageCleanupTests`. Expected: FAIL.
- [ ] **Step 3: Implement and register** the handler and the recurring job in `AddSourcingOutboundAdapters`.
- [ ] **Step 4: Run** `IT StagingImageCleanupTests`. Expected: PASS.
- [ ] **Step 5: Commit.** Subject: `feat(sourcing): StagingImageCleanup recurring job`

---

### Task 25: Resumability: kill mid-job, resume from checkpoint, no duplicate staged records

**Files:**
- Create: `tests/IntegrationTests/Sourcing/ImportTestDriver.cs`, `tests/IntegrationTests/Sourcing/ImportResumabilityTests.cs`

**Interfaces:**
- Produces (test helper):
```csharp
internal sealed class ImportTestDriver(WebApplicationFactory<Program> factory) {
  // Jobs:Mode=Disabled in the factory; the driver plays the worker deterministically.
  public Task<long> CreateFileJobAsync(byte[] csv, bool rightsAttested = true, PublishPolicy policy = PublishPolicy.AutoPublishValidated, long? targetCategoryId = null);
  public Task<long> CreateCrawlJobAsync(IReadOnlyList<string> sources, int maxDepth = 1);
  public Task<ImportStepResult> StepAsync(long jobId, ImportStepKind kind = ImportStepKind.Run); // new DI scope per step, like a worker
  public Task RunToEndAsync(long jobId, ImportStepKind kind = ImportStepKind.Run, int maxSteps = 500);
}
```

- [ ] **Step 1: Write the failing test.** It uses a 260-row CSV, so there are 6 discover steps of 50, and an `ISourceAdapter` decorator registered in `ConfigureTestServices`. The decorator throws `OperationCanceledException` after the 120th yielded listing on its first enumeration only, simulating a killed worker (no checkpoint save after the partial batch).
```csharp
[Fact] public async Task Killed_Worker_Resumes_From_Checkpoint_Without_Duplicates()
{
    var jobId = await driver.CreateFileJobAsync(Csv(rows: 260));
    await driver.StepAsync(jobId); await driver.StepAsync(jobId);                         // 100 staged, checkpoint "100"
    await FluentActions.Awaiting(() => driver.StepAsync(jobId)).Should().ThrowAsync<OperationCanceledException>(); // dies mid-enumeration (listing 120), before StageAsync of rows 101–150
    (await Job(jobId)).Checkpoint.AdapterCursor.Should().Be("100");
    await driver.RunToEndAsync(jobId);
    var job = await Job(jobId);
    job.Status.Should().Be(ImportJobStatus.Completed);
    (await Db.Set<StagedRecord>().CountAsync(r => r.LastJobId == jobId)).Should().Be(260);
    (await Db.Set<StagedRecord>().GroupBy(r => r.Fingerprint).AnyAsync(g => g.Count() > 1)).Should().BeFalse();
    job.Counters.Discovered.Should().Be(260); job.Counters.Duplicate.Should().Be(0);
}
[Fact] public async Task Crash_Between_Staging_And_Checkpoint_Is_Idempotent()
{
    // decorator variant: ISourcingPersistence wrapper throws on the checkpoint save of step 3 once
    // → rows 101–150 are staged but the cursor is still "100"; resume re-stages them as AlreadyInThisJob
    // asserts identical to the test above
}
[Fact] public async Task Pause_Then_Resume_Continues_Where_It_Stopped() { /* Step ×2, PauseAsync, StepAsync returns MoreWork=false, ResumeAsync, RunToEnd → 260 */ }
```
- [ ] **Step 2: Run** `IT ImportResumabilityTests`. Expected: FAIL (compile, until the driver exists), then PASS once the driver is written. The pipeline was built in Task 19.
- [ ] **Step 3: Implement** `ImportTestDriver`. The factory sets `Jobs:Mode=Disabled`, `Sourcing:Crawler:AllowPrivateNetworks=true` and `Sourcing:Crawler:PolitenessDelayMs=0`.
- [ ] **Step 4: Run** `IT ImportResumabilityTests`. Expected: PASS. **Mutation check:** make `Restage` return `Duplicate` for the same job, and watch `Duplicate.Should().Be(0)` fail; restore.
- [ ] **Step 5: Commit.** Subject: `test(sourcing): resumability after a killed worker without duplicate staging`

---

### Task 26: PRD acceptance 10: repeat import, no duplicates, staged invalid/ambiguous, auto-publish validated only

**Files:**
- Create: `tests/IntegrationTests/Sourcing/ImportAcceptance10Tests.cs`

- [ ] **Step 1: Write the test.**
  - Seed through the phase 2–3 Catalog admin services:
    - category `Tools > Power Tools` (alias `power-tools`) with the required attribute `voltage` (Number, unit `V`);
    - brand `Acme` with alias `ACME Inc.`;
    - an existing product `Acme Cordless Drill 18V` with no identifiers, for the fuzzy match.
  - `FixtureHttpServer` serves `pixel.png`.
  - The CSV holds 6 rows:
    - **R1:** 2 variants (`group_key=G1`, colors Red/Blue), GTIN, brand `ACME Inc.`.
    - **R2:** a simple product, brand `acme`.
    - **R3:** price `12.345`.
    - **R4:** missing voltage.
    - **R5:** title `Acme Cordless Drill 18 V Kit`, no identifiers, so it is a fuzzy match.
    - **R6:** an unknown category.
```csharp
[Fact] public async Task Repeat_Import_Creates_No_Duplicates_And_Publishes_Only_Valid()
{
    var before = await CountCatalog();                                     // categories, attribute_blueprints, brands, brand_aliases, products, variants
    var job1 = await driver.CreateFileJobAsync(Csv6, targetCategoryId: null); await driver.RunToEndAsync(job1);
    var after1 = await CountCatalog();
    after1.Products.Should().Be(before.Products + 2); after1.Variants.Should().Be(before.Variants + 3);
    after1.Categories.Should().Be(before.Categories); after1.Attributes.Should().Be(before.Attributes); after1.Brands.Should().Be(before.Brands);
    (await Statuses(job1)).Should().BeEquivalentTo(new Dictionary<string, string> {
        ["R1"] = "Published", ["R2"] = "Published", ["R3"] = "Invalid", ["R4"] = "Invalid", ["R5"] = "NeedsReview", ["R6"] = "NeedsReview" });

    var job2 = await driver.CreateFileJobAsync(Csv6); await driver.RunToEndAsync(job2);
    (await CountCatalog()).Should().Be(after1);
    (await Job(job2)).Counters.Duplicate.Should().Be(6);
    (await Db.Set<StagedRecord>().CountAsync()).Should().Be(6);
    (await Statuses(job2))["R5"].Should().Be("NeedsReview");                  // ambiguity is never auto-resolved
}
[Fact] public async Task ManualReview_Job_Publishes_Nothing() { /* new rows, PublishPolicy.ManualReview → products unchanged, R1/R2 Valid */ }
[Fact] public async Task Brand_Alias_Reuses_Brand() { /* product of R1 BrandId == seeded Acme id */ }
```
- [ ] **Step 2: Run** `IT ImportAcceptance10Tests`. Expected: PASS. A failure is a defect in the task that owns that behaviour; fix it there and re-run.
- [ ] **Step 3: Mutation check.** Temporarily change `RecordMatcher` to return `ExactMatch` for a single fuzzy candidate. Watch `R5` become `Published` and the test fail, then restore.
- [ ] **Step 4: Run** `IT ImportAcceptance10Tests`. Expected: PASS.
- [ ] **Step 5: Commit.** Subject: `test(sourcing): PRD acceptance 10 — repeat import without duplicates`

---

### Task 27: PRD acceptance 15: crawler and search preserve provenance and enforce restrictions

**Files:**
- Create: `tests/IntegrationTests/Sourcing/ImportAcceptance15Tests.cs`

- [ ] **Step 1: Write the test.** `FixtureHttpServer` serves two hosts:
  - Host `127.0.0.1` has a source policy that allows everything. It serves `robots.txt` (disallowing `/private/`), `/` (linking to `/p/drill`, `/private/p2` and `/about`), `/p/drill` (JSON-LD, `SGD 129.90`, `valueAddedTaxIncluded: true`, `sku ACM-D18`), `/about` (no data, also seeded directly) and `/img/drill.png`.
  - Host `localhost` has **no policy** and serves `/p/kit` (JSON-LD).
  - A third run uses policy `ContentUseAllowed=false` on `localhost`.
  - Brave is pointed at the server through `Brave:BaseUrl`. `brave-response.json` is rewritten with the server URLs, and `localhost` is excluded through `ExcludeDomains` in the search job.
```csharp
[Fact] public async Task Crawl_Preserves_Provenance_End_To_End()
{
    var job = await driver.CreateCrawlJobAsync([server.Url("/"), server.Url("/about")]); await driver.RunToEndAsync(job);
    var listing = await Db.Set<SourceListing>().Include(l => l.Observations).SingleAsync(l => l.SourceId == "ACM-D18");
    listing.Url.Should().Be(server.Url("/p/drill")); listing.Domain.Should().Be("127.0.0.1"); listing.Rights.Should().Be("policy:127.0.0.1");
    listing.Observations.Single().Should().Match<PriceObservation>(o => o.AmountMinor == 12990 && o.Currency == "SGD" && o.TaxIncluded == true);
    var price = await PricingProvenanceOfPublishedVariant(listing);          // phase 2–3 VariantPrice provenance columns
    price.PriceObservationId.Should().Be(listing.Observations.Single().Id); price.SourceUrl.Should().Be(listing.Url);
    price.OriginalAmountMinor.Should().Be(12990); price.OriginalCurrency.Should().Be("SGD"); price.TaxIncluded.Should().BeTrue();
    (await ProductMediaOf(listing)).Single().UsageRights.Should().Be("policy:127.0.0.1");
}
[Fact] public async Task Invalid_Content_Is_Rejected_And_Robots_Is_Respected()
{
    var job = await driver.CreateCrawlJobAsync([server.Url("/"), server.Url("/about")]); await driver.RunToEndAsync(job);
    (await RecordFor(server.Url("/about"))).Issues.Should().Contain(i => i.Code == "NO_STRUCTURED_DATA");
    server.Requests.Should().NotContain(r => r.PathAndQuery.StartsWith("/private/"));
}
[Fact] public async Task Unknown_Rights_Go_To_Review_Never_Published()
{
    var job = await driver.CreateCrawlJobAsync([server.Url("/p/kit", host: "localhost")]); await driver.RunToEndAsync(job);
    var r = await RecordFor(server.Url("/p/kit", host: "localhost"));
    r.Status.Should().Be(StagedRecordStatus.NeedsReview); r.Issues.Should().Contain(i => i.Code == "RIGHTS_UNKNOWN"); r.PublishedProductId.Should().BeNull();
}
[Fact] public async Task Content_Denied_Policy_Is_Invalid() { /* policy localhost ContentUseAllowed=false → Invalid RIGHTS_DENIED */ }
[Fact] public async Task CrawlAllowed_False_Means_Not_Fetched() { /* policy localhost CrawlAllowed=false → server saw no localhost request; record Invalid SOURCE_BLOCKED */ }
[Fact] public async Task Web_Search_Respects_Exclusions_And_Keeps_Provenance() { /* WebSearch job, Brave key "test"; localhost excluded → no localhost fetch; drill listing provenance as above */ }
```
- [ ] **Step 2: Run** `IT ImportAcceptance15Tests`. Expected: PASS. A failure is fixed in the owning task.
- [ ] **Step 3: Mutation check.** Make `RightsDecision.For(null, …)` return `Allowed`. Watch `Unknown_Rights_Go_To_Review_Never_Published` fail, then restore.
- [ ] **Step 4: Run** `IT ImportAcceptance15Tests`. Expected: PASS.
- [ ] **Step 5: Commit.** Subject: `test(sourcing): PRD acceptance 15 — provenance and source restrictions`

---

### Task 28: Review Focus 5: `Reimport_Never_Reactivates_Admin_Deactivated_Product`

**Files:**
- Create: `tests/IntegrationTests/Sourcing/ReimportDeactivatedTests.cs`

- [ ] **Step 1: Write the test.**
```csharp
[Fact]
public async Task Reimport_Never_Reactivates_Admin_Deactivated_Product()
{
    var job1 = await driver.CreateFileJobAsync(Csv(("G1", "Acme Drill", "Red", "10.00"), ("G1", "Acme Drill", "Blue", "11.00")));
    await driver.RunToEndAsync(job1);
    var product = await Db.Set<Product>().Include(p => p.Variants).SingleAsync(p => p.Title == "Acme Drill");
    product.Status.Should().Be(ProductStatus.Active);
    var variantIds = product.Variants.Select(v => v.Id).ToList();

    await DeactivateProductAsAdminAsync(product.Id);                          // phase 2–3 ICatalogAdminService product deactivate, as staff

    var job2 = await driver.CreateFileJobAsync(Csv(("G1", "Acme Drill Pro", "Red", "12.00"), ("G1", "Acme Drill Pro", "Blue", "13.00")));
    await driver.RunToEndAsync(job2);

    var reloaded = await FreshDb.Set<Product>().Include(p => p.Variants).SingleAsync(p => p.Id == product.Id);
    reloaded.Title.Should().Be("Acme Drill Pro");                             // fields updated
    reloaded.Status.Should().Be(ProductStatus.Deactivated);
    reloaded.DeactivatedByAdmin.Should().BeTrue();
    reloaded.Variants.Should().OnlyContain(v => !v.IsActive || v.DeactivatedByAdmin);
    (await FreshDb.Set<Product>().CountAsync(p => p.Title.StartsWith("Acme Drill"))).Should().Be(1); // no duplicate created instead
    (await Sellable.GetSellableAsync(variantIds)).Should().BeEmpty();         // not in composition or cart
    (await StagedRecordFor(job2)).PublishSkippedDeactivated.Should().BeTrue();
    (await ReferencePriceMinor(variantIds[0])).Should().Be(1200);             // price fields update too
}
```
- [ ] **Step 2: Run** `IT ReimportDeactivatedTests`. Expected: PASS. If it fails, the guard belongs to Catalog's publishing port (phase 2–3) or to `RecordPublisher` (Task 18); fix it there.
- [ ] **Step 3: Mutation check.** Temporarily remove Catalog's `DeactivatedByAdmin` guard in its upsert path. Watch the test fail on `Status` or `GetSellableAsync`, then restore.
- [ ] **Step 4: Run** `IT ReimportDeactivatedTests`. Expected: PASS.
- [ ] **Step 5: Commit.** Subject: `test(sourcing): re-import never reactivates an admin-deactivated product`

---

### Task 29: Phase gate

**Files:**
- Create: `docs/reports/phase-7.md`
- Modify: `AGENTS.md` (Sourcing module row, exception rows, `Brave:*`/`Anthropic:*`/`Sourcing:*` in the env table, `/api/v1/admin/imports` in the endpoint table), `docs/TESTING-GUARANTEES.md` (what the Sourcing suites cannot see: live sites, live Brave/Anthropic, JS-rendered pages), `.env.example` (`Brave__ApiKey=`, `Anthropic__ApiKey=` blank), and add the `docs/api/` Bruno requests for the import endpoints if phase 8 has not already created the collection.

- [ ] **Step 1: Build.** Run `dotnet build EwaysVNext.slnx`. Expected: 0 errors, 0 warnings-as-errors.
- [ ] **Step 2: Test.** Run `dotnet test EwaysVNext.slnx` (Docker running). Expected: all green, including `AT`.
- [ ] **Step 3: Check boundaries and safety.**
  - `grep -rn "BeginTransaction\|UseTransaction" src/Modules/Sourcing` must be empty.
  - `grep -rn "Guid.NewGuid" src/Modules/Sourcing` must be empty.
  - `grep -rn "temperature" src/Modules/Sourcing` must be empty.
  - No secrets in `appsettings*.json`.
- [ ] **Step 4: Write `docs/reports/phase-7.md`:**
  - What works, mapped to spec §5.5 bullets and acceptance 10 and 15.
  - What is incomplete: no JS rendering; images are not re-encoded; enrichment is never live-verified in CI.
  - Risks:
    - Crawl legality is gated by `SourcePolicy`, which is a §16 release gate.
    - The enrichment cost per record.
    - In-process politeness per worker. Several workers share no per-host gate, so ADR-0010's caveat applies.
  - Reproduction commands: `UT`/`IT` filters, and how to run a sample file import with the templates.
- [ ] **Step 5: Commit.** Subject: `docs(sourcing): phase 7 report, AGENTS and testing-guarantee updates`

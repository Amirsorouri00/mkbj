using System.Text.Json.Nodes;
using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Sourcing.Application.Discovery;
using EwaysVNext.Modules.Sourcing.Application.Domain.SourceListings;
using EwaysVNext.Modules.Sourcing.Application.Domain.SourcePolicies;
using EwaysVNext.Modules.Sourcing.Application.Domain.StagedRecords;
using EwaysVNext.Modules.Sourcing.Application.Pipeline;
using EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;
using EwaysVNext.Outcomes;
using Xunit;

namespace EwaysVNext.UnitTests.Sourcing;

/// <summary>
/// Processing and publishing one record, end to end without a database.
/// </summary>
/// <remarks>
/// The two behaviours worth pinning here are the ones an administrator would notice and could
/// not explain: a re-import quietly putting a withdrawn product back on sale, and an import
/// reporting failure after it had in fact created a product.
/// </remarks>
public sealed class PublishPipelineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_Complete_Listing_With_Known_Rights_Is_Valid()
    {
        var processed = await ProcessAsync(Complete(), policy: Allowed());

        processed.Status.Should().Be(StagedRecordStatus.Valid);
        processed.Rights.Status.Should().Be(RightsStatus.Allowed);
        processed.Product.PriceMinor.Should().Be(1_999);
    }

    [Fact]
    public async Task No_Policy_Sends_The_Record_To_Review_Rather_Than_Publishing_It()
    {
        var processed = await ProcessAsync(Complete(), policy: null);

        processed.Status.Should().Be(StagedRecordStatus.NeedsReview);
        processed.Issues.Should().Contain(issue => issue.Code == ValidationCodes.RightsUnknown);
    }

    [Fact]
    public async Task A_Source_That_Forbids_Its_Content_Produces_An_Invalid_Record()
    {
        var processed = await ProcessAsync(Complete(), policy: Policy(content: false, images: true));

        // Nobody in the backoffice can grant a right the supplier withheld.
        processed.Status.Should().Be(StagedRecordStatus.Invalid);
    }

    [Fact]
    public async Task Re_Import_Never_Puts_A_Withdrawn_Product_Back_On_Sale()
    {
        var world = new World { SkipBecauseDeactivated = true };
        var record = await StagedAndProcessedAsync(world);

        var outcome = await world.Publisher.PublishAsync(
            record.Record, record.Processed, ["Kitchen"], Ct);

        outcome.IsSuccess.Should().BeTrue();

        // An administrator deactivated this product. The import updates it and leaves it off.
        outcome.Value!.SkippedBecauseDeactivated.Should().BeTrue();
        world.Prices.Calls.Should().Be(0,
            "pricing something nobody can buy would put it back in the shop by the side door");

        record.Record.MarkPublished(
            outcome.Value.ProductId, outcome.Value.VariantIds, true, outcome.Value.SourceListingId, Now);

        record.Record.PublishSkippedDeactivated.Should().BeTrue(
            "the queue has to be able to say why a published import changed nothing");
    }

    [Fact]
    public async Task A_Price_Failure_Does_Not_Pretend_The_Product_Was_Not_Created()
    {
        var world = new World { PriceError = new Error("CURRENCY_NOT_ENABLED", "no", 422) };
        var record = await StagedAndProcessedAsync(world);

        var outcome = await world.Publisher.PublishAsync(
            record.Record, record.Processed, ["Kitchen"], Ct);

        // Catalog has already committed; there is no shared transaction, by design. Telling an
        // administrator "the import failed" would send them looking in the wrong place.
        outcome.IsSuccess.Should().BeTrue();
        outcome.Value!.ProductId.Should().Be(100);
        outcome.Value.Warnings.Should().Equal("CURRENCY_NOT_ENABLED");
    }

    [Fact]
    public async Task A_Catalogue_Refusal_Stops_Everything_And_Says_Why()
    {
        var world = new World { CatalogError = new Error("CATEGORY_NOT_FOUND", "no", 422) };
        var record = await StagedAndProcessedAsync(world);

        var outcome = await world.Publisher.PublishAsync(
            record.Record, record.Processed, ["Kitchen"], Ct);

        outcome.Error!.Code.Should().Be("CATEGORY_NOT_FOUND");
        world.Prices.Calls.Should().Be(0, "a price with no variant to attach it to is meaningless");

        record.Record.MarkPublishFailed(outcome.Error.Code, Now);
        record.Record.Status.Should().Be(StagedRecordStatus.NeedsReview);
    }

    [Fact]
    public async Task Images_Are_Left_Behind_When_The_Source_Forbids_Them()
    {
        var world = new World();
        var record = await StagedAndProcessedAsync(world, Policy(content: true, images: false));

        await world.Publisher.PublishAsync(record.Record, record.Processed, ["Kitchen"], Ct);

        // They were downloaded before anybody asked; this is the last point they can be dropped.
        world.Catalog.LastRequest!.ImagesAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task One_Listing_Is_Created_Once_However_Often_It_Is_Published()
    {
        var world = new World();
        var first = await StagedAndProcessedAsync(world);

        await world.Publisher.PublishAsync(first.Record, first.Processed, ["Kitchen"], Ct);
        await world.Publisher.PublishAsync(first.Record, first.Processed, ["Kitchen"], Ct);

        // The price history belongs to the page, not to the run that happened to read it.
        world.Listings.Added.Should().Be(1);
    }

    private static async Task<ProcessedRecord> ProcessAsync(JsonObject document, SourcePolicy? policy)
    {
        var world = new World { Policy = policy };

        return await world.Processor.ProcessAsync(
            StagedRecord.Stage(1, 1, Fingerprint.For(Listing(document)), Listing(document), "{}", Now),
            Listing(document),
            fileRightsAttested: false,
            jobId: 1,
            requireCategory: false,
            adminChoice: null,
            Ct);
    }

    private static async Task<(StagedRecord Record, ProcessedRecord Processed)>
        StagedAndProcessedAsync(World world, SourcePolicy? policy = null)
    {
        world.Policy = policy ?? Allowed();

        var listing = Listing(Complete());
        var record = StagedRecord.Stage(1, 1, Fingerprint.For(listing), listing, "{}", Now);

        var processed = await world.Processor.ProcessAsync(
            record, listing, false, 1, false, null, Ct);

        record.RecordProcessing(
            processed.ToJson(), ["staging/1/a.jpg"], processed.Match, processed.Issues, Now);

        return (record, processed);
    }

    private static JsonObject Complete() => new()
    {
        ["name"] = "Acme Kettle 1.7L",
        ["brand"] = "Acme",
        ["offers"] = new JsonObject { ["price"] = "19.99", ["priceCurrency"] = "SGD" },
    };

    private static SourcePolicy Allowed() => Policy(content: true, images: true);

    private static SourcePolicy Policy(bool content, bool images) =>
        SourcePolicy.Create(1, "shop.example.com", true, content, images, null, Now);

    private static RawListing Listing(JsonObject document) => new(
        "https://shop.example.com/p/1", "shop.example.com", "SKU-1", Now,
        ExtractionMethod.JsonLd, document, null, ["https://shop.example.com/a.jpg"], null, "c");

    private sealed class World
    {
        public World()
        {
            Catalog = new FakeCatalogPublisher(this);
            Prices = new FakePricePublisher(this);
            Listings = new FakeListings();

            Processor = new RecordProcessor(new NoMatches(), new Policies(this));
            Publisher = new RecordPublisher(Catalog, Prices, Listings, new Ids(), new Clock());
        }

        public SourcePolicy? Policy { get; set; }

        public bool SkipBecauseDeactivated { get; init; }

        public Error? CatalogError { get; init; }

        public Error? PriceError { get; init; }

        public RecordProcessor Processor { get; }

        public RecordPublisher Publisher { get; }

        public FakeCatalogPublisher Catalog { get; }

        public FakePricePublisher Prices { get; }

        public FakeListings Listings { get; }
    }

    private sealed class NoMatches : ICatalogMatcher
    {
        public Task<long?> FindByIdentifierAsync(
            string identifierType, string value, CancellationToken ct = default) =>
            Task.FromResult<long?>(null);

        public Task<IReadOnlyList<CatalogCandidate>> FindSimilarAsync(
            string title, double threshold, int limit, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CatalogCandidate>>([]);
    }

    private sealed class Policies(World world) : ISourcePolicyRepository
    {
        public Task<SourcePolicy?> FindByDomainAsync(
            string domain, CancellationToken ct = default) =>
            Task.FromResult(world.Policy);

        public Task<IReadOnlyList<SourcePolicy>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SourcePolicy>>([]);

        public Task AddAsync(SourcePolicy policy, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeCatalogPublisher(World world) : ICatalogPublisher
    {
        public SourcingPublishRequest? LastRequest { get; private set; }

        public Task<Result<SourcingPublishResult>> PublishAsync(
            SourcingPublishRequest request, CancellationToken ct = default)
        {
            LastRequest = request;

            return Task.FromResult(world.CatalogError is { } error
                ? Result.Failure<SourcingPublishResult>(error)
                : Result.Success(new SourcingPublishResult(
                    100, true, world.SkipBecauseDeactivated,
                    [new SourcingPublishedVariant(200, "SKU-1")])));
        }
    }

    private sealed class FakePricePublisher(World world) : IPricePublisher
    {
        public int Calls { get; private set; }

        public Task<Result> SetPriceAsync(
            long variantId, long amountMinor, string currency, string sourceUrl,
            DateTimeOffset collectedAt, CancellationToken ct = default)
        {
            Calls++;

            return Task.FromResult(
                world.PriceError is { } error ? Result.Failure(error) : Result.Success());
        }
    }

    private sealed class FakeListings : ISourceListingRepository
    {
        private SourceListing? _stored;

        public int Added { get; private set; }

        public Task<SourceListing?> FindByUrlAsync(string url, CancellationToken ct = default) =>
            Task.FromResult(_stored);

        public Task AddAsync(SourceListing listing, CancellationToken ct = default)
        {
            _stored = listing;
            Added++;
            return Task.CompletedTask;
        }
    }

    private sealed class Ids : IIdGenerator
    {
        private long _next = 900;

        public long Generate() => Interlocked.Increment(ref _next);
    }

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}

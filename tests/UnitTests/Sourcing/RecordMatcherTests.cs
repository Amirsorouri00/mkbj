using System.Text.Json.Nodes;
using AwesomeAssertions;
using EwaysVNext.Modules.Sourcing.Application.Discovery;
using EwaysVNext.Modules.Sourcing.Application.Domain.StagedRecords;
using EwaysVNext.Modules.Sourcing.Application.Matching;
using EwaysVNext.Modules.Sourcing.Application.Normalization;
using EwaysVNext.Modules.Sourcing.Application.Ports.Outbound;
using Xunit;

namespace EwaysVNext.UnitTests.Sourcing;

/// <summary>
/// Whether a listing is something the catalogue already has.
/// </summary>
/// <remarks>
/// An identifier is a fact and a title is a suggestion, and the whole value of this class is
/// that it never confuses the two. Attaching a listing to the wrong existing product is the
/// expensive mistake — it rewrites a product customers are already buying.
/// </remarks>
public sealed class RecordMatcherTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_Barcode_Settles_The_Question()
    {
        var catalog = new FakeCatalog { ByIdentifier = { ["Gtin:4006381333931"] = 42 } };

        var match = await RecordMatcher.MatchAsync(Product(gtin: "4006381333931"), catalog, null, Ct);

        match.Should().Be(new MatchResult(MatchKind.ExactMatch, 42, "gtin", []));
        catalog.SimilarityCalls.Should().Be(0, "a fact needs no suggestions");
    }

    [Fact]
    public async Task An_Mpn_Is_Tried_When_There_Is_No_Barcode()
    {
        var catalog = new FakeCatalog { ByIdentifier = { ["Mpn:AC-1700"] = 7 } };

        var match = await RecordMatcher.MatchAsync(Product(mpn: "AC-1700"), catalog, null, Ct);

        match.Kind.Should().Be(MatchKind.ExactMatch);
        match.MatchedBy.Should().Be("mpn");
    }

    [Fact]
    public async Task An_Administrators_Decision_Beats_Every_Lookup()
    {
        var catalog = new FakeCatalog { ByIdentifier = { ["Gtin:4006381333931"] = 42 } };

        var match = await RecordMatcher.MatchAsync(
            Product(gtin: "4006381333931"), catalog, adminChoice: 99, Ct);

        // They have already answered the question matching exists to answer.
        match.Should().Be(new MatchResult(MatchKind.ExactMatch, 99, "admin-merge", []));
        catalog.IdentifierCalls.Should().Be(0);
    }

    [Fact]
    public async Task A_Clear_Front_Runner_Is_Still_Only_A_Suggestion()
    {
        var catalog = new FakeCatalog
        {
            Similar = [new CatalogCandidate(5, "Acme Kettle 1.7L", 0.91)],
        };

        var match = await RecordMatcher.MatchAsync(Product(), catalog, null, Ct);

        match.Kind.Should().Be(MatchKind.Fuzzy);
        match.ProductId.Should().BeNull("a suggestion is not a decision");
        match.Candidates.Should().ContainSingle();

        StagedRecord.DeriveStatus(match, []).Should().Be(StagedRecordStatus.NeedsReview);
    }

    [Fact]
    public async Task Near_Ties_Are_Ambiguous_Rather_Than_Resolved()
    {
        var catalog = new FakeCatalog
        {
            Similar =
            [
                new CatalogCandidate(5, "Acme Kettle 1.7L", 0.90),
                new CatalogCandidate(6, "Acme Kettle 1.5L", 0.88),
            ],
        };

        var match = await RecordMatcher.MatchAsync(Product(), catalog, null, Ct);

        // Picking a winner from near-ties is how an import quietly merges two products. Two
        // genuinely different kettles from one brand differ by one character.
        match.Kind.Should().Be(MatchKind.Ambiguous);
        match.Candidates.Should().HaveCount(2, "the runner-up is what the reviewer needs to see");
    }

    [Fact]
    public async Task A_Comfortable_Gap_Is_Not_Ambiguous()
    {
        var catalog = new FakeCatalog
        {
            Similar =
            [
                new CatalogCandidate(5, "Acme Kettle 1.7L", 0.95),
                new CatalogCandidate(6, "Acme Toaster", 0.60),
            ],
        };

        (await RecordMatcher.MatchAsync(Product(), catalog, null, Ct)).Kind
            .Should().Be(MatchKind.Fuzzy);
    }

    [Fact]
    public async Task Nothing_Similar_Means_A_New_Product()
    {
        var match = await RecordMatcher.MatchAsync(Product(), new FakeCatalog(), null, Ct);

        match.Should().Be(MatchResult.New);
        StagedRecord.DeriveStatus(match, []).Should().Be(StagedRecordStatus.Valid);
    }

    [Fact]
    public async Task A_Listing_With_No_Title_Is_Not_Guessed_At()
    {
        var catalog = new FakeCatalog
        {
            Similar = [new CatalogCandidate(5, "Something", 0.99)],
        };

        var match = await RecordMatcher.MatchAsync(
            DeterministicNormalizer.Normalize(Listing(new JsonObject())), catalog, null, Ct);

        match.Should().Be(MatchResult.New);
        catalog.SimilarityCalls.Should().Be(0);
    }

    private static NormalizedProduct Product(string? gtin = null, string? mpn = null)
    {
        var document = new JsonObject { ["name"] = "Acme Kettle 1.7L" };
        if (gtin is not null) document["gtin13"] = gtin;
        if (mpn is not null) document["mpn"] = mpn;

        return DeterministicNormalizer.Normalize(Listing(document));
    }

    private static RawListing Listing(JsonObject document) => new(
        "https://shop.example.com/p/1", "shop.example.com", "SKU-1", Now,
        ExtractionMethod.JsonLd, document, null, [], null, "cursor");

    private sealed class FakeCatalog : ICatalogMatcher
    {
        public Dictionary<string, long> ByIdentifier { get; } = [];

        public IReadOnlyList<CatalogCandidate> Similar { get; init; } = [];

        public int IdentifierCalls { get; private set; }

        public int SimilarityCalls { get; private set; }

        public Task<long?> FindByIdentifierAsync(
            string identifierType, string value, CancellationToken ct = default)
        {
            IdentifierCalls++;

            return Task.FromResult(
                ByIdentifier.TryGetValue($"{identifierType}:{value}", out var id) ? id : (long?)null);
        }

        public Task<IReadOnlyList<CatalogCandidate>> FindSimilarAsync(
            string title, double threshold, int limit, CancellationToken ct = default)
        {
            SimilarityCalls++;

            return Task.FromResult<IReadOnlyList<CatalogCandidate>>(
                [.. Similar.Where(candidate => candidate.Similarity >= threshold).Take(limit)]);
        }
    }
}
